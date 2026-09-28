using PhoneBridge.Credentials;
using PhoneBridge.Discovery;

namespace PhoneBridge.Connection;

public sealed class ConnectionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public enum ConnectionStage { ConfirmingCode, SavingPending, WaitingApproval, Validating, Mounting, Mounted, Revoking }
public sealed record MountOptions(char DriveLetter, string RclonePath, string SessionRoot, string? CacheBaseRoot = null);
public sealed record ConnectedDevice(PairingRecord Record, DeviceEndpoint Endpoint, char DriveLetter);
public sealed record DeletionPreview(string DeviceId, string ConfirmationId, string Path, bool Directory, long Size, long Modified);

/// <summary>Pure timing/state policy. Network, credentials and mounts remain owned by ConnectionClient.</summary>
public sealed class ReconnectPolicy
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SuspectedDisconnectInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)
    ];
    private const int MaximumReconnectAttempts = 6;
    private const int StableHealthSuccessLimit = 3;
    private int healthFailures, retryIndex, reconnectAttempts, stableHealthSuccesses;
    private bool disconnectSuspected, recovering;
    private DateTimeOffset nextHealth, nextAttempt, recoveryStarted, recoveryDeadline;
    public string? DeviceId { get; private set; }
    public MountOptions? Options { get; private set; }
    public bool Armed => DeviceId is not null && Options is not null;
    public bool Recovering => Armed && recovering;
    public int ConsecutiveHealthFailures => healthFailures;
    public int HealthFailureLimit => disconnectSuspected ? 2 : 3;
    public int ReconnectAttempts => reconnectAttempts;
    public int ReconnectAttemptLimit => MaximumReconnectAttempts;
    public int StableHealthSuccesses => stableHealthSuccesses;
    public DateTimeOffset NextAttempt => nextAttempt;
    public DateTimeOffset RecoveryDeadline => recoveryDeadline;

    public void Arm(string deviceId, MountOptions options, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("device id required", nameof(deviceId));
        DeviceId = deviceId; Options = options;
        healthFailures = 0; retryIndex = 0; reconnectAttempts = 0; stableHealthSuccesses = 0;
        disconnectSuspected = false; recovering = false;
        nextHealth = now + HealthInterval; nextAttempt = recoveryStarted = recoveryDeadline = default;
    }
    public void Suppress()
    {
        DeviceId = null; Options = null;
        healthFailures = 0; retryIndex = 0; reconnectAttempts = 0; stableHealthSuccesses = 0;
        disconnectSuspected = false; recovering = false;
        nextHealth = nextAttempt = recoveryStarted = recoveryDeadline = default;
    }
    public bool HealthDue(DateTimeOffset now) => Armed && now >= nextHealth;
    public void SuspectDisconnect(DateTimeOffset now)
    {
        if (!Armed) return;
        disconnectSuspected = true;
        nextHealth = now;
    }
    public void HealthSucceeded(DateTimeOffset now)
    {
        if (!Armed) return;
        healthFailures = 0; disconnectSuspected = false; nextHealth = now + HealthInterval;
        if (!recovering) return;
        stableHealthSuccesses++;
        if (stableHealthSuccesses < StableHealthSuccessLimit) return;
        recovering = false; retryIndex = 0; reconnectAttempts = 0; stableHealthSuccesses = 0;
        nextAttempt = recoveryStarted = recoveryDeadline = default;
    }
    public bool HealthFailed(DateTimeOffset now)
    {
        if (!Armed) return false;
        healthFailures++; stableHealthSuccesses = 0;
        nextHealth = now + (disconnectSuspected ? SuspectedDisconnectInterval : HealthInterval);
        return healthFailures >= HealthFailureLimit;
    }
    public void Disconnected(DateTimeOffset now)
    {
        if (!Armed) return;
        healthFailures = 0; stableHealthSuccesses = 0; disconnectSuspected = false;
        if (!recovering)
        {
            recovering = true; retryIndex = 0; reconnectAttempts = 0;
            recoveryStarted = now; recoveryDeadline = now + RecoveryWindow;
        }
        nextAttempt = now;
    }
    public bool CanReconnect(string? candidateDeviceId, DateTimeOffset now) =>
        Recovering && !RecoveryExhausted(now) &&
        string.Equals(DeviceId, candidateDeviceId, StringComparison.Ordinal) && now >= nextAttempt;
    public bool BeginReconnectAttempt(DateTimeOffset now)
    {
        if (!Recovering || RecoveryExhausted(now)) return false;
        reconnectAttempts++;
        return true;
    }
    public void ReconnectSucceeded(DateTimeOffset now)
    {
        if (!Recovering) return;
        healthFailures = 0; stableHealthSuccesses = 0; disconnectSuspected = false;
        nextHealth = now + HealthInterval; nextAttempt = default;
    }
    public bool ReconnectFailed(DateTimeOffset now)
    {
        if (!Recovering || RecoveryExhausted(now)) return true;
        stableHealthSuccesses = 0;
        var delay = RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)];
        if (retryIndex < RetryDelays.Length - 1) retryIndex++;
        nextAttempt = now + delay;
        return nextAttempt >= recoveryDeadline;
    }
    public bool RecoveryExhausted(DateTimeOffset now) => Recovering &&
        (reconnectAttempts >= MaximumReconnectAttempts || now >= recoveryDeadline);
    public long RecoveryElapsedMilliseconds(DateTimeOffset now) => !Recovering ? 0 :
        Math.Max(0, (long)(now - recoveryStarted).TotalMilliseconds);
}

internal static class EndpointRules
{
    internal static void Check(DeviceEndpoint endpoint)
    {
        if (!CandidateParser.TryManual(endpoint.Address, endpoint.Port, out var candidate) || candidate!.Endpoints[0] != endpoint)
            throw new ConnectionException("invalid-endpoint");
    }
    internal static void CheckCandidate(DeviceCandidate candidate, DeviceEndpoint endpoint)
    {
        Check(endpoint);
        if (candidate.Protocol != CandidateProtocol.PairedV3 || !candidate.Endpoints.Contains(endpoint))
            throw new ConnectionException("v3-required");
    }
}
