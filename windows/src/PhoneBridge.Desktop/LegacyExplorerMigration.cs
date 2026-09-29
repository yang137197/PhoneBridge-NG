using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace PhoneBridge.Desktop;

internal enum LegacyExplorerMigrationResult { Completed, Failed }

internal static class LegacyExplorerMigration
{
    private const string MountPointsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";
    private const string LegacyPrefix = "##pbng-";
    private const string PendingFileName = "explorer-fixed-drive-v1.pending";

    internal static bool IsLegacyMountPointName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int separator = LegacyPrefix.Length + 10;
        if (name.Length <= separator + 1 || !name.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase) ||
            name[separator] != '#') return false;
        for (int index = LegacyPrefix.Length; index < separator; index++)
            if (!char.IsAsciiHexDigit(name[index])) return false;
        return true;
    }

    internal static string PendingPathFor(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        if (!Path.IsPathFullyQualified(appDataRoot) || appDataRoot.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(appDataRoot));
        return Path.Combine(Path.GetFullPath(appDataRoot), "Migrations", PendingFileName);
    }

    internal static string[] SelectLegacyMountPoints(IEnumerable<string> names) =>
        names.Where(IsLegacyMountPointName).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static bool RequiresAction(string appDataRoot, IEnumerable<string> mountPointNames) =>
        File.Exists(PendingPathFor(appDataRoot)) || SelectLegacyMountPoints(mountPointNames).Length != 0;

    internal static bool TryRequiresAction(string appDataRoot, out bool required)
    {
        required = false;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(MountPointsPath, writable: false);
            required = RequiresAction(appDataRoot, root?.GetSubKeyNames() ?? []);
            return true;
        }
        catch (Exception error) when (Expected(error)) { return false; }
    }

    internal static int DeleteLegacyMountPoints(RegistryKey root)
    {
        ArgumentNullException.ThrowIfNull(root);
        string[] names = SelectLegacyMountPoints(root.GetSubKeyNames());
        foreach (string name in names) root.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        return names.Length;
    }

    internal static LegacyExplorerMigrationResult Apply(string appDataRoot)
    {
        string pending;
        try
        {
            pending = PendingPathFor(appDataRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
            File.WriteAllText(pending, "fixed-drive-v1");
            using (var root = Registry.CurrentUser.OpenSubKey(MountPointsPath, writable: true))
                if (root is not null) DeleteLegacyMountPoints(root);
            if (!RestartCurrentSessionExplorer()) return LegacyExplorerMigrationResult.Failed;
            File.Delete(pending);
            return LegacyExplorerMigrationResult.Completed;
        }
        catch (Exception error) when (Expected(error)) { return LegacyExplorerMigrationResult.Failed; }
    }

    private static bool RestartCurrentSessionExplorer()
    {
        using Process current = Process.GetCurrentProcess();
        int session = current.SessionId;
        Process[] oldProcesses = CurrentSessionExplorers(session);
        int[] oldIds = oldProcesses.Select(process => process.Id).ToArray();
        try
        {
            foreach (Process process in oldProcesses)
                if (!process.HasExited) process.Kill(entireProcessTree: false);
            foreach (Process process in oldProcesses)
                if (!process.WaitForExit(5000)) return false;
        }
        finally { foreach (Process process in oldProcesses) process.Dispose(); }

        if (!HasNewExplorer(session, oldIds))
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows)) return false;
            using Process? started = Process.Start(
                new ProcessStartInfo(Path.Combine(windows, "explorer.exe")) { UseShellExecute = true });
        }

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (HasNewExplorer(session, oldIds)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private static bool HasNewExplorer(int session, int[] oldIds)
    {
        Process[] processes = CurrentSessionExplorers(session);
        try { return processes.Any(process => !oldIds.Contains(process.Id)); }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static Process[] CurrentSessionExplorers(int session)
    {
        var matches = new List<Process>();
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            if (SameSession(process, session)) matches.Add(process);
            else process.Dispose();
        }
        return matches.ToArray();
    }

    private static bool SameSession(Process process, int session)
    {
        try { return process.SessionId == session; }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { return false; }
    }

    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException or
        ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException;
}
