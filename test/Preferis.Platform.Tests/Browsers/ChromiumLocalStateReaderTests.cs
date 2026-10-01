using Preferis.Platform.Browsers;

namespace Preferis.Platform.Tests.Browsers;

public class ChromiumLocalStateReaderTests
{
    [Fact]
    public void Reads_profiles_in_the_browser_order()
    {
        using var stream = Fixtures.Open("chromium-local-state.json");

        var profiles = ChromiumLocalStateReader.Read(stream, userDataDir: "/Users/me/Library/Application Support/Google/Chrome");

        Assert.Equal(["Profile 1", "Default", "Profile 2"], profiles.Select(p => p.Directory));
    }

    [Fact]
    public void Maps_name_email_and_avatar()
    {
        using var stream = Fixtures.Open("chromium-local-state.json");

        var profiles = ChromiumLocalStateReader.Read(stream, userDataDir: "/Users/me/Library/Application Support/Google/Chrome");

        var work = profiles.Single(p => p.Directory == "Profile 1");
        Assert.Equal("Work", work.Name);
        Assert.Equal("me@work.example.com", work.Email);
        Assert.Null(work.AvatarPath);

        var personal = profiles.Single(p => p.Directory == "Default");
        Assert.Equal("Mayuki", personal.Name);
        Assert.Null(personal.Email);
        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/Google/Chrome", "Default", "Google Profile Picture.png"), personal.AvatarPath);
    }

    [Fact]
    public void Falls_back_to_the_gaia_name_when_the_name_is_empty()
    {
        using var stream = Fixtures.Open("chromium-local-state.json");

        var profiles = ChromiumLocalStateReader.Read(stream, userDataDir: null);

        Assert.Equal("Gaia Only", profiles.Single(p => p.Directory == "Profile 2").Name);
    }

    [Fact]
    public void Profiles_missing_from_the_order_list_are_appended()
    {
        const string json = """{ "profile": { "info_cache": { "Default": { "name": "A" }, "Profile 3": { "name": "B" } }, "profiles_order": [ "Profile 3" ] } }""";

        var profiles = ChromiumLocalStateReader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), null);

        Assert.Equal(["Profile 3", "Default"], profiles.Select(p => p.Directory));
    }

    [Fact]
    public void A_file_without_profiles_yields_an_empty_list()
    {
        var profiles = ChromiumLocalStateReader.Read(new MemoryStream("{ \"browser\": {} }"u8.ToArray()), null);

        Assert.Empty(profiles);
    }
}
