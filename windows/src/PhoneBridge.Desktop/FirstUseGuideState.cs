using System.IO;
using System.Text;

namespace PhoneBridge.Desktop;

internal static class FirstUseGuideState
{
    private const string Completed = "completed";

    internal static bool ShouldShow(string appDataRoot, bool hasPairedDevices)
    {
        if (IsCompleted(appDataRoot)) return false;
        if (!hasPairedDevices) return true;
        _ = TryMarkCompleted(appDataRoot);
        return false;
    }

    internal static bool TryMarkCompleted(string appDataRoot)
    {
        string path = PathFor(appDataRoot);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, Completed + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static string PathFor(string appDataRoot) => Path.Combine(appDataRoot, "Settings-v1", "first-use-guide.txt");

    private static bool IsCompleted(string appDataRoot)
    {
        try { return File.ReadAllText(PathFor(appDataRoot), Encoding.UTF8).Trim() == Completed; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
