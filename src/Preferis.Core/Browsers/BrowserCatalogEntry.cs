namespace Preferis.Core.Browsers;

/// <summary>How a browser stores user profiles and accepts command line arguments.</summary>
public enum BrowserFamily
{
    /// <summary>Chromium based: <c>--profile-directory</c>, profiles listed in <c>Local State</c>.</summary>
    Chromium,

    /// <summary>Firefox based: <c>-P &lt;name&gt;</c>, profiles listed in <c>profiles.ini</c>.</summary>
    Firefox,

    /// <summary>No profile or argument support worth relying on (Safari, unknown browsers): opened through the OS.</summary>
    Other,
}

/// <summary>Where a browser's Windows user data lives.</summary>
public enum WindowsKnownFolder
{
    LocalAppData,
    RoamingAppData,
}

/// <summary>Static knowledge about one browser (one release channel = one entry). See docs/Configuration.md §2.4.</summary>
public sealed record BrowserCatalogEntry(
    string Id,
    string DisplayName,
    BrowserFamily Family,
    string? WindowsStartMenuKey,
    string? WindowsStartMenuKeyPrefix,
    IReadOnlyList<string> WindowsExecutableNames,
    IReadOnlyList<string> WindowsKnownPaths,
    WindowsKnownFolder WindowsUserDataRoot,
    string? WindowsUserDataDir,
    string? MacBundleId,
    string? MacUserDataDir,
    string? PrivateModeArgument);
