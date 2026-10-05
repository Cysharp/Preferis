using Preferis.Core.Localization;

namespace Preferis.Core.Tests.Localization;

public class UiLanguagesTests
{
    [Fact]
    public void Nested_scopes_restore_the_enclosing_language_even_when_work_fails()
    {
        var original = UiLanguages.Current;
        using (UiLanguages.UseLazily(() => UiLanguage.Japanese))
        {
            Assert.Equal(UiLanguage.Japanese, UiLanguages.Current);
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var inner = UiLanguages.UseLazily(() => UiLanguage.English);
                Assert.Equal(UiLanguage.English, UiLanguages.Current);
                throw new InvalidOperationException("Interrupted.");
            }));
            Assert.Equal(UiLanguage.Japanese, UiLanguages.Current);
        }
        Assert.Equal(original, UiLanguages.Current);
    }

    [Fact]
    public async Task Async_work_inherits_its_scope_without_changing_the_callers_scope()
    {
        using var outer = UiLanguages.UseLazily(() => UiLanguage.Japanese);
        await Task.Run(async () =>
        {
            Assert.Equal(UiLanguage.Japanese, UiLanguages.Current);
            using var inner = UiLanguages.UseLazily(() => UiLanguage.English);
            await Task.Yield();
            Assert.Equal(UiLanguage.English, UiLanguages.Current);
        });
        Assert.Equal(UiLanguage.Japanese, UiLanguages.Current);
    }

    [Fact]
    public void An_explicit_language_scope_does_not_resolve_an_unused_system_language()
    {
        using var loading = UiLanguages.UseLazily(() => throw new InvalidOperationException("OS languages were requested."));
        using var configured = UiLanguages.UseLazily(() => UiLanguage.Japanese);

        Assert.Equal("開く", CoreText.L("Open"));
    }

    [Theory]
    [InlineData("en", UiLanguage.English)]
    [InlineData("ja", UiLanguage.Japanese)]
    public void An_explicit_language_is_used_whatever_the_system_prefers(string setting, UiLanguage expected)
    {
        var asked = false;
        var language = UiLanguages.Resolve(setting, () =>
        {
            asked = true;
            return ["fr-FR"];
        });

        Assert.Equal(expected, language);
        Assert.False(asked);
    }

    [Theory]
    [InlineData(new[] { "ja-JP", "en-US" }, UiLanguage.Japanese)]
    [InlineData(new[] { "en-GB", "ja-JP" }, UiLanguage.English)]
    [InlineData(new[] { "ja" }, UiLanguage.Japanese)]
    // The first language Preferis has: German is skipped for Japanese, the user's next choice.
    [InlineData(new[] { "de-DE", "ja-JP" }, UiLanguage.Japanese)]
    [InlineData(new[] { "de-DE", "fr-FR" }, UiLanguage.English)]
    [InlineData(new string[0], UiLanguage.English)]
    public void Auto_follows_the_systems_preferred_languages(string[] preferred, UiLanguage expected)
        => Assert.Equal(expected, UiLanguages.Resolve("auto", () => preferred));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // A language a newer Preferis added: this one shows what the system prefers instead.
    [InlineData("zh-Hans")]
    public void A_missing_or_unknown_setting_is_auto(string? setting)
        => Assert.Equal(UiLanguage.Japanese, UiLanguages.Resolve(setting, () => ["ja-JP"]));

    [Fact]
    public void Language_tags_are_matched_ignoring_case_and_script_or_region()
    {
        Assert.Equal(UiLanguage.Japanese, UiLanguages.Resolve("auto", () => ["JA-jp"]));
        Assert.Equal(UiLanguage.Japanese, UiLanguages.Resolve("auto", () => ["ja-Jpan-JP"]));
        // "jam" (Jamaican Creole) is not Japanese.
        Assert.Equal(UiLanguage.English, UiLanguages.Resolve("auto", () => ["jam"]));
    }

    [Fact]
    public void Codes_are_those_of_the_configuration()
    {
        Assert.Equal("en", UiLanguage.English.Code());
        Assert.Equal("ja", UiLanguage.Japanese.Code());
        Assert.Equal(UiLanguage.Japanese, UiLanguages.FromCode("ja"));
        Assert.Equal(UiLanguage.English, UiLanguages.FromCode("en"));
        Assert.Null(UiLanguages.FromCode("auto"));
        Assert.Null(UiLanguages.FromCode(null));
    }

    [Fact]
    public void A_catalog_shows_the_translation_of_the_language_and_english_otherwise()
    {
        var catalog = new TextCatalog(() => new Dictionary<string, string> { ["Open"] = "開く", ["Open {0} profiles"] = "{0} 個のプロファイルを開く" });

        Assert.Equal("開く", catalog.Get(UiLanguage.Japanese, "Open"));
        Assert.Equal("Open", catalog.Get(UiLanguage.English, "Open"));
        // Better English than nothing.
        Assert.Equal("Cancel", catalog.Get(UiLanguage.Japanese, "Cancel"));
        Assert.Equal("3 個のプロファイルを開く", catalog.Format(UiLanguage.Japanese, "Open {0} profiles", [3]));
        Assert.Equal("Open 3 profiles", catalog.Format(UiLanguage.English, "Open {0} profiles", [3]));
    }
}
