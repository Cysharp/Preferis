using Preferis.Core.Localization;
using Preferis.Tests.Localization;

namespace Preferis.Core.Tests.Localization;

public class CoreTextTests
{
    // Everything that shows text from the Core table: the Router and the pickers, through Core and Platform.
    private static readonly string[] Projects = ["Preferis.Core", "Preferis.Platform", "Preferis.Router", "Preferis.Picker.Windows", "Preferis.Picker.MacOS"];

    [Fact]
    public void Every_text_has_a_japanese_translation_and_every_translation_a_text()
    {
        var problems = new List<string>();
        var texts = TranslationAudit.FindTexts(Projects, problems);
        problems.AddRange(TranslationAudit.Check(texts, CoreText.Catalog.Japanese));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.NotEmpty(texts);
    }

    [Fact]
    public void Texts_are_english_unless_japanese_is_chosen()
    {
        Assert.Equal("Private", CoreText.Catalog.Get(UiLanguage.English, "Private"));
        Assert.Equal("プライベート", CoreText.Catalog.Get(UiLanguage.Japanese, "Private"));
    }
}
