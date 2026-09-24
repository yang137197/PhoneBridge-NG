using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using PhoneBridge.Credentials;
using PhoneBridge.Discovery;

namespace PhoneBridge.Connection;

internal sealed record ApiReply(int Status, byte[] Body);
internal sealed record VerifiedSession(AccessMode Mode, bool ShareReady);

internal sealed class DeviceApi : IDisposable
{
    private readonly X509Certificate2 anchor;
    private readonly HttpClient http;
    internal DeviceApi(ValidatedDeviceIdentity identity, DeviceEndpoint endpoint)
    {
        EndpointRules.Check(endpoint);
        anchor = X509CertificateLoader.LoadCertificate(identity.CertificateDer);
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true, VerificationFlags = X509VerificationFlags.NoFlag
        };
        policy.CustomTrustStore.Add(anchor);
        policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 1,
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = policy, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };
        http = new HttpClient(handler) { BaseAddress = new Uri(endpoint.HttpsAddress), Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<ApiReply> RequestAsync(HttpMethod method, string path, string scheme, string auth,
        byte[]? body, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(method, path)
        { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, auth);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400 || response.Headers.CacheControl?.NoStore != true ||
            response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > 4096)
            throw new ConnectionException("invalid-response");
        if (response.StatusCode != HttpStatusCode.NoContent &&
            (response.Content.Headers.ContentType?.MediaType != "application/json" ||
             response.Content.Headers.ContentType.CharSet is { } charset && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new ConnectionException("invalid-response");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        byte[] buffer = new byte[4097]; int count = 0;
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count), deadline.Token).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096 || response.StatusCode == HttpStatusCode.NoContent && count != 0)
            throw new ConnectionException("invalid-response");
        return new((int)response.StatusCode, buffer[..count]);
    }

    internal async Task<VerifiedSession?> SessionAsync(PairingRecord record, string basic, CancellationToken cancellationToken)
    {
        var response = await RequestAsync(HttpMethod.Get, "/phonebridge/v1/session", "Basic", basic, null, cancellationToken).ConfigureAwait(false);
        if (response.Status == 401) { Error(response, "unauthorized"); return null; }
        if (response.Status != 200) throw Error(response);
        using var document = Parse(response.Body);
        var body = document.RootElement; Exact(body, "device_id", "client_id", "mode", "share_ready");
        if (Text(body, "device_id") != record.DeviceId || Text(body, "client_id") != record.ClientId)
            throw new ConnectionException("identity-mismatch");
        AccessMode mode = Text(body, "mode") switch
        {
            "safe" => AccessMode.Safe, "readOnly" => AccessMode.ReadOnly, "readWrite" => AccessMode.ReadWrite,
            _ => throw new ConnectionException("invalid-response")
        };
        var ready = body.GetProperty("share_ready");
        if (ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ConnectionException("invalid-response");
        return new(mode, ready.GetBoolean());
    }

    internal async Task<DeletionPreview> PrepareDeletionAsync(PairingRecord record, string basic, string path,
        CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { path });
        try
        {
            var response = await RequestAsync(HttpMethod.Post, "/phonebridge/v1/deletions", "Basic", basic, body, cancellationToken).ConfigureAwait(false);
            if (response.Status != 200) throw Error(response);
            using var document = Parse(response.Body); var root = document.RootElement;
            Exact(root, "confirmation_id", "path", "directory", "size", "modified", "expires_in_seconds");
            string id = Text(root, "confirmation_id"), returnedPath = Text(root, "path");
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{32}$") || returnedPath != path ||
                root.GetProperty("directory").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.GetProperty("size").TryGetInt64(out long size) || size < 0 ||
                !root.GetProperty("modified").TryGetInt64(out long modified) || modified < 0 ||
                !root.GetProperty("expires_in_seconds").TryGetInt32(out int expires) || expires is < 1 or > 30)
                throw new ConnectionException("invalid-response");
            return new(record.DeviceId, id, returnedPath, root.GetProperty("directory").GetBoolean(), size, modified);
        }
        finally { CryptographicOperations.ZeroMemory(body); }
    }

    internal async Task ConfirmDeletionAsync(string basic, DeletionPreview preview, CancellationToken cancellationToken)
    {
        var response = await RequestAsync(HttpMethod.Delete, "/phonebridge/v1/deletions/" + preview.ConfirmationId,
            "Basic", basic, null, cancellationToken).ConfigureAwait(false);
        if (response.Status != 204) throw Error(response);
    }

    internal static bool PairingState(ApiReply reply, string attempt, string client)
    {
        if (reply.Status is not (200 or 202)) throw Error(reply);
        using var document = Parse(reply.Body); var body = document.RootElement;
        Exact(body, "attempt_id", "client_id", "state");
        if (Text(body, "attempt_id") != attempt || Text(body, "client_id") != client ||
            Text(body, "state") != (reply.Status == 200 ? "Active" : "PendingApproval"))
            throw new ConnectionException("invalid-response");
        return reply.Status == 200;
    }

    internal static ConnectionException Error(ApiReply reply, string? expected = null)
    {
        using var document = Parse(reply.Body); Exact(document.RootElement, "code");
        string code = Text(document.RootElement, "code");
        string? mapped = reply.Status switch
        {
            400 => "invalid_request", 401 => "unauthorized", 403 => "rejected", 404 => "not_found",
            405 => "method_not_allowed", 409 => "conflict", 410 => "cancelled", 429 => "capacity", 503 => "storage_failure", _ => null
        };
        if (code != mapped || expected is not null && code != expected) throw new ConnectionException("invalid-response");
        return new ConnectionException(code);
    }
    internal static JsonDocument Parse(byte[] body)
    {
        try { _ = new UTF8Encoding(false, true).GetCharCount(body); return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 }); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException) { throw new ConnectionException("invalid-response"); }
    }
    private static string Text(JsonElement body, string name) => body.GetProperty(name).ValueKind == JsonValueKind.String
        ? body.GetProperty(name).GetString()! : throw new ConnectionException("invalid-response");
    private static void Exact(JsonElement body, params string[] names)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new ConnectionException("invalid-response");
        var fields = body.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Except(names, StringComparer.Ordinal).Any())
            throw new ConnectionException("invalid-response");
    }
    internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string Basic(CredentialLease lease)
    {
        byte[] token = lease.CopyToken();
        try { return Convert.ToBase64String(Encoding.ASCII.GetBytes(lease.Username + ":" + Encode(token))); }
        finally { CryptographicOperations.ZeroMemory(token); }
    }
    public void Dispose() { http.Dispose(); anchor.Dispose(); }
}
