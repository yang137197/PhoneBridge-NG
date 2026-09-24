namespace PhoneBridge.Desktop;

internal enum TrayStatus { Offline, Discovered, Connecting, Mounted, Error }
internal enum WindowCloseAction { Hide, Exit }

internal static class TrayPolicy
{
    internal static TrayStatus ResolveStatus(bool error, bool mounted, bool busy, bool discovered)
    {
        if (error) return TrayStatus.Error;
        if (mounted) return TrayStatus.Mounted;
        if (busy) return TrayStatus.Connecting;
        return discovered ? TrayStatus.Discovered : TrayStatus.Offline;
    }

    internal static WindowCloseAction ResolveClose(bool trayAvailable, bool exitRequested) =>
        trayAvailable && !exitRequested ? WindowCloseAction.Hide : WindowCloseAction.Exit;
}
