namespace Preferis.Core.Localization;

/// <summary>
/// The texts of Core, Platform, the Router and the pickers in <see cref="UiLanguages.Current"/> (docs/Design.md §9.4).
/// Use with <c>using static</c>: <c>L("Open")</c>, <c>L("Open ({0})", seconds)</c>. Always pass the English text as a
/// literal; the tests find the texts that need a translation by looking for them.
/// </summary>
public static partial class CoreText
{
    internal static readonly TextCatalog Catalog = new(Japanese);

    /// <summary><paramref name="text"/> in the language of the UI.</summary>
    public static string L(string text) => Catalog.Get(UiLanguages.Current, text);

    /// <summary><paramref name="format"/> in the language of the UI, with <paramref name="args"/> in its placeholders.</summary>
    public static string L(string format, params ReadOnlySpan<object?> args) => Catalog.Format(UiLanguages.Current, format, args);
}
