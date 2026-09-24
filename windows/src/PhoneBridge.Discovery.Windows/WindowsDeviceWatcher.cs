using global::Windows.Devices.Enumeration;

namespace PhoneBridge.Discovery.Windows;

public enum WatcherEventKind { Added, Updated, Removed, Invalid, Aborted }
public sealed record WatcherEvent(WatcherEventKind Kind, string Id, IReadOnlyDictionary<string, object>? Properties = null);

public interface IDiscoveryWatcher
{
    event Action<WatcherEvent>? Changed;
    void Start();
    Task StopAsync();
}

public sealed class WindowsDeviceWatcher : IDiscoveryWatcher
{
    private const string Selector = "System.Devices.AepService.ProtocolId:=\"{4526e8c1-8aac-4153-9b16-55e86ada0e54}\" " +
        "AND System.Devices.Dnssd.Domain:=\"local\" AND System.Devices.Dnssd.ServiceName:=\"_phonebridge._tcp\"";
    private readonly DeviceWatcher watcher = DeviceInformation.CreateWatcher(Selector,
        WatcherProperties.Requested, DeviceInformationKind.AssociationEndpointService);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started;
    public event Action<WatcherEvent>? Changed;

    public void Start()
    {
        if (started) throw new InvalidOperationException("watcher-already-started");
        watcher.Added += Added;
        watcher.Updated += Updated;
        watcher.Removed += Removed;
        watcher.Stopped += Stopped;
        try { watcher.Start(); started = true; }
        catch { Detach(); throw; }
    }

    private void Added(DeviceWatcher sender, DeviceInformation item) =>
        Publish(WatcherEventKind.Added, item.Id, item.Properties);
    private void Updated(DeviceWatcher sender, DeviceInformationUpdate item) =>
        Publish(WatcherEventKind.Updated, item.Id, item.Properties);
    private void Removed(DeviceWatcher sender, DeviceInformationUpdate item) =>
        Changed?.Invoke(new(WatcherEventKind.Removed, item.Id));

    private void Publish(WatcherEventKind kind, string id, IReadOnlyDictionary<string, object> properties)
    {
        // Keep only requested discovery metadata. Never copy arbitrary OS properties into logs/state.
        var selected = properties.Where(p => WatcherProperties.Requested.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        // Reject large metadata before retaining it in the managed queue.
        Changed?.Invoke(WatcherProperties.AreBounded(selected) ? new(kind, id, selected) :
            new(WatcherEventKind.Invalid, id));
    }

    private void Stopped(DeviceWatcher sender, object args)
    {
        if (sender.Status == DeviceWatcherStatus.Aborted) Changed?.Invoke(new(WatcherEventKind.Aborted, ""));
        stopped.TrySetResult();
    }

    public async Task StopAsync()
    {
        try
        {
            if (started)
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
                if (watcher.Status is DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted) stopped.TrySetResult();
                // Stop is asynchronous. Do not start a replacement until its completion is observed.
                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        finally { Detach(); }
    }

    private void Detach()
    {
        watcher.Added -= Added;
        watcher.Updated -= Updated;
        watcher.Removed -= Removed;
        watcher.Stopped -= Stopped;
    }
}
