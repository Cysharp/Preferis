using Preferis.Core.Configuration;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Core.Routing;
using Preferis.Core.Tests.Localization;

namespace Preferis.Core.Tests.Hosting;

public sealed class RoutingServiceLanguageTests : LanguageTest
{
    // No profiles: the Router offers the browsers it detects, explaining why.
    private static PickerRequest ShowPicker(FakeHost host)
    {
        host.WithBrowser("chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
        host.CreateService().Handle(RoutingUrl.Parse("https://example.com/"));
        return Assert.Single(host.PickerRequests);
    }

    [Fact]
    public void The_picker_and_its_message_are_in_the_configured_language()
    {
        var host = new FakeHost { SystemLanguages = () => ["en-US"] };
        host.Config.Settings = new AppSettings { Language = "ja" };

        var request = ShowPicker(host);

        Assert.Equal("ja", request.Options.Language);
        Assert.Equal("プロファイルがまだ設定されていません。", request.Message);
    }

    [Fact]
    public void Auto_follows_the_languages_of_the_system()
    {
        var host = new FakeHost { SystemLanguages = () => ["ja-JP", "en-US"] };

        var request = ShowPicker(host);

        Assert.Equal("ja", request.Options.Language);
    }

    [Fact]
    public void The_configuration_is_loaded_in_the_systems_language_as_it_may_not_be_readable()
    {
        // The language of an earlier URL must not decide that of a load error.
        UiLanguages.Current = UiLanguage.English;
        var host = new FakeHost { SystemLanguages = () => ["ja-JP"] };
        host.Config.Settings = new AppSettings { Language = "en" };

        var request = ShowPicker(host);

        Assert.Equal(UiLanguage.Japanese, host.LanguageWhileLoading);
        Assert.Equal("en", request.Options.Language);
    }

    [Fact]
    public void A_host_that_does_not_know_the_systems_languages_leaves_the_language_alone()
    {
        var host = new FakeHost();
        host.Config.Settings = new AppSettings { Language = "ja" };

        var request = ShowPicker(host);

        Assert.Equal("en", request.Options.Language);
        Assert.Equal("No profiles are configured yet.", request.Message);
    }

    [Fact]
    public void Configured_profiles_are_offered_in_the_configured_language_too()
    {
        var host = new FakeHost { SystemLanguages = () => [] }.WithSampleConfig();
        host.Config.Settings!.Language = "ja";

        // A rule that offers three profiles.
        host.CreateService().Handle(RoutingUrl.Parse("https://login.microsoftonline.com/"));

        var request = Assert.Single(host.PickerRequests);
        Assert.Equal("ja", request.Options.Language);
    }
}
