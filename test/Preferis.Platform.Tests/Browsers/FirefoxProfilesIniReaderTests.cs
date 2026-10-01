using Preferis.Platform.Browsers;

namespace Preferis.Platform.Tests.Browsers;

public class FirefoxProfilesIniReaderTests
{
    [Fact]
    public void Reads_named_profiles_and_resolves_relative_paths()
    {
        using var reader = new StreamReader(Fixtures.Open("firefox-profiles.ini"));

        var profiles = FirefoxProfilesIniReader.Read(reader, baseDirectory: "/Users/me/Library/Application Support/Firefox");

        Assert.Equal(["Work", "default-release", "External"], profiles.Select(p => p.Name));
        Assert.Equal(Path.Combine("/Users/me/Library/Application Support/Firefox", "Profiles/efgh5678.work"), profiles[0].Directory);
        Assert.Equal("/Volumes/Data/FirefoxProfiles/external", profiles[2].Directory);
        Assert.All(profiles, p => Assert.Null(p.Email));
    }

    [Fact]
    public void Sections_that_are_not_profiles_are_ignored()
    {
        using var reader = new StringReader("[General]\nVersion=2\n[Install1234]\nDefault=x\n[Profile0]\nName=Only\nPath=p\n");

        var profiles = FirefoxProfilesIniReader.Read(reader, baseDirectory: null);

        Assert.Equal("Only", Assert.Single(profiles).Name);
    }
}
