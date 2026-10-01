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
}
