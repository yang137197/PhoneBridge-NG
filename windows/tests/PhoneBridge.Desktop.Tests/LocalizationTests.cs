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
            "ManualEndpointAdded", "ManualEndpointCleared" })
        {
            string en = TextCatalog.Get(key, english);
            string zh = TextCatalog.Get(key, chinese);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en));
            Assert.IsFalse(string.IsNullOrWhiteSpace(zh));
            Assert.AreNotEqual(TextCatalog.Get("OperationFailed", english), en);
            Assert.AreNotEqual(TextCatalog.Get("OperationFailed", chinese), zh);
        }
    }
}
