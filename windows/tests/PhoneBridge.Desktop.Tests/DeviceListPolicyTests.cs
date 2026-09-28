using System.Reflection;
using PhoneBridge.Desktop;
using PhoneBridge.Mounting;

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
    public void SavedDeviceRowIdentityDoesNotChangeWithDiscoveryInstance()
    {
        Assert.AreEqual("phone-a", DeviceListPolicy.StableRowId("candidate-old", "phone-a", true));
        Assert.AreEqual("phone-a", DeviceListPolicy.StableRowId("candidate-new", "phone-a", true));
        Assert.AreEqual("candidate-new", DeviceListPolicy.StableRowId("candidate-new", "phone-a", false));
    }

    [TestMethod]
    public void MountFooterAggregatesAllSessionsWithoutDependingOnSelection()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            (new MountSnapshot(MountState.Mounted), (char?)'P'),
            (new MountSnapshot(MountState.Mounted), (char?)'E'),
            (new MountSnapshot(MountState.Stopped), (char?)null)
        });
        Assert.AreEqual(MountSummaryKind.Mounted, summary.Kind);
        CollectionAssert.AreEqual(new[] { 'E', 'P' }, summary.Drives.ToArray());

        var stopping = DeviceListPolicy.SummarizeMounts(new[]
        {
            (new MountSnapshot(MountState.Mounted), (char?)'P'),
            (new MountSnapshot(MountState.Stopping), (char?)'E')
        });
        Assert.AreEqual(MountSummaryKind.Stopping, stopping.Kind);
    }

    [TestMethod]
    public void MountFooterPrioritizesProtectedStopFailure()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            (new MountSnapshot(MountState.Mounted), (char?)'P'),
            (new MountSnapshot(MountState.StopFailed, ErrorCode: "pending-writes-not-confirmed"), (char?)'E')
        });
        Assert.AreEqual(MountSummaryKind.StopFailed, summary.Kind);
        Assert.AreEqual("pending-writes-not-confirmed", summary.ErrorCode);
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
