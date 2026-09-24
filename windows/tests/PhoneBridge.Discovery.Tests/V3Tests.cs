namespace PhoneBridge.Discovery.Tests;

[TestClass]
public sealed class V3Tests
{
    private static string[] Txt => ["protocol=https", "version=3", "auth=paired-v1", "device_id=pbng-" + new string('a', 64)];
    private static ServiceAdvertisement Ad(params string[] extra) => CandidateTests.Valid() with { TextAttributes = [.. Txt, .. extra] };
    [TestMethod]
    public void ValidV3AndWindowRemainUntrusted()
    {
        Assert.IsTrue(CandidateParser.TryParse(Ad("pairing=jpake1", "pair_port=12345", "pair_window=" + new string('0', 32)), out var candidate, out _));
        Assert.AreEqual(CandidateProtocol.PairedV3, candidate!.Protocol);
        Assert.AreEqual(12345, candidate.Pairing!.Port); Assert.IsFalse(candidate.IsAuthenticated);
        Assert.IsTrue(CandidateParser.TryParse(Ad(), out candidate, out _)); Assert.IsNull(candidate!.Pairing);
    }
    [TestMethod]
    [DataRow("pairing=jpake1")][DataRow("pair_port=12345")][DataRow("pair_window=00000000000000000000000000000000")]
    public void PartialWindowRejected(string field) => Assert.IsFalse(CandidateParser.TryParse(Ad(field), out _, out _));
    [TestMethod]
    [DataRow("0")][DataRow("65536")][DataRow("0123")][DataRow("+123")][DataRow(" 123")]
    public void NonCanonicalPairPortRejected(string port) => Assert.IsFalse(CandidateParser.TryParse(Ad("pairing=jpake1", "pair_port=" + port, "pair_window=" + new string('a', 32)), out _, out _));
    [TestMethod]
    public void IdentityAndAuthAreMandatory()
    {
        foreach (string[] invalid in new[] { Txt.Where(t => !t.StartsWith("auth=", StringComparison.Ordinal)).ToArray(),
            Txt.Select(t => t.StartsWith("device_id=", StringComparison.Ordinal) ? t.ToUpperInvariant() : t).ToArray(),
            new[] { "version=3", "protocol=https" } })
            Assert.IsFalse(CandidateParser.TryParse(Ad() with { TextAttributes = invalid }, out _, out _));
    }
    [TestMethod]
    public void WindowOpeningRotationClosingAndIdentityChangePublishUpdates()
    {
        var registry = new CandidateRegistry(); registry.Apply(Ad());
        var window = Ad("pairing=jpake1", "pair_port=12345", "pair_window=" + new string('a', 32));
        Assert.AreEqual(DiscoveryChangeKind.Updated, registry.Apply(window)!.Kind);
        Assert.IsNull(registry.Apply(window));
        Assert.AreEqual(DiscoveryChangeKind.Updated, registry.Apply(Ad("pairing=jpake1", "pair_port=12345", "pair_window=" + new string('b', 32)))!.Kind);
        Assert.AreEqual(DiscoveryChangeKind.Updated, registry.Apply(Ad())!.Kind);
        Assert.IsNull(registry.Snapshot[0].Pairing);
        Assert.AreEqual(DiscoveryChangeKind.Updated, registry.Apply(Ad() with { TextAttributes = Txt.Select(t => t.Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal)).ToArray() })!.Kind);
    }
    [TestMethod]
    public void V2NeverGainsFormalPairingFromInjectedV3Fields()
    {
        Assert.IsTrue(CandidateParser.TryParse(CandidateTests.Valid() with { TextAttributes = ["version=2", "protocol=https", "auth=paired-v1", "pairing=jpake1", "pair_port=12345", "pair_window=" + new string('a', 32)] }, out var candidate, out _));
        Assert.AreEqual(CandidateProtocol.ExperimentalV2, candidate!.Protocol); Assert.IsNull(candidate.Pairing);
    }
    [TestMethod]
    public void InvalidUtf16TxtCannotBecomeReplacementCharacters() => Assert.IsFalse(CandidateParser.TryParse(Ad("deviceName=bad\ud800"), out _, out _));
}
