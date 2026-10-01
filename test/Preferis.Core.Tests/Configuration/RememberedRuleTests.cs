using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Configuration;

public class RememberedRuleTests
{
    private static PreferisConfig Sample() => ConfigSerializer.Deserialize(Fixtures.ConfigSample);

    [Fact]
    public void Remembered_rule_matches_the_exact_host_and_opens_the_chosen_profile()
    {
        var rule = RememberedRule.Create(RoutingUrl.Parse("https://login.microsoftonline.com/common"), "chrome-work");

        var host = Assert.IsType<HostCondition>(rule.Condition);
        Assert.Equal(MatchKind.Exact, host.Match);
        Assert.Equal("login.microsoftonline.com", host.Pattern);
        Assert.Equal(["chrome-work"], Assert.IsType<OpenAction>(rule.Actions.Single()).Profiles);
        Assert.True(rule.Enabled);
        Assert.False(string.IsNullOrEmpty(rule.Id));
        Assert.Contains("login.microsoftonline.com", rule.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Remembered_rule_is_inserted_before_the_rule_that_showed_the_picker()
    {
        var config = Sample();
        var rule = RememberedRule.Create(RoutingUrl.Parse("https://login.microsoftonline.com/"), "chrome-work");

        RememberedRule.Insert(config, rule, beforeRuleId: "ms-login");

        var index = config.Rules.FindIndex(r => r.Id == rule.Id);
        Assert.Equal(config.Rules.FindIndex(r => r.Id == "ms-login") - 1, index);
        var decision = Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse("https://login.microsoftonline.com/x")).Decision);
        Assert.Equal(["chrome-work"], decision.ProfileIds);
    }

    [Fact]
    public void Remembered_rule_is_appended_when_the_picker_came_from_the_catch_all()
    {
        var config = Sample();
        var rule = RememberedRule.Create(RoutingUrl.Parse("https://unknown.example.net/"), "edge");

        RememberedRule.Insert(config, rule, beforeRuleId: null);

        Assert.Same(rule, config.Rules[^1]);
    }

    [Fact]
    public void Generated_ids_are_unique_within_the_config()
    {
        var config = Sample();
        var first = RememberedRule.Create(RoutingUrl.Parse("https://a.example.com/"), "edge");
        var second = RememberedRule.Create(RoutingUrl.Parse("https://a.example.com/"), "edge");
        RememberedRule.Insert(config, first, null);
        RememberedRule.Insert(config, second, null);

        Assert.Equal(config.Rules.Count, config.Rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }
}
