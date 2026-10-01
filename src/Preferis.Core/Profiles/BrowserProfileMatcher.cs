using Preferis.Core.Browsers;
using Preferis.Core.Configuration;

namespace Preferis.Core.Profiles;

/// <summary>
/// Finds the detected browser profile a stored <see cref="BrowserProfileRef"/> refers to (docs/Design.md §7.2).
/// Shared by the resolver, the picker (avatars) and the settings UI so that all of them agree.
/// </summary>
public static class BrowserProfileMatcher
{
    public static BrowserProfileInfo? Match(IReadOnlyList<BrowserProfileInfo> profiles, BrowserProfileRef reference, BrowserFamily family) => family switch
    {
        // The account survives a profile being renamed or created in another order on another machine; the directory does neither.
        BrowserFamily.Chromium =>
            FindBy(profiles, p => p.Email, reference.Email, StringComparison.OrdinalIgnoreCase)
            ?? FindBy(profiles, p => p.Name, reference.Name, StringComparison.OrdinalIgnoreCase)
            ?? FindBy(profiles, p => p.Directory, reference.Directory, StringComparison.Ordinal),
        // Firefox selects profiles by name (-P); profiles.ini names are unique across channels.
        BrowserFamily.Firefox => FindBy(profiles, p => p.Name, reference.Name ?? reference.Directory, StringComparison.OrdinalIgnoreCase),
        _ => null,
    };

    private static BrowserProfileInfo? FindBy(IReadOnlyList<BrowserProfileInfo> profiles, Func<BrowserProfileInfo, string?> key, string? value, StringComparison comparison)
        => string.IsNullOrEmpty(value) ? null : profiles.FirstOrDefault(p => string.Equals(key(p), value, comparison));
}
