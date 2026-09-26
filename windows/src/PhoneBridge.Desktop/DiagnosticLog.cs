using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneBridge.Desktop;

internal enum DiagnosticLevel { Information, Warning, Error }
internal enum DiagnosticEventName
{
    AppStarted, AppStopping, DiscoveryChanged, DiscoveryFailed,
    PairingStarted, AuthenticationCompleted, ConnectionStarted, ConnectionCompleted,
    MountStateChanged, RcloneExited, WinFspChecked, NetworkCheckCompleted,
    ReconnectScheduled, TrayVisibilityChanged, StartupSettingChanged,
    DiagnosticExportCompleted, DiagnosticExportFailed, ManualEndpointChanged, DeviceMetadataChanged
}
internal enum DiagnosticResultCode
{
    None, Success, Failure, Cancelled, Timeout, Unauthorized, IdentityMismatch,
    InvalidResponse, RecordChanged, DriveOccupied, DriveReserved, WinFspMissing,
    RcloneIntegrity, PendingWrites, RecoveryStalled, RecoveryTimeout, RecoveryCancelled,
    UnmountUnconfirmed, StorageFailure, Ambiguous
}
internal enum DiagnosticState
{
    None, Manual, Startup, Added, Updated, Removed, Checking, Healthy, Lost,
    Starting, Recovering, Mounted, Stopping, Stopped, Failed, Hidden, Visible, Enabled,
    Disabled, Waiting
}

internal readonly record struct DiagnosticEvent(
    DiagnosticEventName Event,
    DiagnosticLevel Level = DiagnosticLevel.Information,
    DiagnosticResultCode Code = DiagnosticResultCode.None,
    DiagnosticState State = DiagnosticState.None,
    int? Count = null,
    long? DurationMs = null);

internal sealed class DiagnosticEventLog : IDisposable
{
    internal const long DefaultMaxFileBytes = 1024 * 1024;
    internal const int DefaultMaxFiles = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly object gate = new();
    private readonly string root;
    private readonly long maxFileBytes;
    private readonly int maxFiles;
    private readonly string version;
    private FileStream? stream;
    private long sequence;
    private bool disposed;

    private DiagnosticEventLog(string root, string version, long maxFileBytes, int maxFiles)
    {
        this.root = root;
        this.version = version;
        this.maxFileBytes = maxFileBytes;
        this.maxFiles = maxFiles;
    }

    internal bool IsAvailable { get { lock (gate) return stream is not null && !disposed; } }
    internal string HealthCode { get; private set; } = "available";

    internal static DiagnosticEventLog OpenDefault() => Open(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge-NG", "Logs"),
        ProductVersion(), DefaultMaxFileBytes, DefaultMaxFiles);

    internal static DiagnosticEventLog Open(string root, string version, long maxFileBytes = DefaultMaxFileBytes, int maxFiles = DefaultMaxFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!Path.IsPathFullyQualified(root) || maxFileBytes is < 256 or > 16 * 1024 * 1024 || maxFiles is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(root));
        var log = new DiagnosticEventLog(Path.GetFullPath(root), version, maxFileBytes, maxFiles);
        try
        {
            log.PrepareDirectory();
            log.RemoveOversizedFiles();
            log.stream = log.OpenCurrent();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            log.HealthCode = "storage-unavailable";
        }
        return log;
    }

    internal void Write(DiagnosticEvent item)
    {
        lock (gate)
        {
            if (stream is null || disposed) return;
            try
            {
                ReopenIfPathWasReplaced();
                var entry = new LogEntry(1, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ++sequence, version, Name(item.Level), Name(item.Event),
                    item.Code == DiagnosticResultCode.None ? null : Name(item.Code),
                    item.State == DiagnosticState.None ? null : Name(item.State),
                    item.Count is >= 0 ? item.Count : null,
                    item.DurationMs is >= 0 ? item.DurationMs : null);
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
                long required = json.Length + 1L;
                if (required > maxFileBytes) { Disable("event-too-large"); return; }
                if (stream.Length > 0 && stream.Length + required > maxFileBytes) Rotate();
                stream.Write(json); stream.WriteByte((byte)'\n'); stream.Flush(flushToDisk: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
            {
                Disable("write-failed");
            }
        }
    }

    internal void WriteFailure(DiagnosticEventName name, DiagnosticResultCode code, Exception ignored) =>
        Write(new(name, DiagnosticLevel.Error, code, DiagnosticState.Failed));

    internal IReadOnlyList<DiagnosticLogFile> Snapshot()
    {
        lock (gate)
        {
            if (disposed) throw new DiagnosticExportException("diagnostics-unavailable");
            try
            {
                stream?.Flush(flushToDisk: true);
                var result = new List<DiagnosticLogFile>();
                for (int index = maxFiles - 1; index >= 0; index--)
                {
                    string name = $"events-{index}.jsonl";
                    string path = Path.Combine(root, name);
                    if (!File.Exists(path)) continue;
                    if (index == 0 && stream is not null)
                    {
                        long restore = stream.Position;
                        if (stream.Length > int.MaxValue) throw new IOException("log-too-large");
                        var content = new byte[(int)stream.Length];
                        stream.Position = 0; stream.ReadExactly(content); stream.Position = restore;
                        result.Add(new(name, content));
                    }
                    else result.Add(new(name, File.ReadAllBytes(path)));
                }
                return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new DiagnosticExportException("diagnostics-read-failed", error);
            }
        }
    }

    private void PrepareDirectory()
    {
        string full = Path.GetFullPath(root);
        for (DirectoryInfo? item = new(full); item is not null && item.Exists; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("unsafe-log-location");
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(full);
        if (!directory.Exists) directory.Create(security);
        else directory.SetAccessControl(security);
        directory.Refresh();
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("unsafe-log-location");
    }

    private FileStream OpenCurrent()
    {
        string path = Path.Combine(root, "events-0.jsonl");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("unsafe-log-file");
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough)
            { Position = new FileInfo(path).Length };
    }

    private void ReopenIfPathWasReplaced()
    {
        string path = Path.Combine(root, "events-0.jsonl");
        bool exists = File.Exists(path);
        long pathLength = exists ? new FileInfo(path).Length : 0;
        if (CurrentPathMatches(stream!.Length, exists, pathLength)) return;
        stream?.Dispose();
        stream = null;
        PrepareDirectory();
        RemoveOversizedFiles();
        stream = OpenCurrent();
    }

    internal static bool CurrentPathMatches(long openLength, bool pathExists, long pathLength) =>
        pathExists && openLength == pathLength;

    private void RemoveOversizedFiles()
    {
        for (int index = 0; index < maxFiles; index++)
        {
            string path = Path.Combine(root, $"events-{index}.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length > maxFileBytes) File.Delete(path);
        }
    }

    private void Rotate()
    {
        stream!.Dispose(); stream = null;
        string oldest = Path.Combine(root, $"events-{maxFiles - 1}.jsonl");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int index = maxFiles - 2; index >= 0; index--)
        {
            string source = Path.Combine(root, $"events-{index}.jsonl");
            if (File.Exists(source)) File.Move(source, Path.Combine(root, $"events-{index + 1}.jsonl"));
        }
        stream = OpenCurrent();
    }

    private void Disable(string code)
    {
        try { stream?.Dispose(); } catch { }
        stream = null; HealthCode = code;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try { stream?.Dispose(); } catch { }
            stream = null;
        }
    }

    internal static string ProductVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    private static string Name<T>(T value) where T : struct, Enum => value.ToString();

    private sealed record LogEntry(int Schema, string Utc, long Sequence, string AppVersion,
        string Level, string Event, string? Code, string? State, int? Count, long? DurationMs);
}

internal readonly record struct DiagnosticLogFile(string Name, byte[] Content);

internal sealed class DiagnosticExportException(string code, Exception? inner = null) : Exception(code, inner)
{
    internal string Code { get; } = code;
}

internal sealed class DiagnosticBundleExporter(DiagnosticEventLog log)
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal async Task ExportAsync(string destination, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination) ||
            !string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new DiagnosticExportException("diagnostics-invalid-destination");
        string target = Path.GetFullPath(destination);
        string? directory = Path.GetDirectoryName(target);
        if (directory is null || !Directory.Exists(directory)) throw new DiagnosticExportException("diagnostics-invalid-destination");
        string temporary = Path.Combine(directory, ".phonebridge-diagnostics-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = log.Snapshot();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifest = new DiagnosticManifest(1, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        DiagnosticEventLog.ProductVersion(), RuntimeInformation.OSDescription,
                        CultureInfo.CurrentUICulture.Name, files.Count, log.IsAvailable, log.HealthCode);
                    await WriteEntryAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestOptions), cancellationToken);
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await WriteEntryAsync(archive, "logs/" + file.Name, file.Content, cancellationToken);
                    }
                }
                await output.FlushAsync(cancellationToken); output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        catch (OperationCanceledException) { TryDelete(temporary); throw; }
        catch (DiagnosticExportException) { TryDelete(temporary); throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException)
        {
            TryDelete(temporary);
            throw new DiagnosticExportException("diagnostics-export-failed", error);
        }
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] content, CancellationToken token)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, token);
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private sealed record DiagnosticManifest(int Schema, string GeneratedUtc, string AppVersion, string OsVersion,
        string UiCulture, int LogFileCount, bool LoggerAvailable, string LoggerHealth);
}

internal static class DiagnosticCodeMap
{
    internal static DiagnosticResultCode From(string? code) => code switch
    {
        null => DiagnosticResultCode.None,
        "unauthorized" => DiagnosticResultCode.Unauthorized,
        "identity-mismatch" => DiagnosticResultCode.IdentityMismatch,
        "invalid-response" => DiagnosticResultCode.InvalidResponse,
        "record-changed" => DiagnosticResultCode.RecordChanged,
        "drive-occupied" => DiagnosticResultCode.DriveOccupied,
        "drive-reserved" => DiagnosticResultCode.DriveReserved,
        "winfsp-missing" => DiagnosticResultCode.WinFspMissing,
        "rclone-hash-mismatch" => DiagnosticResultCode.RcloneIntegrity,
        "pending-writes-not-confirmed" => DiagnosticResultCode.PendingWrites,
        "recovery-stalled-cache-retained" => DiagnosticResultCode.RecoveryStalled,
        "recovery-timeout-cache-retained" => DiagnosticResultCode.RecoveryTimeout,
        "recovery-cancelled-cache-retained" => DiagnosticResultCode.RecoveryCancelled,
        "unmount-not-confirmed" => DiagnosticResultCode.UnmountUnconfirmed,
        "storage_failure" => DiagnosticResultCode.StorageFailure,
        "startup-timeout" => DiagnosticResultCode.Timeout,
        "start-cancelled" or "start-stopped" or "cancelled" => DiagnosticResultCode.Cancelled,
        _ => DiagnosticResultCode.Failure
    };
}
