using System.Text;
using PhoneBridge.Desktop;
using PhoneBridge.Discovery;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class ManualEndpointSessionTests
{
    [TestMethod]
    [DataRow("192.168.1.25", "1", "192.168.1.25", 1)]
    [DataRow("192.168.1.25", "65535", "192.168.1.25", 65535)]
    [DataRow("2001:db8::25", "8273", "2001:db8::25", 8273)]
    public void AcceptsLiteralAddressesAndCanonicalPorts(string address, string port, string expectedAddress, int expectedPort)
    {
        var session = new ManualEndpointSession();
        Assert.IsTrue(session.TrySet("pbng-device", address, port, out var endpoint));
        Assert.AreEqual(expectedAddress, endpoint!.Address);
        Assert.AreEqual(expectedPort, endpoint.Port);
        Assert.IsTrue(session.TryGet("pbng-device", out var stored));
        Assert.AreEqual(endpoint, stored);
    }

    [TestMethod]
    [DataRow("192.168.1.25", "")]
    [DataRow("192.168.1.25", "0")]
    [DataRow("192.168.1.25", "00")]
    [DataRow("192.168.1.25", "08273")]
    [DataRow("192.168.1.25", "+8273")]
    [DataRow("192.168.1.25", " 8273")]
    [DataRow("192.168.1.25", "65536")]
    [DataRow("https://192.168.1.25", "8273")]
    [DataRow("phone.local", "8273")]
    [DataRow("127.0.0.1", "8273")]
    [DataRow("192.168.1.25 ", "8273")]
    public void RejectsNonCanonicalOrUnsafeInput(string address, string port)
    {
        var session = new ManualEndpointSession();
        Assert.IsFalse(session.TrySet("pbng-device", address, port, out var endpoint));
        Assert.IsNull(endpoint);
        Assert.IsFalse(session.TryGet("pbng-device", out _));
    }

    [TestMethod]
    public void EndpointIsScopedPerRecordDeduplicatedAndClearable()
    {
        var session = new ManualEndpointSession();
        Assert.IsTrue(session.TrySet("pbng-a", "192.168.1.25", "8273", out var first));
        Assert.IsTrue(session.TrySet("pbng-b", "192.168.1.26", "8274", out var second));
        var merged = session.Merge("pbng-a", new[] { new DeviceEndpoint("192.168.1.25", 8273) });
        Assert.HasCount(1, merged);
        Assert.AreEqual(first, merged[0]);
        Assert.IsTrue(session.Clear("pbng-a"));
        Assert.IsFalse(session.TryGet("pbng-a", out _));
        Assert.IsTrue(session.TryGet("pbng-b", out var retained));
        Assert.AreEqual(second, retained);
        Assert.IsFalse(session.Clear("pbng-a"));
    }

    [TestMethod]
    public void ReconnectUsesMatchingDiscoveryThenFallsBackToManualWithoutMdns()
    {
        var session = new ManualEndpointSession();
        Assert.IsTrue(session.TrySet("pbng-a", "192.168.1.25", "8273", out var manual));
        Assert.AreEqual(manual, session.SelectForReconnect("pbng-a", []));

        var automatic = new DeviceEndpoint("192.168.1.30", 8273);
        var unrelated = new DeviceCandidate("other", CandidateSource.Mdns, "Other", [automatic])
            { Protocol = CandidateProtocol.PairedV3, DeviceIdHint = "pbng-b" };
        Assert.AreEqual(manual, session.SelectForReconnect("pbng-a", [unrelated]));

        var matching = new DeviceCandidate("matching", CandidateSource.Mdns, "Phone", [automatic])
            { Protocol = CandidateProtocol.PairedV3, DeviceIdHint = "pbng-a" };
        Assert.AreEqual(automatic, session.SelectForReconnect("pbng-a", [unrelated, matching]));
        Assert.IsNull(session.SelectForReconnect("pbng-missing", [matching]));
    }

    [TestMethod]
    public void FixedDiagnosticEventCannotContainManualInput()
    {
        const string address = "198.51.100.42";
        string root = Path.Combine(Path.GetTempPath(), "pbng-manual-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new ManualEndpointSession();
            Assert.IsTrue(session.TrySet("pbng-device", address, "8273", out _));
            using var log = DiagnosticEventLog.Open(root, "test");
            log.Write(new(DiagnosticEventName.ManualEndpointChanged, Code: DiagnosticResultCode.Success,
                State: DiagnosticState.Added));
            string content = Encoding.UTF8.GetString(log.Snapshot().Single().Content);
            StringAssert.Contains(content, "ManualEndpointChanged");
            Assert.IsFalse(content.Contains(address, StringComparison.Ordinal));
            Assert.IsFalse(content.Contains("8273", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
