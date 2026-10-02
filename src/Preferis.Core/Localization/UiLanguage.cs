namespace Preferis.Core.Localization;

/// <summary>The languages Preferis shows (docs/Design.md §9.4).</summary>
public enum UiLanguage
{
    English,
    Japanese,
}

/// <summary>
/// Which language this process shows. <c>settings.language</c> chooses it; <c>auto</c> follows the OS's preferred
/// languages. Process wide, like the UI it describes: set once at start-up, before any text is built (the settings'
/// drop-down lists are built once), and by the Router for each URL.
/// </summary>
public static class UiLanguages
{
    private static Lazy<UiLanguage> _current = new(UiLanguage.English);

    /// <summary>The language of everything shown from now on. English until something sets it.</summary>
    public static UiLanguage Current
    {
        get => _current.Value;
        set => _current = new(value);
    }

    /// <summary>
    /// Decides <see cref="Current"/> when it is first read, for the Router, which sets it for every URL before it knows
    /// whether any text will be needed.
    /// </summary>
    public static void SetLazily(Func<UiLanguage> resolve) => _current = new(resolve);

    /// <summary>The code of <c>settings.language</c> and of the picker request.</summary>
    public static string Code(this UiLanguage language) => language == UiLanguage.Japanese ? "ja" : "en";

    /// <summary>The language of a code, or null for <c>auto</c> and codes this version does not know.</summary>
    public static UiLanguage? FromCode(string? code) => code switch
    {
        "en" => UiLanguage.English,
        "ja" => UiLanguage.Japanese,
        _ => null,
    };

    /// <summary>
    /// The language for <c>settings.language</c>. <c>auto</c>, as well as a language a newer Preferis added, is the first
    /// of <paramref name="systemLanguages"/> (BCP 47 tags, most preferred first) that Preferis has, else English.
    /// <paramref name="systemLanguages"/> is asked only for those.
    /// </summary>
    public static UiLanguage Resolve(string? setting, Func<IEnumerable<string>> systemLanguages)
    {
        if (FromCode(setting) is { } chosen)
        {
            return chosen;
        }
        foreach (var tag in systemLanguages())
        {
            // The primary subtag decides: "ja-JP" and "ja-Jpan-JP" are Japanese, "jam" is not.
            var primary = tag.Split('-', '_')[0];
            if (primary.Equals("ja", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguage.Japanese;
            }
            if (primary.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguage.English;
            }
        }
        return UiLanguage.English;
    }
}
