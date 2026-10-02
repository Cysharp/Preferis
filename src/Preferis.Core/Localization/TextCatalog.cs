using System.Globalization;

namespace Preferis.Core.Localization;

/// <summary>
/// The translations of one assembly's texts (docs/Design.md §9.4). The English text is the key: the code reads as the
/// UI does, and a text without a translation shows in English rather than not at all. Plain C# tables rather than
/// .resx resources: these would need CultureInfo, which InvariantGlobalization reduces to the invariant culture, and
/// satellite assemblies in every NativeAOT build.
/// </summary>
public sealed class TextCatalog
{
    private readonly Lazy<Dictionary<string, string>> _japanese;

    /// <param name="japanese">Builds the Japanese table, the first time a Japanese text is needed.</param>
    public TextCatalog(Func<Dictionary<string, string>> japanese)
    {
        _japanese = new(japanese);
    }

    /// <summary>English texts and their Japanese translations, for the tests that compare them with the code.</summary>
    public IReadOnlyDictionary<string, string> Japanese => _japanese.Value;

    public string Get(UiLanguage language, string text)
        => language == UiLanguage.Japanese && _japanese.Value.TryGetValue(text, out var translated) ? translated : text;

    /// <summary><paramref name="format"/> translated, with <paramref name="args"/> in its placeholders.</summary>
    public string Format(UiLanguage language, string format, ReadOnlySpan<object?> args)
        => string.Format(CultureInfo.InvariantCulture, Get(language, format), args);
}
