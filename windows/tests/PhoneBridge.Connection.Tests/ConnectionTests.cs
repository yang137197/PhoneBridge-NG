using System.Security.Cryptography;
using System.Text.Json;
using PhoneBridge.Credentials;
using PhoneBridge.Mounting;

namespace PhoneBridge.Connection.Tests;

[TestClass]
public sealed class ConnectionTests
{
    private static PairingStore Store()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
        string parent = Path.Combine(d!.FullName, ".audit", "p1-008", "tests"); Directory.CreateDirectory(parent);
        return PairingStore.OpenAt(Path.Combine(parent, Guid.NewGuid().ToString("N")));
    }
    private static MountOptions NoMount => new('Z', Path.GetFullPath("missing-rclone.exe"), Path.GetTempPath());
    private static string Status(NetworkPeer peer, string state) => JsonSerializer.Serialize(new { attempt_id = peer.Attempt, client_id = peer.ClientId, state });
    private static Reply Session(NetworkPeer peer) => new(200, JsonSerializer.Serialize(new { device_id = peer.Identity.DeviceId, client_id = peer.ClientId, mode = "safe", share_ready = true }));
    private static Reply Unauthorized => new(401, "{\"code\":\"unauthorized\"}");
    private sealed class StageObserver(Action<ConnectionStage> action) : IProgress<ConnectionStage> { public void Report(ConnectionStage value) => action(value); }

    [TestMethod]
    public async Task StrictCaAndIpTlsAcceptsOnlyMatchingIdentity()
    {
        await using var server = new NetworkPeer(); await using var other = new NetworkPeer();
        using var valid = new DeviceApi(server.Identity, server.Endpoint);
        Assert.AreEqual(401, (await valid.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default)).Status);
        using var wrong = new DeviceApi(other.Identity, server.Endpoint);
        await Assert.ThrowsAsync<HttpRequestException>(() => wrong.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default));
        Assert.AreEqual(1, server.RequestCount);
    }
    [TestMethod]
    public async Task WrongSanSendsNoAuthorization()
    {
        await using var server = new NetworkPeer(wrongSan: true);
        using var api = new DeviceApi(server.Identity, server.Endpoint);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default));
        Assert.AreEqual(0, server.RequestCount);
    }
    [TestMethod]
    [DataRow(302, "{}", "Location: https://example.invalid/\r\n")]
    [DataRow(200, "{}", "Content-Encoding: gzip\r\n")]
    public async Task RedirectAndEncodingRejected(int status, string body, string extra)
    {
        await using var server = new NetworkPeer { Respond = _ => Task.FromResult(new Reply(status, body, extra)) };
        using var api = new DeviceApi(server.Identity, server.Endpoint);
        await Assert.ThrowsAsync<ConnectionException>(() => api.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default));
        Assert.AreEqual(1, server.RequestCount);
    }
    [TestMethod]
    public async Task OversizedResponseAndCallerCancellationAreBounded()
    {
        await using var server = new NetworkPeer { Respond = _ => Task.FromResult(new Reply(200, new string('a', 4097))) };
        using var api = new DeviceApi(server.Identity, server.Endpoint);
        await Assert.ThrowsAsync<ConnectionException>(() => api.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default));
        server.Respond = _ => Task.FromResult(new Reply(200, "{}", Delay: 30000));
        using var cancel = new CancellationTokenSource(200);
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, cancel.Token));
    }
    [TestMethod]
    public async Task SilentHttpPeerHitsRequestDeadline()
    {
        await using var server = new NetworkPeer { Respond = _ => Task.FromResult(new Reply(200, "{}", Delay: 30000)) };
        using var api = new DeviceApi(server.Identity, server.Endpoint);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", "synthetic", null, default));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.AreEqual(1, server.RequestCount);
    }
    [TestMethod]
    public async Task WrongCodeAndTrailingFrameNeverCreateTokenOrSendHttps()
    {
        foreach (bool trailing in new[] { false, true })
        {
            await using var server = new NetworkPeer(); server.StartPairing(trailing: trailing);
            var store = Store(); await using var client = new ConnectionClient(store);
            char[] code = (trailing ? "01234567" : "87654321").ToCharArray();
            await Assert.ThrowsAsync<Exception>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, code, "Synthetic PC", NoMount, null, default));
            Assert.IsEmpty(store.List()); Assert.AreEqual(0, server.RequestCount); Assert.IsTrue(code.All(c => c == '\0'));
        }
    }
    [TestMethod]
    public async Task PakeCancellationDoesNotWaitForFrameDeadline()
    {
        await using var server = new NetworkPeer(); server.StartPairing(hold: true);
        var store = Store(); await using var client = new ConnectionClient(store);
        using var cancel = new CancellationTokenSource(200);
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, "01234567".ToCharArray(), "Synthetic PC", NoMount, null, cancel.Token));
        Assert.IsEmpty(store.List());
    }
    [TestMethod]
    public async Task SilentPakePeerHitsFrameDeadline()
    {
        await using var server = new NetworkPeer(); server.StartPairing(hold: true);
        var store = Store(); await using var client = new ConnectionClient(store);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, "01234567".ToCharArray(), "Synthetic PC", NoMount, null, default));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10)); Assert.IsEmpty(store.List());
    }
    [TestMethod]
    public async Task CancellationCannotDisableAReplacementClientRecord()
    {
        await using var server = new NetworkPeer(); server.StartPairing();
        var store = Store(); await using var client = new ConnectionClient(store);
        server.Respond = _ => Task.FromResult(new Reply(202, Status(server, "PendingApproval")));
        using var cancel = new CancellationTokenSource();
        const string replacement = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        await Assert.ThrowsAsync<CredentialStoreException>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, "01234567".ToCharArray(), "Synthetic PC", NoMount,
            new StageObserver(stage =>
            {
                if (stage != ConnectionStage.WaitingApproval) return;
                store.RemoveLocally(store.BeginRevocation(store.List().Single()));
                store.CreatePending(server.Identity, replacement, "New phone", "New PC");
                cancel.Cancel();
            }), cancel.Token));
        var retained = store.List().Single();
        Assert.AreEqual(replacement, retained.ClientId); Assert.AreEqual(PairingRecordState.Pending, retained.State);
        Assert.AreEqual(1, server.RequestCount);
    }
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task CancelBeforeAndAfterApprovalDisablesAndRevokes(bool alreadyApproved)
    {
        await using var server = new NetworkPeer(); server.StartPairing();
        var store = Store(); await using var client = new ConnectionClient(store);
        bool sawPost = false, deleted = false;
        server.Respond = request =>
        {
            var record = store.Load(server.Identity.DeviceId);
            if (request.Method == "POST")
            {
                Assert.AreEqual(PairingRecordState.Pending, record.State);
                using var body = JsonDocument.Parse(request.Body);
                using var lease = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.PairingSubmission);
                var token = lease.CopyToken();
                try { Assert.AreEqual(DeviceApi.Encode(token), body.RootElement.GetProperty("credential").GetString()); }
                finally { CryptographicOperations.ZeroMemory(token); }
                sawPost = true; return Task.FromResult(new Reply(202, Status(server, "PendingApproval")));
            }
            Assert.AreEqual(PairingRecordState.RevocationPending, record.State);
            if (request.Path.Contains("/pairing/", StringComparison.Ordinal))
                return Task.FromResult(alreadyApproved ? new Reply(409, "{\"code\":\"conflict\"}") : new Reply(410, "{\"code\":\"cancelled\"}"));
            if (request.Method == "DELETE" && alreadyApproved) { deleted = true; return Task.FromResult(new Reply(204, "")); }
            return Task.FromResult(Unauthorized);
        };
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, "01234567".ToCharArray(), "Synthetic PC", NoMount,
            new StageObserver(stage => { if (stage == ConnectionStage.WaitingApproval) cancel.Cancel(); }), cancel.Token));
        Assert.IsTrue(sawPost); Assert.AreEqual(alreadyApproved, deleted); Assert.IsEmpty(store.List());
    }
    [TestMethod]
    public async Task LostPostReplyPreservesPendingAndRecoveryOnlyUsesSession()
    {
        await using var server = new NetworkPeer(); server.StartPairing();
        var store = Store(); await using var client = new ConnectionClient(store);
        int posts = 0;
        server.Respond = request =>
        {
            if (request.Method == "POST") { posts++; return Task.FromResult(new Reply(200, "", Disconnect: true)); }
            Assert.AreEqual("/phonebridge/v1/session", request.Path); return Task.FromResult(Session(server));
        };
        await Assert.ThrowsAsync<HttpRequestException>(() => client.PairAndConnectAsync(server.Candidate, server.Endpoint, "01234567".ToCharArray(), "Synthetic PC", NoMount, null, default));
        Assert.AreEqual(PairingRecordState.Pending, store.List().Single().State);
        await Assert.ThrowsAsync<MountException>(() => client.ConnectAsync(server.Identity.DeviceId, server.Endpoint, NoMount, null, default));
        Assert.AreEqual(PairingRecordState.Active, store.List().Single().State); Assert.AreEqual(1, posts);
    }
    [TestMethod]
    public async Task OfflineRevokeCannotBeReactivatedOrMounted()
    {
        await using var server = new NetworkPeer(); var store = Store();
        var record = store.CreatePending(server.Identity, new string('a', 32), "Phone", "PC");
        record = store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, AccessMode.Safe);
        await using var client = new ConnectionClient(store);
        Assert.IsFalse(await client.RevokeAsync(record.DeviceId, null, default));
        Assert.AreEqual(PairingRecordState.RevocationPending, store.List().Single().State);
        await Assert.ThrowsAsync<CredentialStoreException>(() => client.ConnectAsync(record.DeviceId, server.Endpoint, NoMount, null, default));
        Assert.Throws<CredentialStoreException>(() => store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, AccessMode.Safe));
        Assert.AreEqual(0, server.RequestCount);
    }
    [TestMethod]
    public async Task LocalRemovalNeedsNoEndpointAndAllowsFreshPairing()
    {
        await using var server = new NetworkPeer(); var store = Store();
        var record = store.CreatePending(server.Identity, new string('a', 32), "Phone", "PC");
        record = store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, AccessMode.Safe);
        await using var client = new ConnectionClient(store);
        Assert.IsTrue(await client.RemoveLocallyAsync(record.DeviceId));
        Assert.IsEmpty(store.List()); Assert.AreEqual(0, server.RequestCount);
        var fresh = store.CreatePending(server.Identity, new string('b', 32), "Phone", "PC");
        Assert.AreEqual(PairingRecordState.Pending, fresh.State);
    }
    [TestMethod]
    public async Task DuplicateWrongIdentityAndNotReadySessionCannotActivate()
    {
        await using var server = new NetworkPeer(); var store = Store();
        var record = store.CreatePending(server.Identity, new string('a', 32), "Phone", "PC");
        await using var client = new ConnectionClient(store);
        string valid = JsonSerializer.Serialize(new { device_id = record.DeviceId, client_id = record.ClientId, mode = "safe", share_ready = true });
        foreach (string body in new[] { valid.Replace("{", "{\"mode\":\"safe\",", StringComparison.Ordinal), valid.Replace(record.ClientId, new string('b', 32), StringComparison.Ordinal), valid.Replace("true", "false", StringComparison.Ordinal) })
        {
            server.Respond = _ => Task.FromResult(new Reply(200, body));
            await Assert.ThrowsAsync<ConnectionException>(() => client.ConnectAsync(record.DeviceId, server.Endpoint, NoMount, null, default));
            Assert.AreEqual(PairingRecordState.Pending, store.List().Single().State);
        }
    }

    [TestMethod]
    public async Task SessionHealthUsesProtectedActiveRecordAndFailsClosed()
    {
        await using var server = new NetworkPeer(); var store = Store();
        var record = store.CreatePending(server.Identity, new string('a', 32), "Phone", "PC");
        record = store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, AccessMode.Safe);
        await using var client = new ConnectionClient(store);
        server.Respond = _ => Task.FromResult(new Reply(200, JsonSerializer.Serialize(new
            { device_id = record.DeviceId, client_id = record.ClientId, mode = "safe", share_ready = true })));
        Assert.IsTrue(await client.CheckSessionAsync(record.DeviceId, server.Endpoint, default));

        server.Respond = _ => Task.FromResult(new Reply(200, JsonSerializer.Serialize(new
            { device_id = record.DeviceId, client_id = record.ClientId, mode = "safe", share_ready = false })));
        var notReady = await Assert.ThrowsAsync<ConnectionException>(() =>
            client.CheckSessionAsync(record.DeviceId, server.Endpoint, default));
        Assert.AreEqual("share-not-ready", notReady.Code);

        server.Respond = _ => Task.FromResult(new Reply(200, JsonSerializer.Serialize(new
            { device_id = record.DeviceId, client_id = record.ClientId, mode = "readOnly", share_ready = true })));
        var changed = await Assert.ThrowsAsync<ConnectionException>(() =>
            client.CheckSessionAsync(record.DeviceId, server.Endpoint, default));
        Assert.AreEqual("mode-changed", changed.Code);
        Assert.AreEqual(AccessMode.ReadOnly, store.Load(record.DeviceId).Mode);

        server.Respond = _ => Task.FromResult(Unauthorized);
        var unauthorized = await Assert.ThrowsAsync<ConnectionException>(() =>
            client.CheckSessionAsync(record.DeviceId, server.Endpoint, default));
        Assert.AreEqual("unauthorized", unauthorized.Code);
    }

    [TestMethod]
    public void ReconnectPolicyDebouncesHealthFailuresAndResetsOnSuccess()
    {
        var policy = new ReconnectPolicy();
        var now = DateTimeOffset.Parse("2026-09-21T00:00:00Z");
        policy.Arm("pbng-device", NoMount, now);
        Assert.IsFalse(policy.HealthDue(now.AddSeconds(4)));
        Assert.IsTrue(policy.HealthDue(now.AddSeconds(5)));
        Assert.IsFalse(policy.HealthFailed(now.AddSeconds(5)));
        Assert.IsFalse(policy.HealthFailed(now.AddSeconds(10)));
        Assert.AreEqual(2, policy.ConsecutiveHealthFailures);
        policy.HealthSucceeded(now.AddSeconds(11));
        Assert.AreEqual(0, policy.ConsecutiveHealthFailures);
        Assert.IsFalse(policy.HealthFailed(now.AddSeconds(16)));
        Assert.IsFalse(policy.HealthFailed(now.AddSeconds(21)));
        Assert.IsTrue(policy.HealthFailed(now.AddSeconds(26)));
    }

    [TestMethod]
    public void ReconnectPolicyRequiresExactIdentityBacksOffAndCanBeSuppressed()
    {
        var policy = new ReconnectPolicy();
        var now = DateTimeOffset.Parse("2026-09-21T00:00:00Z");
        policy.Arm("pbng-device", NoMount, now);
        policy.Disconnected(now);
        Assert.IsFalse(policy.CanReconnect("pbng-other", now));
        Assert.IsTrue(policy.CanReconnect("pbng-device", now));
        foreach (var delay in new[] { 2, 5, 10, 20, 30, 30 })
        {
            policy.ReconnectFailed(now);
            Assert.AreEqual(now.AddSeconds(delay), policy.NextAttempt);
            Assert.IsFalse(policy.CanReconnect("pbng-device", policy.NextAttempt.AddMilliseconds(-1)));
            Assert.IsTrue(policy.CanReconnect("pbng-device", policy.NextAttempt));
            now = policy.NextAttempt;
        }
        policy.Suppress();
        Assert.IsFalse(policy.Armed);
        Assert.IsFalse(policy.CanReconnect("pbng-device", now.AddHours(1)));
    }
}
