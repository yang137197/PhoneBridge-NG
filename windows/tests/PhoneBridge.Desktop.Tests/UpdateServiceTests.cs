using System.Net;
using System.Security.Cryptography;
using System.Text;
using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    public async Task LatestFormalReleaseDoesNotDowngradeNewerInstalledVersion()
    {
        byte[] installer = Encoding.ASCII.GetBytes("installer");
        using var client = Client(_ => JsonResponse(Release("0.2.3", installer)));
        using var service = new UpdateService(client);

        UpdateCheckResult result = await service.CheckAsync("0.2.4", CancellationToken.None);

        Assert.AreEqual(UpdateCheckKind.Current, result.Kind);
        Assert.AreEqual("0.2.3", result.Latest.DisplayVersion);
    }

    [TestMethod]
    public async Task NewerReleaseDownloadsOnlyAfterExactDigestAndSizeMatch()
    {
        byte[] installer = Encoding.ASCII.GetBytes("future-windows-installer");
        using var client = Client(request => request.RequestUri?.Host == "api.github.com"
            ? JsonResponse(Release("0.2.5", installer))
            : BinaryResponse(installer));
        using var service = new UpdateService(client);
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-UpdateTests", Guid.NewGuid().ToString("N"));
        try
        {
            UpdateCheckResult check = await service.CheckAsync("0.2.4", CancellationToken.None);
            PendingUpdate pending = await service.DownloadAsync(check.Latest, root, CancellationToken.None);

            Assert.AreEqual(UpdateCheckKind.Available, check.Kind);
            Assert.IsTrue(UpdateService.VerifyInstaller(pending));
            CollectionAssert.AreEqual(installer, await File.ReadAllBytesAsync(pending.InstallerPath));

            await File.WriteAllTextAsync(pending.InstallerPath, "tampered");
            Assert.IsFalse(UpdateService.VerifyInstaller(pending));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DigestMismatchStopsDownloadAndLeavesNoInstaller()
    {
        byte[] expected = Encoding.ASCII.GetBytes("expected-installer");
        byte[] received = Encoding.ASCII.GetBytes("received-installer");
        using var client = Client(request => request.RequestUri?.Host == "api.github.com"
            ? JsonResponse(Release("0.2.5", expected, received.Length))
            : BinaryResponse(received));
        using var service = new UpdateService(client);
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-UpdateTests", Guid.NewGuid().ToString("N"));
        try
        {
            UpdateCheckResult check = await service.CheckAsync("0.2.4", CancellationToken.None);
            UpdateException error = await Assert.ThrowsExactlyAsync<UpdateException>(
                () => service.DownloadAsync(check.Latest, root, CancellationToken.None));

            Assert.AreEqual("UpdateIntegrityFailed", error.Code);
            Assert.IsFalse(Directory.Exists(root) && Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).Any());
            Assert.IsFalse(Directory.Exists(root) && Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UntrustedAssetUrlIsRejectedBeforeDownload()
    {
        byte[] installer = Encoding.ASCII.GetBytes("installer");
        string json = Release("0.2.5", installer).Replace(
            "https://github.com/yang137197/PhoneBridge-NG/releases/download/",
            "https://example.com/yang137197/PhoneBridge-NG/releases/download/",
            StringComparison.Ordinal);
        using var client = Client(_ => JsonResponse(json));
        using var service = new UpdateService(client);

        UpdateException error = await Assert.ThrowsExactlyAsync<UpdateException>(
            () => service.CheckAsync("0.2.4", CancellationToken.None));

        Assert.AreEqual("UpdateResponseInvalid", error.Code);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new StubHandler(response)) { Timeout = TimeSpan.FromSeconds(5) };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BinaryResponse(byte[] value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
        response.Content.Headers.ContentLength = value.Length;
        return response;
    }

    private static string Release(string version, byte[] installer, int? declaredSize = null)
    {
        string digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        int size = declaredSize ?? installer.Length;
        return $$"""
            {
              "tag_name": "v{{version}}",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "PhoneBridge-NG-Setup-{{version}}.exe",
                  "state": "uploaded",
                  "size": {{size}},
                  "digest": "sha256:{{digest}}",
                  "browser_download_url": "https://github.com/yang137197/PhoneBridge-NG/releases/download/v{{version}}/PhoneBridge-NG-Setup-{{version}}.exe"
                }
              ]
            }
            """;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
