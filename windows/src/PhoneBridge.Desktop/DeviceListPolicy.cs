namespace PhoneBridge.Desktop;

internal enum MountSummaryKind { None, Starting, RecoveringWrites, Mounted, Stopping, StopFailed }
internal sealed record MountSummary(MountSummaryKind Kind, IReadOnlyList<char> Drives, string? ErrorCode = null);

internal static class DeviceListPolicy
{
    internal static bool IncludeCandidate(bool pairingWindowOpen, bool hasSavedPairing) =>
        pairingWindowOpen || hasSavedPairing;

    internal static bool IsConnected(string? pairedDeviceId, string? mountedDeviceId) =>
        pairedDeviceId is not null && mountedDeviceId is not null &&
        string.Equals(pairedDeviceId, mountedDeviceId, StringComparison.Ordinal);

    internal static string StableRowId(string candidateId, string deviceId, bool hasSavedPairing) =>
        hasSavedPairing ? deviceId : candidateId;

    internal static MountSummary SummarizeMounts(IEnumerable<(PhoneBridge.Mounting.MountSnapshot Mount, char? Drive)> sessions)
    {
        var snapshots = sessions.ToArray();
        var failed = snapshots.FirstOrDefault(item => item.Mount.State == PhoneBridge.Mounting.MountState.StopFailed);
        if (failed.Mount is not null) return new(MountSummaryKind.StopFailed, [], failed.Mount.ErrorCode);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.RecoveringWrites))
            return new(MountSummaryKind.RecoveringWrites, []);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.Stopping))
            return new(MountSummaryKind.Stopping, []);
        if (snapshots.Any(item => item.Mount.State == PhoneBridge.Mounting.MountState.Starting))
            return new(MountSummaryKind.Starting, []);
        var drives = snapshots.Where(item => item.Mount.State == PhoneBridge.Mounting.MountState.Mounted && item.Drive is not null)
            .Select(item => item.Drive!.Value).Distinct().Order().ToArray();
        return drives.Length == 0 ? new(MountSummaryKind.None, []) : new(MountSummaryKind.Mounted, drives);
    }
}
