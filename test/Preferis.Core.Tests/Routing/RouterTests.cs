using System.Text.Json;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class RouterTests
{
    private static RoutingResult Route(PreferisConfig config, string url) => new Router(config).Route(RoutingUrl.Parse(url));

    private static PreferisConfig Sample() => ConfigSerializer.Deserialize(Fixtures.ConfigSample);

    private static PreferisConfig Config(string defaultProfile, params Rule[] rules)
    {
        var config = PreferisConfig.CreateDefault();
        config.DefaultProfile = defaultProfile;
        config.Profiles.AddRange(
        [
            new Profile { Id = "edge", Name = "Edge", Browser = new BrowserReference("edge") },
            new Profile { Id = "chrome", Name = "Chrome", Browser = new BrowserReference("chrome") },
        ]);
        config.Rules.AddRange(rules);
        return config;
    }

    private static Rule RuleFor(string id, Condition condition, params RoutingAction[] actions)
        => new() { Id = id, Name = id, Condition = condition, Actions = [.. actions] };

    private static HostCondition Host(string host) => new() { Match = MatchKind.Exact, Pattern = host };

    [Fact]
    public void First_matching_rule_wins()
    {
        var result = Route(Sample(), "https://admin.google.com/ac/users");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["chrome-work"], open.ProfileIds);
        Assert.Equal("google-admin", open.RuleId);
        Assert.Equal(OpenSource.Explicit, open.Source);
        Assert.Equal("https://admin.google.com/ac/users", open.Url.AbsoluteUri);
    }

    [Fact]
    public void Several_profiles_are_handed_over_for_the_picker()
    {
        var result = Route(Sample(), "https://login.microsoftonline.com/common/oauth2");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge", "chrome-work", "chrome-default"], open.ProfileIds);
        Assert.Equal("ms-login", open.RuleId);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var config = Sample();
        config.Rules.Single(r => r.Id == "google-admin").Enabled = false;

        var result = Route(config, "https://admin.google.com/");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge"], open.ProfileIds);
        Assert.Null(open.RuleId);
        Assert.Contains(result.Trace, t => t.RuleId == "google-admin" && t.Kind == TraceKind.RuleDisabled);
    }

    [Fact]
    public void Catch_all_opens_the_default_profile()
    {
        var result = Route(Sample(), "https://www.example.com/");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge"], open.ProfileIds);
        Assert.Equal(OpenSource.DefaultProfile, open.Source);
        Assert.Null(open.RuleId);
    }

    [Fact]
    public void Without_a_default_profile_the_catch_all_offers_every_profile()
    {
        var config = Sample();
        config.DefaultProfile = null;

        var open = Assert.IsType<RoutingDecision.Open>(Route(config, "https://www.example.com/").Decision);

        Assert.Equal(config.Profiles.Select(p => p.Id), open.ProfileIds);
        Assert.Equal(OpenSource.AllProfiles, open.Source);
    }

    [Fact]
    public void Without_any_profile_the_decision_has_no_candidates()
    {
        var open = Assert.IsType<RoutingDecision.Open>(Route(PreferisConfig.CreateDefault(), "https://www.example.com/").Decision);

        Assert.Empty(open.ProfileIds);
        Assert.Equal(OpenSource.AllProfiles, open.Source);
    }

    [Fact]
    public void Unknown_profile_ids_are_dropped_and_fall_back_to_the_default()
    {
        var config = Config("edge",
            RuleFor("a", Host("a.example.com"), new OpenAction { Profiles = ["nope", "chrome"] }),
            RuleFor("b", Host("b.example.com"), new OpenAction { Profiles = ["nope"] }));

        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(Route(config, "https://a.example.com/").Decision).ProfileIds);
        var b = Assert.IsType<RoutingDecision.Open>(Route(config, "https://b.example.com/").Decision);
        Assert.Equal(["edge"], b.ProfileIds);
        Assert.Equal(OpenSource.DefaultProfile, b.Source);
    }

    [Fact]
    public void Safe_links_are_unwrapped_and_rerouted()
    {
        var wrapped = "https://nam.safelinks.protection.outlook.com/?url=https%3A%2F%2Fadmin.google.com%2Fac%2Fusers%3Fhl%3Den&data=05%7Cx";

        var result = Route(Sample(), wrapped);

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal("https://admin.google.com/ac/users?hl=en", open.Url.AbsoluteUri);
        Assert.Equal(["chrome-work"], open.ProfileIds);
        Assert.Contains(result.Trace, t => t.Kind == TraceKind.Rewritten);
        Assert.Contains(result.Trace, t => t.Kind == TraceKind.Rerouted);
    }

    [Fact]
    public void A_rewrite_only_rule_falls_through_to_the_next_rule()
    {
        var config = Config("edge",
            RuleFor("upgrade", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "http://" },
                new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" }),
            RuleFor("secure", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "https://intranet.example.com/" }, new OpenAction { Profiles = ["chrome"] }));

        var result = Route(config, "http://intranet.example.com/wiki");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal("https://intranet.example.com/wiki", open.Url.AbsoluteUri);
        Assert.Equal("secure", open.RuleId);
        Assert.Contains(result.Trace, t => t.Kind == TraceKind.FellThrough && t.RuleId == "upgrade");
    }

    [Fact]
    public void Rerouting_the_same_url_is_detected_as_a_loop()
    {
        var config = Config("edge", RuleFor("loop", Host("loop.example.com"), new RerouteAction()));

        var failed = Assert.IsType<RoutingDecision.Failed>(Route(config, "https://loop.example.com/").Decision);

        Assert.Contains("loop", failed.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rerouting_stops_after_the_hop_limit()
    {
        // Every hop produces a new URL, so only the hop counter can stop it.
        var config = Config("edge", RuleFor("grow", Host("grow.example.com"),
            new RewriteAction { Mode = RewriteMode.Regex, Pattern = "$", Replacement = "x" }, new RerouteAction()));

        var result = Route(config, "https://grow.example.com/");

        var failed = Assert.IsType<RoutingDecision.Failed>(result.Decision);
        Assert.Contains("hop", failed.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Router.MaxHops, result.Trace.Count(t => t.Kind == TraceKind.Rerouted));
    }

    [Fact]
    public void System_and_none_actions_map_to_their_decisions()
    {
        var config = Config("edge",
            RuleFor("sys", Host("sys.example.com"), new SystemAction()),
            RuleFor("block", Host("block.example.com"), new NoneAction()));

        Assert.IsType<RoutingDecision.System>(Route(config, "https://sys.example.com/").Decision);
        Assert.IsType<RoutingDecision.None>(Route(config, "https://block.example.com/").Decision);
    }

    [Fact]
    public void A_rewrite_that_yields_an_invalid_url_fails()
    {
        var config = Config("edge", RuleFor("bad", Host("bad.example.com"),
            new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^.*$", Replacement = "not a url" }, new OpenAction()));

        Assert.IsType<RoutingDecision.Failed>(Route(config, "https://bad.example.com/").Decision);
    }

    [Fact]
    public void A_missing_query_parameter_fails_the_rewrite()
    {
        var config = Config("edge", RuleFor("unwrap", Host("wrap.example.com"),
            new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new RerouteAction()));

        var failed = Assert.IsType<RoutingDecision.Failed>(Route(config, "https://wrap.example.com/?other=1").Decision);
        Assert.Contains("url", failed.Reason);
    }

    [Fact]
    public void Unknown_actions_fail_instead_of_routing_elsewhere()
    {
        using var document = JsonDocument.Parse("""{ "type": "notify" }""");
        var config = Config("edge", RuleFor("future", Host("future.example.com"), new UnknownAction("notify", document.RootElement.Clone())));

        var failed = Assert.IsType<RoutingDecision.Failed>(Route(config, "https://future.example.com/").Decision);
        Assert.Contains("notify", failed.Reason);
    }

    [Fact]
    public void A_catch_all_without_a_terminal_action_opens_the_default_profile()
    {
        var config = Config("chrome");
        config.CatchAll.Actions = [new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" }];

        var open = Assert.IsType<RoutingDecision.Open>(Route(config, "http://www.example.com/").Decision);

        Assert.Equal("https://www.example.com/", open.Url.AbsoluteUri);
        Assert.Equal(["chrome"], open.ProfileIds);
    }

    [Fact]
    public void Trace_records_every_evaluated_rule_and_the_decision()
    {
        var result = Route(Sample(), "https://app2.example.com/");

        Assert.Equal(["unwrap-safelinks", "google-admin", "app-split"], result.Trace.Where(t => t.Kind is TraceKind.RuleMatched or TraceKind.RuleNotMatched).Select(t => t.RuleId));
        Assert.Equal(TraceKind.RuleMatched, result.Trace.Single(t => t.RuleId == "app-split" && t.Kind != TraceKind.Action).Kind);
        Assert.Equal(TraceKind.Decision, result.Trace[^1].Kind);
    }
}
