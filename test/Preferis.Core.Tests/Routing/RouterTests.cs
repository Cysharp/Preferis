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

    private static readonly SourceApp Slack = new(null, @"C:\Users\a\AppData\Local\slack\app-4.41.105\slack.exe");

    private static AppPathCondition FromSlack() => new() { Match = MatchKind.EndsWith, Pattern = @"\slack.exe" };

    private static RewriteAction AToB() => new() { Mode = RewriteMode.Regex, Pattern = @"//a\.example\.com/", Replacement = "//b.example.com/" };

    [Fact]
    public void A_rule_can_decide_by_the_app_that_opened_the_url()
    {
        var config = Config("edge", RuleFor("slack", FromSlack(), new OpenAction { Profiles = ["chrome"] }));

        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse("https://a.example.com/"), Slack).Decision).ProfileIds);
        Assert.Equal(["edge"], Assert.IsType<RoutingDecision.Open>(Route(config, "https://a.example.com/").Decision).ProfileIds);
    }

    [Fact]
    public void The_app_still_counts_after_a_rewrite_and_a_reroute()
    {
        var config = Config("edge",
            RuleFor("slack-b", new AndCondition { Conditions = [Host("b.example.com"), FromSlack()] }, new OpenAction { Profiles = ["chrome"] }),
            RuleFor("a-to-b", Host("a.example.com"), AToB(), new RerouteAction()));

        var open = Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse("https://a.example.com/"), Slack).Decision);

        Assert.Equal(["chrome"], open.ProfileIds);
        Assert.Equal("https://b.example.com/", open.Url.AbsoluteUri);
    }

    [Fact]
    public void The_rules_evaluated_again_before_the_picker_see_the_app_too()
    {
        // The second rule rewrites and would show the picker; the first one decides the rewritten URL when Slack opened it.
        var config = Config("edge",
            RuleFor("slack-b", new AndCondition { Conditions = [Host("b.example.com"), FromSlack()] }, new OpenAction { Profiles = ["chrome"] }),
            RuleFor("a-to-b", Host("a.example.com"), AToB(), new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = new Router(config).Route(RoutingUrl.Parse("https://a.example.com/"), Slack);

        Assert.Equal("slack-b", result.RuleId);
        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(result.Decision).ProfileIds);
    }

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
    public void Several_profiles_are_handed_over_for_the_picker_in_the_order_of_the_profile_list()
    {
        // The rule lists ["edge", "chrome-work", "chrome-default"]; the profiles are edge, chrome-default, chrome-work.
        var result = Route(Sample(), "https://login.microsoftonline.com/common/oauth2");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge", "chrome-default", "chrome-work"], open.ProfileIds);
        Assert.Equal("ms-login", open.RuleId);
    }

    [Fact]
    public void A_profile_listed_twice_in_an_open_action_is_offered_once()
    {
        var config = Config("edge", RuleFor("r", Host("a.example.com"), new OpenAction { Profiles = ["chrome", "edge", "chrome"] }));

        var open = Assert.IsType<RoutingDecision.Open>(Route(config, "https://a.example.com/").Decision);

        Assert.Equal(["edge", "chrome"], open.ProfileIds);
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
    public void The_result_names_the_rule_whose_actions_decided()
    {
        Assert.Equal("google-admin", Route(Sample(), "https://admin.google.com/").RuleId);
        Assert.Equal("google-admin", Route(Sample(), "https://nl.safelinks.protection.outlook.com/?url=https%3A%2F%2Fadmin.google.com%2F").RuleId);
        Assert.Null(Route(Sample(), "https://www.example.com/").RuleId);
    }

    [Fact]
    public void A_catch_all_without_actions_decides_even_after_a_rule_fell_through()
    {
        var config = Config("edge",
            RuleFor("upgrade", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "http://" },
                new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" }));
        config.CatchAll.Actions.Clear();

        var result = Route(config, "http://www.example.com/");

        Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Null(result.RuleId);
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
    public void The_reason_of_an_invalid_rewrite_masks_the_query_of_what_it_produced()
    {
        var config = Config("edge", RuleFor("bad", Host("bad.example.com"),
            new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^https://", Replacement = "" }, new OpenAction()));

        var failed = Assert.IsType<RoutingDecision.Failed>(Route(config, "https://bad.example.com/?token=secret").Decision);
        Assert.DoesNotContain("secret", failed.Reason);
        Assert.Contains("bad.example.com/?***", failed.Reason);
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
    public void Properties_of_a_newer_preferis_on_known_conditions_and_actions_do_not_change_routing()
    {
        const string json = """
            { "version": 1, "defaultProfile": "edge",
              "profiles": [ { "id": "edge", "name": "Edge", "browser": "edge" }, { "id": "chrome", "name": "Chrome", "browser": "chrome" } ],
              "rules": [
                { "id": "upgrade", "name": "upgrade", "condition": { "type": "url", "match": "startsWith", "pattern": "http://", "futureSetting": true },
                  "actions": [ { "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://", "futureSetting": true } ] },
                { "id": "intranet", "name": "intranet",
                  "condition": { "type": "and", "futureSetting": true, "conditions": [ { "type": "not", "futureSetting": true, "condition": { "type": "host", "match": "equals", "pattern": "www.example.com", "futureSetting": true } } ] },
                  "actions": [ { "type": "open", "profiles": ["chrome"], "futureSetting": true } ] }
              ],
              "catchAll": { "actions": [ { "type": "none", "futureSetting": true } ] } }
            """;

        var open = Assert.IsType<RoutingDecision.Open>(Route(ConfigSerializer.Deserialize(json), "http://intranet.example.com/wiki").Decision);

        Assert.Equal("https://intranet.example.com/wiki", open.Url.AbsoluteUri);
        Assert.Equal(["chrome"], open.ProfileIds);
        Assert.IsType<RoutingDecision.None>(Route(ConfigSerializer.Deserialize(json), "https://www.example.com/").Decision);
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

    private static Rule Unwrap(string id, params RoutingAction[] then)
        => RuleFor(id, Host("wrapper.example"), [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, .. then]);

    [Fact]
    public void Before_the_picker_for_a_url_its_own_rule_rewrote_the_rules_above_decide_with_that_url()
    {
        var config = Config("edge",
            RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }),
            Unwrap("unwrap", new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2Fpage");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["chrome"], open.ProfileIds);
        Assert.Equal("https://target.example/page", open.Url.AbsoluteUri);
        Assert.Equal("pinned", result.RuleId);
        Assert.Contains(result.Trace, t => t.Kind == TraceKind.Reevaluated && t.RuleId == "unwrap");
    }

    [Fact]
    public void When_no_rule_above_decides_the_rule_that_rewrote_shows_the_picker()
    {
        var config = Config("edge",
            RuleFor("other", Host("other.example"), new OpenAction { Profiles = ["chrome"] }),
            Unwrap("unwrap", new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge", "chrome"], open.ProfileIds);
        Assert.Equal("https://target.example/", open.Url.AbsoluteUri);
        Assert.Equal("unwrap", result.RuleId);
        Assert.Equal(["other", "other"], result.Trace.Where(t => t.Kind == TraceKind.RuleNotMatched).Select(t => t.RuleId));
        Assert.Equal(2, result.Trace.Count(t => t.Kind == TraceKind.Reevaluated && t.RuleId == "unwrap"));
    }

    [Fact]
    public void The_rules_above_are_evaluated_as_usual_and_a_rewrite_there_does_not_reach_the_picker()
    {
        // "upgrade" only rewrites and falls through to "pinned"; without "pinned", the picker opens the URL "unwrap" produced.
        var upgrade = RuleFor("upgrade", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "http://target.example/" },
            new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" });
        var pinned = RuleFor("pinned", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "https://target.example/" }, new OpenAction { Profiles = ["chrome"] });
        var unwrap = Unwrap("unwrap", new OpenAction { Profiles = ["edge", "chrome"] });
        var url = "https://wrapper.example/?url=http%3A%2F%2Ftarget.example%2F";

        var decided = Assert.IsType<RoutingDecision.Open>(Route(Config("edge", upgrade, pinned, unwrap), url).Decision);
        var picker = Assert.IsType<RoutingDecision.Open>(Route(Config("edge", upgrade, unwrap), url).Decision);

        Assert.Equal(["chrome"], decided.ProfileIds);
        Assert.Equal("https://target.example/", decided.Url.AbsoluteUri);
        Assert.Equal(["edge", "chrome"], picker.ProfileIds);
        Assert.Equal("http://target.example/", picker.Url.AbsoluteUri);
    }

    [Fact]
    public void Several_rewrites_in_one_rule_let_the_rules_above_decide_with_the_last_url()
    {
        var config = Config("edge",
            RuleFor("pinned", new UrlCondition { Match = MatchKind.StartsWith, Pattern = "https://target.example/" }, new OpenAction { Profiles = ["chrome"] }),
            Unwrap("unwrap", new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" }, new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = Route(config, "https://wrapper.example/?url=http%3A%2F%2Ftarget.example%2Fpage");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["chrome"], open.ProfileIds);
        Assert.Equal("https://target.example/page", open.Url.AbsoluteUri);
    }

    [Fact]
    public void Without_a_rewrite_of_its_own_the_rule_shows_the_picker_without_evaluating_the_rules_above_again()
    {
        // "pinned" would decide for the URL "unwrap" produced, but "unwrap" comes after it and only rewrites: as before,
        // the rule that opens decides for the URL it was given.
        var config = Config("edge",
            RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }),
            Unwrap("unwrap"),
            RuleFor("choose", Host("target.example"), new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge", "chrome"], open.ProfileIds);
        Assert.Equal("choose", result.RuleId);
        Assert.DoesNotContain(result.Trace, t => t.Kind == TraceKind.Reevaluated);
    }

    [Fact]
    public void A_rewrite_that_leaves_the_url_as_it_was_does_not_evaluate_the_rules_above_again()
    {
        var config = Config("edge",
            RuleFor("other", Host("other.example"), new OpenAction { Profiles = ["chrome"] }),
            RuleFor("choose", Host("target.example"), new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" }, new OpenAction { Profiles = ["edge", "chrome"] }));

        var result = Route(config, "https://target.example/");

        Assert.Equal(["edge", "chrome"], Assert.IsType<RoutingDecision.Open>(result.Decision).ProfileIds);
        Assert.DoesNotContain(result.Trace, t => t.Kind == TraceKind.Reevaluated);
    }

    [Fact]
    public void An_open_with_one_profile_after_a_rewrite_does_not_evaluate_the_rules_above()
    {
        var config = Config("edge",
            RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }),
            Unwrap("unwrap", new OpenAction { Profiles = ["edge"] }));

        var result = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["edge"], open.ProfileIds);
        Assert.Equal("unwrap", result.RuleId);
        Assert.DoesNotContain(result.Trace, t => t.Kind == TraceKind.Reevaluated);
    }

    [Fact]
    public void A_rewrite_and_reroute_is_not_evaluated_again_before_the_picker()
    {
        // The rule that shows the picker did not rewrite: the Safe Links rule above did, and rerouted.
        var result = Route(Sample(), "https://nam.safelinks.protection.outlook.com/?url=https%3A%2F%2Flogin.microsoftonline.com%2Fcommon");

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal("ms-login", result.RuleId);
        Assert.Equal(3, open.ProfileIds.Count);
        Assert.DoesNotContain(result.Trace, t => t.Kind == TraceKind.Reevaluated);
    }

    [Fact]
    public void Before_the_catch_alls_picker_for_a_url_it_rewrote_every_rule_decides_with_that_url()
    {
        var config = Config("edge", RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }));
        config.CatchAll.Actions = [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new OpenAction { Profiles = ["edge", "chrome"] }];

        var decided = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F");
        var picker = Route(config, "https://wrapper.example/?url=https%3A%2F%2Felsewhere.example%2F");

        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(decided.Decision).ProfileIds);
        Assert.Equal("pinned", decided.RuleId);
        var open = Assert.IsType<RoutingDecision.Open>(picker.Decision);
        Assert.Equal(["edge", "chrome"], open.ProfileIds);
        Assert.Equal("https://elsewhere.example/", open.Url.AbsoluteUri);
        Assert.Null(picker.RuleId);
        Assert.Contains(picker.Trace, t => t.Kind == TraceKind.Reevaluated && t.RuleId is null);
    }

    [Fact]
    public void The_catch_alls_implicit_picker_after_a_rewrite_evaluates_the_rules_too()
    {
        var config = Config("edge", RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }));
        config.DefaultProfile = null;
        config.CatchAll.Actions = [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }];

        var result = Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F");

        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(result.Decision).ProfileIds);
        Assert.Equal("pinned", result.RuleId);
    }

    [Fact]
    public void A_reroute_from_the_rules_above_restarts_the_routing()
    {
        var config = Config("edge",
            RuleFor("inner", Host("inner.example"), new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new RerouteAction()),
            Unwrap("unwrap", new OpenAction { Profiles = ["edge", "chrome"] }),
            RuleFor("final", Host("final.example"), new OpenAction { Profiles = ["chrome"] }));
        var inner = "https://inner.example/?url=" + Uri.EscapeDataString("https://final.example/");

        var result = Route(config, "https://wrapper.example/?url=" + Uri.EscapeDataString(inner));

        var open = Assert.IsType<RoutingDecision.Open>(result.Decision);
        Assert.Equal(["chrome"], open.ProfileIds);
        Assert.Equal("https://final.example/", open.Url.AbsoluteUri);
        Assert.Equal("final", result.RuleId);
        Assert.Contains(result.Trace, t => t.Kind == TraceKind.Rerouted && t.RuleId == "inner");
    }

    [Fact]
    public void A_reroute_from_the_rules_above_is_guarded_against_loops()
    {
        // The rewritten URL reroutes as it is, and every time it comes back to the same rule.
        var config = Config("edge",
            RuleFor("loop", Host("target.example"), new RerouteAction()),
            Unwrap("unwrap", new OpenAction { Profiles = ["edge", "chrome"] }));

        var failed = Assert.IsType<RoutingDecision.Failed>(Route(config, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F").Decision);

        Assert.Contains("loop", failed.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_rule_above_that_rewrites_before_its_own_picker_evaluates_only_the_rules_above_it()
    {
        // "outer" rewrites to a URL for "middle", which rewrites to a URL for "pinned": each looks only further up.
        var config = Config("edge",
            RuleFor("pinned", Host("target.example"), new OpenAction { Profiles = ["chrome"] }),
            RuleFor("middle", Host("middle.example"), new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new OpenAction { Profiles = ["edge", "chrome"] }),
            Unwrap("outer", new OpenAction { Profiles = ["edge", "chrome"] }));
        var middle = "https://middle.example/?url=" + Uri.EscapeDataString("https://target.example/");

        var result = Route(config, "https://wrapper.example/?url=" + Uri.EscapeDataString(middle));

        Assert.Equal(["chrome"], Assert.IsType<RoutingDecision.Open>(result.Decision).ProfileIds);
        Assert.Equal("pinned", result.RuleId);
        // Once through all three, then the two above "outer", then the one above "middle".
        Assert.Equal(["pinned", "middle", "outer", "pinned", "middle", "pinned"], result.Trace.Where(t => t.Kind is TraceKind.RuleMatched or TraceKind.RuleNotMatched).Select(t => t.RuleId));
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
