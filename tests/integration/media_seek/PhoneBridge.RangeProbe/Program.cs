using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using PhoneBridge.Credentials;

var options = Parse(args);
var baseline = ReadBaseline(options["baseline"]);
var records = PairingStore.Open().List().Where(record =>
    record.State == PairingRecordState.Active &&
    string.Equals(record.DeviceName, options["device-name"], StringComparison.Ordinal)).ToArray();
if (records.Length != 1) throw new InvalidOperationException("Expected exactly one matching active pairing record.");
var record = records[0];
if (!int.TryParse(options["port"], out var port) || port is < 1 or > 65535)
    throw new ArgumentException("Invalid port.");
var address = options["address"];
if (!IPAddress.TryParse(address, out _)) throw new ArgumentException("Address must be an IP literal.");
var requestPath = EncodePath(options["path"]);

using var anchor = X509CertificateLoader.LoadCertificate(record.Identity.CertificateDer);
var policy = new X509ChainPolicy
{
    TrustMode = X509ChainTrustMode.CustomRootTrust,
    RevocationMode = X509RevocationMode.NoCheck,
    DisableCertificateDownloads = true,
    VerificationFlags = X509VerificationFlags.NoFlag
};
policy.CustomTrustStore.Add(anchor);
policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
var handler = new SocketsHttpHandler
{
    UseProxy = false,
    UseCookies = false,
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    ConnectTimeout = TimeSpan.FromSeconds(5),
    MaxConnectionsPerServer = 1,
    SslOptions = new SslClientAuthenticationOptions
    {
        CertificateChainPolicy = policy,
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
    }
};
using var http = new HttpClient(handler)
{
    BaseAddress = new UriBuilder("https", address, port).Uri,
    Timeout = Timeout.InfiniteTimeSpan
};

using var lease = PairingStore.Open().OpenCredential(record.DeviceId, record.ClientId, CredentialPurpose.Mount);
byte[] token = lease.CopyToken();
string authorization;
try
{
    var encodedToken = Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    authorization = Convert.ToBase64String(Encoding.ASCII.GetBytes(lease.Username + ":" + encodedToken));
}
finally { CryptographicOperations.ZeroMemory(token); }

var results = new List<object>();
long totalBytes = 0;
foreach (var range in baseline.Ranges)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, requestPath)
    {
        Version = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authorization);
    request.Headers.Range = new RangeHeaderValue(range.Offset, checked(range.Offset + range.Length - 1));
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
    var expectedEnd = checked(range.Offset + range.Length - 1);
    var contentRange = response.Content.Headers.ContentRange;
    if (response.StatusCode != HttpStatusCode.PartialContent ||
        response.Content.Headers.ContentLength != range.Length ||
        contentRange?.Unit != "bytes" || contentRange.From != range.Offset ||
        contentRange.To != expectedEnd || contentRange.Length != baseline.FileLength ||
        !response.Headers.AcceptRanges.Contains("bytes") ||
        response.Headers.CacheControl?.NoStore != true ||
        response.Content.Headers.ContentEncoding.Count != 0)
        throw new InvalidDataException($"Invalid range response at offset {range.Offset}.");

    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
    var buffer = new byte[64 * 1024];
    long readTotal = 0;
    while (readTotal < range.Length)
    {
        var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, range.Length - readTotal)), deadline.Token);
        if (count == 0) throw new EndOfStreamException($"Short response at offset {range.Offset}.");
        hash.AppendData(buffer, 0, count);
        readTotal += count;
    }
    if (await input.ReadAsync(buffer.AsMemory(0, 1), deadline.Token) != 0)
        throw new InvalidDataException($"Long response at offset {range.Offset}.");
    var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
    if (!string.Equals(actual, range.Sha256, StringComparison.Ordinal))
        throw new InvalidDataException($"Range hash mismatch at offset {range.Offset}.");
    totalBytes += readTotal;
    results.Add(new
    {
        offset = range.Offset,
        length = range.Length,
        status = 206,
        contentRange = $"bytes {range.Offset}-{expectedEnd}/{baseline.FileLength}",
        sha256 = actual,
        match = true
    });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = 1,
    rangeCount = results.Count,
    totalBytesRead = totalBytes,
    allStatus206 = true,
    allHashesMatched = true,
    ranges = results
}));

static Dictionary<string, string> Parse(string[] values)
{
    if (values.Length != 10) throw new ArgumentException("Expected five named options.");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < values.Length; index += 2)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal) || values[index].Length < 3 ||
            !result.TryAdd(values[index][2..], values[index + 1]))
            throw new ArgumentException("Invalid or duplicate option.");
    }
    foreach (var required in new[] { "address", "port", "path", "baseline", "device-name" })
        if (!result.ContainsKey(required)) throw new ArgumentException($"Missing --{required}.");
    return result;
}

static string EncodePath(string value)
{
    if (value.Length is < 2 or > 1024 || value[0] != '/' || value.Contains('\\') ||
        value.Contains('?') || value.Contains('#')) throw new ArgumentException("Invalid path.");
    var parts = value.Split('/');
    if (parts.Skip(1).Any(part => part.Length == 0 || part is "." or ".."))
        throw new ArgumentException("Invalid path segment.");
    return "/" + string.Join('/', parts.Skip(1).Select(Uri.EscapeDataString));
}

static Baseline ReadBaseline(string path)
{
    var fullPath = Path.GetFullPath(path);
    using var document = JsonDocument.Parse(File.ReadAllBytes(fullPath), new JsonDocumentOptions { MaxDepth = 4 });
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || root.GetProperty("schema").GetInt32() != 1 ||
        !root.GetProperty("fileLength").TryGetInt64(out var fileLength) || fileLength < 1)
        throw new InvalidDataException("Invalid baseline.");
    var ranges = new List<RangeSample>();
    foreach (var item in root.GetProperty("ranges").EnumerateArray())
    {
        if (!item.GetProperty("offset").TryGetInt64(out var offset) ||
            !item.GetProperty("length").TryGetInt32(out var length) || offset < 0 ||
            length is < 1 or > 4 * 1024 * 1024 || offset + length > fileLength)
            throw new InvalidDataException("Invalid baseline range.");
        var sha = item.GetProperty("sha256").GetString();
        if (sha is null || sha.Length != 64 || sha.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid baseline hash.");
        ranges.Add(new RangeSample(offset, length, sha));
    }
    if (ranges.Count is < 1 or > 16) throw new InvalidDataException("Invalid baseline range count.");
    return new Baseline(fileLength, ranges);
}

sealed record RangeSample(long Offset, int Length, string Sha256);
sealed record Baseline(long FileLength, IReadOnlyList<RangeSample> Ranges);
