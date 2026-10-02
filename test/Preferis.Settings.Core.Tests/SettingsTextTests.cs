using Preferis.Core.Localization;
using Preferis.Settings.Localization;
using Preferis.Tests.Localization;

namespace Preferis.Settings.Core.Tests;

public class SettingsTextTests
{
    // The view models and both native UIs; the AppKit one is checked here although it builds only on macOS.
    private static readonly string[] Projects = ["Preferis.Settings.Core", "Preferis.Settings.Windows", "Preferis.Settings.MacOS"];

    [Fact]
    public void Every_text_has_a_japanese_translation_and_every_translation_a_text()
    {
        var problems = new List<string>();
        var texts = TranslationAudit.FindTexts(Projects, problems);
        problems.AddRange(TranslationAudit.Check(texts, SettingsText.Catalog.Japanese));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.NotEmpty(texts);
    }

    [Fact]
    public void Texts_are_english_unless_japanese_is_chosen()
    {
        Assert.Equal("Profiles", SettingsText.Catalog.Get(UiLanguage.English, "Profiles"));
        Assert.Equal("プロファイル", SettingsText.Catalog.Get(UiLanguage.Japanese, "Profiles"));
    }
}
