using System.Runtime.Versioning;
using Preferis.Platform.MacOS;
using Preferis.Platform.Windows;

namespace Preferis.Platform;

/// <summary>
/// The user's preferred UI languages, which <c>settings.language: "auto"</c> follows (docs/Design.md §9.4). Asked of the
/// OS: with InvariantGlobalization, CultureInfo.CurrentUICulture knows nothing about them.
/// </summary>
public static class SystemLanguages
{
    /// <summary>BCP 47 tags, most preferred first ("ja-JP", "en-US"). Empty when the OS does not say.</summary>
    public static IReadOnlyList<string> Preferred()
    {
        try
        {
            return OperatingSystem.IsWindows() ? FromWindows()
                : OperatingSystem.IsMacOS() ? FromMacOS()
                : [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Not knowing the language is no reason to fail: the UI shows in English.
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string[] FromWindows()
    {
        uint length = 0;
        if (!Native.GetUserPreferredUILanguages(Native.MuiLanguageName, out _, null, ref length) || length == 0)
        {
            return [];
        }
        var buffer = new char[length];
        fixed (char* languages = buffer)
        {
            if (!Native.GetUserPreferredUILanguages(Native.MuiLanguageName, out _, languages, ref length))
            {
                return [];
            }
        }
        // A list of names each ending with a NUL, and an empty one at the end.
        return new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [SupportedOSPlatform("macos")]
    private static List<string> FromMacOS()
    {
        using var pool = ObjC.AutoreleasePool();
        return ObjC.Enumerate(ObjC.Send(ObjC.GetClass("NSLocale"), "preferredLanguages")).Select(ObjC.FromNSString).OfType<string>().ToList();
    }
}
