using System.Globalization;
using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    public void NewDesktopResourcesExistInEnglishAndChinese()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        var chinese = CultureInfo.GetCultureInfo("zh-CN");
        foreach (string key in new[] { "ExportDiagnostics", "DiagnosticsZipFilter", "DiagnosticsExported", "DiagnosticsExportFailed",
            "ManualAddress", "ManualPort", "UseManualAddress", "ClearManualAddress", "ManualEndpointInvalid",
            "ManualEndpointAdded", "ManualEndpointCleared", "English", "Working", "LanguageSaveFailed",
            "AutomaticDrive", "AutomaticDriveHelp", "DefaultDrive", "MountedSummary", "UnmountingDrive",
            "ReconnectWaiting", "ReconnectBackoff", "ReconnectStopped", "SharingStopped",
            "ConnectedDevicesSummary", "MountedDeviceItem", "MountedDevicesSummary",
            "FooterNameSeparator", "FooterItemSeparator", "LegacyExplorerMigrationPrompt",
            "LegacyExplorerMigrationCompleted", "LegacyExplorerMigrationFailed",
            "LegacyExplorerMigrationInspectionFailed", "DeviceDiscovered", "SavedAddressAvailable",
            "DeviceNotFound", "DeviceIp", "LastVerifiedDeviceIp", "MountedDrive", "RefreshingDevices",
            "RefreshDevicesFound", "RefreshNoDevices", "RefreshFailed", "CurrentVersion", "CheckForUpdates",
            "CheckingForUpdates", "UpdateIsCurrent", "UpdateAvailable", "UpdateDownloadPrompt",
            "UpdateDownloading", "UpdateDownloaded", "UpdateInstallPrompt", "UpdateInstalling",
            "UpdateCurrentVersionInvalid", "UpdateCheckFailed", "UpdateResponseInvalid", "UpdateAssetMissing",
            "UpdateIntegrityFailed", "UpdateDownloadFailed" })
        {
            string en = TextCatalog.Get(key, english);
            string zh = TextCatalog.Get(key, chinese);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en));
            Assert.IsFalse(string.IsNullOrWhiteSpace(zh));
            Assert.AreNotEqual(TextCatalog.Get("OperationFailed", english), en);
            Assert.AreNotEqual(TextCatalog.Get("OperationFailed", chinese), zh);
        }
    }

    [TestMethod]
    public void DesktopLanguageSettingDefaultsValidatesAndPersists()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-LanguageTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.AreEqual(LanguageSettings.Chinese, LanguageSettings.Load(root));
            LanguageSettings.Save(root, LanguageSettings.English);
            Assert.AreEqual(LanguageSettings.English, LanguageSettings.Load(root));
            File.WriteAllText(LanguageSettings.PathFor(root), "unsupported");
            Assert.AreEqual(LanguageSettings.Chinese, LanguageSettings.Load(root));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LanguageSettings.Save(root, "fr-FR"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CultureChangeRefreshesCatalogAndRaisesNotification()
    {
        int changes = 0;
        void Changed() => changes++;
        TextCatalog.CultureChanged += Changed;
        try
        {
            TextCatalog.SetCulture(LanguageSettings.English);
            Assert.AreEqual("Connect a phone.", TextCatalog.Get("Ready"));
            TextCatalog.SetCulture(LanguageSettings.Chinese);
            Assert.AreEqual("请连接手机。", TextCatalog.Get("Ready"));
            Assert.IsGreaterThanOrEqualTo(1, changes);
        }
        finally
        {
            TextCatalog.CultureChanged -= Changed;
            TextCatalog.SetCulture(LanguageSettings.Chinese);
        }
    }
}
