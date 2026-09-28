using System.Globalization;
using System.Text;

namespace PhoneBridge.Mounting;

internal static class MountVolumeName
{
    private const string Forbidden = "\"\\/[]:|<>+=;,*?";
    private const int MaximumUtf16Length = 64;

    internal static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder();
        foreach (var rune in value.Trim().EnumerateRunes())
        {
            if (result.Length + rune.Utf16SequenceLength > MaximumUtf16Length) break;
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control ||
                (rune.IsAscii && Forbidden.Contains((char)rune.Value, StringComparison.Ordinal)))
                result.Append('_');
            else result.Append(rune.ToString());
        }
        string normalized = result.ToString().TrimEnd(' ', '.');
        return normalized.Length == 0 ? "PhoneBridge" : normalized;
    }
}
