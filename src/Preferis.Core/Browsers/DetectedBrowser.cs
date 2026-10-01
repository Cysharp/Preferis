namespace Preferis.Core.Browsers;

/// <summary>A browser found on this machine.</summary>
/// <param name="Id">Catalog id, or a <c>win:</c> / <c>mac:</c> prefixed id for browsers outside the catalog.</param>
/// <param name="ExecutablePath">The binary to execute directly (on macOS: inside <c>Contents/MacOS</c>).</param>
/// <param name="ApplicationPath">The <c>.app</c> bundle on macOS; null on Windows.</param>
/// <param name="UserDataDir">Where this browser's profiles live, when known.</param>
public sealed record DetectedBrowser(
    string Id,
    string DisplayName,
    BrowserFamily Family,
    string ExecutablePath,
    string? ApplicationPath,
    string? UserDataDir,
    BrowserCatalogEntry? CatalogEntry);

/// <summary>One profile of a detected browser.</summary>
/// <param name="Directory">Chromium: directory name under the user data dir. Firefox: profile path.</param>
/// <param name="Email">Chromium <c>user_name</c>.</param>
/// <param name="AvatarPath">Picture file for the picker, when available.</param>
public sealed record BrowserProfileInfo(string Directory, string? Name, string? Email, string? AvatarPath = null);

/// <summary>Resolves one browser id to its installation. Implemented by the Platform layer; kept cheap for the fast path.</summary>
public interface IBrowserLocator
{
    DetectedBrowser? TryResolve(string browserId);
}

/// <summary>Lists the profiles of a browser. Returns null when the profile data cannot be read.</summary>
public interface IBrowserProfileEnumerator
{
    IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser);
}
