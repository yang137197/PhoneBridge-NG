using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class DeviceListPolicyTests
{
    [TestMethod]
    public void UnpairedPhoneIsEligibleOnlyWhileItsPairingWindowIsOpen()
    {
        Assert.IsFalse(DeviceListPolicy.IncludeCandidate(false, false));
        Assert.IsTrue(DeviceListPolicy.IncludeCandidate(true, false));
        Assert.IsTrue(DeviceListPolicy.IncludeCandidate(false, true));
    }

    [TestMethod]
    public void UnpairedCandidateIsNeverReportedConnected() =>
        Assert.IsFalse(DeviceListPolicy.IsConnected(null, null));

    [TestMethod]
    public void OnlyExactMountedPairingIsReportedConnected()
    {
        Assert.IsTrue(DeviceListPolicy.IsConnected("phone-a", "phone-a"));
        Assert.IsFalse(DeviceListPolicy.IsConnected("phone-a", "phone-b"));
        Assert.IsFalse(DeviceListPolicy.IsConnected("phone-a", null));
    }
}
