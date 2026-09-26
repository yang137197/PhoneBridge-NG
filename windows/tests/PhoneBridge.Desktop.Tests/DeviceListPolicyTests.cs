using System.Reflection;
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

    [TestMethod]
    public void BusyCardOnlyDisablesAndCancelsItsOwnDevice()
    {
        Type type = typeof(MainWindow).GetNestedType("DeviceRow", BindingFlags.NonPublic)!;
        object NewRow(string id, bool busy) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [id, id, null, null, false, null, busy, false], culture: null)!;
        object busy = NewRow("a", true);
        object other = NewRow("b", false);
        T Property<T>(object row, string name) => (T)type.GetProperty(name)!.GetValue(row)!;

        Assert.IsTrue(Property<bool>(busy, "IsBusy"));
        Assert.IsFalse(Property<bool>(busy, "CanUsePrimary"));
        Assert.IsTrue(Property<bool>(busy, "CanCancel"));
        Assert.IsFalse(Property<bool>(busy, "CanModifySession"));
        Assert.AreEqual(TextCatalog.Get("Working"), Property<string>(busy, "State"));
        Assert.IsFalse(Property<bool>(other, "IsBusy"));
        Assert.IsTrue(Property<bool>(other, "CanUsePrimary"));
        Assert.IsFalse(Property<bool>(other, "CanCancel"));
        Assert.IsTrue(Property<bool>(other, "CanModifySession"));
    }
}
