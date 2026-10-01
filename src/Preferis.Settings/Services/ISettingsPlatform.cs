using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Hosting;

namespace Preferis.Settings.Services;

/// <summary>A browser found on this machine with everything the settings UI shows about it.</summary>
/// <param name="Profiles">Its browser profiles, or null when they cannot be read (docs/Platform.md §3.4).</param>
/// <param name="IconPng">The application icon as PNG, when available.</param>
public sealed record DetectedBrowserInfo(DetectedBrowser Browser, IReadOnlyList<BrowserProfileInfo>? Profiles, byte[]? IconPng);

/// <summary>What the settings UI needs from the OS. One implementation per OS; faked in the tests.</summary>
public interface ISettingsPlatform
{
    HostPlatform Platform { get; }

    /// <summary>Every browser installed here, with profiles and icons. Slow (tens of milliseconds); call off the UI thread when it matters.</summary>
    IReadOnlyList<DetectedBrowserInfo> DetectBrowsers();

    /// <summary>For resolving profiles on the test page exactly like the Router would.</summary>
    IBrowserLocator BrowserLocator { get; }

    IBrowserProfileEnumerator BrowserProfiles { get; }

    Func<string, string?> GetEnvironmentVariable { get; }

    string HomeDirectory { get; }

    /// <summary>False when the settings UI does not run from inside <c>Preferis.app</c> (e.g. <c>dotnet run</c>): registration needs the Router's path.</summary>
    bool CanRegister { get; }

    RegistrationStatus GetRegistrationStatus();

    /// <summary>Registers Preferis as the default browser by running <c>Preferis --register</c> (docs/Design.md §9.1). The OS may ask the user to confirm.</summary>
    Task<RegistrationResult> RegisterAsync();

    /// <summary>Opens the OS page where the default browser is chosen.</summary>
    void OpenDefaultBrowserSettings();

    /// <summary>Shows a file or folder in Finder / Explorer.</summary>
    void Reveal(string path);
}
