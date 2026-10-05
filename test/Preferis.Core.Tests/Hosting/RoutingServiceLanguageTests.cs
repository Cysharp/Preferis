using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Core.Routing;
using Preferis.Core.Tests.Localization;

namespace Preferis.Core.Tests.Hosting;

public sealed class RoutingServiceLanguageTests : LanguageTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completing_or_failing_a_url_preserves_the_callers_language(bool fail)
    {
        UiLanguages.Current = UiLanguage.English;
        var host = new FakeHost { SystemLanguages = () => ["en-US"] };
        host.Config.Settings = new AppSettings { Language = "ja" };
        if (fail)
        {
            host.BeforeLogWrite = () => throw new InvalidOperationException("Routing interrupted.");
            Assert.Throws<InvalidOperationException>(() => ShowPicker(host));
        }
        else
        {
            ShowPicker(host);
        }

        Assert.Equal(UiLanguage.English, UiLanguages.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parallel_urls_keep_their_configured_language_while_another_configuration_loads(bool configuredProfiles)
    {
        var host = new FakeHost { SystemLanguages = () => ["en-US"] };
        if (configuredProfiles)
        {
            host.WithSampleConfig();
        }
        host.Config.Settings = new AppSettings { Language = "ja" };
        host.WithBrowser("chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");

        var firstRouted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var secondLoading = new ManualResetEventSlim();
        using var finishSecondLoad = new ManualResetEventSlim();
        host.BeforeLogWrite = () =>
        {
            firstRouted.TrySetResult();
            Assert.True(secondLoading.Wait(TimeSpan.FromSeconds(10)), "The second URL did not start loading.");
        };
        var loads = 0;
        host.BeforeConfigLoad = () =>
        {
            if (Interlocked.Increment(ref loads) != 2)
            {
                return;
            }
            secondLoading.Set();
            Assert.True(finishSecondLoad.Wait(TimeSpan.FromSeconds(10)), "The first URL did not finish routing.");
        };

        var service = host.CreateService();
        var firstTask = Task.Run(() => service.Handle(RoutingUrl.Parse("https://login.microsoftonline.com/")));
        await firstRouted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = Task.Run(() => service.Handle(RoutingUrl.Parse("https://second.example/")));
        try
        {
            await firstTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            finishSecondLoad.Set();
            await secondTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var request = Assert.Single(host.PickerRequests, request => request.Url == "https://login.microsoftonline.com/");
        Assert.Equal("ja", request.Options.Language);
        if (!configuredProfiles)
        {
            Assert.Equal("プロファイルがまだ設定されていません。", request.Message);
        }
    }

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
    public void A_successful_load_with_an_explicit_language_does_not_query_the_system()
    {
        var config = new PreferisConfig { Settings = new AppSettings { Language = "ja" } };
        var host = new FakeHost
        {
            ConfigStore = new QuietConfigStore(config),
            SystemLanguages = () => throw new InvalidOperationException("OS languages were requested."),
        };

        var request = ShowPicker(host);

        Assert.Equal("ja", request.Options.Language);
        Assert.Equal("プロファイルがまだ設定されていません。", request.Message);
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

    // A successful load emits no text. FakeHost's regular store observes the language for the load-error tests.
    private sealed class QuietConfigStore(PreferisConfig config) : IConfigStore
    {
        public ConfigLoadResult Load() => new(config, new LocalConfig(), "/config.json", true, null);

        public void Save(PreferisConfig value, string configPath) => throw new NotSupportedException();

        public IDisposable Lock() => throw new NotSupportedException();
    }
}
