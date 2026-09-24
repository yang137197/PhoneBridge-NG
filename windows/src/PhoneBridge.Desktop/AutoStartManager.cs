using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;

namespace PhoneBridge.Desktop;

internal enum AutoStartState { Disabled, Enabled, Conflict }

internal readonly record struct AutoStartValue(bool Exists, string? Command, bool IsValid)
{
    internal static AutoStartValue Missing => new(false, null, false);
}

internal interface IAutoStartStore
{
    AutoStartValue Read();
    void Write(string executablePath, string arguments);
    void Delete();
}

internal sealed class WindowsAutoStartStore : IAutoStartStore
{
    internal const string ShortcutName = "PhoneBridge NG.lnk";
    private readonly string startupDirectory;
    internal string ShortcutPath => Path.Combine(startupDirectory, ShortcutName);

    internal WindowsAutoStartStore(string? startupDirectory = null)
    {
        this.startupDirectory = startupDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(this.startupDirectory) || !Path.IsPathFullyQualified(this.startupDirectory))
            throw new IOException("Startup folder is unavailable.");
    }

    public AutoStartValue Read()
    {
        if (!File.Exists(ShortcutPath)) return AutoStartValue.Missing;
        if ((File.GetAttributes(ShortcutPath) & FileAttributes.ReparsePoint) != 0)
            return new(true, null, false);
        try
        {
            var shortcut = ShellShortcut.Load(ShortcutPath);
            return new(true, BuildCommand(shortcut.TargetPath, shortcut.Arguments), true);
        }
        catch (Exception error) when (StoreFailure(error))
        {
            return new(true, null, false);
        }
    }

    public void Write(string executablePath, string arguments)
    {
        Directory.CreateDirectory(startupDirectory);
        if (File.Exists(ShortcutPath)) throw new IOException("Startup shortcut already exists.");
        string temporary = Path.Combine(startupDirectory, $"PhoneBridge NG.{Guid.NewGuid():N}.tmp.lnk");
        try
        {
            ShellShortcut.Save(temporary, executablePath, arguments, Path.GetDirectoryName(executablePath)!);
            File.Move(temporary, ShortcutPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Delete() => File.Delete(ShortcutPath);

    internal static string BuildCommand(string executablePath, string arguments) =>
        $"\"{Path.GetFullPath(executablePath)}\" {arguments}";

    private static bool StoreFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or SecurityException or COMException;
}

internal readonly record struct ShellShortcutValue(string TargetPath, string Arguments);

internal static class ShellShortcut
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private const uint RawPath = 0x0004;
    private const int BufferSize = 32768;

    internal static ShellShortcutValue Load(string path)
    {
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass, throwOnError: true)!)!;
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(BufferSize);
            var arguments = new StringBuilder(BufferSize);
            Marshal.ThrowExceptionForHR(link.GetPath(target, target.Capacity, IntPtr.Zero, RawPath));
            Marshal.ThrowExceptionForHR(link.GetArguments(arguments, arguments.Capacity));
            if (target.Length == 0) throw new IOException("Startup shortcut has no target.");
            return new(target.ToString(), arguments.ToString());
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    internal static void Save(string path, string targetPath, string arguments, string workingDirectory)
    {
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass, throwOnError: true)!)!;
        try
        {
            var link = (IShellLinkW)instance;
            Marshal.ThrowExceptionForHR(link.SetPath(targetPath));
            Marshal.ThrowExceptionForHR(link.SetArguments(arguments));
            Marshal.ThrowExceptionForHR(link.SetWorkingDirectory(workingDirectory));
            Marshal.ThrowExceptionForHR(link.SetDescription("PhoneBridge NG"));
            Marshal.ThrowExceptionForHR(link.SetIconLocation(targetPath, 0));
            ((IPersistFile)instance).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int count);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

internal sealed class AutoStartException(string code, Exception? inner = null) : Exception(code, inner)
{
    internal string Code { get; } = code;
}

internal sealed class AutoStartManager
{
    internal const string StartupArgument = "--startup";
    private const int MaximumCommandLength = 32767;
    private readonly IAutoStartStore store;

    internal AutoStartManager(IAutoStartStore store, string executablePath)
    {
        this.store = store;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            executablePath.Contains('"') || executablePath.Contains('\0'))
            throw new AutoStartException("autostart-invalid-path");
        if (executablePath.Length + StartupArgument.Length + 4 > MaximumCommandLength)
            throw new AutoStartException("autostart-command-too-long");
        ExecutablePath = Path.GetFullPath(executablePath);
        Command = WindowsAutoStartStore.BuildCommand(ExecutablePath, StartupArgument);
        if (Command.Length > MaximumCommandLength) throw new AutoStartException("autostart-command-too-long");
    }

    internal string ExecutablePath { get; }
    internal string Command { get; }

    internal AutoStartState GetState()
    {
        var value = Read();
        if (!value.Exists) return AutoStartState.Disabled;
        return Matches(value) ? AutoStartState.Enabled : AutoStartState.Conflict;
    }

    internal void SetEnabled(bool enabled)
    {
        var current = Read();
        if (current.Exists && !Matches(current)) throw new AutoStartException("autostart-entry-conflict");
        try
        {
            if (enabled)
            {
                if (!current.Exists) store.Write(ExecutablePath, StartupArgument);
                if (!Matches(store.Read())) throw new AutoStartException("autostart-write-failed");
            }
            else
            {
                if (current.Exists) store.Delete();
                if (store.Read().Exists) throw new AutoStartException("autostart-write-failed");
            }
        }
        catch (AutoStartException) { throw; }
        catch (Exception error) when (StoreFailure(error))
        {
            throw new AutoStartException("autostart-write-failed", error);
        }
    }

    internal static bool IsStartupLaunch(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && string.Equals(arguments[0], StartupArgument, StringComparison.Ordinal);

    private AutoStartValue Read()
    {
        try { return store.Read(); }
        catch (Exception error) when (StoreFailure(error))
        {
            throw new AutoStartException("autostart-read-failed", error);
        }
    }

    private bool Matches(AutoStartValue value) => value.IsValid &&
        string.Equals(value.Command, Command, StringComparison.OrdinalIgnoreCase);

    private static bool StoreFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or SecurityException or COMException;
}
