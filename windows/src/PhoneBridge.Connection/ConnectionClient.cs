using System.Security.Cryptography;
using System.Text.Json;
using PhoneBridge.Credentials;
using PhoneBridge.Discovery;
using PhoneBridge.Mounting;

namespace PhoneBridge.Connection;

/// <summary>One serialized desktop owner. All synchronous crypto/storage runs on a worker.</summary>
public sealed class ConnectionClient(PairingStore store) : IAsyncDisposable
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly ReadOnlyMountManager mounts = new();
    private bool disposed;
    public ConnectedDevice? Connected { get; private set; }
    public MountSnapshot Mount => mounts.Snapshot;
    public Task<IReadOnlyList<PairingRecord>> RecordsAsync() => Task.Run(store.List);

    public Task<PairingRecord> PairAndConnectAsync(DeviceCandidate candidate, DeviceEndpoint endpoint, char[] code,
        string clientName, MountOptions options, IProgress<ConnectionStage>? progress, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            PairingRecord? record = null;
            DeviceApi? api = null;
            byte[]? grant = null;
            string? attempt = null;
            bool submitted = false;
            try
            {
                RequireIdle(); EndpointRules.CheckCandidate(candidate, endpoint);
                if (candidate.Pairing is not { } pairing) throw new ConnectionException("pairing-window-required");
                progress?.Report(ConnectionStage.ConfirmingCode);
                using var confirmed = await PakeTransport.ConfirmAsync(endpoint, pairing, code, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var der = confirmed.CandidateCaDer;
                var identity = ValidatedDeviceIdentity.Validate(der, Convert.ToHexStringLower(SHA256.HashData(der)));
                if (identity.DeviceId != candidate.DeviceIdHint) throw new ConnectionException("identity-mismatch");
                var verifiedEndpoint = new DeviceEndpoint(endpoint.Address, confirmed.HttpsPort);
                api = new DeviceApi(identity, verifiedEndpoint);
                grant = confirmed.CopyGrant();
                attempt = Convert.ToHexStringLower(confirmed.AttemptId);
                progress?.Report(ConnectionStage.SavingPending);
                record = store.CreatePending(identity, Convert.ToHexStringLower(confirmed.ClientId), candidate.DisplayName, clientName);
                using var credential = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.PairingSubmission);
                byte[] token = credential.CopyToken();
                byte[] body;
                try { body = JsonSerializer.SerializeToUtf8Bytes(new { client_id = record.ClientId, client_name = record.ClientName, credential = DeviceApi.Encode(token) }); }
                finally { CryptographicOperations.ZeroMemory(token); }
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Mark before send: an exception does not tell us whether the server received the POST.
                    submitted = true;
                    var response = await api.RequestAsync(HttpMethod.Post, PairPath(attempt), "Bearer", DeviceApi.Encode(grant), body, cancellationToken).ConfigureAwait(false);
                    bool active = DeviceApi.PairingState(response, attempt, record.ClientId);
                    progress?.Report(ConnectionStage.WaitingApproval);
                    using var approval = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    approval.CancelAfter(TimeSpan.FromSeconds(120));
                    while (!active)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1.1), approval.Token).ConfigureAwait(false);
                        response = await api.RequestAsync(HttpMethod.Get, PairPath(attempt), "Bearer", DeviceApi.Encode(grant), null, approval.Token).ConfigureAwait(false);
                        active = DeviceApi.PairingState(response, attempt, record.ClientId);
                    }
                }
                finally { CryptographicOperations.ZeroMemory(body); }
                record = await ValidateAsync(api, record, progress, cancellationToken).ConfigureAwait(false);
                await MountAsync(record, verifiedEndpoint, options, progress, cancellationToken).ConfigureAwait(false);
                return record;
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                // Independent cleanup deadline: UI cancellation cannot skip durable local disablement.
                if (record is not null && api is not null && grant is not null && attempt is not null)
                    await CancelPairingAsync(record, api, attempt, grant, submitted).ConfigureAwait(false);
                throw new OperationCanceledException(cancellationToken);
            }
            finally { Array.Clear(code); if (grant is not null) CryptographicOperations.ZeroMemory(grant); api?.Dispose(); }
        }, cancellationToken, () => Array.Clear(code));

    public Task<PairingRecord> ConnectAsync(string deviceId, DeviceEndpoint endpoint, MountOptions options,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        RequireIdle();
        var record = store.Load(deviceId);
        using var api = new DeviceApi(record.Identity, endpoint);
        record = await ValidateAsync(api, record, progress, cancellationToken).ConfigureAwait(false);
        await MountAsync(record, endpoint, options, progress, cancellationToken).ConfigureAwait(false);
        return record;
    }, cancellationToken);

    public Task<bool> CheckSessionAsync(string deviceId, DeviceEndpoint endpoint,
        CancellationToken cancellationToken) => RunAsync(async () =>
    {
        var record = store.Load(deviceId);
        if (!record.CanMount) throw new ConnectionException("record-changed");
        using var api = new DeviceApi(record.Identity, endpoint);
        using var credential = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.SessionValidation);
        var session = await api.SessionAsync(record, DeviceApi.Basic(credential), cancellationToken).ConfigureAwait(false);
        if (session is null) throw new ConnectionException("unauthorized");
        if (!session.ShareReady) throw new ConnectionException("share-not-ready");
        if (session.Mode != record.Mode)
        {
            store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, session.Mode);
            throw new ConnectionException("mode-changed");
        }
        return true;
    }, cancellationToken);

    private async Task<PairingRecord> ValidateAsync(DeviceApi api, PairingRecord record,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(ConnectionStage.Validating);
        using var credential = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.SessionValidation);
        var session = await api.SessionAsync(record, DeviceApi.Basic(credential), cancellationToken).ConfigureAwait(false);
        if (session is null) throw new ConnectionException("authorization-unconfirmed");
        if (!session.ShareReady) throw new ConnectionException("share-not-ready");
        cancellationToken.ThrowIfCancellationRequested();
        return store.ApplyVerifiedSession(record, record.DeviceId, record.ClientId, session.Mode);
    }

    private async Task MountAsync(PairingRecord record, DeviceEndpoint endpoint, MountOptions options,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Reload after session validation; a disabled/revised record cannot supply mount credentials.
        var current = store.Load(record.DeviceId);
        if (current.Revision != record.Revision || current.ClientId != record.ClientId || !current.CanMount)
            throw new ConnectionException("record-changed");
        using var lease = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.Mount);
        byte[] token = lease.CopyToken();
        try
        {
            var request = new MountRequest(new ConfirmedIdentity(record.Identity.CertificateDer, record.Identity.Sha256),
                new SessionCredentials(lease.Username, DeviceApi.Encode(token)), endpoint.Address, endpoint.Port, "",
                options.DriveLetter, options.RclonePath, options.SessionRoot, record.Mode switch
                {
                    AccessMode.Safe => MountAccessMode.Safe,
                    AccessMode.ReadWrite => MountAccessMode.ReadWrite,
                    _ => MountAccessMode.ReadOnly
                });
            progress?.Report(ConnectionStage.Mounting);
            await mounts.StartAsync(request, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await StopCoreAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            Connected = new(record, endpoint, options.DriveLetter);
            progress?.Report(ConnectionStage.Mounted);
        }
        finally { CryptographicOperations.ZeroMemory(token); }
    }

    public Task<bool> RevokeAsync(string deviceId, DeviceEndpoint? endpoint, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        var record = store.Load(deviceId);
        record = store.BeginRevocation(record); // Disable before stopping mount or touching the network.
        if (Connected?.Record.DeviceId == deviceId) await StopCoreAsync().ConfigureAwait(false);
        if (endpoint is null) return false;
        using var api = new DeviceApi(record.Identity, endpoint);
        return await RevokeRemoteAsync(record, api, false, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<DeletionPreview> PrepareDeletionAsync(string deviceId, DeviceEndpoint endpoint, string path,
        CancellationToken cancellationToken) => RunAsync(async () =>
    {
        path = NormalizeDeletionPath(path);
        var record = store.Load(deviceId);
        if (!record.CanMount || record.Mode == AccessMode.ReadOnly) throw new ConnectionException("rejected");
        using var api = new DeviceApi(record.Identity, endpoint);
        using var lease = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.Mount);
        return await api.PrepareDeletionAsync(record, DeviceApi.Basic(lease), path, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<bool> ConfirmDeletionAsync(DeletionPreview preview, DeviceEndpoint endpoint,
        CancellationToken cancellationToken) => RunAsync(async () =>
    {
        if (preview is null || preview.DeviceId.Length == 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(preview.ConfirmationId, "^[0-9a-f]{32}$"))
            throw new ConnectionException("invalid_request");
        var record = store.Load(preview.DeviceId);
        if (!record.CanMount || record.Mode == AccessMode.ReadOnly) throw new ConnectionException("rejected");
        using var api = new DeviceApi(record.Identity, endpoint);
        using var lease = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.Mount);
        await api.ConfirmDeletionAsync(DeviceApi.Basic(lease), preview, cancellationToken).ConfigureAwait(false);
        return true;
    }, cancellationToken);

    private async Task CancelPairingAsync(PairingRecord expected, DeviceApi api, string attempt, byte[] grant, bool submitted)
    {
        var current = store.Load(expected.DeviceId);
        if (current.ClientId != expected.ClientId) throw new CredentialStoreException(StoreError.IdentityMismatch);
        var record = store.BeginRevocation(current);
        await StopCoreAsync().ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            bool cancelled = !submitted;
            if (submitted)
            {
                var response = await api.RequestAsync(HttpMethod.Delete, PairPath(attempt), "Bearer", DeviceApi.Encode(grant), null, deadline.Token).ConfigureAwait(false);
                if (response.Status == 410) { DeviceApi.Error(response, "cancelled"); cancelled = true; }
                else if (response.Status == 409) DeviceApi.Error(response, "conflict");
                else throw DeviceApi.Error(response);
            }
            await RevokeRemoteAsync(record, api, cancelled, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ConnectionException or HttpRequestException or IOException or OperationCanceledException)
        { /* Durable RevocationPending remains. Never report confirmed remote revocation here. */ }
    }

    private async Task<bool> RevokeRemoteAsync(PairingRecord record, DeviceApi api, bool cancellationConfirmed, CancellationToken cancellationToken)
    {
        using var lease = store.OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.SelfRevocation);
        string basic = DeviceApi.Basic(lease);
        var result = await api.RequestAsync(HttpMethod.Delete, "/phonebridge/v1/pairings/self", "Basic", basic, null, cancellationToken).ConfigureAwait(false);
        bool confirmed = result.Status == 204;
        if (!confirmed) DeviceApi.Error(result, "unauthorized");
        // 401 alone is not proof: a PendingApproval request could still be approved later.
        if (!confirmed && !cancellationConfirmed) return false;
        if (await api.SessionAsync(record, basic, cancellationToken).ConfigureAwait(false) is not null)
            throw new ConnectionException("revocation-unconfirmed");
        store.RemoveAfterVerifiedRevocation(record);
        return true;
    }

    public Task<bool> StopAsync() => RunAsync(async () => { await StopCoreAsync().ConfigureAwait(false); return true; }, CancellationToken.None);
    public Task<bool> ForgetLocallyAsync(string deviceId) => RunAsync(async () =>
    {
        var record = store.BeginRevocation(store.Load(deviceId));
        if (Connected?.Record.DeviceId == deviceId) await StopCoreAsync().ConfigureAwait(false);
        store.ForgetLocally(record);
        return true;
    }, CancellationToken.None);
    private async Task StopCoreAsync()
    {
        var final = await mounts.StopAsync().ConfigureAwait(false);
        if (final.State == MountState.StopFailed) throw new ConnectionException(final.ErrorCode ?? "unmount-not-confirmed");
        Connected = null;
    }
    private void RequireIdle()
    {
        if (Mount.State is MountState.Starting or MountState.RecoveringWrites or MountState.Mounted or MountState.Stopping or MountState.StopFailed)
            throw new ConnectionException("mount-already-active");
    }
    private static string PairPath(string attempt) => "/phonebridge/v1/pairing/" + attempt;
    private static string NormalizeDeletionPath(string value)
    {
        value = value?.Trim() ?? "";
        if (!value.StartsWith('/') || value == "/" || value.Length > 1024 || value.EndsWith('/') ||
            value.Any(c => char.IsControl(c) || c == '\\') ||
            value[1..].Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.StartsWith(".phonebridge-upload-", StringComparison.Ordinal)))
            throw new ConnectionException("invalid-delete-path");
        return value;
    }
    private Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, Action? cleanup = null) => Task.Run(async () =>
    {
        bool acquired = false;
        try
        {
            acquired = await operations.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!acquired) throw new ConnectionException("operation-in-progress");
            ObjectDisposedException.ThrowIf(disposed, this);
            return await operation().ConfigureAwait(false);
        }
        finally { if (acquired) operations.Release(); cleanup?.Invoke(); }
    });
    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); await mounts.DisposeAsync().ConfigureAwait(false); disposed = true; }
        finally { operations.Release(); }
    }
}
