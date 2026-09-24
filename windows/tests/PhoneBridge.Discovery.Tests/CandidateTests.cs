using PhoneBridge.Discovery.Windows;

namespace PhoneBridge.Discovery.Tests;

[TestClass]
public sealed class CandidateTests
{
    internal static ServiceAdvertisement Valid(string key = "service-1") => new(key, "Phone", "_phonebridge._tcp",
        "local", 8273, ["192.168.1.10"], ["protocol=https", "version=2", "deviceName=测试手机"]);

    [TestMethod]
    public void DuplicateAndReorderedAddressesDoNotEmitChange()
    {
        var registry = new CandidateRegistry();
        var value = Valid() with { Addresses = ["192.168.1.10", "fe80::1234%9"] };
        Assert.AreEqual(DiscoveryChangeKind.Added, registry.Apply(value)!.Kind);
        Assert.IsNull(registry.Apply(value with { Addresses = ["fe80::1234%9", "192.168.1.10", "192.168.1.10"] }));
        Assert.HasCount(1, registry.Snapshot);
        Assert.IsFalse(registry.Snapshot[0].IsAuthenticated);
    }

    [TestMethod]
    public void AddressChangeReplacesEndpointWithoutChangingCandidateKey()
    {
        var registry = new CandidateRegistry();
        var before = registry.Apply(Valid())!;
        var after = registry.Apply(Valid() with { Addresses = ["192.168.1.11"], Port = 9000 })!;
        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(DiscoveryChangeKind.Updated, after.Kind);
        Assert.AreEqual("https://192.168.1.11:9000/", after.Candidate!.Endpoints.Single().HttpsAddress);
    }

    [TestMethod]
    public void SameDisplayNameDoesNotMergeServicesAndRemoveIsExact()
    {
        var registry = new CandidateRegistry();
        registry.Apply(Valid("one")); registry.Apply(Valid("two"));
        Assert.HasCount(2, registry.Snapshot);
        Assert.IsNotNull(registry.RemoveService("one"));
        Assert.IsNull(registry.RemoveService("one"));
        Assert.AreEqual(CandidateParser.Key(CandidateSource.Mdns, "two"), registry.Snapshot.Single().Id);
    }

    [TestMethod]
    [DataRow("http", "2", "https-required")]
    [DataRow("HTTPS", "2", "https-required")]
    [DataRow("https", "4", "unsupported-version")]
    public void RejectsProtocolAndVersion(string protocol, string version, string expected)
    {
        Assert.IsFalse(CandidateParser.TryParse(Valid() with { TextAttributes = [$"protocol={protocol}", $"version={version}"] }, out _, out var reason));
        Assert.AreEqual(expected, reason);
    }

    [TestMethod]
    [DataRow("0.0.0.0")][DataRow("127.0.0.1")][DataRow("255.255.255.255")]
    [DataRow("224.0.0.251")][DataRow("::")][DataRow("::1")][DataRow("ff02::fb")]
    [DataRow("fe80::1")][DataRow("2130706433")][DataRow("192.168.001.2")]
    [DataRow("192.168.1.2/path")][DataRow("https://192.168.1.2")][DataRow("phone.local")]
    [DataRow("user@192.168.1.2")][DataRow("::ffff:127.0.0.1")][DataRow(" 192.168.1.2")]
    public void ManualAndMdnsShareAddressRejection(string address)
    {
        Assert.IsFalse(CandidateParser.TryManual(address, 8273, out _));
        Assert.IsFalse(CandidateParser.TryParse(Valid() with { Addresses = [address] }, out _, out _));
    }

    [TestMethod]
    [DataRow(0)][DataRow(-1)][DataRow(65536)]
    public void RejectsInvalidPort(int port)
    {
        Assert.IsFalse(CandidateParser.TryManual("192.168.1.2", port, out _));
        Assert.IsFalse(CandidateParser.TryParse(Valid() with { Port = port }, out _, out _));
    }

    [TestMethod]
    public void RejectsMalformedAndOversizedTxt()
    {
        string[][] cases = [[], ["protocol=https", "version=2", "flag"],
            ["protocol=https", "PROTOCOL=https", "version=2"],
            ["protocol=https", "version=2", "value=" + new string('x', 250)],
            ["protocol=https", "version=2", "value=" + new string('中', 100)],
            ["protocol=https", "version=2", "deviceName=bad\nname"],
            ["protocol=https", "version=2", "deviceName=bad\u202ename"],
            ["protocol=https", "version=2", "deviceName=" + new string('x', 129)],
            [.. Enumerable.Range(0, 33).Select(n => $"k{n}=v")],
            ["protocol=https", "version=2", .. Enumerable.Range(0, 20).Select(n => $"k{n}=" + new string('x', 230))]];
        foreach (var txt in cases)
            Assert.IsFalse(CandidateParser.TryParse(Valid() with { TextAttributes = txt }, out _, out _));
    }

    [TestMethod]
    public void InvalidReplacementRevokesPreviouslyValidCandidate()
    {
        var registry = new CandidateRegistry();
        registry.Apply(Valid());
        Assert.AreEqual(DiscoveryChangeKind.Removed, registry.Apply(Valid() with { TextAttributes = ["protocol=http", "version=2"] })!.Kind);
        Assert.IsEmpty(registry.Snapshot);
    }

    [TestMethod]
    public void AdvertisedFingerprintAndUnknownAttributesCannotEstablishTrust()
    {
        var raw = Valid() with { TextAttributes = [.. Valid().TextAttributes, "fingerprint=attacker", "device_id=forged"] };
        Assert.IsTrue(CandidateParser.TryParse(raw, out var candidate, out _));
        Assert.IsFalse(candidate!.IsAuthenticated);
        Assert.AreEqual(CandidateParser.Key(CandidateSource.Mdns, raw.ServiceKey), candidate.Id);
    }

    [TestMethod]
    public void ManualHasSeparateKeyAndSurvivesMdnsRefresh()
    {
        var registry = new CandidateRegistry();
        registry.Apply(Valid()); registry.AddManual("192.168.1.10", 8273);
        Assert.HasCount(2, registry.Snapshot);
        registry.Clear("network-refresh", CandidateSource.Mdns);
        Assert.AreEqual(CandidateSource.Manual, registry.Snapshot.Single().Source);
        Assert.HasCount(1, registry.Clear("stopped"));
        Assert.IsEmpty(registry.Snapshot);
    }

    [TestMethod]
    public void CandidateStorageIsBounded()
    {
        var registry = new CandidateRegistry();
        for (var i = 0; i < CandidateParser.MaxCandidates; i++) registry.Apply(Valid(i.ToString()));
        Assert.AreEqual("candidate-limit", registry.Apply(Valid("extra"))!.Reason);
        Assert.HasCount(CandidateParser.MaxCandidates, registry.Snapshot);
    }

    [TestMethod]
    public void RemovalRequiresTwoMissingScansAndRediscoveryResetsMisses()
    {
        var registry = new CandidateRegistry();
        var first = registry.Apply(Valid())!;
        registry.AddManual("192.168.1.20", 8273);
        Assert.IsEmpty(registry.CompleteScan(new HashSet<string>()));
        Assert.IsEmpty(registry.CompleteScan(new HashSet<string> { first.Id }));
        Assert.IsEmpty(registry.CompleteScan(new HashSet<string>()));
        var removed = registry.CompleteScan(new HashSet<string>());
        Assert.AreEqual("not-rediscovered", removed.Single().Reason);
        Assert.AreEqual(CandidateSource.Manual, registry.Snapshot.Single().Source);
    }

    [TestMethod]
    public void OversizedMetadataIsRejectedBeforeRetention()
    {
        var fields = ServiceTests.Properties();
        fields[WatcherProperties.Text] = new[] { new string('x', 4097) };
        Assert.IsFalse(WatcherProperties.AreBounded(fields));
        fields[WatcherProperties.Text] = Enumerable.Repeat("value", 33).ToArray();
        Assert.IsFalse(WatcherProperties.AreBounded(fields));
    }

    [TestMethod]
    public void PropertyParserRejectsMissingOrWrongTypedValues()
    {
        Assert.IsNull(WatcherProperties.Parse("id", new Dictionary<string, object>()));
        var properties = ServiceTests.Properties();
        properties[WatcherProperties.Addresses] = "192.168.1.10";
        Assert.IsNull(WatcherProperties.Parse("id", properties));
    }
}
