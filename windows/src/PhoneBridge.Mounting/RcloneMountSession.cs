using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PhoneBridge.Mounting;

internal sealed class RcloneMountSession : IMountSession
{
    internal const string RemoteName = "phonebridge";
    private readonly MountResources resources;
    private readonly OwnedProcess process;
    private readonly HttpClient control;
    private readonly char drive;
    private readonly bool recoverableWrites;
    private volatile bool forceStopSafe;
    private long recoveryBytes;
    public int ProcessId => process.ProcessId;
    public Task<int> Exit => process.Exit;
    public bool IsDrivePresent => MountResources.DrivePresent(drive);

    public bool ForceStopSafe => forceStopSafe;

    private RcloneMountSession(MountResources resources, OwnedProcess process, HttpClient control, char drive,
        bool recoverableWrites)
    {
        this.resources = resources; this.process = process; this.control = control; this.drive = drive;
        this.recoverableWrites = recoverableWrites; forceStopSafe = !recoverableWrites;
    }

    internal static ProcessStartInfo ChildInfo(string executable)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        // Inherited RCLONE_* or proxy variables must not weaken our fixed settings.
        info.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
            "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            if (Environment.GetEnvironmentVariable(name) is { } value) info.Environment[name] = value;
        return info;
    }

    internal static string ConfigText(MountRequest request, string obscuredPassword) =>
        $"[{RemoteName}]\ntype = webdav\nurl = {request.WebDavUrl}\nvendor = other\nuser = {request.Credentials.User}\npass = {obscuredPassword}\n";

    internal static ProcessStartInfo MountInfo(MountRequest request, string caPath, string configPath, int rcPort,
        string controlPassword)
    {
        var info = ChildInfo(request.RclonePath);
        foreach (var argument in new[] { "mount", RemoteName + ":", $"{request.DriveLetter}:", "--config", configPath,
            "--ca-cert", caPath, "--network-mode", "--volname", "PhoneBridge-" + Guid.NewGuid().ToString("N"),
            "--no-console", "--rc", "--rc-addr", $"127.0.0.1:{rcPort}", "--contimeout", "5s", "--timeout", "15s",
            "--dir-cache-time", "5s", "--poll-interval", "0", "--log-level", "ERROR" })
            info.ArgumentList.Add(argument);
        if (request.AccessMode == MountAccessMode.ReadOnly)
        {
            info.ArgumentList.Insert(3, "--read-only");
            info.ArgumentList.Add("--vfs-cache-mode"); info.ArgumentList.Add("off");
        }
        else if (request.AccessMode == MountAccessMode.Safe)
        {
            // SAFE creates new targets only. Direct writes surface the server's 412 conflict to
            // Windows instead of reporting success for a replacement left dirty in VFS cache.
            info.ArgumentList.Add("--vfs-cache-mode"); info.ArgumentList.Add("off");
        }
        else
        {
            foreach (var argument in new[] { "--vfs-cache-mode", "writes", "--cache-dir", request.CacheRoot,
                "--vfs-write-back", "0s", "--vfs-cache-max-size", "32Gi", "--vfs-cache-min-free-space", "2Gi",
                "--vfs-cache-max-age", "168h", "--vfs-cache-poll-interval", "5s" })
                info.ArgumentList.Add(argument);
        }
        info.Environment["RCLONE_RC_USER"] = "session";
        info.Environment["RCLONE_RC_PASS"] = controlPassword;
        return info;
    }

    public static async Task<IMountSession> StartAsync(MountRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resources = MountResources.Acquire(request);
        HttpClient? control = null;
        try
        {
            var info = ChildInfo(request.RclonePath);
            info.ArgumentList.Add("obscure"); info.ArgumentList.Add("-");
            string obscured;
            await using (var helper = OwnedProcess.Start(info, 4096))
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(TimeSpan.FromSeconds(10));
                await helper.Input.WriteLineAsync(request.Credentials.Password.AsMemory(), limit.Token).ConfigureAwait(false);
                helper.Input.Close();
                if (await helper.Exit.WaitAsync(limit.Token).ConfigureAwait(false) != 0) throw new MountException("credential-format-failed");
                obscured = (await helper.StandardOutput.ConfigureAwait(false)).Trim();
                if (obscured.Length is 0 or >= 4096 || obscured.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                    throw new MountException("credential-format-failed");
            }
            cancellationToken.ThrowIfCancellationRequested();
            resources.WriteRcloneConfig(request, obscured);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            // A race to claim this ephemeral port cannot impersonate our random authenticated RC endpoint.
            var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            control = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
            control.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("session:" + secret)));
            var child = OwnedProcess.Start(MountInfo(request, resources.CertificatePath, resources.ConfigPath, port, secret));
            return new RcloneMountSession(resources, child, control, request.DriveLetter,
                request.AccessMode == MountAccessMode.ReadWrite);
        }
        catch { control?.Dispose(); resources.Dispose(); throw; }
    }

    private async Task<JsonDocument> CallAsync(string method, object input, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, method)
        { Content = new StringContent(JsonSerializer.Serialize(input), Encoding.UTF8, "application/json") };
        using var response = await control.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new MountException("rclone-control-request-failed");
        // Remote listings and diagnostics cannot allocate unbounded memory or leak raw error bodies.
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<MountProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        // Persistent cache metadata is local, protected by the device cache lease and
        // available before rclone finishes bringing up RC or WinFsp. A recovery upload
        // may hold VFS calls for its full duration, so enter the recovery state before
        // issuing any RC request which could wait behind that upload.
        if (recoverableWrites && DirtyCachePresent(resources.CacheMetadataRoot))
        {
            await RefreshRecoveryProgressAsync(cancellationToken).ConfigureAwait(false);
            return new(MountReadiness.RecoveringWrites, recoveryBytes);
        }
        try
        {
            using var owner = await CallAsync("core/pid", new { }, cancellationToken).ConfigureAwait(false);
            if (!owner.RootElement.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var actual) || actual != ProcessId)
                throw new MountException("rclone-control-owner-mismatch");
        }
        catch (HttpRequestException) { return new(MountReadiness.Pending); } // listener not yet ready; the manager owns the deadline
        // VFS replays persistent dirty cache before WinFsp necessarily exposes the drive.
        // Detect that recovery through authenticated RC first, otherwise the ordinary
        // drive-startup deadline misclassifies a healthy write-back as a mount timeout.
        if (recoverableWrites)
        {
            using var queue = await CallAsync("vfs/queue", new { }, cancellationToken).ConfigureAwait(false);
            using var stats = await CallAsync("vfs/stats", new { }, cancellationToken).ConfigureAwait(false);
            if (!CacheDrained(queue.RootElement, stats.RootElement) || DirtyCachePresent(resources.CacheMetadataRoot))
            {
                using var transfers = await CallAsync("core/stats", new { }, cancellationToken).ConfigureAwait(false);
                recoveryBytes = Math.Max(recoveryBytes, TransferBytes(transfers.RootElement));
                return new(MountReadiness.RecoveringWrites, recoveryBytes);
            }
        }
        if (!IsDrivePresent) return new(MountReadiness.Pending);
        // core/pid and drive presence alone do not prove the phone's TLS identity or credentials.
        using var listing = await CallAsync("operations/list", new
        {
            fs = RemoteName + ":", remote = "", opt = new { dirsOnly = true, recurse = false, noModTime = true, noMimeType = true }
        }, cancellationToken).ConfigureAwait(false);
        if (!listing.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new MountException("remote-validation-failed");
        return new(!Exit.IsCompleted ? MountReadiness.Ready : MountReadiness.Pending);
    }

    private async Task RefreshRecoveryProgressAsync(CancellationToken cancellationToken)
    {
        using var diagnosticLimit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, diagnosticLimit.Token);
        try
        {
            using var owner = await CallAsync("core/pid", new { }, linked.Token).ConfigureAwait(false);
            if (!owner.RootElement.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var actual) || actual != ProcessId)
                throw new MountException("rclone-control-owner-mismatch");
            using var transfers = await CallAsync("core/stats", new { }, linked.Token).ConfigureAwait(false);
            recoveryBytes = Math.Max(recoveryBytes, TransferBytes(transfers.RootElement));
        }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (diagnosticLimit.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
    }

    internal static long TransferBytes(JsonElement root)
    {
        if (!root.TryGetProperty("bytes", out var bytes) || bytes.ValueKind != JsonValueKind.Number ||
            !bytes.TryGetInt64(out var value) || value < 0)
            throw new MountException("rclone-cache-status-invalid");
        return value;
    }

    public async Task RequestStopAsync(CancellationToken cancellationToken)
    {
        if (recoverableWrites)
        {
            while (true)
            {
                using var queue = await CallAsync("vfs/queue", new { }, cancellationToken).ConfigureAwait(false);
                using var stats = await CallAsync("vfs/stats", new { }, cancellationToken).ConfigureAwait(false);
                if (CacheDrained(queue.RootElement, stats.RootElement) && !DirtyCachePresent(resources.CacheMetadataRoot)) break;
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            forceStopSafe = true;
        }
        using var result = await CallAsync("core/quit", new { exitCode = 0 }, cancellationToken).ConfigureAwait(false);
    }

    internal static bool CacheDrained(JsonElement queueRoot, JsonElement statsRoot)
    {
        if (!queueRoot.TryGetProperty("queue", out var items) || items.ValueKind != JsonValueKind.Array ||
            !statsRoot.TryGetProperty("diskCache", out var disk) || disk.ValueKind != JsonValueKind.Object ||
            !disk.TryGetProperty("uploadsInProgress", out var active) || !active.TryGetInt32(out var activeCount) || activeCount < 0 ||
            !disk.TryGetProperty("uploadsQueued", out var queued) || !queued.TryGetInt32(out var queuedCount) || queuedCount < 0 ||
            !disk.TryGetProperty("erroredFiles", out var errors) || !errors.TryGetInt32(out var errorCount) || errorCount < 0 ||
            !disk.TryGetProperty("outOfSpace", out var outOfSpace) || outOfSpace.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new MountException("rclone-cache-status-invalid");
        if (errorCount != 0 || outOfSpace.GetBoolean()) throw new MountException("pending-writes-not-confirmed");
        return items.GetArrayLength() == 0 && activeCount == 0 && queuedCount == 0;
    }

    internal static bool DirtyCachePresent(string metadataRoot)
    {
        if (!Directory.Exists(metadataRoot)) return false;
        try
        {
            var pending = new Stack<string>();
            pending.Push(metadataRoot);
            while (pending.TryPop(out var directory))
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                    throw new MountException("rclone-cache-status-invalid");
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new MountException("rclone-cache-status-invalid");
                    if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(path); continue; }
                    var file = new FileInfo(path);
                    if (file.Length > 64 * 1024) throw new MountException("rclone-cache-status-invalid");
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (!document.RootElement.TryGetProperty("Dirty", out var dirty) ||
                        dirty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new MountException("rclone-cache-status-invalid");
                    if (dirty.GetBoolean()) return true;
                }
            }
            return false;
        }
        catch (MountException) { throw; }
        catch { throw new MountException("rclone-cache-status-invalid"); }
    }

    public void KillOwnedProcess() => process.Kill();
    public async ValueTask DisposeAsync()
    {
        control.Dispose();
        await process.DisposeAsync().ConfigureAwait(false);
        resources.Dispose();
    }
}
