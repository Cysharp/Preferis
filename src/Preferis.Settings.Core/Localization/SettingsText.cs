using Preferis.Core.Localization;

namespace Preferis.Settings.Localization;

/// <summary>
/// The texts of the settings, view models and both native UIs, in <see cref="UiLanguages.Current"/>
/// (docs/Design.md §9.4). Use with <c>using static</c>: <c>L("Profiles")</c>, <c>L("Delete “{0}”?", name)</c>. Always
/// pass the English text as a literal; the tests find the texts that need a translation by looking for them.
/// </summary>
public static partial class SettingsText
{
    internal static readonly TextCatalog Catalog = new(Japanese);

    /// <summary><paramref name="text"/> in the language of the UI.</summary>
    public static string L(string text) => Catalog.Get(UiLanguages.Current, text);

    /// <summary><paramref name="format"/> in the language of the UI, with <paramref name="args"/> in its placeholders.</summary>
    public static string L(string format, params ReadOnlySpan<object?> args) => Catalog.Format(UiLanguages.Current, format, args);

    /// <summary><paramref name="text"/> in each language, for finding what an instance showing another language shows.</summary>
    public static IReadOnlyList<string> InEveryLanguage(string text)
        => [.. Enum.GetValues<UiLanguage>().Select(language => Catalog.Get(language, text)).Distinct()];
}
