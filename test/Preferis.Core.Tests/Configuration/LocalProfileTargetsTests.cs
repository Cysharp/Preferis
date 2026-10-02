using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class LocalProfileTargetsTests
{
    private static Profile Work() => new()
    {
        Id = "work",
        Name = "Work",
        Browser = new BrowserReference("edge"),
        BrowserProfile = new BrowserProfileRef { Email = "me@corp.example" },
        Private = true,
        ExtraArgs = ["--new-window"],
        Fallback = "personal",
    };

    private static PreferisConfig Config(params Profile[] profiles) => new() { Profiles = [.. profiles], DefaultProfile = profiles.FirstOrDefault()?.Id };

    private static LocalConfig Local(string id, ProfileTarget target) => new() { ProfileTargets = new() { [id] = target } };

    [Fact]
    public void The_local_target_replaces_the_whole_shared_target()
    {
        var config = Config(Work());

        var effective = LocalProfileTargets.Apply(config, Local("work", new ProfileTarget { Browser = "safari" }));

        var work = effective.Profiles.Single();
        Assert.Equal("safari", work.Browser?.For(HostPlatform.MacOS));
        Assert.True(work.Browser?.IsPlain);
        Assert.Null(work.BrowserProfile);
        Assert.False(work.Private);
        Assert.Null(work.ExtraArgs);
        Assert.Null(work.Command);
        // Name and fallback are the shared ones.
        Assert.Equal(("work", "Work", "personal"), (work.Id, work.Name, work.Fallback));
    }

    [Fact]
    public void A_local_command_applies_on_this_machine_whatever_its_os()
    {
        var config = Config(Work());
        var command = new CommandSpec { Path = @"C:\Tools\tool.exe", Args = ["{url}"] };

        var work = LocalProfileTargets.Apply(config, Local("work", new ProfileTarget { Command = command })).Profiles.Single();

        Assert.Null(work.Browser);
        Assert.Equal(@"C:\Tools\tool.exe", work.Command?.For(HostPlatform.Windows)?.Path);
        Assert.Equal(@"C:\Tools\tool.exe", work.Command?.For(HostPlatform.MacOS)?.Path);
    }

    [Fact]
    public void A_local_target_with_nothing_to_open_leaves_the_profile_unresolvable()
    {
        var work = LocalProfileTargets.Apply(Config(Work()), Local("work", new ProfileTarget())).Profiles.Single();

        Assert.Null(work.Browser);
        Assert.Null(work.Command);
    }

    [Fact]
    public void The_shared_configuration_is_left_as_it_is()
    {
        var shared = Work();
        var other = new Profile { Id = "personal", Name = "Personal", Browser = new BrowserReference("chrome") };
        var config = Config(shared, other);

        var effective = LocalProfileTargets.Apply(config, Local("work", new ProfileTarget { Browser = "safari" }));

        Assert.Equal("edge", shared.Browser?.Default);
        Assert.Same(shared, config.Profiles[0]);
        Assert.Same(other, effective.Profiles[1]);
        Assert.Same(config.Rules, effective.Rules);
        Assert.Equal(config.DefaultProfile, effective.DefaultProfile);
    }

    [Fact]
    public void Without_local_targets_the_configuration_is_used_as_it_is()
    {
        var config = Config(Work());

        Assert.Same(config, LocalProfileTargets.Apply(config, new LocalConfig()));
        Assert.Same(config, LocalProfileTargets.Apply(config, Local("someone-else", new ProfileTarget { Browser = "safari" })));
    }

    [Fact]
    public void A_target_kept_under_a_previous_id_is_found()
    {
        var work = Work();
        work.PreviousIds = ["microsoft-edge", "edge"];
        var local = new LocalConfig
        {
            ProfileTargets = new()
            {
                ["edge"] = new ProfileTarget { Browser = "chrome" },
                ["microsoft-edge"] = new ProfileTarget { Browser = "safari" },
            },
        };

        // The most recent previous id first.
        Assert.Equal("safari", LocalProfileTargets.Find(local, work)?.Browser);

        local.ProfileTargets["work"] = new ProfileTarget { Browser = "brave" };
        Assert.Equal("brave", LocalProfileTargets.Find(local, work)?.Browser);
    }

    [Fact]
    public void Keys_of_previous_ids_move_to_the_current_id()
    {
        var work = Work();
        work.PreviousIds = ["microsoft-edge"];
        var local = Local("microsoft-edge", new ProfileTarget { Browser = "safari" });

        Assert.True(LocalProfileTargets.MigrateKeys(Config(work), local));

        Assert.Equal(["work"], local.ProfileTargets!.Keys);
        Assert.Equal("safari", local.ProfileTargets["work"].Browser);
        Assert.False(LocalProfileTargets.MigrateKeys(Config(work), local));
    }

    [Fact]
    public void A_stale_key_under_a_previous_id_is_dropped_when_the_current_id_has_one()
    {
        var work = Work();
        work.PreviousIds = ["microsoft-edge"];
        var local = Local("microsoft-edge", new ProfileTarget { Browser = "safari" });
        local.ProfileTargets!["work"] = new ProfileTarget { Browser = "brave" };
        local.ProfileTargets["unknown"] = new ProfileTarget { Browser = "chrome" };

        LocalProfileTargets.MigrateKeys(Config(work), local);

        // Keys of profiles this machine does not know (yet) stay: the synced config.json may catch up.
        Assert.Equal(["unknown", "work"], local.ProfileTargets.Keys.Order());
        Assert.Equal("brave", local.ProfileTargets["work"].Browser);
    }

    [Fact]
    public void The_shared_target_seen_from_this_machine_becomes_a_local_one()
    {
        var work = Work();
        work.Browser = new BrowserReference { Windows = "edge", MacOS = "safari" };

        var target = LocalProfileTargets.FromShared(work, HostPlatform.MacOS);

        Assert.Equal("safari", target.Browser);
        Assert.Equal("me@corp.example", target.BrowserProfile?.Email);
        Assert.NotSame(work.BrowserProfile, target.BrowserProfile);
        Assert.True(target.Private);
        Assert.Equal(["--new-window"], target.ExtraArgs);
        Assert.NotSame(work.ExtraArgs, target.ExtraArgs);
    }

    [Fact]
    public void A_shared_command_becomes_this_machines_command()
    {
        var tool = new Profile
        {
            Id = "tool",
            Name = "Tool",
            Command = new CommandTarget { Windows = new CommandSpec { Path = @"C:\tool.exe", Args = ["{url}"] }, MacOS = new CommandSpec { Path = "/usr/bin/tool" } },
        };

        var target = LocalProfileTargets.FromShared(tool, HostPlatform.Windows);

        Assert.Null(target.Browser);
        Assert.Equal(@"C:\tool.exe", target.Command?.Path);
        Assert.Equal(["{url}"], target.Command?.Args);
    }
}
