using System.IO;
using System.Text;

namespace PhoneBridge.Desktop;

internal static class LanguageSettings
{
    internal const string Chinese = "zh-CN";
    internal const string English = "en-US";

    internal static string Load(string appDataRoot)
    {
        try
        {
            string value = File.ReadAllText(PathFor(appDataRoot), Encoding.UTF8).Trim();
            return value is Chinese or English ? value : Chinese;
        }
        catch (IOException) { return Chinese; }
        catch (UnauthorizedAccessException) { return Chinese; }
    }

    internal static void Save(string appDataRoot, string language)
    {
        if (language is not (Chinese or English)) throw new ArgumentOutOfRangeException(nameof(language));
        string path = PathFor(appDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, language + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static string PathFor(string appDataRoot) => Path.Combine(appDataRoot, "Settings-v1", "language.txt");
}
