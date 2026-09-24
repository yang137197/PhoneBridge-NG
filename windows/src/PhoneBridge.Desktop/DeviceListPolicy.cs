namespace PhoneBridge.Desktop;

internal static class DeviceListPolicy
{
    internal static bool IncludeCandidate(bool pairingWindowOpen, bool hasSavedPairing) =>
        pairingWindowOpen || hasSavedPairing;

    internal static bool IsConnected(string? pairedDeviceId, string? mountedDeviceId) =>
        pairedDeviceId is not null && mountedDeviceId is not null &&
        string.Equals(pairedDeviceId, mountedDeviceId, StringComparison.Ordinal);
}
