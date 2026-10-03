using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class ProfileIdsTests
{
    private static PreferisConfig Sample() => ConfigSerializer.Deserialize(Fixtures.ConfigSample);

    [Theory]
    [InlineData("work", null)]
    [InlineData("Work_2.0-b", null)]
    [InlineData("chrome-work", null)]
    [InlineData("", "empty")]
    [InlineData("my work", "letters")]
    [InlineData("browser:edge", "letters")]
    [InlineData("仕事", "letters")]
    [InlineData("edge", "already")]
    public void New_ids_must_be_simple_and_unused(string id, string? error)
    {
        var message = ProfileIds.Validate(Sample(), "chrome-work", id);

        if (error is null)
        {
            Assert.Null(message);
        }
        else
        {
            Assert.Contains(error, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Ids_are_at_most_48_characters()
    {
        Assert.Null(ProfileIds.Validate(Sample(), "edge", new string('a', 48)));
        Assert.NotNull(ProfileIds.Validate(Sample(), "edge", new string('a', 49)));
    }

    [Fact]
    public void Renaming_rewrites_every_reference()
    {
        var config = Sample();
        config.Profiles.Single(p => p.Id == "chrome-work").Fallback = "edge";
        config.Profiles.Single(p => p.Id == "edge-private").Fallback = "chrome-work";
        config.CatchAll.Actions = [new OpenAction { Profiles = ["edge", "chrome-default"] }];
        var local = new LocalConfig { ProfileTargets = new() { ["edge"] = new ProfileTarget { Browser = "safari" } } };

        ProfileIds.Rename(config, local, "edge", "personal");

        var renamed = config.Profiles.Single(p => p.Name == "Microsoft Edge");
        Assert.Equal("personal", renamed.Id);
        Assert.Equal(["edge"], renamed.PreviousIds);
        Assert.Equal("personal", config.DefaultProfile);
        Assert.Equal("personal", config.Profiles.Single(p => p.Id == "chrome-work").Fallback);
        Assert.Equal("chrome-work", config.Profiles.Single(p => p.Id == "edge-private").Fallback);
        var login = config.Rules.Single(r => r.Id == "ms-login").Actions.OfType<OpenAction>().Single();
        Assert.Equal(["personal", "chrome-work", "chrome-default"], login.Profiles);
        Assert.Equal(["personal", "chrome-default"], config.CatchAll.Actions.OfType<OpenAction>().Single().Profiles);
        Assert.Equal(["personal"], local.ProfileTargets.Keys);
    }

    [Fact]
    public void Previous_ids_keep_the_most_recent_first_and_forget_the_id_returned_to()
    {
        var config = Sample();
        var local = new LocalConfig();

        ProfileIds.Rename(config, local, "edge", "personal");
        ProfileIds.Rename(config, local, "personal", "home");
        Assert.Equal(["personal", "edge"], config.Profiles.Single(p => p.Id == "home").PreviousIds);

        ProfileIds.Rename(config, local, "home", "edge");
        Assert.Equal(["home", "personal"], config.Profiles.Single(p => p.Id == "edge").PreviousIds);
    }

    [Fact]
    public void A_profile_is_found_by_every_id_it_has_had()
    {
        var config = Sample();
        var local = new LocalConfig();
        ProfileIds.Rename(config, local, "edge", "personal");
        ProfileIds.Rename(config, local, "personal", "home");

        Assert.Equal("home", ProfileIds.Current(config, "home"));
        Assert.Equal("home", ProfileIds.Current(config, "personal"));
        Assert.Equal("home", ProfileIds.Current(config, "edge"));
        Assert.Equal("chrome-work", ProfileIds.Current(config, "chrome-work"));
    }

    [Fact]
    public void A_deleted_profile_has_no_current_id()
    {
        var config = Sample();
        ProfileIds.Rename(config, new LocalConfig(), "edge", "personal");
        config.Profiles.RemoveAll(p => p.Id == "personal");

        Assert.Null(ProfileIds.Current(config, "personal"));
        Assert.Null(ProfileIds.Current(config, "edge"));
        Assert.Null(ProfileIds.Current(config, "never-existed"));
    }

    [Fact]
    public void An_id_another_profile_has_now_is_that_profiles()
    {
        // Only a hand-written file gets here: the settings refuse an id another profile had.
        var config = Sample();
        config.Profiles.Single(p => p.Id == "chrome-work").PreviousIds = ["edge"];

        Assert.Equal("edge", ProfileIds.Current(config, "edge"));
    }

    [Fact]
    public void An_id_several_profiles_have_had_is_none_of_theirs()
    {
        var config = Sample();
        config.Profiles.Single(p => p.Id == "chrome-work").PreviousIds = ["chrome"];
        config.Profiles.Single(p => p.Id == "chrome-default").PreviousIds = ["chrome"];

        Assert.Null(ProfileIds.Current(config, "chrome"));
    }
}
