using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class IdGeneratorTests
{
    [Theory]
    [InlineData("Chrome (Work)", "chrome-work")]
    [InlineData("Microsoft Edge", "microsoft-edge")]
    [InlineData("  Edge -- InPrivate  ", "edge-inprivate")]
    [InlineData("app1.example.com", "app1-example-com")]
    [InlineData("社内ツール", "profile")]
    [InlineData("", "profile")]
    public void Names_become_readable_slugs(string name, string expected)
    {
        Assert.Equal(expected, IdGenerator.Slugify(name, "profile"));
    }

    [Fact]
    public void Slugs_are_kept_short()
    {
        var slug = IdGenerator.Slugify(new string('a', 100), "profile");

        Assert.Equal(48, slug.Length);
    }

    [Fact]
    public void A_free_id_is_used_as_is()
    {
        Assert.Equal("chrome-work", IdGenerator.ForName("Chrome (Work)", "profile", ["edge"]));
    }

    [Fact]
    public void A_taken_id_gets_the_next_free_number()
    {
        Assert.Equal("chrome-work-2", IdGenerator.ForName("Chrome (Work)", "profile", ["chrome-work"]));
        Assert.Equal("chrome-work-3", IdGenerator.ForName("Chrome (Work)", "profile", ["chrome-work", "chrome-work-2"]));
    }

    [Fact]
    public void Profiles_are_numbered_from_one()
    {
        Assert.Equal("profile-1", IdGenerator.NextProfileId([]));
        Assert.Equal("profile-1", IdGenerator.NextProfileId(["edge", "work"]));
    }

    [Fact]
    public void A_new_profile_number_follows_the_highest_one_without_filling_gaps()
    {
        // A number given up may still be the key of a target on another machine.
        Assert.Equal("profile-4", IdGenerator.NextProfileId(["profile-1", "profile-3"]));
        Assert.Equal("profile-10", IdGenerator.NextProfileId(["profile-9", "profile-x", "profile-", "my-profile-20", "profile-2"]));
    }
}
