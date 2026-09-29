using System.Collections.Concurrent;
using PhoneBridge.Discovery.Windows;

namespace PhoneBridge.Discovery.Tests;

[TestClass]
public sealed class ServiceTests
{
    internal static Dictionary<string, object> Properties() => new()
    {
        [WatcherProperties.InstanceName] = "Phone", [WatcherProperties.ServiceName] = "_phonebridge._tcp",
        [WatcherProperties.Domain] = "local", [WatcherProperties.Port] = (ushort)8273,
        [WatcherProperties.Addresses] = new[] { "192.168.1.10" },
        [WatcherProperties.Text] = new[] { "protocol=https", "version=2", "deviceName=Phone" }
    };

    internal static Dictionary<string, object> V3Properties() => new()
    {
        [WatcherProperties.InstanceName] = "Phone", [WatcherProperties.ServiceName] = "_phonebridge._tcp",
        [WatcherProperties.Domain] = "local", [WatcherProperties.Port] = (ushort)8273,
        [WatcherProperties.Addresses] = new[] { "192.168.1.10" },
        [WatcherProperties.Text] = new[]
        {
            "protocol=https", "version=3", "deviceName=Phone", "auth=paired-v1",
            "device_id=pbng-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
    };

    private sealed class FakeWatcher : IDiscoveryWatcher
    {
        public event Action<WatcherEvent>? Changed;
        public Action<WatcherEvent>? CapturedHandler { get; private set; }
        public int Stops { get; private set; }
        public bool EmitOnStart { get; init; }
        public bool FailStop { get; init; }
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public void Start()
        {
            CapturedHandler = Changed;
            if (EmitOnStart) Emit(WatcherEventKind.Added, properties: Properties());
        }
        public Task StopAsync()
        {
            Stops++;
            return FailStop ? Task.FromException(new TimeoutException("test-stop-timeout")) : Task.CompletedTask;
        }
        public void Emit(WatcherEventKind kind, string id = "service", IReadOnlyDictionary<string, object>? properties = null) =>
            Changed?.Invoke(new(kind, id, properties));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    [TestMethod]
    public async Task UpdatesUseDeltasAndRemovedCannotBeResurrectedByLateUpdate()
    {
        var watcher = new FakeWatcher();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource();
        var service = new DiscoveryService(() => watcher, observeNetwork: false);
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        watcher.Emit(WatcherEventKind.Added, properties: Properties());
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Added));
        watcher.Emit(WatcherEventKind.Updated, properties: new Dictionary<string, object> { [WatcherProperties.Addresses] = new[] { "192.168.1.11" } });
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Updated));
        Assert.AreEqual("192.168.1.11", changes.Last(c => c.Kind == DiscoveryChangeKind.Updated).Candidate!.Endpoints.Single().Address);
        watcher.Emit(WatcherEventKind.Removed);
        watcher.Emit(WatcherEventKind.Updated, properties: Properties());
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Removed));
        await stop.CancelAsync(); await run;
        Assert.HasCount(1, changes.Where(c => c.Kind == DiscoveryChangeKind.Added));
        Assert.AreEqual(1, watcher.Stops);
        Assert.AreEqual(0, watcher.Subscribers);
    }

    [TestMethod]
    public async Task NativeRemovedEventRetainsAuthenticatedReachableCandidate()
    {
        var watcher = new FakeWatcher();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        var allow = 1;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() => watcher, false, TimeSpan.FromSeconds(2), (_, _) =>
            Task.FromResult(Volatile.Read(ref allow) == 1));
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        watcher.Emit(WatcherEventKind.Added, properties: Properties());
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Added));

        watcher.Emit(WatcherEventKind.Removed);
        await Task.Delay(100);
        Assert.IsFalse(changes.Any(c => c.Kind == DiscoveryChangeKind.Removed));

        Volatile.Write(ref allow, 0);
        watcher.Emit(WatcherEventKind.Removed);
        await WaitUntil(() => changes.Any(c => c.Reason == "service-removed"));
        await stop.CancelAsync(); await run;
        Assert.HasCount(1, changes.Where(c => c.Kind == DiscoveryChangeKind.Removed && c.Reason == "service-removed"));
    }

    [TestMethod]
    public async Task NetworkRefreshStopsOldWatcherAndIgnoresItsQueuedCallbacks()
    {
        var watchers = new ConcurrentQueue<FakeWatcher>();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource();
        var service = new DiscoveryService(() => { var w = new FakeWatcher(); watchers.Enqueue(w); return w; }, false);
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        var first = watchers.First();
        first.Emit(WatcherEventKind.Added, properties: Properties());
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Added));
        service.RequestRefresh();
        await WaitUntil(() => watchers.Count == 2);
        first.CapturedHandler!(new(WatcherEventKind.Added, "stale", Properties()));
        watchers.Last().Emit(WatcherEventKind.Added, "current", Properties());
        await WaitUntil(() => changes.Count(c => c.Kind == DiscoveryChangeKind.Added) == 2);
        await stop.CancelAsync(); await run;
        Assert.IsFalse(changes.Any(c => c.Reason == "network-refresh"));
        Assert.IsFalse(changes.Any(c => c.Id == CandidateParser.Key(CandidateSource.Mdns, "stale")));
        Assert.IsTrue(watchers.All(w => w.Stops == 1 && w.Subscribers == 0));
        Assert.AreEqual("stopped", changes.Last().Reason);
    }

    [TestMethod]
    public async Task ManualRefreshCompletesAfterScanAndReportsDistinctDevices()
    {
        var watchers = new ConcurrentQueue<FakeWatcher>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() =>
        {
            var watcher = new FakeWatcher();
            watchers.Enqueue(watcher);
            return watcher;
        }, false, TimeSpan.FromMilliseconds(300));
        var run = service.RunAsync(_ => { }, stop.Token);
        await WaitUntil(() => watchers.Count >= 1);

        Task<DiscoveryRefreshResult> first = service.RefreshAsync();
        Task<DiscoveryRefreshResult> duplicate = service.RefreshAsync();
        await WaitUntil(() => watchers.Count >= 2);
        watchers.Last().Emit(WatcherEventKind.Added, "service-a", V3Properties());
        watchers.Last().Emit(WatcherEventKind.Added, "service-b", V3Properties());
        DiscoveryRefreshResult result = await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreSame(first, duplicate);
        Assert.AreEqual(1, result.DeviceCount);
        CollectionAssert.AreEqual(new[] { "pbng-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
            result.DeviceKeys.ToArray());
        Assert.IsGreaterThanOrEqualTo(2, watchers.Count);
        await stop.CancelAsync();
        await run;
    }

    [TestMethod]
    public async Task ManualRefreshReportsNoDevicesOnlyAfterEmptyScanCompletes()
    {
        var watchers = new ConcurrentQueue<FakeWatcher>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() =>
        {
            var watcher = new FakeWatcher();
            watchers.Enqueue(watcher);
            return watcher;
        }, false, TimeSpan.FromMilliseconds(200));
        var run = service.RunAsync(_ => { }, stop.Token);
        await WaitUntil(() => watchers.Count >= 1);

        DiscoveryRefreshResult result = await service.RefreshAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(0, result.DeviceCount);
        Assert.IsGreaterThanOrEqualTo(2, watchers.Count);
        await stop.CancelAsync();
        await run;
    }

    [TestMethod]
    public async Task ManualRefreshReportsWatcherFailureInsteadOfSuccess()
    {
        var watcher = new FakeWatcher { FailStop = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() => watcher, false);
        Task run = service.RunAsync(_ => { }, stop.Token);

        Task<DiscoveryRefreshResult> refresh = service.RefreshAsync(stop.Token);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => refresh);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => run);
    }

    [TestMethod]
    public async Task ManualRefreshFailsWhenDiscoveryIsNotRunning()
    {
        var service = new DiscoveryService(() => new FakeWatcher(), false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RefreshAsync());
    }

    [TestMethod]
    public async Task CancellationRemovesCandidatesAndDetachesEvenWithQueuedTraffic()
    {
        var watcher = new FakeWatcher();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource();
        var service = new DiscoveryService(() => watcher, false);
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        watcher.Emit(WatcherEventKind.Added, properties: Properties());
        await WaitUntil(() => changes.Any(c => c.Kind == DiscoveryChangeKind.Added));
        for (var i = 0; i < 300; i++) watcher.Emit(WatcherEventKind.Updated, properties: Properties());
        await stop.CancelAsync(); await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, watcher.Stops);
        Assert.AreEqual(0, watcher.Subscribers);
        Assert.IsTrue(changes.Any(c => c.Kind == DiscoveryChangeKind.Removed && c.Reason == "discovery-stopped"));
    }

    [TestMethod]
    public async Task AbortedWatcherFailsAndStillCleansUp()
    {
        var watcher = new FakeWatcher();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = new DiscoveryService(() => watcher, false).RunAsync(changes.Enqueue, stop.Token);
        watcher.Emit(WatcherEventKind.Aborted);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run);
        Assert.AreEqual(1, watcher.Stops);
        Assert.AreEqual(0, watcher.Subscribers);
        Assert.AreEqual("stopped", changes.Last().Reason);
    }

    [TestMethod]
    public async Task AlreadyCancelledDoesNotStartNetworkWatcher()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var service = new DiscoveryService(() => throw new AssertFailedException("factory must not run"), false);
        await service.RunAsync(_ => { }, stop.Token);
    }

    [TestMethod]
    public async Task PeriodicScansKeepLiveCandidateWithoutFlickerAndReleaseEveryWatcher()
    {
        var watchers = new ConcurrentQueue<FakeWatcher>();
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource();
        var service = new DiscoveryService(() => { var w = new FakeWatcher { EmitOnStart = true }; watchers.Enqueue(w); return w; },
            false, TimeSpan.FromMilliseconds(200));
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        await WaitUntil(() => watchers.Count >= 4);
        Assert.HasCount(1, changes.Where(c => c.Kind == DiscoveryChangeKind.Added));
        Assert.IsFalse(changes.Any(c => c.Kind == DiscoveryChangeKind.Removed));
        await stop.CancelAsync(); await run;
        Assert.IsTrue(watchers.All(w => w.Stops == 1 && w.Subscribers == 0));
    }

    [TestMethod]
    public async Task PeriodicScansRemoveMissingCandidateWithoutNativeRemovedEvent()
    {
        var count = 0;
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() => new FakeWatcher { EmitOnStart = count++ == 0 },
            false, TimeSpan.FromMilliseconds(200));
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        await WaitUntil(() => changes.Any(c => c.Reason == "not-rediscovered"));
        await stop.CancelAsync(); await run;
        Assert.HasCount(1, changes.Where(c => c.Kind == DiscoveryChangeKind.Removed));
    }

    [TestMethod]
    public async Task PeriodicScansRetainAuthenticatedMissingCandidateUntilVerificationFails()
    {
        var count = 0;
        var verificationCount = 0;
        var allow = 1;
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new DiscoveryService(() => new FakeWatcher { EmitOnStart = count++ == 0 },
            false, TimeSpan.FromMilliseconds(200), (_, _) =>
            {
                Interlocked.Increment(ref verificationCount);
                return Task.FromResult(Volatile.Read(ref allow) == 1);
            });
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        await WaitUntil(() => Volatile.Read(ref verificationCount) >= 1);
        Assert.IsFalse(changes.Any(c => c.Kind == DiscoveryChangeKind.Removed));

        Volatile.Write(ref allow, 0);
        await WaitUntil(() => changes.Any(c => c.Reason == "not-rediscovered"));
        await stop.CancelAsync(); await run;
        Assert.HasCount(1, changes.Where(c => c.Kind == DiscoveryChangeKind.Removed && c.Reason == "not-rediscovered"));
    }

    [TestMethod]
    public async Task StopFailureIsReportedWithoutClaimingSuccessfulShutdown()
    {
        var watcher = new FakeWatcher { FailStop = true };
        var changes = new ConcurrentQueue<DiscoveryChange>();
        using var stop = new CancellationTokenSource();
        var service = new DiscoveryService(() => watcher, false);
        var run = service.RunAsync(changes.Enqueue, stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => run);
        Assert.AreEqual("stop-failed", changes.Last().Reason);
        Assert.AreEqual(0, watcher.Subscribers);
    }
}
