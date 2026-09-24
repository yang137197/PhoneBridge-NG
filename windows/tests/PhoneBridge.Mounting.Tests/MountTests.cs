using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PhoneBridge.Mounting;

[assembly: DoNotParallelize]
namespace PhoneBridge.Mounting.Tests;

[TestClass]
public sealed class MountTests
{
    private static readonly MountTimeouts Limits = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(80));

    internal static ConfirmedIdentity Identity()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=PhoneBridge synthetic CA", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var der = certificate.Export(X509ContentType.Cert);
        return new(der, Convert.ToHexStringLower(SHA256.HashData(der)));
    }

    internal static MountRequest Request(string remote = "test folder/中文", char drive = 'P', string address = "192.0.2.1") =>
        new(Identity(), new("synthetic-user", "synthetic-password"), address, 8443, remote, drive,
            @"C:\test folder\rclone.exe", @"C:\test folder\sessions");

    private sealed class FakeSession : IMountSession
    {
        internal readonly TaskCompletionSource<int> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Ready = true, Drive = true, StopExits = true, KillExits = true, Disposed;
        internal bool SafeToForce = true;
        internal int Stops, Kills;
        internal string? ReadyFailure;
        internal int FailAfterProbes;
        internal int ProbeCalls;
        internal MountProbe? Probe;
        internal readonly Queue<MountProbe> Probes = new();
        internal string? StopFailure;
        public int ProcessId => 1234;
        public Task<int> Exit => Completion.Task;
        public bool IsDrivePresent => Drive;
        public bool ForceStopSafe => SafeToForce;
        public Task<MountProbe> ProbeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ProbeCalls++;
            if (ReadyFailure is { } code && ProbeCalls > FailAfterProbes) throw new MountException(code);
            if (Probes.TryDequeue(out var queued)) return Task.FromResult(queued);
            return Task.FromResult(Probe ?? new(Ready ? MountReadiness.Ready : MountReadiness.Pending));
        }
        public async Task RequestStopAsync(CancellationToken token)
        {
            Stops++;
            if (StopFailure is { } code) throw new MountException(code);
            if (StopExits) { Drive = false; Completion.TrySetResult(0); }
            else await Task.Delay(Timeout.Infinite, token);
        }
        public void KillOwnedProcess() { Kills++; if (KillExits) { Drive = false; Completion.TrySetResult(1); } }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [TestMethod]
    public async Task DuplicateStartAndConcurrentStopsRetainSingleOwner()
    {
        var session = new FakeSession();
        var starts = 0;
        await using var manager = new ReadOnlyMountManager((_, _) => { starts++; return Task.FromResult<IMountSession>(session); }, Limits);
        Assert.AreEqual(MountState.Mounted, (await manager.StartAsync(Request())).State);
        Assert.Throws<MountException>(() => manager.StartAsync(Request()));
        var stopped = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.StopAsync()));
        Assert.IsTrue(stopped.All(s => s.State == MountState.Stopped && !s.Forced));
        Assert.AreEqual(1, starts); Assert.AreEqual(1, session.Stops); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task StartFailureDoesNotExposeExceptionText()
    {
        await using var manager = new ReadOnlyMountManager((_, _) => throw new IOException("synthetic secret must not escape"), Limits);
        var error = await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual("mount-start-failed", error.Message);
        Assert.AreEqual(MountState.Failed, manager.Snapshot.State);
    }

    [TestMethod]
    public async Task StopDuringStartupCleansOwnedChild()
    {
        var session = new FakeSession { Ready = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        var start = manager.StartAsync(Request());
        var stopped = await manager.StopAsync();
        await Assert.ThrowsAsync<MountException>(() => start);
        Assert.AreEqual(MountState.Stopped, stopped.State); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task CallerCancellationCleansStartup()
    {
        var session = new FakeSession { Ready = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        using var cancel = new CancellationTokenSource();
        var start = manager.StartAsync(Request(), cancel.Token); cancel.Cancel();
        var error = await Assert.ThrowsAsync<MountException>(() => start);
        Assert.AreEqual("start-cancelled", error.Code); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task StartupTimeoutIsBoundedAndCleaned()
    {
        var session = new FakeSession { Ready = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits with { Startup = TimeSpan.FromMilliseconds(100) });
        var error = await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual("startup-timeout", error.Code); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task ProgressingDirtyCacheCanOutliveStartupDeadlineAndBecomeReadyInSameSession()
    {
        // Real rclone recovery may start before WinFsp exposes the drive letter.
        var session = new FakeSession { Drive = false };
        var starts = 0;
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 0));
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 100));
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 200));
        session.Probes.Enqueue(new(MountReadiness.Ready, 200));
        var limits = Limits with { Startup = TimeSpan.FromMilliseconds(60), RecoveryIdle = TimeSpan.FromMilliseconds(250) };
        await using var manager = new ReadOnlyMountManager((_, _) => { starts++; return Task.FromResult<IMountSession>(session); }, limits);
        var mounted = await manager.StartAsync(Request());
        Assert.AreEqual(MountState.Mounted, mounted.State);
        Assert.AreEqual(4, session.ProbeCalls);
        Assert.AreEqual(1, starts);
        Assert.AreEqual(0, session.Stops);
        session.Drive = true;
        await manager.StopAsync();
    }

    [TestMethod]
    public async Task ProgressingRecoveryStillHasAnAbsoluteBoundAndRetainsCache()
    {
        var session = new FakeSession { Probe = new(MountReadiness.RecoveringWrites, 400), SafeToForce = false };
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 0));
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 100));
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 200));
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 300));
        var limits = Limits with { RecoveryIdle = TimeSpan.FromSeconds(1), RecoveryMaximum = TimeSpan.FromMilliseconds(230) };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), limits);
        var error = await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual("recovery-timeout-cache-retained", error.Code);
        Assert.AreEqual(MountState.StopFailed, manager.Snapshot.State);
        Assert.AreEqual(0, session.Kills); Assert.IsFalse(session.Disposed);
        session.SafeToForce = true;
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
    }

    [TestMethod]
    public async Task StalledDirtyCacheIsRetainedWithoutKillingOwnedProcess()
    {
        var session = new FakeSession { Probe = new(MountReadiness.RecoveringWrites, 42), SafeToForce = false };
        var limits = Limits with { RecoveryIdle = TimeSpan.FromMilliseconds(140), RecoveryMaximum = TimeSpan.FromSeconds(2) };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), limits);
        var error = await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual("recovery-stalled-cache-retained", error.Code);
        Assert.AreEqual(MountState.StopFailed, manager.Snapshot.State);
        Assert.AreEqual(0, session.Stops); Assert.AreEqual(0, session.Kills); Assert.IsFalse(session.Disposed);
        session.Probe = null; session.Ready = true; session.SafeToForce = true;
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
    }

    [TestMethod]
    public async Task CancellingDirtyCacheRecoveryReturnsPromptlyAndRetainsCacheOwner()
    {
        var session = new FakeSession { Probe = new(MountReadiness.RecoveringWrites, 42), SafeToForce = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        using var cancel = new CancellationTokenSource();
        var start = manager.StartAsync(Request(), cancel.Token);
        await WaitState(manager, MountState.RecoveringWrites);
        cancel.Cancel();
        var error = await Assert.ThrowsAsync<MountException>(() => start);
        Assert.AreEqual("recovery-cancelled-cache-retained", error.Code);
        Assert.AreEqual(MountState.StopFailed, manager.Snapshot.State);
        Assert.AreEqual(0, session.Stops); Assert.AreEqual(0, session.Kills); Assert.IsFalse(session.Disposed);
        session.SafeToForce = true;
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
    }

    [TestMethod]
    public async Task IdentityFailureAfterRecoveryNeverBecomesMounted()
    {
        var session = new FakeSession { ReadyFailure = "rclone-control-request-failed", FailAfterProbes = 1 };
        session.Probes.Enqueue(new(MountReadiness.RecoveringWrites, 100));
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        var error = await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual("rclone-control-request-failed", error.Code);
        Assert.AreEqual(MountState.Failed, manager.Snapshot.State);
        Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task GracefulTimeoutForcesOnlyOwnedChildAndReportsIt()
    {
        var session = new FakeSession { StopExits = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        var stopped = await manager.StopAsync();
        Assert.AreEqual(MountState.Stopped, stopped.State); Assert.IsTrue(stopped.Forced);
        Assert.AreEqual(1, session.Kills); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task FailedTerminationRetainsOwnershipAndBlocksRestartUntilRetry()
    {
        var session = new FakeSession { StopExits = false, KillExits = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        Assert.AreEqual(MountState.StopFailed, (await manager.StopAsync()).State);
        Assert.IsFalse(session.Disposed);
        Assert.Throws<MountException>(() => manager.StartAsync(Request()));
        session.Drive = false; session.Completion.TrySetResult(1);
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
        Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task PendingWritableCacheIsNeverForceKilled()
    {
        var session = new FakeSession { SafeToForce = false, StopFailure = "pending-writes-not-confirmed" };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        var stopped = await manager.StopAsync();
        Assert.AreEqual(MountState.StopFailed, stopped.State);
        Assert.AreEqual("pending-writes-not-confirmed", stopped.ErrorCode);
        Assert.AreEqual(0, session.Kills); Assert.IsFalse(session.Disposed);
        session.SafeToForce = true; session.StopFailure = null;
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
    }

    [TestMethod]
    public async Task LingeringDriveCannotBeReportedStopped()
    {
        var session = new FakeSession();
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        session.Completion.TrySetResult(99); // process exit does not prove drive removal
        await WaitState(manager, MountState.StopFailed);
        Assert.IsFalse(session.Disposed);
        session.Drive = false;
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
    }

    [TestMethod]
    public async Task UnexpectedExitIsFailedAndPreservesExitCode()
    {
        var session = new FakeSession();
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        session.Drive = false; session.Completion.TrySetResult(99);
        await WaitState(manager, MountState.Failed);
        Assert.AreEqual(99, manager.Snapshot.ExitCode); Assert.AreEqual("rclone-unexpected-exit", manager.Snapshot.ErrorCode);
    }

    [TestMethod]
    public async Task IdentityFailureAtRemoteReadinessNeverBecomesMounted()
    {
        var session = new FakeSession { ReadyFailure = "rclone-control-request-failed" };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await Assert.ThrowsAsync<MountException>(() => manager.StartAsync(Request()));
        Assert.AreEqual(MountState.Failed, manager.Snapshot.State); Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task DisposeRejectsFurtherStart()
    {
        var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(new FakeSession()), Limits);
        await manager.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => manager.StartAsync(Request()));
    }

    [TestMethod]
    public async Task CancellingStopWaitDoesNotAbandonCleanup()
    {
        var session = new FakeSession { StopExits = false };
        await using var manager = new ReadOnlyMountManager((_, _) => Task.FromResult<IMountSession>(session), Limits);
        await manager.StartAsync(Request());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => manager.StopAsync(cancelled.Token));
        Assert.AreEqual(MountState.Stopped, (await manager.StopAsync()).State);
        Assert.IsTrue(session.Disposed);
    }

    [TestMethod]
    public async Task CompletedStopAllowsNewSession()
    {
        var starts = 0;
        await using var manager = new ReadOnlyMountManager((_, _) => { starts++; return Task.FromResult<IMountSession>(new FakeSession()); }, Limits);
        await manager.StartAsync(Request()); await manager.StopAsync();
        await manager.StartAsync(Request()); await manager.StopAsync();
        Assert.AreEqual(2, starts);
    }

    [TestMethod]
    public void ActualProcessStartFailureIsSanitized()
    {
        var info = RcloneMountSession.ChildInfo(@"C:\P1-002 nonexistent folder\synthetic-secret.exe");
        var error = Assert.Throws<MountException>(() => OwnedProcess.Start(info));
        Assert.AreEqual("process-start-failed", error.Message);
    }

    [TestMethod]
    public async Task ActualWindowsArgumentBoundaryPreservesQuotesSpacesAndUnicode()
    {
        var output = new DirectoryInfo(Path.GetDirectoryName(typeof(MountTests).Assembly.Location)!);
        var fixture = Path.Combine(output.Parent!.Parent!.Parent!.Parent!.FullName, "PhoneBridge.ProcessFixture", "bin",
            output.Parent.Name, output.Name, "PhoneBridge.ProcessFixture.dll");
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "../../..", "dotnet.exe"));
        var arguments = new[] { "", "中文 with spaces", "quote\"inside", @"C:\folder with space\", @"slash\before\" + '"', "--literal" };
        var info = RcloneMountSession.ChildInfo(dotnet);
        foreach (var arg in new[] { fixture }.Concat(arguments)) info.ArgumentList.Add(arg);
        await using var process = OwnedProcess.Start(info, 4096);
        Assert.AreEqual(0, await process.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
        CollectionAssert.AreEqual(arguments, System.Text.Json.JsonSerializer.Deserialize<string[]>(await process.StandardOutput));
    }

    private static async Task WaitState(ReadOnlyMountManager manager, MountState state)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (manager.Snapshot.State != state) await Task.Delay(10, limit.Token);
    }

    [TestMethod]
    [DataRow("../Music")][DataRow("a/../b")][DataRow("/Music")][DataRow("a//b")]
    [DataRow("a%2fb")][DataRow("a\\b")][DataRow("a:b")][DataRow("a?b")][DataRow("a#b")]
    public void AmbiguousRemotePathsAreRejected(string path) => Assert.Throws<MountException>(() => Request(path));

    [TestMethod]
    [DataRow("http://192.0.2.1")][DataRow("127.0.0.1")][DataRow("0.0.0.0")][DataRow("phone.local")]
    public void EndpointRequiresLiteralUnicastIp(string address) => Assert.Throws<MountException>(() => Request(address: address));

    [TestMethod]
    public void IdentityFingerprintMismatchAndNonCertificateAreRejected()
    {
        Assert.Throws<MountException>(() => new ConfirmedIdentity([1, 2], new string('0', 64)));
        Assert.Throws<MountException>(() => new ConfirmedIdentity([1, 2], Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2 }))));
    }

    [TestMethod]
    public void ArgumentsAreSeparateAndCannotDisableTlsOrReadonly()
    {
        var request = Request();
        var info = RcloneMountSession.MountInfo(request, @"C:\test folder\ca.pem", @"C:\test folder\rclone.conf", 12345, "rc-secret-test");
        Assert.IsFalse(info.UseShellExecute); Assert.IsTrue(info.CreateNoWindow);
        Assert.AreEqual("", info.Arguments);
        CollectionAssert.Contains(info.ArgumentList.ToArray(), "--read-only");
        CollectionAssert.DoesNotContain(info.ArgumentList.ToArray(), "--no-check-certificate");
        CollectionAssert.Contains(info.ArgumentList.ToArray(), @"C:\test folder\ca.pem");
        Assert.IsFalse(string.Join(' ', info.ArgumentList).Contains("secret", StringComparison.Ordinal));
        StringAssert.Contains(RcloneMountSession.ConfigText(request, "obscured-test"),
            "url = https://192.0.2.1:8443/test%20folder/%E4%B8%AD%E6%96%87/\n");
        Assert.AreEqual("[redacted]", new SessionCredentials("user", "secret").ToString());
    }

    [TestMethod]
    public void SafeWritesAreDirectAndReadWriteUsesIsolatedRecoverableCache()
    {
        var source = Request();
        MountRequest WithMode(MountAccessMode mode) => new(source.Identity,
            new SessionCredentials("synthetic-user", "synthetic-password"), source.Endpoint.Address,
            source.Endpoint.Port, source.RemoteDirectory, source.DriveLetter, source.RclonePath,
            source.SessionRoot, mode);

        var safe = RcloneMountSession.MountInfo(WithMode(MountAccessMode.Safe), @"C:\test folder\ca.pem",
            @"C:\test folder\rclone.conf", 12345, "control");
        CollectionAssert.DoesNotContain(safe.ArgumentList.ToArray(), "--read-only");
        int safeCache = safe.ArgumentList.IndexOf("--vfs-cache-mode");
        Assert.IsGreaterThanOrEqualTo(0, safeCache); Assert.AreEqual("off", safe.ArgumentList[safeCache + 1]);
        CollectionAssert.DoesNotContain(safe.ArgumentList.ToArray(), "--cache-dir");

        var readWriteRequest = WithMode(MountAccessMode.ReadWrite);
        var readWrite = RcloneMountSession.MountInfo(readWriteRequest, @"C:\test folder\ca.pem",
            @"C:\test folder\rclone.conf", 12345, "control");
        CollectionAssert.DoesNotContain(readWrite.ArgumentList.ToArray(), "--read-only");
        int cache = readWrite.ArgumentList.IndexOf("--vfs-cache-mode");
        Assert.IsGreaterThanOrEqualTo(0, cache); Assert.AreEqual("writes", readWrite.ArgumentList[cache + 1]);
        int root = readWrite.ArgumentList.IndexOf("--cache-dir");
        Assert.IsGreaterThanOrEqualTo(0, root); Assert.AreEqual(readWriteRequest.CacheRoot, readWrite.ArgumentList[root + 1]);
        CollectionAssert.Contains(readWrite.ArgumentList.ToArray(), "--vfs-cache-max-size");
        CollectionAssert.Contains(readWrite.ArgumentList.ToArray(), "--vfs-cache-min-free-space");
    }

    [TestMethod]
    public void CacheDrainRequiresEmptyQueueAndHealthyCounters()
    {
        using var emptyQueue = JsonDocument.Parse("{\"queue\":[]}");
        using var healthy = JsonDocument.Parse("{\"diskCache\":{\"uploadsInProgress\":0,\"uploadsQueued\":0,\"erroredFiles\":0,\"outOfSpace\":false}}");
        Assert.IsTrue(RcloneMountSession.CacheDrained(emptyQueue.RootElement, healthy.RootElement));
        using var queued = JsonDocument.Parse("{\"queue\":[{\"name\":\"must-not-be-logged\"}]}");
        Assert.IsFalse(RcloneMountSession.CacheDrained(queued.RootElement, healthy.RootElement));
        using var failed = JsonDocument.Parse("{\"diskCache\":{\"uploadsInProgress\":0,\"uploadsQueued\":0,\"erroredFiles\":1,\"outOfSpace\":false}}");
        Assert.AreEqual("pending-writes-not-confirmed",
            Assert.Throws<MountException>(() => RcloneMountSession.CacheDrained(emptyQueue.RootElement, failed.RootElement)).Code);
        using var malformed = JsonDocument.Parse("{\"diskCache\":{}}");
        Assert.AreEqual("rclone-cache-status-invalid",
            Assert.Throws<MountException>(() => RcloneMountSession.CacheDrained(emptyQueue.RootElement, malformed.RootElement)).Code);
    }

    [TestMethod]
    public void RecoveryProgressRequiresAValidNonNegativeByteCounter()
    {
        using var valid = JsonDocument.Parse("{\"bytes\":10000000000}");
        Assert.AreEqual(10_000_000_000, RcloneMountSession.TransferBytes(valid.RootElement));
        foreach (var json in new[] { "{}", "{\"bytes\":-1}", "{\"bytes\":\"1\"}" })
        {
            using var invalid = JsonDocument.Parse(json);
            Assert.AreEqual("rclone-cache-status-invalid",
                Assert.Throws<MountException>(() => RcloneMountSession.TransferBytes(invalid.RootElement)).Code);
        }
    }

    [TestMethod]
    public void WritableMountUsesStableNamedRemoteAndProtectedConfigBoundary()
    {
        var request = Request();
        var info = RcloneMountSession.MountInfo(request, @"C:\session\device-ca.pem", @"C:\session\rclone.conf", 12345, "control");
        CollectionAssert.Contains(info.ArgumentList.ToArray(), "phonebridge:");
        CollectionAssert.Contains(info.ArgumentList.ToArray(), @"C:\session\rclone.conf");
        Assert.IsFalse(info.Environment.Keys.Any(key => key.StartsWith("RCLONE_WEBDAV_", StringComparison.Ordinal)));
        var config = RcloneMountSession.ConfigText(request, "obscured");
        StringAssert.StartsWith(config, "[phonebridge]\n");
        StringAssert.Contains(config, "type = webdav\n");
        StringAssert.Contains(config, "pass = obscured\n");
        Assert.IsFalse(config.Contains(request.Credentials.Password, StringComparison.Ordinal));
    }

    [TestMethod]
    public void DirtyCacheMetadataBlocksShutdownAndMalformedMetadataFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhoneBridge-MountTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.IsFalse(RcloneMountSession.DirtyCachePresent(root));
            var meta = Path.Combine(root, "file");
            File.WriteAllText(meta, "{\"Dirty\":false}");
            Assert.IsFalse(RcloneMountSession.DirtyCachePresent(root));
            File.WriteAllText(meta, "{\"Dirty\":true}");
            Assert.IsTrue(RcloneMountSession.DirtyCachePresent(root));
            File.WriteAllText(meta, "{}");
            Assert.AreEqual("rclone-cache-status-invalid",
                Assert.Throws<MountException>(() => RcloneMountSession.DirtyCachePresent(root)).Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void HostEnvironmentCannotInjectRcloneOrProxyOptions()
    {
        var variables = new[] { "RCLONE_NO_CHECK_CERTIFICATE", "RCLONE_CONFIG", "HTTPS_PROXY" };
        var saved = variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in variables) Environment.SetEnvironmentVariable(name, "synthetic-injection");
            var info = RcloneMountSession.ChildInfo(@"C:\test folder\rclone.exe");
            foreach (var name in variables) Assert.IsFalse(info.Environment.ContainsKey(name));
        }
        finally { foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value); }
    }

    [TestMethod]
    public async Task ActualChildDrainsBothPipesWithBoundedCapture()
    {
        var info = RcloneMountSession.ChildInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"));
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "for ($i=0; $i -lt 2000; $i++) { [Console]::Out.WriteLine(('x' * 1024)); [Console]::Error.WriteLine(('y' * 1024)) }" }) info.ArgumentList.Add(arg);
        await using var process = OwnedProcess.Start(info, 128);
        Assert.AreEqual(0, await process.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(128, (await process.StandardOutput).Length);
    }

    [TestMethod]
    public async Task ClosingJobTerminatesOnlyTheRetainedChild()
    {
        var info = RcloneMountSession.ChildInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"));
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" }) info.ArgumentList.Add(arg);
        var process = OwnedProcess.Start(info);
        await Task.Delay(200);
        Assert.IsFalse(process.Exit.IsCompleted);
        await process.DisposeAsync();
        Assert.IsTrue(process.Exit.IsCompletedSuccessfully);
        // Kill-on-close promises termination, not a particular exit code.
    }
}
