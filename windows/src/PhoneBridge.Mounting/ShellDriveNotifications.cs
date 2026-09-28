using System.Runtime.InteropServices;

namespace PhoneBridge.Mounting;

internal static class ShellDriveNotifications
{
    private const uint DriveRemoved = 0x00000080;
    private const uint PathUnicode = 0x0005;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, nint item2);

    internal static void NotifyRemoved(char driveLetter)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SHChangeNotify(DriveRemoved, PathUnicode, $"{char.ToUpperInvariant(driveLetter)}:\\", 0); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
