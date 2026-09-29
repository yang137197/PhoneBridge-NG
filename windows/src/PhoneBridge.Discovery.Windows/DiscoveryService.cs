using System.Net.NetworkInformation;
using System.Threading.Channels;
using System.Diagnostics;

namespace PhoneBridge.Discovery.Windows;

public sealed record DiscoveryRefreshResult(IReadOnlyList<string> DeviceKeys)
{
    public int DeviceCount => DeviceKeys.Count;
}

public sealed class DiscoveryService(Func<IDiscoveryWatcher>? watcherFactory = null, bool observeNetwork = true,
    TimeSpan? scanWindow = null,
    Func<DeviceCandidate, CancellationToken, Task<bool>>? verifyMissingCandidate = null)
{
    private readonly Func<IDiscoveryWatcher> factory = watcherFactory ?? (() => new WindowsDeviceWatcher());
    private readonly object refreshGate = new();
    private int running;
    private int refreshRequested;
    private TaskCompletionSource<DiscoveryRefreshResult>? refreshCompletion;
    private readonly TimeSpan window = scanWindow ?? TimeSpan.FromSeconds(12);

    public void RequestRefresh() => Interlocked.Exchange(ref refreshRequested, 1);

    public Task<DiscoveryRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Task<DiscoveryRefreshResult> refresh;
        lock (refreshGate)
        {
            if (Volatile.Read(ref running) == 0)
                return Task.FromException<DiscoveryRefreshResult>(new InvalidOperationException("discovery-not-running"));
            refreshCompletion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            refresh = refreshCompletion.Task;
            Interlocked.Exchange(ref refreshRequested, 1);
        }
        return cancellationToken.CanBeCanceled ? refresh.WaitAsync(cancellationToken) : refresh;
    }

    private TaskCompletionSource<DiscoveryRefreshResult>? CurrentRefresh()
    {
        lock (refreshGate) return refreshCompletion;
    }

    private void CompleteRefresh(TaskCompletionSource<DiscoveryRefreshResult> completion,
        DiscoveryRefreshResult result)
    {
        lock (refreshGate)
        {
            if (ReferenceEquals(refreshCompletion, completion)) refreshCompletion = null;
        }
        completion.TrySetResult(result);
    }

    private void FailRefresh(TaskCompletionSource<DiscoveryRefreshResult>? completion, Exception error)
    {
        TaskCompletionSource<DiscoveryRefreshResult>? pending;
        lock (refreshGate)
        {
            pending = refreshCompletion;
            refreshCompletion = null;
        }
        completion?.TrySetException(error);
        if (!ReferenceEquals(pending, completion)) pending?.TrySetException(error);
    }

    private void CancelRefresh(TaskCompletionSource<DiscoveryRefreshResult>? completion,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<DiscoveryRefreshResult>? pending;
        lock (refreshGate)
        {
            pending = refreshCompletion;
            refreshCompletion = null;
        }
        completion?.TrySetCanceled(cancellationToken);
        if (!ReferenceEquals(pending, completion)) pending?.TrySetCanceled(cancellationToken);
    }

    public async Task RunAsync(Action<DiscoveryChange> publish, CancellationToken cancellationToken,
        IReadOnlyList<DeviceEndpoint>? manual = null)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("discovery-already-running");
        var queue = Channel.CreateBounded<(int Generation, WatcherEvent Event)>(new BoundedChannelOptions(256)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var registry = new CandidateRegistry();
        var properties = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        IDiscoveryWatcher? watcher = null;
        Action<WatcherEvent>? handler = null;
        var generation = 0;
        var overflow = 0;
        var scanStarted = Stopwatch.GetTimestamp();
        var cleanupFailed = false;
        TaskCompletionSource<DiscoveryRefreshResult>? activeRefresh = null;
        void Emit(DiscoveryChange? change) { if (change is not null) publish(change); }
        void Clear(string reason, CandidateSource? source = null)
        {
            foreach (var change in registry.Clear(reason, source)) Emit(change);
            properties.Clear();
            observed.Clear();
        }
        void NetworkChanged(object? sender, EventArgs args) => RequestRefresh();
        async Task StopWatcher()
        {
            generation++; // queued callbacks from a previous watcher cannot restore removed devices
            if (watcher is null) return;
            var stopping = watcher;
            watcher = null;
            stopping.Changed -= handler;
            try { await stopping.StopAsync().ConfigureAwait(false); }
            catch { cleanupFailed = true; throw; }
        }
        void StartWatcher()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activeGeneration = ++generation;
            watcher = factory();
            handler = item =>
            {
                if (!queue.Writer.TryWrite((activeGeneration, item))) Interlocked.Exchange(ref overflow, 1);
            };
            watcher.Changed += handler;
            watcher.Start();
            scanStarted = Stopwatch.GetTimestamp();
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observeNetwork) NetworkChange.NetworkAddressChanged += NetworkChanged;
            foreach (var endpoint in manual ?? []) Emit(registry.AddManual(endpoint.Address, endpoint.Port));
            StartWatcher();
            Emit(new(DiscoveryChangeKind.Status, "", null, "watching"));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                bool refresh = Interlocked.Exchange(ref refreshRequested, 0) != 0;
                if (refresh || Interlocked.Exchange(ref overflow, 0) != 0)
                {
                    if (refresh) activeRefresh ??= CurrentRefresh();
                    await StopWatcher().ConfigureAwait(false);
                    properties.Clear();
                    observed.Clear();
                    while (queue.Reader.TryRead(out _)) { }
                    StartWatcher();
                    Emit(new(DiscoveryChangeKind.Status, "", null, "watching"));
                }
                // Bound work per tick so a flood cannot starve cancellation or network reset.
                for (var count = 0; count < 256 && queue.Reader.TryRead(out var item); count++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Generation != generation) continue;
                    var update = item.Event;
                    if (update.Kind == WatcherEventKind.Aborted) throw new InvalidOperationException("watcher-aborted");
                    if (update.Id.Length is 0 or > 1024) continue;
                    if (update.Kind == WatcherEventKind.Invalid)
                    {
                        properties.Remove(update.Id);
                        observed.Remove(CandidateParser.Key(CandidateSource.Mdns, update.Id));
                        Emit(registry.RemoveService(update.Id, "invalid-metadata"));
                        continue;
                    }
                    if (update.Kind == WatcherEventKind.Removed)
                    {
                        properties.Remove(update.Id);
                        var candidate = registry.FindService(update.Id);
                        if (candidate is not null)
                            observed.Remove(candidate.Id);
                        bool verified = false;
                        if (candidate is not null && verifyMissingCandidate is not null)
                        {
                            try { verified = await verifyMissingCandidate(candidate, cancellationToken).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                            catch { }
                        }
                        if (verified) observed.Add(candidate!.Id);
                        else Emit(registry.RemoveService(update.Id, "service-removed"));
                        continue;
                    }
                    if (update.Properties is null) continue;
                    if (!properties.TryGetValue(update.Id, out var state))
                    {
                        // Updated has only deltas; never create a new service from an unknown update.
                        if (update.Kind != WatcherEventKind.Added || properties.Count >= CandidateParser.MaxCandidates) continue;
                        properties.Add(update.Id, state = new(StringComparer.Ordinal));
                    }
                    if (update.Kind == WatcherEventKind.Added) state.Clear();
                    foreach (var field in update.Properties.Where(p => WatcherProperties.Requested.Contains(p.Key)))
                        state[field.Key] = field.Value;
                    var advertisement = WatcherProperties.Parse(update.Id, state);
                    if (advertisement is null) Emit(registry.RemoveService(update.Id, "incomplete-advertisement"));
                    else Emit(registry.Apply(advertisement));
                    if (advertisement is not null && CandidateParser.TryParse(advertisement, out var accepted, out _))
                        observed.Add(accepted!.Id);
                }
                if (Stopwatch.GetElapsedTime(scanStarted) >= window)
                {
                    await StopWatcher().ConfigureAwait(false);
                    if (verifyMissingCandidate is not null)
                    {
                        foreach (var candidate in registry.CandidatesRequiringVerification(observed))
                        {
                            bool verified = false;
                            try { verified = await verifyMissingCandidate(candidate, cancellationToken).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                            catch { }
                            if (verified) observed.Add(candidate.Id);
                        }
                    }
                    foreach (var change in registry.CompleteScan(observed)) Emit(change);
                    if (activeRefresh is { } completion)
                    {
                        string[] devices = registry.Snapshot
                            .Where(candidate => candidate.Source == CandidateSource.Mdns && observed.Contains(candidate.Id))
                            .Select(candidate => candidate.DeviceIdHint ?? candidate.Id)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray();
                        CompleteRefresh(completion, new(devices));
                        activeRefresh = null;
                    }
                    properties.Clear();
                    observed.Clear();
                    while (queue.Reader.TryRead(out _)) { }
                    StartWatcher();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelRefresh(activeRefresh, cancellationToken);
            activeRefresh = null;
        }
        catch (Exception error)
        {
            FailRefresh(activeRefresh, error);
            activeRefresh = null;
            throw;
        }
        finally
        {
            if (activeRefresh is not null) CancelRefresh(activeRefresh, cancellationToken);
            if (observeNetwork) NetworkChange.NetworkAddressChanged -= NetworkChanged;
            try { await StopWatcher().ConfigureAwait(false); }
            finally
            {
                queue.Writer.TryComplete();
                try
                {
                    Clear("discovery-stopped");
                    Emit(new(DiscoveryChangeKind.Status, "", null, cleanupFailed ? "stop-failed" : "stopped"));
                }
                finally { Interlocked.Exchange(ref running, 0); }
            }
        }
    }
}
