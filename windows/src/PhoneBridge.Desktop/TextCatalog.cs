using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace PhoneBridge.Desktop;

public static class TextCatalog
{
    private static readonly ResourceManager Resources = new("PhoneBridge.Desktop.Resources.Strings", typeof(TextCatalog).Assembly);
    private static readonly LocalizedTextSource Source = new();
    internal static object BindingSource => Source;
    internal static event Action? CultureChanged;

    public static string Get(string key) => Get(key, CultureInfo.CurrentUICulture);
    internal static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? Resources.GetString("OperationFailed", culture)!;

    internal static void SetCulture(string language)
    {
        if (language is not (LanguageSettings.Chinese or LanguageSettings.English))
            throw new ArgumentOutOfRangeException(nameof(language));
        var culture = CultureInfo.GetCultureInfo(language);
        bool changed = CultureInfo.CurrentUICulture.Name != culture.Name;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (!changed) return;
        Source.Refresh();
        CultureChanged?.Invoke();
    }

    private sealed class LocalizedTextSource : INotifyPropertyChanged
    {
        public string this[string key] => Get(key);
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new System.Windows.Data.Binding($"[{key}]") { Source = TextCatalog.BindingSource, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
