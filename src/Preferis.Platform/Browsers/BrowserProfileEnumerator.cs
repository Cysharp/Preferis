using System.Text.Json;
using Preferis.Core.Browsers;

namespace Preferis.Platform.Browsers;

/// <summary>
/// Lists a detected browser's profiles from its user data directory. Returns null whenever the data cannot be
/// read (missing, denied as on macOS 27, corrupt) so that the resolver falls back to the stored directory.
/// </summary>
public sealed class BrowserProfileEnumerator : IBrowserProfileEnumerator
{
    public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser)
    {
        if (browser.UserDataDir is null)
        {
            return null;
        }

        try
        {
            switch (browser.Family)
            {
                case BrowserFamily.Chromium:
                    var localState = Path.Combine(browser.UserDataDir, "Local State");
                    if (!File.Exists(localState))
                    {
                        return null;
                    }
                    using (var stream = File.OpenRead(localState))
                    {
                        return ChromiumLocalStateReader.Read(stream, browser.UserDataDir);
                    }

                case BrowserFamily.Firefox:
                    var ini = Path.Combine(browser.UserDataDir, "profiles.ini");
                    if (!File.Exists(ini))
                    {
                        return null;
                    }
                    using (var reader = new StreamReader(ini))
                    {
                        return FirefoxProfilesIniReader.Read(reader, browser.UserDataDir);
                    }

                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
