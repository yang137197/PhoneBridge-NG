using PhoneBridge.Discovery;

namespace PhoneBridge.Desktop;

internal enum MountSummaryKind { None, Starting, RecoveringWrites, Mounted, Stopping, StopFailed }
internal sealed record DeviceMountStatus(string DeviceId, string DisplayName, string? MountedDeviceId,
    PhoneBridge.Mounting.MountSnapshot Mount, char? Drive);
internal sealed record MountedDeviceSummary(string DeviceId, string DisplayName, char Drive);
internal sealed record MountSummary(MountSummaryKind Kind, IReadOnlyList<MountedDeviceSummary> Devices, string? ErrorCode = null);
internal enum DevicePresenceKind { NotFound, SavedAddress, Discovered, Connected }
internal sealed record DevicePresence(DevicePresenceKind Kind, IReadOnlyList<string> Addresses)
{
    internal bool CanConnect => Kind is not DevicePresenceKind.NotFound;
}

internal static class DeviceListPolicy
{
    internal static bool IncludeCandidate(bool pairingWindowOpen, bool hasSavedPairing) =>
        pairingWindowOpen || hasSavedPairing;

    internal static bool IsConnected(string? pairedDeviceId, string? mountedDeviceId) =>
        pairedDeviceId is not null && mountedDeviceId is not null &&
        string.Equals(pairedDeviceId, mountedDeviceId, StringComparison.Ordinal);

    internal static string StableRowId(string candidateId, string deviceId, bool hasSavedPairing) =>
        hasSavedPairing ? deviceId : candidateId;

    internal static DevicePresence ResolvePresence(bool connected, DeviceEndpoint? activeEndpoint,
        IEnumerable<DeviceEndpoint>? discovered, string? lastVerifiedAddress, int? lastVerifiedPort)
    {
        if (connected)
            return new(DevicePresenceKind.Connected,
                activeEndpoint is null ? [] : [activeEndpoint.Address]);
        var current = (discovered ?? []).Select(endpoint => endpoint.Address)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (current.Length > 0) return new(DevicePresenceKind.Discovered, current);
        return string.IsNullOrEmpty(lastVerifiedAddress) || lastVerifiedPort is null
            ? new(DevicePresenceKind.NotFound, [])
            : new(DevicePresenceKind.SavedAddress, [lastVerifiedAddress]);
    }

    internal static int CountRefreshedDevices(IEnumerable<string> discoveredKeys,
        IEnumerable<string> verifiedDeviceIds) => discoveredKeys.Concat(verifiedDeviceIds)
        .Distinct(StringComparer.Ordinal).Count();

    internal static MountSummary SummarizeMounts(IEnumerable<DeviceMountStatus> sessions)
    {
        var snapshots = sessions.ToArray();
        var mounted = snapshots
            .Where(item => item.Mount.State == PhoneBridge.Mounting.MountState.Mounted && item.Drive is not null &&
                IsConnected(item.DeviceId, item.MountedDeviceId))
            .GroupBy(item => item.DeviceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(item => new MountedDeviceSummary(item.DeviceId, item.DisplayName, item.Drive!.Value))
            .OrderBy(item => item.Drive)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToArray();
        var failed = snapshots.FirstOrDefault(item => item.Mount.State == PhoneBridge.Mounting.MountState.StopFailed);
        if (failed is not null) return new(MountSummaryKind.StopFailed, mounted, failed.Mount.ErrorCode);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.RecoveringWrites))
            return new(MountSummaryKind.RecoveringWrites, mounted);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.Stopping))
            return new(MountSummaryKind.Stopping, mounted);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.Starting))
            return new(MountSummaryKind.Starting, mounted);
        return mounted.Length == 0 ? new(MountSummaryKind.None, mounted) : new(MountSummaryKind.Mounted, mounted);
    }
}

internal static class FooterTextPolicy
{
    internal static string Connection(MountSummary summary, Func<string, string> text)
    {
        if (summary.Devices.Count == 0) return text("Ready");
        string names = string.Join(text("FooterNameSeparator"), summary.Devices.Select(item => item.DisplayName));
        return string.Format(text("ConnectedDevicesSummary"), names);
    }

    internal static string Mounts(MountSummary summary, Func<string, string> text)
    {
        if (summary.Devices.Count == 0) return text("NoMount");
        string items = string.Join(text("FooterItemSeparator"), summary.Devices.Select(item =>
            string.Format(text("MountedDeviceItem"), item.DisplayName, item.Drive)));
        return string.Format(text("MountedDevicesSummary"), items);
    }
}
