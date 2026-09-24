using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhoneBridge.Mounting;

// All process operations use the creation handle, never a name or a later PID lookup.
internal sealed class OwnedProcess : IAsyncDisposable
{
    private readonly SafeProcessHandle process;
    private readonly SafeFileHandle job;
    private readonly StreamReader output, errors;
    public int ProcessId { get; }
    public Task<int> Exit { get; }
    public Task<string> StandardOutput { get; }
    public StreamWriter Input { get; }

    private OwnedProcess(SafeProcessHandle process, int processId, SafeFileHandle job,
        AnonymousPipeServerStream input, AnonymousPipeServerStream output, AnonymousPipeServerStream errors, int outputLimit)
    {
        this.process = process;
        this.job = job;
        ProcessId = processId;
        Input = new StreamWriter(input, new UTF8Encoding(false)) { AutoFlush = true };
        this.output = new StreamReader(output, Encoding.UTF8);
        this.errors = new StreamReader(errors, Encoding.UTF8);
        StandardOutput = DrainAsync(this.output, outputLimit);
        Exit = WaitAsync(DrainAsync(this.errors, 0));
    }

    public static OwnedProcess Start(ProcessStartInfo info, int outputLimit = 0)
    {
        var job = NativeJob.Create();
        AnonymousPipeServerStream? input = null, output = null, errors = null;
        SafeProcessHandle? handle = null;
        try
        {
            input = new(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new(PipeDirection.In, HandleInheritability.Inheritable);
            errors = new(PipeDirection.In, HandleInheritability.Inheritable);
            var child = NativeProcess.Start(info, job, input.ClientSafePipeHandle, output.ClientSafePipeHandle, errors.ClientSafePipeHandle);
            handle = child.Handle;
            input.DisposeLocalCopyOfClientHandle(); output.DisposeLocalCopyOfClientHandle(); errors.DisposeLocalCopyOfClientHandle();
            return new(handle, child.Id, job, input, output, errors, outputLimit);
        }
        catch
        {
            job.Dispose();
            input?.Dispose(); output?.Dispose(); errors?.Dispose(); handle?.Dispose();
            throw new MountException("process-start-failed");
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, int maximum)
    {
        var buffer = new char[4096];
        var capture = new System.Text.StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) break;
            if (maximum > capture.Length) capture.Append(buffer, 0, Math.Min(count, maximum - capture.Length));
        }
        return capture.ToString(); // never logged; only obscure helper requests bounded stdout
    }

    private async Task<int> WaitAsync(Task<string> errors)
    {
        using var wait = new ProcessWaitHandle(process);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) => completed.TrySetResult(), null, -1, true);
        try { await completed.Task.ConfigureAwait(false); }
        finally { registration.Unregister(null); }
        await Task.WhenAll(StandardOutput, errors).ConfigureAwait(false);
        if (!NativeProcess.GetExitCodeProcess(process, out var code)) throw new MountException("process-exit-query-failed");
        return unchecked((int)code);
    }

    public void Kill()
    {
        if (!Exit.IsCompleted && !NativeProcess.TerminateProcess(process, 1)) throw new MountException("process-terminate-failed");
    }

    public async ValueTask DisposeAsync()
    {
        // Last-resort containment belongs only to this child. Wait before releasing its handle.
        job.Dispose();
        await Exit.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Input.Dispose(); output.Dispose(); errors.Dispose(); process.Dispose();
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle handle) => SafeWaitHandle = new(handle.DangerousGetHandle(), ownsHandle: false);
    }
}

internal static class NativeJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    public static SafeFileHandle Create()
    {
        var handle = CreateJobObjectW(nint.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); throw new MountException("job-create-failed"); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { handle.Dispose(); throw new MountException("job-limit-failed"); }
        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits info, uint size);
}
