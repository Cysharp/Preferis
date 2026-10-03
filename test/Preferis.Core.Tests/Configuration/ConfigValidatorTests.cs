using System.Text.Json;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;

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
    public void Profile_names_are_unique_whatever_the_case_and_surrounding_spaces()
    {
        var config = WithProfiles(Browser("a"), Browser("b"), Browser("c"));
        config.Profiles[0].Name = "Work";
        config.Profiles[1].Name = "Personal";
        config.Profiles[2].Name = " work ";

        var issue = Single(config, IssueSeverity.Error);

        Assert.Equal(IssueScope.Profile, issue.Scope);
        Assert.Equal("c", issue.Id);
        Assert.Contains("“Work”", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Profiles_without_a_name_are_not_compared()
    {
        var config = WithProfiles(Browser("a"), Browser("b"));
        config.Profiles[0].Name = "";
        config.Profiles[1].Name = " ";

        Assert.Empty(ConfigValidator.Validate(config));
    }

    [Fact]
    public void A_profile_without_a_browser_or_a_command_is_set_on_each_computer()
    {
        // Each computer sets its target in local.json; one that has not is warned about by the settings there.
        Assert.Empty(ConfigValidator.Validate(WithProfiles(new Profile { Id = "x", Name = "X" })));
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
    public void Patterns_are_checked_with_the_case_sensitivity_the_router_compiles_them_with()
    {
        var config = WithProfiles(Browser("edge"));
        var url = new UrlCondition { Match = MatchKind.Regex, Pattern = "a" };
        config.Rules = [RuleWith("r", new AndCondition { Conditions = [Host("b", MatchKind.Regex), url] }, new OpenAction())];
        // Only case-sensitive patterns fail here; hosts always ignore case.
        var patterns = new PatternErrorCache((_, _, ignoreCase) => ignoreCase ? null : "case-sensitive");

        Assert.Empty(ConfigValidator.Validate(config, patterns));

        url.IgnoreCase = false;

        Assert.Contains("case-sensitive", Assert.Single(ConfigValidator.Validate(config, patterns)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Patterns_are_checked_again_only_when_they_change()
    {
        var checkedPatterns = new List<(MatchKind, string, bool)>();
        var patterns = new PatternErrorCache((kind, pattern, ignoreCase) =>
        {
            checkedPatterns.Add((kind, pattern, ignoreCase));
            return PatternMatcher.ErrorOf(kind, pattern, ignoreCase);
        });
        var leaf = new UrlCondition { Match = MatchKind.Regex, Pattern = "(" };
        var config = WithProfiles(Browser("edge"));
        config.Rules =
        [
            RuleWith("r", new OrCondition { Conditions = [Host("*.example.com", MatchKind.Wildcard), leaf] }, new OpenAction()),
            RuleWith("s", Host("*.example.com", MatchKind.Wildcard), new RewriteAction { Mode = RewriteMode.Regex, Pattern = "x", Replacement = "y" }, new OpenAction()),
        ];
        Assert.Single(ConfigValidator.Validate(config, patterns), i => i.Severity == IssueSeverity.Error);
        Assert.Equal([(MatchKind.Wildcard, "*.example.com", true), (MatchKind.Regex, "(", true), (MatchKind.Regex, "x", false)], checkedPatterns);

        void Expect(int errors, params (MatchKind, string, bool)[] checkedAgain)
        {
            checkedPatterns.Clear();
            Assert.Equal(errors, ConfigValidator.Validate(config, patterns).Count(i => i.Severity == IssueSeverity.Error));
            Assert.Equal(checkedAgain, checkedPatterns);
        }

        config.Rules[0].Name = "renamed";
        Expect(1);
        leaf.Pattern = "(a)";
        Expect(0, (MatchKind.Regex, "(a)", true));
        leaf.Match = MatchKind.Contains;
        Expect(0, (MatchKind.Contains, "(a)", true));
        leaf.Match = MatchKind.Regex;
        leaf.IgnoreCase = false;
        Expect(0, (MatchKind.Regex, "(a)", false));
        Expect(0);
    }

    [Fact]
    public void Patterns_no_longer_in_the_configuration_are_forgotten()
    {
        var checkedPatterns = new List<string>();
        var patterns = new PatternErrorCache((kind, pattern, ignoreCase) =>
        {
            checkedPatterns.Add(pattern);
            return null;
        });
        var leaf = Host("a", MatchKind.Regex);
        var config = WithProfiles(Browser("edge"));
        config.Rules = [RuleWith("r", leaf, new OpenAction())];

        // Typing "ab" and deleting the "b" again: only what the last validation looked at is kept.
        ConfigValidator.Validate(config, patterns);
        leaf.Pattern = "ab";
        ConfigValidator.Validate(config, patterns);
        leaf.Pattern = "a";
        ConfigValidator.Validate(config, patterns);

        Assert.Equal(["a", "ab", "a"], checkedPatterns);
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
