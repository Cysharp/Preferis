using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;

namespace Preferis.Core.Tests.Profiles;

public class BrowserProfileMatcherTests
{
    private static readonly IReadOnlyList<BrowserProfileInfo> Chromium =
    [
        new("Default", "Mayuki", null),
        new("Profile 1", "Work", "me@work.example.com"),
        new("Profile 2", "Mayuki", "me@example.com"),
    ];

    private static readonly IReadOnlyList<BrowserProfileInfo> Firefox =
    [
        new("Profiles/abc.default-release", "default-release", null),
        new("Profiles/def.work", "Work", null),
    ];

    [Fact]
    public void Chromium_prefers_the_email()
    {
        var match = BrowserProfileMatcher.Match(Chromium, new BrowserProfileRef { Email = "ME@example.com", Name = "Work", Directory = "Default" }, BrowserFamily.Chromium);

        Assert.Equal("Profile 2", match?.Directory);
    }

    [Fact]
    public void Chromium_then_uses_the_name_and_finally_the_directory()
    {
        Assert.Equal("Profile 1", BrowserProfileMatcher.Match(Chromium, new BrowserProfileRef { Email = "gone@example.com", Name = "work", Directory = "Default" }, BrowserFamily.Chromium)?.Directory);
        Assert.Equal("Profile 2", BrowserProfileMatcher.Match(Chromium, new BrowserProfileRef { Name = "Renamed", Directory = "Profile 2" }, BrowserFamily.Chromium)?.Directory);
    }

    [Fact]
    public void Chromium_directories_are_case_sensitive()
    {
        Assert.Null(BrowserProfileMatcher.Match(Chromium, new BrowserProfileRef { Directory = "profile 1" }, BrowserFamily.Chromium));
    }

    [Fact]
    public void Firefox_matches_the_profile_name()
    {
        Assert.Equal("Profiles/def.work", BrowserProfileMatcher.Match(Firefox, new BrowserProfileRef { Name = "work" }, BrowserFamily.Firefox)?.Directory);
        Assert.Equal("Profiles/def.work", BrowserProfileMatcher.Match(Firefox, new BrowserProfileRef { Directory = "Work" }, BrowserFamily.Firefox)?.Directory);
        Assert.Null(BrowserProfileMatcher.Match(Firefox, new BrowserProfileRef { Email = "me@example.com" }, BrowserFamily.Firefox));
    }

    [Fact]
    public void Other_browsers_have_no_profiles_to_match()
    {
        Assert.Null(BrowserProfileMatcher.Match(Chromium, new BrowserProfileRef { Directory = "Default" }, BrowserFamily.Other));
    }
}
