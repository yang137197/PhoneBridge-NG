using PhoneBridge.Desktop;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class TrayLifecycleTests
{
    [TestMethod]
    [DataRow(false, false, false, false, 0)]
    [DataRow(false, false, false, true, 1)]
    [DataRow(false, false, true, true, 2)]
    [DataRow(false, true, true, true, 3)]
    [DataRow(true, true, true, true, 4)]
    public void StatusUsesDeterministicPriority(bool error, bool mounted, bool busy, bool discovered, int expected) =>
        Assert.AreEqual((TrayStatus)expected, TrayPolicy.ResolveStatus(error, mounted, busy, discovered));

    [TestMethod]
    public void WindowCloseHidesOnlyWhenTrayCanRestoreIt()
    {
        Assert.AreEqual(WindowCloseAction.Hide, TrayPolicy.ResolveClose(true, false));
        Assert.AreEqual(WindowCloseAction.Exit, TrayPolicy.ResolveClose(true, true));
        Assert.AreEqual(WindowCloseAction.Exit, TrayPolicy.ResolveClose(false, false));
    }
}
