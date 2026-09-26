using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
    [TestMethod]
    public void JsonLinesUseOnlyTheFixedSchema()
    {
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "1.2.3");
        log.Write(new(DiagnosticEventName.DiscoveryChanged, State: DiagnosticState.Added, Count: 2, DurationMs: 9, Session: 7));
        var line = Encoding.UTF8.GetString(log.Snapshot().Single().Content).Trim();
        using var json = JsonDocument.Parse(line);
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "appVersion", "count", "durationMs", "event", "level", "schema", "sequence", "session", "state", "utc" }.Order().ToArray(), names);
        Assert.AreEqual("DiscoveryChanged", json.RootElement.GetProperty("event").GetString());
        Assert.AreEqual(2, json.RootElement.GetProperty("schema").GetInt32());
        Assert.AreEqual("Added", json.RootElement.GetProperty("state").GetString());
        Assert.AreEqual("1.2.3", json.RootElement.GetProperty("appVersion").GetString());
        Assert.AreEqual(7, json.RootElement.GetProperty("session").GetInt32());
    }

    [TestMethod]
    public async Task ConcurrentWritesRemainCompleteJsonWithUniqueSequences()
    {
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "test");
        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            log.Write(new(DiagnosticEventName.NetworkCheckCompleted, State: DiagnosticState.Healthy, Count: index)))));
        var documents = Lines(log).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.HasCount(100, documents);
            Assert.AreEqual(100, documents.Select(item => item.RootElement.GetProperty("sequence").GetInt64()).Distinct().Count());
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    [TestMethod]
    public void RotationHonorsFileCountAndPerFileLimit()
    {
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "test", maxFileBytes: 512, maxFiles: 3);
        for (int index = 0; index < 30; index++)
            log.Write(new(DiagnosticEventName.MountStateChanged, State: DiagnosticState.Mounted, Count: index));
        var files = log.Snapshot();
        Assert.HasCount(3, files);
        Assert.IsTrue(files.All(file => file.Content.Length <= 512));
        Assert.IsTrue(files.All(file => file.Name is "events-0.jsonl" or "events-1.jsonl" or "events-2.jsonl"));
        Assert.IsTrue(log.IsAvailable);
    }

    [TestMethod]
    public void UnusableStorageNeverEscapesIntoApplicationFlow()
    {
        using var temporary = new TemporaryDirectory();
        string collision = temporary.Child("not-a-directory");
        File.WriteAllText(collision, "x");
        using var log = DiagnosticEventLog.Open(collision, "test");
        Assert.IsFalse(log.IsAvailable);
        Assert.AreEqual("storage-unavailable", log.HealthCode);
        log.Write(new(DiagnosticEventName.AppStarted));
    }

    [TestMethod]
    public void LogDirectoryAclIsProtectedForCurrentUserAndSystemOnly()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Child("logs");
        using var log = DiagnosticEventLog.Open(root, "test");
        Assert.IsTrue(log.IsAvailable);
        var security = new DirectoryInfo(root).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        Assert.AreEqual(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.IsTrue(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        Assert.HasCount(2, rules);
        var allowed = new HashSet<string>(new[] { identity.User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value }, StringComparer.Ordinal);
        Assert.IsTrue(rules.All(rule => rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights == FileSystemRights.FullControl && allowed.Contains(rule.IdentityReference.Value)));
    }

    [TestMethod]
    public void OversizedPriorLogIsRemovedBeforeOpening()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Child("logs"); Directory.CreateDirectory(root);
        File.WriteAllBytes(System.IO.Path.Combine(root, "events-0.jsonl"), new byte[513]);
        using var log = DiagnosticEventLog.Open(root, "test", maxFileBytes: 512, maxFiles: 3);
        Assert.IsTrue(log.IsAvailable);
        Assert.HasCount(1, log.Snapshot());
        Assert.IsEmpty(log.Snapshot().Single().Content);
    }

    [TestMethod]
    public void DetectsWhenCurrentLogPathNoLongerMatchesOpenFile()
    {
        Assert.IsTrue(DiagnosticEventLog.CurrentPathMatches(886, true, 886));
        Assert.IsFalse(DiagnosticEventLog.CurrentPathMatches(886, true, 775486));
        Assert.IsFalse(DiagnosticEventLog.CurrentPathMatches(886, false, 0));
    }

    [TestMethod]
    public void RawExceptionAndSyntheticSecretsAreNeverSerialized()
    {
        const string secret = "SYNTHETIC-TOKEN-5f6395c0";
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "test");
        log.WriteFailure(DiagnosticEventName.ConnectionCompleted, DiagnosticResultCode.Failure,
            new IOException("password=" + secret + " C:\\Users\\Private\\file.txt"));
        string content = string.Join('\n', Lines(log));
        Assert.IsFalse(content.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(content.Contains("Private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExportContainsOnlyManifestAndLogWhitelistAndLoggerContinues()
    {
        const string secret = "SYNTHETIC-SECRET-DO-NOT-EXPORT";
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "7.8.9");
        log.WriteFailure(DiagnosticEventName.ConnectionCompleted, DiagnosticResultCode.Failure, new Exception(secret));
        string zipPath = temporary.Child("diagnostics.zip");
        await new DiagnosticBundleExporter(log).ExportAsync(zipPath);
        log.Write(new(DiagnosticEventName.DiagnosticExportCompleted, Code: DiagnosticResultCode.Success));
        Assert.IsTrue(log.IsAvailable);

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(entry => entry.FullName).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "logs/events-0.jsonl", "manifest.json" }, names);
        string all = string.Join('\n', archive.Entries.Select(ReadEntry));
        Assert.IsFalse(all.Contains(secret, StringComparison.Ordinal));
        using var manifest = JsonDocument.Parse(ReadEntry(archive.GetEntry("manifest.json")!));
        var keys = manifest.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "appVersion", "generatedUtc", "logFileCount", "loggerAvailable", "loggerHealth", "osVersion", "schema", "uiCulture" }.Order().ToArray(), keys);
        Assert.IsFalse(all.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(all.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CancelAndCommitFailurePreserveLogsAndRemoveTemporaryFiles()
    {
        using var temporary = new TemporaryDirectory();
        using var log = DiagnosticEventLog.Open(temporary.Child("logs"), "test");
        log.Write(new(DiagnosticEventName.AppStarted));
        byte[] before = log.Snapshot().Single().Content;
        var exporter = new DiagnosticBundleExporter(log);

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => exporter.ExportAsync(temporary.Child("cancelled.zip"), cancelled.Token));
        Directory.CreateDirectory(temporary.Child("blocked.zip"));
        var failure = await Assert.ThrowsExactlyAsync<DiagnosticExportException>(() => exporter.ExportAsync(temporary.Child("blocked.zip")));
        Assert.AreEqual("diagnostics-export-failed", failure.Code);
        CollectionAssert.AreEqual(before, log.Snapshot().Single().Content);
        Assert.AreEqual(0, Directory.EnumerateFiles(temporary.Path, ".phonebridge-diagnostics-*.tmp").Count());
    }

    [TestMethod]
    public void ErrorCodesAreAllowlistedAndUnknownInputIsCollapsed()
    {
        Assert.AreEqual(DiagnosticResultCode.Unauthorized, DiagnosticCodeMap.From("unauthorized"));
        Assert.AreEqual(DiagnosticResultCode.PendingWrites, DiagnosticCodeMap.From("pending-writes-not-confirmed"));
        Assert.AreEqual(DiagnosticResultCode.Failure, DiagnosticCodeMap.From("secret=must-not-pass-through"));
    }

    private static IEnumerable<string> Lines(DiagnosticEventLog log) => log.Snapshot()
        .SelectMany(file => Encoding.UTF8.GetString(file.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    private static string ReadEntry(ZipArchiveEntry entry)
    { using var reader = new StreamReader(entry.Open(), Encoding.UTF8); return reader.ReadToEnd(); }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pbng-log-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);
        internal string Child(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
