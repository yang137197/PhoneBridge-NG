using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PhoneBridge.Credentials;
using PhoneBridge.Discovery;
using PhoneBridge.Pairing;

// Peers use separate ports, synthetic identities and private GUID stores; no shared product records.
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
namespace PhoneBridge.Connection.Tests;

internal sealed record Request(string Method, string Path, string Authorization, byte[] Body);
internal sealed record Reply(int Status, string Body, string Extra = "", int Delay = 0, bool Disconnect = false);
internal sealed class NetworkPeer : IAsyncDisposable
{
    internal static readonly IPAddress Address = NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
    private readonly RSA caKey = RSA.Create(2048), leafKey = RSA.Create(2048);
    private readonly X509Certificate2 ca, leaf;
    private readonly TcpListener tls = new(Address, 0), pake = new(Address, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task accept;
    private Task pairing = Task.CompletedTask;
    private readonly ConcurrentBag<Task> requests = [];
    internal ValidatedDeviceIdentity Identity { get; }
    internal DeviceEndpoint Endpoint { get; }
    internal DeviceCandidate Candidate { get; private set; } = null!;
    internal byte[] Window { get; } = RandomNumberGenerator.GetBytes(16);
    internal string ClientId { get; private set; } = "";
    internal string Attempt { get; private set; } = "";
    internal int RequestCount;
    internal Func<Request, Task<Reply>> Respond { get; set; } = _ => Task.FromResult(new Reply(401, "{\"code\":\"unauthorized\"}"));
    internal NetworkPeer(bool wrongSan = false)
    {
        Note("ctor-start");
        var now = DateTimeOffset.UtcNow;
        var request = new CertificateRequest("CN=Synthetic PhoneBridge CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        ca = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(2));
        Note("ca-created");
        var der = ca.Export(X509ContentType.Cert);
        Identity = ValidatedDeviceIdentity.Validate(der, Convert.ToHexStringLower(SHA256.HashData(der)));
        Note("identity-validated");
        var leafRequest = new CertificateRequest("CN=Synthetic PhoneBridge server", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(wrongSan ? IPAddress.Parse("192.0.2.1") : Address);
        leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var signed = leafRequest.Create(ca, now.AddHours(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        Note("leaf-created");
        using var ephemeral = signed.CopyWithPrivateKey(leafKey);
        // Schannel cannot host TLS with this ephemeral key. Import only the synthetic test key,
        // without PersistKeySet: disposal removes its temporary provider container; no trust-store change.
        byte[] pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);
        try { leaf = X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
        Note("key-imported");
        tls.Start(); Note("tls-listening"); Endpoint = new(Address.ToString(), ((IPEndPoint)tls.LocalEndpoint).Port);
        accept = AcceptAsync();
        Note("ctor-end");
    }
    internal void StartPairing(string code = "01234567", bool trailing = false, bool hold = false)
    {
        Note("pake-start");
        pake.Start(); Note("pake-listening");
        Candidate = new("synthetic", CandidateSource.Mdns, "Synthetic phone", [Endpoint])
        {
            Protocol = CandidateProtocol.PairedV3, DeviceIdHint = Identity.DeviceId,
            Pairing = new(((IPEndPoint)pake.LocalEndpoint).Port, Convert.ToHexStringLower(Window))
        };
        pairing = Task.Run(async () =>
        {
            try
            {
                Note("pake-accept-wait");
                using var client = await pake.AcceptTcpClientAsync(lifetime.Token);
                Note("pake-accepted");
                using var stream = client.GetStream();
                if (hold) { await Task.Delay(Timeout.Infinite, lifetime.Token); return; }
                using var session = PairingSession.CreateAndroid(Window, code.ToCharArray(), (ushort)Endpoint.Port, Identity.CertificateDer);
                foreach (byte expected in new byte[] { 1, 0x11, 0x21, 0x31 })
                {
                    byte[] header = new byte[9]; await stream.ReadExactlyAsync(header, lifetime.Token);
                    var accumulator = new FrameAccumulator(expected); accumulator.Feed(header);
                    byte[] payload = new byte[BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5))];
                    await stream.ReadExactlyAsync(payload, lifetime.Token); accumulator.Feed(payload); session.AcceptFrame(accumulator.GetFrame());
                    if (expected == 0x31) Assert.AreEqual(0, await stream.ReadAsync(new byte[1], lifetime.Token));
                    await stream.WriteAsync(session.CreateNextFrame(), lifetime.Token);
                }
                using var confirmation = session.TakeConfirmation();
                ClientId = Convert.ToHexStringLower(confirmation.ClientId); Attempt = Convert.ToHexStringLower(confirmation.AttemptId);
                if (trailing) await stream.WriteAsync(new byte[] { 0 }, lifetime.Token);
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or PairingProtocolException) { }
        });
    }
    private async Task AcceptAsync()
    {
        try { while (!lifetime.IsCancellationRequested) { var client = await tls.AcceptTcpClientAsync(lifetime.Token); requests.Add(ServeAsync(client)); } }
        catch (OperationCanceledException) { }
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var stream = new SslStream(client.GetStream(), false))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = leaf, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, lifetime.Token);
                var bytes = new List<byte>(); byte[] one = new byte[1];
                while (bytes.Count < 16384)
                {
                    await stream.ReadExactlyAsync(one, lifetime.Token); bytes.Add(one[0]);
                    if (bytes.Count >= 4 && bytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                }
                var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
                var first = lines[0].Split(' ');
                string header(string name) => lines.FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? "";
                int length = int.TryParse(header("Content-Length"), out int parsed) ? parsed : 0;
                if (length > 4096) throw new InvalidDataException();
                byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, lifetime.Token);
                Interlocked.Increment(ref RequestCount);
                var reply = await Respond(new(first[0], first[1], header("Authorization"), body));
                if (reply.Disconnect) return;
                if (reply.Delay > 0) await Task.Delay(reply.Delay, lifetime.Token);
                byte[] content = Encoding.UTF8.GetBytes(reply.Body);
                string headers = $"HTTP/1.1 {reply.Status} Test\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nContent-Length: {content.Length}\r\nConnection: close\r\n{reply.Extra}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), lifetime.Token);
                await stream.WriteAsync(content, lifetime.Token);
            }
            catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException)
            { if (e is not OperationCanceledException) Console.WriteLine("Synthetic TLS peer: " + e.GetType().Name + ": " + e.Message); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Note("dispose-start");
        lifetime.Cancel(); tls.Stop(); pake.Stop();
        await accept; await pairing; await Task.WhenAll(requests);
        lifetime.Dispose(); leaf.Dispose(); ca.Dispose(); caKey.Dispose(); leafKey.Dispose();
        Note("dispose-end");
    }
    private static readonly object TraceGate = new();
    private static void Note(string stage)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AGENTS.md"))) folder = folder.Parent;
        if (folder is null) return;
        lock (TraceGate) File.AppendAllText(Path.Combine(folder.FullName, ".audit", "p1-008", "synthetic-timing.log"), DateTime.UtcNow.ToString("O") + " " + stage + Environment.NewLine);
    }
}
