namespace Preferis.Core.Localization;

/// <summary>The languages Preferis shows (docs/Design.md §9.4).</summary>
public enum UiLanguage
{
    English,
    Japanese,
}

/// <summary>
/// Which language this execution shows. UI processes set their default once at start-up, before building any text.
/// The Router uses a scope for each URL so concurrent requests and their diagnostics keep their own language.
/// </summary>
public static class UiLanguages
{
    private static Lazy<UiLanguage> _current = new(UiLanguage.English);
    private static readonly AsyncLocal<Lazy<UiLanguage>?> Scoped = new();

    /// <summary>The active scope's language, or the process default. Set only the default, at UI process start-up.</summary>
    public static UiLanguage Current
    {
        get => (Scoped.Value ?? _current).Value;
        set => _current = new(value);
    }

    /// <summary>
    /// Keeps a language for this execution until disposed, restoring the enclosing scope even on failure. Concurrent
    /// executions have separate scopes; asynchronous continuations inherit their caller's. Resolve only when text is
    /// needed, so a successful configuration load need not query the OS before an explicit language takes over.
    /// </summary>
    public static IDisposable UseLazily(Func<UiLanguage> resolve)
    {
        var scope = new LanguageScope(Scoped.Value);
        Scoped.Value = new(resolve);
        return scope;
    }

    private sealed class LanguageScope(Lazy<UiLanguage>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Scoped.Value = previous;
            _disposed = true;
        }
    }

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
