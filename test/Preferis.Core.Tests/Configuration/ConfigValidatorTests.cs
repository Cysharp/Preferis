using System.Text.Json;
using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class ConfigValidatorTests
{
    private static PreferisConfig Sample() => ConfigSerializer.Deserialize(Fixtures.ConfigSample);

    private static PreferisConfig WithProfiles(params Profile[] profiles) => new() { Profiles = [.. profiles] };

    private static Profile Browser(string id, string browser = "edge") => new() { Id = id, Name = id, Browser = new BrowserReference(browser) };

    private static Rule RuleWith(string id, Condition? condition, params RoutingAction[] actions) => new() { Id = id, Name = id, Condition = condition, Actions = [.. actions] };

    private static HostCondition Host(string pattern, MatchKind match = MatchKind.Exact) => new() { Match = match, Pattern = pattern };

    private static ConfigIssue Single(PreferisConfig config, IssueSeverity severity)
        => Assert.Single(ConfigValidator.Validate(config), i => i.Severity == severity);

    [Fact]
    public void The_documented_sample_is_valid()
    {
        Assert.Empty(ConfigValidator.Validate(Sample()));
    }

    [Fact]
    public void The_default_configuration_is_valid()
    {
        Assert.Empty(ConfigValidator.Validate(PreferisConfig.CreateDefault()));
    }

    [Fact]
    public void Duplicate_and_empty_profile_ids_are_errors()
    {
        var config = WithProfiles(Browser("edge"), Browser("edge"), Browser(""));

        var issues = ConfigValidator.Validate(config);

        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.Profile, Id: "edge" } && i.Message.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.Profile, Id: "" });
    }

    [Fact]
    public void A_profile_needs_a_browser_or_a_command()
    {
        var issue = Single(WithProfiles(new Profile { Id = "x", Name = "X" }), IssueSeverity.Error);

        Assert.Equal(IssueScope.Profile, issue.Scope);
        Assert.Equal("x", issue.Id);
    }

    [Fact]
    public void A_profile_with_both_a_browser_and_a_command_uses_the_command()
    {
        var profile = Browser("x");
        profile.Command = new CommandTarget { MacOS = new CommandSpec { Path = "/usr/bin/open" } };

        var issue = Single(WithProfiles(profile), IssueSeverity.Warning);

        Assert.Contains("command", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_needs_a_path_for_at_least_one_os()
    {
        var profile = new Profile { Id = "x", Name = "X", Command = new CommandTarget { MacOS = new CommandSpec { Path = " " } } };

        Assert.Equal("x", Single(WithProfiles(profile), IssueSeverity.Error).Id);
    }

    [Theory]
    [InlineData("edge", false)]
    [InlineData("mac:com.example.Browser", false)]
    [InlineData("win:Some Browser", false)]
    [InlineData("netscape", true)]
    public void Browser_ids_must_come_from_the_catalog(string id, bool invalid)
    {
        var issues = ConfigValidator.Validate(WithProfiles(Browser("x", id)));

        Assert.Equal(invalid, issues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains(id, StringComparison.Ordinal)));
    }

    [Fact]
    public void Per_os_browser_ids_are_checked_too()
    {
        var profile = new Profile { Id = "x", Name = "X", Browser = new BrowserReference { Windows = "edge", MacOS = "netscape" } };

        Assert.Contains("netscape", Single(WithProfiles(profile), IssueSeverity.Error).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fallbacks_must_point_at_another_existing_profile()
    {
        var missing = Browser("a");
        missing.Fallback = "nope";
        var self = Browser("b");
        self.Fallback = "b";

        var issues = ConfigValidator.Validate(WithProfiles(missing, self));

        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Id: "a" } && i.Message.Contains("nope", StringComparison.Ordinal));
        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Id: "b" });
    }

    [Fact]
    public void The_default_profile_must_exist()
    {
        var config = WithProfiles(Browser("edge"));
        config.DefaultProfile = "chrome";

        var issue = Single(config, IssueSeverity.Error);

        Assert.Equal(IssueScope.Config, issue.Scope);
        Assert.Contains("chrome", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_need_unique_ids_and_at_least_one_action()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", Host("a.example.com"), new OpenAction()), RuleWith("r", Host("b.example.com"), new OpenAction()), RuleWith("empty", Host("c.example.com"))];

        var issues = ConfigValidator.Validate(config);

        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.Rule, Id: "r" });
        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.Rule, Id: "empty" } && i.Message.Contains("action", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rule_without_a_condition_never_matches()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", null, new OpenAction())];

        Assert.Equal("r", Single(config, IssueSeverity.Warning).Id);
    }

    [Theory]
    [InlineData(MatchKind.Regex, "(unclosed", IssueSeverity.Error)]
    [InlineData(MatchKind.Regex, "", IssueSeverity.Warning)]
    [InlineData(MatchKind.StartsWith, "", IssueSeverity.Warning)]
    public void Leaf_patterns_are_checked(MatchKind match, string pattern, IssueSeverity severity)
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", new AndCondition { Conditions = [Host("ok.example.com"), new UrlCondition { Match = match, Pattern = pattern }] }, new OpenAction())];

        var issue = Assert.Single(ConfigValidator.Validate(config));

        Assert.Equal(severity, issue.Severity);
        Assert.Equal("r", issue.Id);
    }

    [Fact]
    public void Empty_groups_are_suspicious()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules =
        [
            RuleWith("and", new AndCondition(), new OpenAction()),
            RuleWith("or", new OrCondition(), new OpenAction()),
            RuleWith("not", new NotCondition(), new OpenAction()),
        ];

        var issues = ConfigValidator.Validate(config);

        Assert.Equal(["and", "or", "not"], issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Id));
    }

    [Fact]
    public void Unknown_conditions_and_actions_are_reported()
    {
        var config = Sample();
        using var raw = JsonDocument.Parse("""{ "type": "future" }""");
        config.Rules[0].Condition = new UnknownCondition("future", raw.RootElement.Clone());
        config.Rules[1].Actions.Insert(0, new UnknownAction("teleport", raw.RootElement.Clone()));

        var issues = ConfigValidator.Validate(config);

        Assert.Contains(issues, i => i.Id == config.Rules[0].Id && i.Message.Contains("future", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Id == config.Rules[1].Id && i.Message.Contains("teleport", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_actions_naming_missing_profiles_are_warned_about()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", Host("a.example.com"), new OpenAction { Profiles = ["edge", "gone"] })];

        var issue = Single(config, IssueSeverity.Warning);

        Assert.Contains("gone", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RewriteMode.Regex, "(", "x", null)]
    [InlineData(RewriteMode.Regex, null, "x", null)]
    [InlineData(RewriteMode.QueryParameter, null, null, "")]
    public void Rewrites_need_their_parameters(RewriteMode mode, string? pattern, string? replacement, string? parameter)
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", Host("a.example.com"), new RewriteAction { Mode = mode, Pattern = pattern, Replacement = replacement, Parameter = parameter }, new OpenAction())];

        Assert.Equal("r", Single(config, IssueSeverity.Error).Id);
    }

    [Fact]
    public void Actions_after_a_terminal_action_never_run()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", Host("a.example.com"), new OpenAction(), new NoneAction())];

        Assert.Equal("r", Single(config, IssueSeverity.Warning).Id);
    }

    [Fact]
    public void Reroute_without_a_preceding_rewrite_always_loops()
    {
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", Host("a.example.com"), new RerouteAction())];
        config.CatchAll.Actions = [new RerouteAction()];

        var issues = ConfigValidator.Validate(config);

        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.Rule, Id: "r" });
        Assert.Contains(issues, i => i is { Severity: IssueSeverity.Error, Scope: IssueScope.CatchAll });
    }
}
