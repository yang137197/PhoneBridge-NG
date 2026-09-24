using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhoneBridge.Mounting;

// Create and assign atomically. No uncontained interval between Start and AssignProcessToJobObject.
internal static class NativeProcess
{
    internal static (SafeProcessHandle Handle, int Id) Start(ProcessStartInfo info, SafeFileHandle job,
        SafePipeHandle input, SafePipeHandle output, SafePipeHandle errors)
    {
        if (info.UseShellExecute || !info.CreateNoWindow || info.Arguments.Length != 0 ||
            !Path.IsPathFullyQualified(info.FileName)) throw new MountException("invalid-process-options");
        var command = new StringBuilder(string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList).Select(Quote)));
        if (command.Length >= 32767) throw new MountException("command-too-long");
        var environment = (string.Join('\0', info.Environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key + "=" + p.Value)) + "\0\0").ToCharArray();
        var pinned = GCHandle.Alloc(environment, GCHandleType.Pinned);
        nint attributes = 0, handles = 0, jobs = 0;
        var initialized = false;
        try
        {
            nuint size = 0;
            InitializeProcThreadAttributeList(0, 2, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((nint)size));
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref size)) throw new MountException("process-attributes-failed");
            initialized = true;
            handles = Marshal.AllocHGlobal(3 * nint.Size);
            Marshal.WriteIntPtr(handles, 0, input.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, nint.Size, output.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, 2 * nint.Size, errors.DangerousGetHandle());
            jobs = Marshal.AllocHGlobal(nint.Size);
            Marshal.WriteIntPtr(jobs, job.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(3 * nint.Size), 0, 0) ||
                !UpdateProcThreadAttribute(attributes, 0, 0x2000D, jobs, (nuint)nint.Size, 0, 0))
                throw new MountException("process-attributes-failed");
            var startup = new StartupInfoEx
            {
                Info = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    Input = input.DangerousGetHandle(), Output = output.DangerousGetHandle(), Error = errors.DangerousGetHandle() },
                Attributes = attributes
            };
            // Explicit application path, Unicode environment, no console, only the three allowed handles inherited.
            if (!CreateProcessW(info.FileName, command, 0, 0, true, 0x08080400, pinned.AddrOfPinnedObject(),
                info.WorkingDirectory, ref startup, out var created)) throw new MountException("process-start-failed");
            using var thread = new SafeFileHandle(created.Thread, ownsHandle: true);
            return (new SafeProcessHandle(created.Process, ownsHandle: true), checked((int)created.ProcessId));
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(jobs);
            Array.Clear(environment); pinned.Free();
            GC.KeepAlive(job); GC.KeepAlive(input); GC.KeepAlive(output); GC.KeepAlive(errors);
        }
    }

    // Windows argv quoting: double backslashes before quotes and at a quoted argument's end.
    internal static string Quote(string value)
    {
        if (value.Contains('\0')) throw new MountException("invalid-process-argument");
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCount, YCount, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Info; public nint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, nint processSecurity, nint threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint code);
}
