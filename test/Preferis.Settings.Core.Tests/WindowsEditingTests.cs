using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

/// <summary>The same view models on a Windows machine: "this machine" means Windows there, and macOS is the other OS.</summary>
public sealed class WindowsEditingTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public WindowsEditingTests()
    {
        _harness.Platform.OnWindows();
    }

    public void Dispose() => _harness.Dispose();

    private ProfileViewModel Profile(string id, out SettingsViewModel settings)
    {
        settings = _harness.WithSample().Create();
        return settings.Profiles.Profiles.Single(p => p.Id == id);
    }

    [Fact]
    public void Browser_choices_are_the_browsers_of_windows()
    {
        var profile = Profile("edge", out _);

        var ids = profile.BrowserChoices.Select(c => c.Id).ToList();
        Assert.Equal(["edge", "firefox"], ids.Take(2));
        Assert.Contains("chrome-canary", ids);
        Assert.DoesNotContain("safari", ids);
        Assert.Same(profile.BrowserChoices, profile.WindowsBrowserChoices);
    }

    [Fact]
    public void The_other_os_is_chosen_from_the_browsers_of_macos()
    {
        var profile = Profile("system-browser", out _);

        Assert.True(profile.PerOs);
        Assert.Equal("edge", profile.WindowsBrowser?.Id);
        Assert.Equal("safari", profile.MacOSBrowser?.Id);
        Assert.Contains(profile.MacOSBrowserChoices, c => c.Id == "safari");
        Assert.DoesNotContain(profile.MacOSBrowserChoices, c => c.Id == "chrome-canary" && c.Title.Contains("installed", StringComparison.Ordinal));
    }

    [Fact]
    public void Turning_per_os_on_and_off_keeps_this_machines_browser()
    {
        var profile = Profile("edge", out _);

        profile.PerOs = true;
        Assert.Equal("edge", profile.WindowsBrowser?.Id);
        Assert.Equal("edge", profile.MacOSBrowser?.Id);

        profile.MacOSBrowser = profile.MacOSBrowserChoices.Single(c => c.Id == "safari");
        Assert.Equal("edge", profile.Model.Browser?.Windows);
        Assert.Equal("safari", profile.Model.Browser?.MacOS);

        profile.PerOs = false;
        Assert.Equal("edge", profile.Browser?.Id);
        Assert.Equal("edge", profile.Model.Browser?.Default);
    }

    [Fact]
    public void The_browser_profiles_follow_the_browser_used_on_windows()
    {
        var profile = Profile("system-browser", out _);
        Assert.Equal(["(None)", "Personal · me@example.com"], profile.BrowserProfileChoices.Select(c => c.Title));

        profile.WindowsBrowser = profile.WindowsBrowserChoices.Single(c => c.Id == "firefox");

        Assert.Equal(["(None)", "default-release"], profile.BrowserProfileChoices.Select(c => c.Title));
    }

    [Fact]
    public void A_new_command_profile_starts_with_a_windows_command()
    {
        var settings = _harness.Create();

        settings.Profiles.AddCommandProfileCommand.Execute(null);

        var command = settings.Config.Profiles.Single().Command;
        Assert.NotNull(command?.Windows);
        Assert.Null(command?.MacOS);
        Assert.Equal(["{url}"], command?.Windows?.Args);
    }

    [Fact]
    public async Task Browsing_for_a_program_sets_the_windows_command()
    {
        var profile = Profile("custom-tool", out _);
        _harness.Dialogs.Files.Enqueue(@"C:\Tools\Foo\foo.exe");

        await profile.BrowseWindowsCommandCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Tools\Foo\foo.exe", profile.Model.Command?.Windows?.Path);
        Assert.Equal(["--open", "{url}"], profile.Model.Command?.Windows?.Args);
        Assert.Equal("/Applications/Foo.app/Contents/MacOS/Foo", profile.Model.Command?.MacOS?.Path);
    }

    [Fact]
    public void Unreadable_browser_profiles_are_explained_for_windows()
    {
        _harness.Platform.Browsers.Add(new Services.DetectedBrowserInfo(
            new DetectedBrowser("chrome", "Google Chrome", BrowserFamily.Chromium, @"C:\Chrome\chrome.exe", null, @"C:\Users\me\AppData\Local\Google\Chrome\User Data", BrowserCatalog.Find("chrome")),
            null,
            null));

        var chrome = _harness.Create().Profiles.DetectedBrowsers.Single(b => b.Id == "chrome");

        Assert.NotNull(chrome.Note);
        Assert.DoesNotContain("macOS", chrome.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_explanation_of_unreadable_browser_profiles_has_no_system_settings_on_windows()
    {
        _harness.Platform.Browsers.Add(new Services.DetectedBrowserInfo(
            new DetectedBrowser("chrome", "Google Chrome", BrowserFamily.Chromium, @"C:\Chrome\chrome.exe", null, @"C:\Users\me\AppData\Local\Google\Chrome\User Data", BrowserCatalog.Find("chrome")),
            null,
            null));
        var page = _harness.Create().Profiles;
        var chrome = page.DetectedBrowsers.Single(b => b.Id == "chrome");
        _harness.Dialogs.Answers.Enqueue("OK");

        await page.ExplainNoteCommand.ExecuteAsync(chrome);

        var (_, message, buttons) = Assert.Single(_harness.Dialogs.Asked);
        Assert.Equal(chrome.Note, message);
        Assert.Equal(["OK"], buttons);
    }

    [Fact]
    public void Registration_outside_the_folder_of_preferis_exe_is_explained()
    {
        _harness.Platform.CanRegister = false;

        var page = _harness.Create().General;

        Assert.Contains("Preferis.exe", page.RegistrationText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_default_is_shown_by_its_name()
    {
        _harness.Platform.Status = new RegistrationStatus(true, false, "Firefox 128.0");

        var page = _harness.Create().General;

        Assert.Equal("The default browser is Firefox 128.0.", page.RegistrationText);
    }
}
