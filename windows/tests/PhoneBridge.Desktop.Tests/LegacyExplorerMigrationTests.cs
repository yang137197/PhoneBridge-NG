using Microsoft.Win32;
using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class LegacyExplorerMigrationTests
{
    [TestMethod]
    public void LegacyMountPointNamesAreNarrowlyMatched()
    {
        Assert.IsTrue(LegacyExplorerMigration.IsLegacyMountPointName("##pbng-3ce4642a38#K40"));
        Assert.IsTrue(LegacyExplorerMigration.IsLegacyMountPointName("##PBNG-ABCDEF1234#Phone"));
        Assert.IsFalse(LegacyExplorerMigration.IsLegacyMountPointName("##pbng-3ce4642a3#K40"));
        Assert.IsFalse(LegacyExplorerMigration.IsLegacyMountPointName("##pbng-3ce4642a38#"));
        Assert.IsFalse(LegacyExplorerMigration.IsLegacyMountPointName("##server#PhoneBridge"));
        Assert.IsFalse(LegacyExplorerMigration.IsLegacyMountPointName("E"));
    }

    [TestMethod]
    public void CleanupDeletesOnlyLegacyPhoneBridgeNetworkMounts()
    {
        string testPath = @"Software\PhoneBridge-NG-Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (RegistryKey root = Registry.CurrentUser.CreateSubKey(testPath, writable: true))
            {
                root.CreateSubKey("##pbng-3ce4642a38#K40").Dispose();
                root.CreateSubKey("##pbng-abcdef1234#Other").Dispose();
                root.CreateSubKey("##server#Unrelated").Dispose();
                root.CreateSubKey("E").Dispose();
                Assert.AreEqual(2, LegacyExplorerMigration.DeleteLegacyMountPoints(root));
                CollectionAssert.AreEquivalent(new[] { "##server#Unrelated", "E" }, root.GetSubKeyNames());
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testPath, throwOnMissingSubKey: false); }
    }

    [TestMethod]
    public void PendingMarkerKeepsMigrationVisibleAfterCleanupFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-MigrationTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.IsFalse(LegacyExplorerMigration.RequiresAction(root, []));
            string pending = LegacyExplorerMigration.PendingPathFor(root);
            Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
            File.WriteAllText(pending, "pending");
            Assert.IsTrue(LegacyExplorerMigration.RequiresAction(root, []));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
