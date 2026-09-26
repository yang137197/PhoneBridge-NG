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
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)
    ];
    private int healthFailures, retryIndex;
    private DateTimeOffset nextHealth, nextAttempt;
    public string? DeviceId { get; private set; }
    public MountOptions? Options { get; private set; }
    public bool Armed => DeviceId is not null && Options is not null;
    public int ConsecutiveHealthFailures => healthFailures;
    public DateTimeOffset NextAttempt => nextAttempt;

    public void Arm(string deviceId, MountOptions options, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("device id required", nameof(deviceId));
        DeviceId = deviceId; Options = options;
        healthFailures = 0; retryIndex = 0;
        nextHealth = now + HealthInterval; nextAttempt = now;
    }
    public void Suppress()
    {
        DeviceId = null; Options = null;
        healthFailures = 0; retryIndex = 0;
        nextHealth = nextAttempt = default;
    }
    public bool HealthDue(DateTimeOffset now) => Armed && now >= nextHealth;
    public void HealthSucceeded(DateTimeOffset now)
    {
        if (!Armed) return;
        healthFailures = 0; nextHealth = now + HealthInterval;
    }
    public bool HealthFailed(DateTimeOffset now)
    {
        if (!Armed) return false;
        healthFailures++;
        nextHealth = now + HealthInterval;
        return healthFailures >= 3;
    }
    public void Disconnected(DateTimeOffset now)
    {
        if (!Armed) return;
        healthFailures = 0; retryIndex = 0; nextAttempt = now;
    }
    public bool CanReconnect(string? candidateDeviceId, DateTimeOffset now) =>
        Armed && string.Equals(DeviceId, candidateDeviceId, StringComparison.Ordinal) && now >= nextAttempt;
    public void ReconnectFailed(DateTimeOffset now)
    {
        if (!Armed) return;
        var delay = RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)];
        if (retryIndex < RetryDelays.Length - 1) retryIndex++;
        nextAttempt = now + delay;
    }
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
