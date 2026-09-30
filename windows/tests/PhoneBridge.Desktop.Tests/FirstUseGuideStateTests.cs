using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class FirstUseGuideStateTests
{
    [TestMethod]
    public void FreshInstallShowsUntilCompleted()
    {
        WithRoot(root =>
        {
            Assert.IsTrue(FirstUseGuideState.ShouldShow(root, hasPairedDevices: false));
            Assert.IsTrue(FirstUseGuideState.TryMarkCompleted(root));
            Assert.IsFalse(FirstUseGuideState.ShouldShow(root, hasPairedDevices: false));
        });
    }

    [TestMethod]
    public void ExistingPairingMigratesWithoutShowingGuide()
    {
        WithRoot(root =>
        {
            Assert.IsFalse(FirstUseGuideState.ShouldShow(root, hasPairedDevices: true));
            Assert.IsFalse(FirstUseGuideState.ShouldShow(root, hasPairedDevices: false));
        });
    }

    [TestMethod]
    public void UnknownMarkerDoesNotSuppressGuide()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FirstUseGuideState.PathFor(root))!);
            File.WriteAllText(FirstUseGuideState.PathFor(root), "unknown");
            Assert.IsTrue(FirstUseGuideState.ShouldShow(root, hasPairedDevices: false));
        });
    }

    private static void WithRoot(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-FirstUseGuideTests", Guid.NewGuid().ToString("N"));
        try { action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
