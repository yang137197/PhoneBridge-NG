using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace PhoneBridge.Desktop;

public static class TextCatalog
{
    private static readonly ResourceManager Resources = new("PhoneBridge.Desktop.Resources.Strings", typeof(TextCatalog).Assembly);
    public static string Get(string key) => Get(key, CultureInfo.CurrentUICulture);
    internal static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? Resources.GetString("OperationFailed", culture)!;
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => TextCatalog.Get(key);
}
