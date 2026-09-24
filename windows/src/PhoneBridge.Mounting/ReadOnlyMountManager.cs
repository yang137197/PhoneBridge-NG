namespace PhoneBridge.Mounting;

public enum MountState { Idle, Starting, RecoveringWrites, Mounted, Stopping, Stopped, Failed, StopFailed }
public sealed record MountSnapshot(MountState State, int? ProcessId = null, int? ExitCode = null,
    string? ErrorCode = null, bool Forced = false, long RecoveryBytes = 0);

internal enum MountReadiness { Pending, RecoveringWrites, Ready }
internal readonly record struct MountProbe(MountReadiness Readiness, long RecoveryBytes = 0);

internal interface IMountSession : IAsyncDisposable
{
    int ProcessId { get; }
    Task<int> Exit { get; }
    bool IsDrivePresent { get; }
    bool ForceStopSafe { get; }
    Task<MountProbe> ProbeAsync(CancellationToken cancellationToken);
    Task RequestStopAsync(CancellationToken cancellationToken);
    void KillOwnedProcess();
}

internal sealed record MountTimeouts(TimeSpan Startup, TimeSpan RecoveryIdle, TimeSpan RecoveryMaximum,
    TimeSpan GracefulStop, TimeSpan ForcedStop, TimeSpan DriveRemoval)
{
    public static MountTimeouts Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
        TimeSpan.FromHours(24), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
}

public sealed class ReadOnlyMountManager : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Func<MountRequest, CancellationToken, Task<IMountSession>> startSession;
    private readonly MountTimeouts timeouts;
    private MountSnapshot snapshot = new(MountState.Idle);
    private Task run = Task.CompletedTask;
    private TaskCompletionSource<MountSnapshot>? ready;
    private TaskCompletionSource? stopSignal;
    private CancellationTokenSource? startupStop;
    private IMountSession? retained;
    private bool disposed;

    public ReadOnlyMountManager() : this(RcloneMountSession.StartAsync, MountTimeouts.Default) { }
    internal ReadOnlyMountManager(Func<MountRequest, CancellationToken, Task<IMountSession>> startSession, MountTimeouts timeouts)
    { this.startSession = startSession; this.timeouts = timeouts; }
    public MountSnapshot Snapshot { get { lock (sync) return snapshot; } }

    public Task<MountSnapshot> StartAsync(MountRequest request, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!run.IsCompleted || snapshot.State == MountState.StopFailed) throw new MountException("mount-already-active");
            cancellationToken.ThrowIfCancellationRequested();
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            startupStop = new();
            snapshot = new(MountState.Starting);
            run = Task.Run(() => RunAsync(request, cancellationToken));
            return ready.Task;
        }
    }

    public async Task<MountSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (sync)
        {
            stopSignal?.TrySetResult();
            startupStop?.Cancel();
            if (run.IsCompleted && retained is not null)
            {
                var previous = retained;
                run = Task.Run(async () => { await CleanupAsync(previous, null).ConfigureAwait(false); });
            }
            pending = run;
        }
        // Cancelling the wait does not cancel ownership cleanup.
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Snapshot;
    }

    private void Set(MountSnapshot value) { lock (sync) snapshot = value; }

    private async Task RunAsync(MountRequest request, CancellationToken callerCancellation)
    {
        IMountSession? session = null;
        string? failure = null;
        bool retainRecovery = false, recovering = false;
        long recoveryBytes = 0;
        var startupElapsed = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Stopwatch? recoveryElapsed = null, recoveryIdle = null;
        try
        {
            using (var deadline = new CancellationTokenSource(timeouts.Startup))
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, startupStop!.Token, deadline.Token))
                session = await startSession(request, startup.Token).ConfigureAwait(false);
            Set(new(MountState.Starting, session.ProcessId));
            while (true)
            {
                callerCancellation.ThrowIfCancellationRequested();
                startupStop!.Token.ThrowIfCancellationRequested();
                if (session.Exit.IsCompleted) throw new MountException("rclone-exited-before-ready");
                TimeSpan remaining;
                if (recovering)
                {
                    remaining = Min(timeouts.RecoveryIdle - recoveryIdle!.Elapsed,
                        timeouts.RecoveryMaximum - recoveryElapsed!.Elapsed);
                    if (remaining <= TimeSpan.Zero)
                        throw new MountException(recoveryIdle.Elapsed >= timeouts.RecoveryIdle
                            ? "recovery-stalled-cache-retained" : "recovery-timeout-cache-retained");
                }
                else
                {
                    remaining = timeouts.Startup - startupElapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero) throw new MountException("startup-timeout");
                }

                using var deadline = new CancellationTokenSource(remaining);
                using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellation, startupStop.Token, deadline.Token);
                MountProbe probe;
                try { probe = await session.ProbeAsync(probeCancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
                    !callerCancellation.IsCancellationRequested && !startupStop.Token.IsCancellationRequested)
                {
                    throw new MountException(recovering
                        ? (recoveryIdle!.Elapsed >= timeouts.RecoveryIdle
                            ? "recovery-stalled-cache-retained" : "recovery-timeout-cache-retained")
                        : "startup-timeout");
                }
                if (probe.Readiness == MountReadiness.Ready) break;
                if (probe.Readiness == MountReadiness.RecoveringWrites)
                {
                    var enteredRecovery = !recovering;
                    if (!recovering)
                    {
                        recovering = true;
                        recoveryBytes = probe.RecoveryBytes;
                        recoveryElapsed = System.Diagnostics.Stopwatch.StartNew();
                        recoveryIdle = System.Diagnostics.Stopwatch.StartNew();
                    }
                    else if (probe.RecoveryBytes > recoveryBytes)
                    {
                        recoveryBytes = probe.RecoveryBytes;
                        recoveryIdle!.Restart();
                    }
                    Set(new(MountState.RecoveringWrites, session.ProcessId, RecoveryBytes: recoveryBytes));
                    // The current deadline belongs to startup. Re-probe immediately under the
                    // recovery deadlines instead of letting that old token cancel recovery.
                    if (enteredRecovery) continue;
                }
                try { await Task.Delay(100, probeCancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
                    !callerCancellation.IsCancellationRequested && !startupStop.Token.IsCancellationRequested)
                {
                    throw new MountException(recovering
                        ? (recoveryIdle!.Elapsed >= timeouts.RecoveryIdle
                            ? "recovery-stalled-cache-retained" : "recovery-timeout-cache-retained")
                        : "startup-timeout");
                }
            }
            callerCancellation.ThrowIfCancellationRequested();
            startupStop!.Token.ThrowIfCancellationRequested();
            Set(new(MountState.Mounted, session.ProcessId));
            ready!.TrySetResult(Snapshot);
            await Task.WhenAny(session.Exit, stopSignal!.Task).ConfigureAwait(false);
            if (!stopSignal.Task.IsCompleted) failure = "rclone-unexpected-exit";
        }
        catch (OperationCanceledException)
        {
            failure = stopSignal!.Task.IsCompleted ? "start-stopped" : callerCancellation.IsCancellationRequested
                ? "start-cancelled" : "startup-timeout";
            if (recovering && callerCancellation.IsCancellationRequested)
            {
                failure = "recovery-cancelled-cache-retained";
                retainRecovery = true;
            }
        }
        catch (MountException e)
        {
            failure = e.Code;
            retainRecovery = recovering && e.Code is "recovery-stalled-cache-retained" or "recovery-timeout-cache-retained";
        }
        catch { failure = "mount-start-failed"; }
        finally
        {
            if (session is null) Set(new(stopSignal!.Task.IsCompleted ? MountState.Stopped : MountState.Failed, ErrorCode: failure));
            else if (retainRecovery)
            {
                lock (sync) retained = session;
                Set(new(MountState.StopFailed, session.ProcessId, ErrorCode: failure, RecoveryBytes: recoveryBytes));
            }
            else await CleanupAsync(session, failure).ConfigureAwait(false);
            ready!.TrySetException(new MountException(Snapshot.ErrorCode ?? failure ?? "mount-not-ready"));
            lock (sync) { startupStop?.Dispose(); startupStop = null; }
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private async Task CleanupAsync(IMountSession session, string? failure)
    {
        var forced = Snapshot.Forced;
        Set(new(MountState.Stopping, session.ProcessId, ErrorCode: failure, Forced: forced));
        try
        {
            if (!session.Exit.IsCompleted)
            {
                using var graceful = new CancellationTokenSource(timeouts.GracefulStop);
                try
                {
                    await session.RequestStopAsync(graceful.Token).ConfigureAwait(false);
                    await session.Exit.WaitAsync(graceful.Token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    if (!session.Exit.IsCompleted)
                    {
                        if (!session.ForceStopSafe)
                        {
                            lock (sync) retained = session;
                            Set(new(MountState.StopFailed, session.ProcessId,
                                ErrorCode: error is MountException mount ? mount.Code : "pending-writes-not-confirmed", Forced: forced));
                            return;
                        }
                        forced = true;
                        session.KillOwnedProcess();
                        await session.Exit.WaitAsync(timeouts.ForcedStop).ConfigureAwait(false);
                    }
                }
            }
            var exitCode = await session.Exit.ConfigureAwait(false);
            using var removal = new CancellationTokenSource(timeouts.DriveRemoval);
            while (session.IsDrivePresent) await Task.Delay(50, removal.Token).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            lock (sync) retained = null;
            Set(new(failure is null || failure == "start-stopped" ? MountState.Stopped : MountState.Failed,
                session.ProcessId, exitCode, failure, forced));
        }
        catch
        {
            lock (sync) retained = session;
            Set(new(MountState.StopFailed, session.ProcessId, ErrorCode: "unmount-not-confirmed", Forced: forced));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync) disposed = true;
        var final = await StopAsync().ConfigureAwait(false);
        if (final.State == MountState.StopFailed) throw new MountException("unmount-not-confirmed");
    }
}
