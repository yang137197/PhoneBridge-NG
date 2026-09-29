using System.Reflection;
using PhoneBridge.Desktop;
using PhoneBridge.Discovery;
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
    public void CardKeepsSavedVerifiedAddressDistinctFromLiveDiscovery()
    {
        var saved = DeviceListPolicy.ResolvePresence(false, null, null, "192.168.5.3", 8273);
        Assert.AreEqual(DevicePresenceKind.SavedAddress, saved.Kind);
        Assert.IsTrue(saved.CanConnect);
        CollectionAssert.AreEqual(new[] { "192.168.5.3" }, saved.Addresses.ToArray());

        var missing = DeviceListPolicy.ResolvePresence(false, null, null, "192.168.5.3", null);
        Assert.AreEqual(DevicePresenceKind.NotFound, missing.Kind);
        Assert.IsFalse(missing.CanConnect);
        Assert.IsEmpty(missing.Addresses);
    }

    [TestMethod]
    public void CurrentDiscoveryAddressesTakePrecedenceOverSavedAddress()
    {
        var current = new[]
        {
            new DeviceEndpoint("192.168.5.4", 8273),
            new DeviceEndpoint("192.168.5.4", 8274),
            new DeviceEndpoint("2001:db8::4", 8273)
        };

        var presence = DeviceListPolicy.ResolvePresence(false, null, current, "192.168.5.3", 8273);
        Assert.AreEqual(DevicePresenceKind.Discovered, presence.Kind);
        Assert.IsTrue(presence.CanConnect);
        CollectionAssert.AreEqual(new[] { "192.168.5.4", "2001:db8::4" }, presence.Addresses.ToArray());
    }

    [TestMethod]
    public void ConnectedCardUsesOnlyTheAuthenticatedActiveEndpoint()
    {
        var presence = DeviceListPolicy.ResolvePresence(true, new DeviceEndpoint("192.168.5.9", 8273),
            [new DeviceEndpoint("192.168.5.4", 8273)], "192.168.5.3", 8273);
        Assert.AreEqual(DevicePresenceKind.Connected, presence.Kind);
        CollectionAssert.AreEqual(new[] { "192.168.5.9" }, presence.Addresses.ToArray());
    }

    [TestMethod]
    public void RefreshCountUnionsLiveAndStrictlyVerifiedDevicesWithoutDuplicates()
    {
        Assert.AreEqual(3, DeviceListPolicy.CountRefreshedDevices(
            ["phone-a", "unpaired-candidate"], ["phone-a", "phone-b"]));
        Assert.AreEqual(0, DeviceListPolicy.CountRefreshedDevices([], []));
    }

    [TestMethod]
    public void MountFooterAggregatesAllSessionsWithoutDependingOnSelection()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            new DeviceMountStatus("phone-p", "Redmi K40", "phone-p", new MountSnapshot(MountState.Mounted), 'P'),
            new DeviceMountStatus("phone-e", "客厅手机", "phone-e", new MountSnapshot(MountState.Mounted), 'E'),
            new DeviceMountStatus("phone-z", "离线手机", null, new MountSnapshot(MountState.Stopped), null)
        });
        Assert.AreEqual(MountSummaryKind.Mounted, summary.Kind);
        CollectionAssert.AreEqual(new[] { 'E', 'P' }, summary.Devices.Select(item => item.Drive).ToArray());
        CollectionAssert.AreEqual(new[] { "客厅手机", "Redmi K40" }, summary.Devices.Select(item => item.DisplayName).ToArray());

        var stopping = DeviceListPolicy.SummarizeMounts(new[]
        {
            new DeviceMountStatus("phone-p", "Redmi K40", "phone-p", new MountSnapshot(MountState.Mounted), 'P'),
            new DeviceMountStatus("phone-e", "客厅手机", "phone-e", new MountSnapshot(MountState.Stopping), 'E')
        });
        Assert.AreEqual(MountSummaryKind.Stopping, stopping.Kind);
        Assert.HasCount(1, stopping.Devices);
        Assert.AreEqual("phone-p", stopping.Devices[0].DeviceId);
    }

    [TestMethod]
    public void MountFooterPrioritizesProtectedStopFailure()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            new DeviceMountStatus("phone-p", "Redmi K40", "phone-p", new MountSnapshot(MountState.Mounted), 'P'),
            new DeviceMountStatus("phone-e", "客厅手机", "phone-e",
                new MountSnapshot(MountState.StopFailed, ErrorCode: "pending-writes-not-confirmed"), 'E')
        });
        Assert.AreEqual(MountSummaryKind.StopFailed, summary.Kind);
        Assert.AreEqual("pending-writes-not-confirmed", summary.ErrorCode);
        Assert.HasCount(1, summary.Devices);
        Assert.AreEqual("phone-p", summary.Devices[0].DeviceId);
    }

    [TestMethod]
    public void MountFooterRequiresExactMountedIdentity()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            new DeviceMountStatus("phone-a", "客厅手机", "phone-b", new MountSnapshot(MountState.Mounted), 'E'),
            new DeviceMountStatus("phone-b", "Redmi K40", null, new MountSnapshot(MountState.Mounted), 'P')
        });

        Assert.AreEqual(MountSummaryKind.None, summary.Kind);
        Assert.IsEmpty(summary.Devices);
    }

    [TestMethod]
    public void FooterTextListsEveryConnectedDeviceWithItsDrive()
    {
        var summary = DeviceListPolicy.SummarizeMounts(new[]
        {
            new DeviceMountStatus("phone-p", "Redmi K40", "phone-p", new MountSnapshot(MountState.Mounted), 'P'),
            new DeviceMountStatus("phone-e", "客厅手机", "phone-e", new MountSnapshot(MountState.Mounted), 'E')
        });
        var chinese = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        string Zh(string key) => TextCatalog.Get(key, chinese);

        Assert.AreEqual("客厅手机、Redmi K40 已连接。", FooterTextPolicy.Connection(summary, Zh));
        Assert.AreEqual("客厅手机 挂载在 E:\\；Redmi K40 挂载在 P:\\。", FooterTextPolicy.Mounts(summary, Zh));

        var empty = new MountSummary(MountSummaryKind.None, []);
        Assert.AreEqual("请连接手机。", FooterTextPolicy.Connection(empty, Zh));
        Assert.AreEqual("当前无挂载盘符。", FooterTextPolicy.Mounts(empty, Zh));

        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        string En(string key) => TextCatalog.Get(key, english);
        Assert.AreEqual("Connected: 客厅手机, Redmi K40.", FooterTextPolicy.Connection(summary, En));
        Assert.AreEqual("客厅手机 is mounted at E:\\; Redmi K40 is mounted at P:\\.", FooterTextPolicy.Mounts(summary, En));
    }

    [TestMethod]
    public void BusyCardOnlyDisablesAndCancelsItsOwnDevice()
    {
        Type type = typeof(MainWindow).GetNestedType("DeviceRow", BindingFlags.NonPublic)!;
        object NewRow(string id, bool busy) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [id, id, null, null, false, null, null, busy], culture: null)!;
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

    [TestMethod]
    public void EquivalentDeviceRowsDoNotRequireReplacingTheVisibleCard()
    {
        Type type = typeof(MainWindow).GetNestedType("DeviceRow", BindingFlags.NonPublic)!;
        object NewRow(bool operationInProgress) => Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: ["phone-a", "phone-a", null, null, false, null, null, operationInProgress], culture: null)!;

        object visible = NewRow(false);
        object unchanged = NewRow(false);
        object busy = NewRow(true);

        Assert.IsFalse(DeviceListPolicy.RowsChanged<object>([visible], [unchanged]));
        Assert.IsTrue(DeviceListPolicy.RowsChanged<object>([visible], [busy]));
    }
}
