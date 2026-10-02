using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class ProfileNamesTests
{
    [Theory]
    [InlineData("Work", "work", true)]
    [InlineData(" Work ", "WORK", true)]
    [InlineData("Work", "Work 2", false)]
    [InlineData("", " ", false)]
    public void Names_are_the_same_whatever_the_case_and_surrounding_spaces(string a, string b, bool same)
    {
        Assert.Equal(same, ProfileNames.Same(a, b));
    }

    [Fact]
    public void A_free_name_is_used_as_is()
    {
        Assert.Equal("Safari", ProfileNames.Unique("Safari", ["Microsoft Edge"]));
    }

    [Fact]
    public void A_taken_name_gets_the_next_free_number()
    {
        Assert.Equal("Safari 2", ProfileNames.Unique("Safari", ["safari"]));
        Assert.Equal("Safari 3", ProfileNames.Unique("Safari", ["Safari", "Safari 2"]));
    }

    [Fact]
    public void Logs_name_a_profile_with_its_id()
    {
        var config = new PreferisConfig { Profiles = [new Profile { Id = "profile-3", Name = "Chrome (Work)" }, new Profile { Id = "profile-4", Name = "" }] };

        Assert.Equal("'Chrome (Work)' (profile-3)", ProfileNames.Describe(config, "profile-3"));
        Assert.Equal("'profile-4'", ProfileNames.Describe(config, "profile-4"));
        Assert.Equal("'browser:edge'", ProfileNames.Describe(config, "browser:edge"));
    }
}
