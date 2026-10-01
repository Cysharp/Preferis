using Preferis.Core.Configuration;
using Preferis.Core.Picker;

namespace Preferis.Core.Tests.Picker;

public class PickerDisplayTests
{
    [Theory]
    [InlineData("https://login.microsoftonline.com/common/oauth2?x=1#top", "https://", "login.microsoftonline.com", "/common/oauth2?x=1#top")]
    [InlineData("http://user@example.com:8080/a", "http://user@", "example.com", ":8080/a")]
    [InlineData("https://xn--r8jz45g.jp/", "https://", "例え.jp", "/")]
    [InlineData("file:///Users/me/page.html", "file://", "", "/Users/me/page.html")]
    [InlineData("not a url", "not a url", "", "")]
    public void The_url_is_split_around_the_host_for_emphasis(string url, string before, string host, string after)
    {
        var parts = PickerDisplay.SplitUrl(url);

        Assert.Equal(before, parts.BeforeHost);
        Assert.Equal(host, parts.Host);
        Assert.Equal(after, parts.AfterHost);
        Assert.Equal(url.StartsWith("https://xn--", StringComparison.Ordinal) ? "https://例え.jp/" : url, parts.BeforeHost + parts.Host + parts.AfterHost);
    }

    [Theory]
    [InlineData("Work", "me@work.example.com", "Profile 1", false, "Work · me@work.example.com")]
    [InlineData(null, "me@work.example.com", "Profile 1", false, "me@work.example.com")]
    [InlineData(null, null, "Profile 1", false, "Profile 1")]
    [InlineData("Work", "Work", null, false, "Work")]
    [InlineData(null, null, null, true, "Private")]
    [InlineData("Work", null, null, true, "Work · Private")]
    public void The_detail_line_describes_the_browser_profile(string? name, string? email, string? directory, bool isPrivate, string expected)
    {
        var candidate = new PickerCandidate
        {
            ProfileId = "x",
            Name = "X",
            BrowserProfile = name is null && email is null && directory is null ? null : new BrowserProfileRef { Name = name, Email = email, Directory = directory },
            Private = isPrivate,
        };

        Assert.Equal(expected, PickerDisplay.DetailLine(candidate));
    }

    [Fact]
    public void A_candidate_without_profile_or_mode_has_no_detail_line()
    {
        Assert.Equal("", PickerDisplay.DetailLine(new PickerCandidate { ProfileId = "x", Name = "X" }));
    }
}
