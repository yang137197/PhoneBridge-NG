using System.Security.Cryptography;
using System.Text;
using PhoneBridge.Credentials;
using PhoneBridge.Discovery;
using PhoneBridge.Mounting;

namespace PhoneBridge.Connection;

/// <summary>Runtime state owned by exactly one device identity.</summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly object operationGate = new();
    private CancellationTokenSource? operationCancellation;
    private Task operation = Task.CompletedTask;
    private int supervisorBusy;

    internal DeviceSession(string deviceId, int logContext, ConnectionClient client)
    {
        DeviceId = deviceId;
        LogContext = logContext;
        Client = client;
    }

    public string DeviceId { get; }
    public int LogContext { get; }
    public ConnectionClient Client { get; }
    public ReconnectPolicy Reconnect { get; } = new();
    public char? ReservedDrive { get; internal set; }
    public bool OperationInProgress { get { lock (operationGate) return operationCancellation is not null; } }
    public bool SupervisorBusy => Volatile.Read(ref supervisorBusy) != 0;

    public Task StartOperation(Func<CancellationToken, Task> work, CancellationToken applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (operationGate)
        {
            if (operationCancellation is not null) throw new ConnectionException("operation-in-progress");
            var owned = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime);
            operationCancellation = owned;
            operation = RunOwnedAsync(work, owned);
            return operation;
        }
    }

    private async Task RunOwnedAsync(Func<CancellationToken, Task> work, CancellationTokenSource owned)
    {
        // Let StartOperation publish the task and cancellation owner before completion can run.
        await Task.Yield();
        try { await work(owned.Token).ConfigureAwait(false); }
        finally
        {
            lock (operationGate)
            {
                if (ReferenceEquals(operationCancellation, owned)) operationCancellation = null;
            }
            owned.Dispose();
        }
    }

    public void CancelOperation()
    {
        lock (operationGate) operationCancellation?.Cancel();
    }

    public Task WaitForOperationAsync()
    {
        lock (operationGate) return operation;
    }

    public bool TryEnterSupervisor() => Interlocked.CompareExchange(ref supervisorBusy, 1, 0) == 0;
    public void ExitSupervisor() => Volatile.Write(ref supervisorBusy, 0);

    public async ValueTask DisposeAsync()
    {
        CancelOperation();
        try { await WaitForOperationAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await Client.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Coordinates session membership, drive conflicts and application shutdown only.</summary>
public sealed class DeviceSessionCoordinator(PairingStore store) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, DeviceSession> sessions = new(StringComparer.Ordinal);
    private int nextLogContext;
    private bool disposed;

    public IReadOnlyList<DeviceSession> Sessions
    {
        get { lock (gate) return sessions.Values.OrderBy(item => item.LogContext).ToArray(); }
    }

    public DeviceSession GetOrCreate(string deviceId)
    {
        ValidateDeviceId(deviceId);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sessions.TryGetValue(deviceId, out var existing)) return existing;
            var created = new DeviceSession(deviceId, checked(++nextLogContext), new ConnectionClient(store, deviceId));
            sessions.Add(deviceId, created);
            return created;
        }
    }

    public bool TryGet(string deviceId, out DeviceSession? session)
    {
        lock (gate) return sessions.TryGetValue(deviceId, out session);
    }

    public Task<IReadOnlyList<PairingRecord>> RecordsAsync() => Task.Run(store.List);

    public Task<PairingRecord> UpdateDeviceNoteAsync(string deviceId, string note) =>
        GetOrCreate(deviceId).Client.UpdateDeviceNoteAsync(deviceId, note);

    public async Task<PairingRecord> ConnectAsync(string deviceId, DeviceEndpoint endpoint, MountOptions options,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken)
    {
        var session = GetOrCreate(deviceId);
        ReserveDrive(session, options.DriveLetter);
        try
        {
            return await session.Client.ConnectAsync(deviceId, endpoint, options, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!OwnsMount(session.Client.Mount) && session.Client.Connected is null) ReleaseDrive(session);
            throw;
        }
    }

    public async Task StopAsync(string deviceId, bool preserveDriveReservation = false)
    {
        var session = GetOrCreate(deviceId);
        await session.Client.StopAsync().ConfigureAwait(false);
        if (!preserveDriveReservation) ReleaseDrive(session);
    }

    public async Task<bool> RemoveLocallyAsync(string deviceId)
    {
        var session = GetOrCreate(deviceId);
        bool removed = await session.Client.RemoveLocallyAsync(deviceId).ConfigureAwait(false);
        ReleaseDrive(session);
        session.Reconnect.Suppress();
        return removed;
    }

    public bool CanUseDrive(string? deviceId, char driveLetter)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        lock (gate)
            return sessions.Values.All(item => item.ReservedDrive != driveLetter ||
                string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
    }

    public string SessionRoot(string root, string deviceId)
    {
        ValidateDeviceId(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)));
        return Path.Combine(root, key);
    }

    internal void ReserveDrive(DeviceSession session, char driveLetter)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (driveLetter is < 'D' or > 'Z') throw new ConnectionException("drive-occupied");
        lock (gate)
        {
            if (session.ReservedDrive is { } current && current != driveLetter)
                throw new ConnectionException("drive-reserved");
            if (sessions.Values.Any(item => !ReferenceEquals(item, session) && item.ReservedDrive == driveLetter))
                throw new ConnectionException("drive-reserved");
            session.ReservedDrive = driveLetter;
        }
    }

    internal void ReleaseDrive(DeviceSession session)
    {
        lock (gate) session.ReservedDrive = null;
    }

    public async Task StopAllAsync()
    {
        var snapshot = Sessions;
        foreach (var session in snapshot)
        {
            session.Reconnect.Suppress();
            session.CancelOperation();
        }
        await Task.WhenAll(snapshot.Select(async session =>
        {
            try { await session.WaitForOperationAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { /* Stop is still required even when the user operation failed. */ }
        })).ConfigureAwait(false);

        var failures = new List<Exception>();
        await Task.WhenAll(snapshot.Select(async session =>
        {
            try
            {
                await session.Client.StopAsync().ConfigureAwait(false);
                ReleaseDrive(session);
            }
            catch (Exception error)
            {
                lock (failures) failures.Add(error);
            }
        })).ConfigureAwait(false);
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        var snapshot = Sessions;
        foreach (var session in snapshot) await session.Client.DisposeAsync().ConfigureAwait(false);
        lock (gate) disposed = true;
    }

    private static bool OwnsMount(MountSnapshot snapshot) => snapshot.State is
        MountState.Starting or MountState.RecoveringWrites or MountState.Mounted or MountState.Stopping or MountState.StopFailed;

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 || deviceId.Any(char.IsControl))
            throw new ArgumentException("device id required", nameof(deviceId));
    }
}
