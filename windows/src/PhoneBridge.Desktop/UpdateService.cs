using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhoneBridge.Desktop;

internal enum UpdateCheckKind { Current, Available }

internal sealed record UpdateRelease(
    Version Version,
    string DisplayVersion,
    string FileName,
    Uri DownloadUri,
    long Size,
    string Sha256);

internal sealed record UpdateCheckResult(UpdateCheckKind Kind, string CurrentVersion, UpdateRelease Latest);

internal sealed record PendingUpdate(string InstallerPath, long Size, string Sha256, string DisplayVersion);

internal sealed class UpdateException(string code, Exception? inner = null) : Exception(code, inner)
{
    internal string Code { get; } = code;
}

internal sealed partial class UpdateService : IDisposable
{
    internal const string LatestReleaseUrl = "https://api.github.com/repos/yang137197/PhoneBridge-NG/releases/latest";
    internal const long MaxInstallerBytes = 512L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeProvider timeProvider;
    private DateTimeOffset rateLimitedUntilUtc;

    internal UpdateService(HttpClient? client = null, TimeProvider? timeProvider = null)
    {
        ownsClient = client is null;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static string CurrentVersion()
    {
        string value = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
        int metadata = value.IndexOfAny(['+', '-']);
        return metadata < 0 ? value : value[..metadata];
    }

    internal async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        if (!TryParseVersion(currentVersion, allowPrefix: false, out Version? current, out string? normalizedCurrent))
            throw new UpdateException("UpdateCurrentVersionInvalid");
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (rateLimitedUntilUtc > now) throw new UpdateException("UpdateRateLimited");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("PhoneBridge-NG/" + normalizedCurrent);
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (TryGetRateLimitReset(response, timeProvider.GetUtcNow(), out DateTimeOffset retryAt))
            {
                rateLimitedUntilUtc = retryAt;
                throw new UpdateException("UpdateRateLimited");
            }
            response.EnsureSuccessStatusCode();
            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(content,
                new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            UpdateRelease release = ParseRelease(document.RootElement);
            rateLimitedUntilUtc = default;
            return new(release.Version > current ? UpdateCheckKind.Available : UpdateCheckKind.Current,
                normalizedCurrent!, release);
        }
        catch (OperationCanceledException) { throw; }
        catch (UpdateException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            throw new UpdateException("UpdateCheckFailed", error);
        }
    }

    private static bool TryGetRateLimitReset(HttpResponseMessage response, DateTimeOffset now,
        out DateTimeOffset retryAt)
    {
        retryAt = default;
        bool primaryLimit = response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
            remaining.Any(value => value == "0");
        if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests && !primaryLimit) return false;

        retryAt = response.Headers.RetryAfter?.Date ??
            (response.Headers.RetryAfter?.Delta is TimeSpan delta ? now + delta : default);
        if (retryAt == default && response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? resets))
        {
            string? value = resets.FirstOrDefault();
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            {
                try { retryAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
                catch (ArgumentOutOfRangeException) { retryAt = default; }
            }
        }
        if (retryAt <= now) retryAt = now.AddMinutes(1);
        return true;
    }

    internal async Task<PendingUpdate> DownloadAsync(UpdateRelease release, string appDataRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (string.IsNullOrWhiteSpace(appDataRoot) || !Path.IsPathFullyQualified(appDataRoot))
            throw new UpdateException("UpdateDownloadFailed");
        ValidateRelease(release);

        string updateRoot = Path.Combine(Path.GetFullPath(appDataRoot), "Updates");
        string versionRoot = Path.Combine(updateRoot, release.DisplayVersion);
        string finalPath = Path.Combine(versionRoot, release.FileName);
        string partialPath = Path.Combine(versionRoot, "." + release.FileName + "." + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            EnsureSafeDirectory(updateRoot);
            EnsureSafeDirectory(versionRoot);
            if (File.Exists(finalPath))
            {
                var existing = new PendingUpdate(finalPath, release.Size, release.Sha256, release.DisplayVersion);
                if (VerifyInstaller(existing)) return existing;
                File.Delete(finalPath);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUri);
            request.Headers.UserAgent.ParseAdd("PhoneBridge-NG/" + CurrentVersion());
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long advertised && advertised != release.Size)
                throw new UpdateException("UpdateIntegrityFailed");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long total = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > release.Size || total > MaxInstallerBytes) throw new UpdateException("UpdateIntegrityFailed");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            if (total != release.Size || !CryptographicOperations.FixedTimeEquals(
                hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                throw new UpdateException("UpdateIntegrityFailed");
            output.Close();
            File.Move(partialPath, finalPath, overwrite: true);
            var pending = new PendingUpdate(finalPath, release.Size, release.Sha256, release.DisplayVersion);
            if (!VerifyInstaller(pending)) throw new UpdateException("UpdateIntegrityFailed");
            return pending;
        }
        catch (OperationCanceledException) { throw; }
        catch (UpdateException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new UpdateException("UpdateDownloadFailed", error);
        }
        finally
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        }
    }

    internal static bool VerifyInstaller(PendingUpdate pending)
    {
        try
        {
            if (!Path.IsPathFullyQualified(pending.InstallerPath) || !File.Exists(pending.InstallerPath) ||
                new FileInfo(pending.InstallerPath).Length != pending.Size ||
                (File.GetAttributes(pending.InstallerPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            using FileStream stream = new(pending.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] actual = SHA256.HashData(stream);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(pending.Sha256));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static UpdateRelease ParseRelease(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("draft", out JsonElement draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out JsonElement prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String ||
            !TryParseVersion(tag.GetString(), allowPrefix: true, out Version? version, out string? displayVersion) ||
            !root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            throw new UpdateException("UpdateResponseInvalid");

        string fileName = $"PhoneBridge-NG-Setup-{displayVersion}.exe";
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !asset.TryGetProperty("name", out JsonElement name) || name.GetString() != fileName) continue;
            if (!asset.TryGetProperty("state", out JsonElement state) || state.GetString() != "uploaded" ||
                !asset.TryGetProperty("size", out JsonElement sizeElement) || !sizeElement.TryGetInt64(out long size) ||
                !asset.TryGetProperty("digest", out JsonElement digestElement) || digestElement.ValueKind != JsonValueKind.String ||
                !asset.TryGetProperty("browser_download_url", out JsonElement urlElement) || urlElement.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? downloadUri))
                throw new UpdateException("UpdateResponseInvalid");
            string digest = digestElement.GetString() ?? string.Empty;
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("UpdateResponseInvalid");
            var release = new UpdateRelease(version!, displayVersion!, fileName, downloadUri, size, digest[7..].ToUpperInvariant());
            ValidateRelease(release);
            return release;
        }
        throw new UpdateException("UpdateAssetMissing");
    }

    private static void ValidateRelease(UpdateRelease release)
    {
        string expectedFile = $"PhoneBridge-NG-Setup-{release.DisplayVersion}.exe";
        string expectedPath = $"/yang137197/PhoneBridge-NG/releases/download/v{release.DisplayVersion}/{expectedFile}";
        if (release.Size is <= 0 or > MaxInstallerBytes || release.FileName != expectedFile ||
            release.Sha256.Length != 64 || !HexDigest().IsMatch(release.Sha256) ||
            release.DownloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(release.DownloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(release.DownloadUri.AbsolutePath, expectedPath, StringComparison.Ordinal))
            throw new UpdateException("UpdateResponseInvalid");
    }

    private static bool TryParseVersion(string? value, bool allowPrefix, out Version? version, out string? normalized)
    {
        version = null; normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        Match match = VersionTag().Match(value);
        if (!match.Success || (!allowPrefix && match.Groups[1].Value.Length != 0)) return false;
        normalized = string.Join('.', match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
        return Version.TryParse(normalized, out version);
    }

    private static void EnsureSafeDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        for (DirectoryInfo? item = new(full); item is not null && item.Exists; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("unsafe-update-location");
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }

    [GeneratedRegex("^(v?)(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTag();

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexDigest();
}
