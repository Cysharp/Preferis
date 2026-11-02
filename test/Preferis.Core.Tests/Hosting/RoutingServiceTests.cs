using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Hosting;

public class RoutingServiceTests
{
    private const string Chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
    private const string Edge = "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
    private const string Safari = "/Applications/Safari.app/Contents/MacOS/Safari";

    private static FakeHost FullHost() => new FakeHost().WithSampleConfig()
        .WithBrowser("chrome", Chrome, "/Applications/Google Chrome.app")
        .WithBrowser("edge", Edge, "/Applications/Microsoft Edge.app")
        .WithBrowser("safari", Safari, "/Applications/Safari.app")
        .WithChromeProfiles();

    private static HandleResult Handle(FakeHost host, string url) => host.CreateService().Handle(RoutingUrl.Parse(url));

    private static PickerResponse Select(string profileId, bool remember = false) => new() { Result = PickerResult.Selected, ProfileId = profileId, Remember = remember };

    // The settings UI saves an edit while the picker is open. The Router keeps the copy it loaded before.
    private static void SaveMeanwhile(FakeHost host, Action<PreferisConfig> edit)
    {
        var edited = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(host.Config));
        edit(edited);
        host.Config = edited;
    }

    private static List<string>? RememberedProfiles(PreferisConfig saved)
        => saved.Rules.Single(r => r.Id.StartsWith("remember-", StringComparison.Ordinal)).Actions.OfType<OpenAction>().Single().Profiles;

    [Fact]
    public void A_single_candidate_is_resolved_and_launched()
    {
        var host = FullHost();

        var result = Handle(host, "https://admin.google.com/ac/users");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("chrome-work", result.ProfileId);
        var plan = Assert.Single(host.Launched);
        Assert.Equal(Chrome, plan.Path);
        Assert.Equal(["--profile-directory=Profile 1", "https://admin.google.com/ac/users"], plan.Arguments);
        Assert.Empty(host.PickerRequests);
    }

    [Fact]
    public void The_app_that_opened_the_url_reaches_the_rules()
    {
        var host = FullHost();
        host.Config.Rules.Insert(0, new Rule
        {
            Id = "from-slack",
            Name = "From Slack",
            Condition = new AppIdCondition { Match = MatchKind.Exact, Pattern = "com.tinyspeck.slackmacgap" },
            Actions = [new OpenAction { Profiles = ["chrome-work"] }],
        });
        var url = RoutingUrl.Parse("https://www.example.org/");

        var fromSlack = host.CreateService().Handle(url, new SourceApp("com.tinyspeck.slackmacgap", "/Applications/Slack.app"));
        var fromElsewhere = host.CreateService().Handle(url);

        Assert.Equal("from-slack", fromSlack.RuleId);
        Assert.Equal("chrome-work", fromSlack.ProfileId);
        Assert.NotEqual("from-slack", fromElsewhere.RuleId);
    }

    [Theory]
    [InlineData("preferis://open?url=https%3A%2F%2Fwww.example.org%2F%3Fq%3D1")]
    [InlineData("preferis:https://www.example.org/?q=1")]
    public void A_preferis_url_routes_the_url_it_carries_as_opened_by_the_app_that_opened_it(string input)
    {
        // Reopening from a browser: a rule can tell which browser the URL is coming back from.
        var host = FullHost();
        host.Config.Rules.Insert(0, new Rule
        {
            Id = "from-safari",
            Name = "Back from Safari",
            Condition = new AppIdCondition { Match = MatchKind.Exact, Pattern = "com.apple.Safari" },
            Actions = [new OpenAction { Profiles = ["chrome-work"] }],
        });

        var result = host.CreateService().Handle(input, new SourceApp("com.apple.Safari", "/Applications/Safari.app"));

        Assert.Equal(("from-safari", "chrome-work"), (result.RuleId, result.ProfileId));
        Assert.Equal("https://www.example.org/?q=1", result.Url?.AbsoluteUri);
        Assert.Equal("https://www.example.org/?q=1", Assert.Single(host.Launched).Arguments[^1]);
    }

    [Fact]
    public void A_url_given_as_text_is_routed_as_it_is()
    {
        var host = FullHost();

        var result = host.CreateService().Handle("https://admin.google.com/ac/users");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("https://admin.google.com/ac/users", result.Url?.AbsoluteUri);
    }

    [Fact]
    public void A_preferis_url_that_carries_no_web_address_is_shown_to_the_user()
    {
        // The click in the browser would otherwise seem to do nothing.
        var host = FullHost();

        var result = host.CreateService().Handle("preferis:file:///Applications/Calculator.app?token=secret");

        Assert.Equal(HandleOutcome.Failed, result.Outcome);
        Assert.Null(result.Url);
        Assert.Empty(host.Launched);
        Assert.Empty(host.OpenedWithSystem);
        Assert.Empty(host.PickerRequests);
        var error = Assert.Single(host.Errors);
        Assert.Contains("only http and https", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Url);
        Assert.Contains(host.Log, l => l.Level == LogLevel.Error && l.Message.Contains("preferis:file:///Applications/Calculator.app?***", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Log, l => l.Message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Input_that_is_no_url_at_all_is_logged_without_bothering_the_user()
    {
        var host = FullHost();

        var result = host.CreateService().Handle("not a url?token=secret");

        Assert.Equal(HandleOutcome.Failed, result.Outcome);
        Assert.Null(result.Url);
        Assert.Empty(host.Errors);
        Assert.Contains(host.Log, l => l.Level == LogLevel.Error && l.Message == "Not an absolute URL or a rooted file path: not a url?***");
    }

    [Fact]
    public void The_log_names_profiles_as_well_as_their_ids()
    {
        var host = FullHost();

        var result = Handle(host, "https://admin.google.com/ac/users");

        Assert.Contains(host.Log, l => l.Message.Contains("with ['Chrome (Work)' (chrome-work)]", StringComparison.Ordinal));
        Assert.Contains(host.Log, l => l.Message.StartsWith("Launched 'Chrome (Work)' (chrome-work):", StringComparison.Ordinal));
        Assert.Equal("Launched 'Chrome (Work)' (chrome-work).", result.Summary);
    }

    [Theory]
    [InlineData("https://admin.google.com/", "google-admin")]
    [InlineData("https://nl.safelinks.protection.outlook.com/?url=https%3A%2F%2Fadmin.google.com%2F", "google-admin")]
    [InlineData("https://www.example.com/", null)]
    public void The_result_names_the_rule_that_decided(string url, string? ruleId)
    {
        var result = Handle(FullHost(), url);

        Assert.Equal(ruleId, result.RuleId);
    }

    [Fact]
    public void Several_candidates_show_the_picker_and_the_choice_is_launched()
    {
        var host = FullHost();
        host.PickerAnswer = _ => Select("chrome-default");

        var result = Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        var request = Assert.Single(host.PickerRequests);
        Assert.Equal(["edge", "chrome-default", "chrome-work"], request.Candidates.Select(c => c.ProfileId));
        Assert.Null(request.Message);
        Assert.Equal("--profile-directory=Default", Assert.Single(host.Launched).Arguments[0]);
    }

    [Fact]
    public void The_rewritten_url_is_what_opens()
    {
        var host = FullHost();

        Handle(host, "https://nl.safelinks.protection.outlook.com/?url=https%3A%2F%2Fadmin.google.com%2Fac%2Fusers");

        Assert.Equal("https://admin.google.com/ac/users", Assert.Single(host.Launched).Arguments[^1]);
    }

    [Fact]
    public void The_picker_and_remembering_see_the_rewritten_url()
    {
        var host = FullHost();
        host.PickerAnswer = _ => Select("edge", remember: true);

        Handle(host, "https://nl.safelinks.protection.outlook.com/?url=https%3A%2F%2Flogin.microsoftonline.com%2Fcommon");

        Assert.Equal("https://login.microsoftonline.com/common", Assert.Single(host.PickerRequests).Url);
        var remembered = Assert.IsType<HostCondition>(host.SavedConfig!.Rules.Single(r => r.Id.StartsWith("remember-", StringComparison.Ordinal)).Condition);
        Assert.Equal("login.microsoftonline.com", remembered.Pattern);
    }

    [Fact]
    public void Remembering_keeps_changes_saved_while_the_picker_was_open()
    {
        var host = FullHost();
        host.PickerAnswer = _ =>
        {
            // Meanwhile the settings UI saved an edit.
            var edited = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(host.Config));
            edited.Profiles[0].Name = "Edited meanwhile";
            host.Config = edited;
            return Select("chrome-work", remember: true);
        };

        Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal("Edited meanwhile", host.SavedConfig!.Profiles[0].Name);
        Assert.Contains(host.SavedConfig.Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_remembered_over_a_configuration_that_became_unreadable()
    {
        var host = FullHost();
        host.PickerAnswer = _ =>
        {
            host.ConfigError = "config.json could not be read";
            return Select("chrome-work", remember: true);
        };

        var result = Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Null(host.SavedConfig);
    }

    [Fact]
    public void Remembering_uses_the_id_the_profile_was_given_while_the_picker_was_open()
    {
        var host = FullHost();
        host.PickerAnswer = _ =>
        {
            SaveMeanwhile(host, config => ProfileIds.Rename(config, new LocalConfig(), "chrome-work", "work"));
            return Select("chrome-work", remember: true);
        };

        var result = Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal(["work"], RememberedProfiles(host.SavedConfig!));
        Assert.Empty(ConfigValidator.Validate(host.SavedConfig!));

        host.Config = host.SavedConfig!;
        host.PickerRequests.Clear();
        Assert.Equal("work", Handle(host, "https://login.microsoftonline.com/other").ProfileId);
        Assert.Empty(host.PickerRequests);
    }

    [Fact]
    public void Remembering_follows_several_renames_made_while_the_picker_was_open()
    {
        var host = FullHost();
        host.PickerAnswer = _ =>
        {
            SaveMeanwhile(host, config => ProfileIds.Rename(config, new LocalConfig(), "chrome-work", "work"));
            SaveMeanwhile(host, config => ProfileIds.Rename(config, new LocalConfig(), "work", "office"));
            return Select("chrome-work", remember: true);
        };

        Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(["office"], RememberedProfiles(host.SavedConfig!));
    }

    [Fact]
    public void A_profile_deleted_while_the_picker_was_open_is_not_remembered_but_still_opens_the_url()
    {
        var host = FullHost();
        host.PickerAnswer = _ =>
        {
            SaveMeanwhile(host, config => config.Profiles.RemoveAll(p => p.Id == "chrome-work"));
            return Select("chrome-work", remember: true);
        };

        var result = Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("chrome-work", result.ProfileId);
        Assert.Equal(["--profile-directory=Profile 1", "https://login.microsoftonline.com/common"], Assert.Single(host.Launched).Arguments);
        Assert.Null(host.SavedConfig);
        Assert.Contains(host.Log, l => l.Level == LogLevel.Warning && l.Message.StartsWith("Not remembering 'login.microsoftonline.com'", StringComparison.Ordinal));
    }

    [Fact]
    public void Remembering_keeps_an_id_that_another_profile_had_before()
    {
        // A hand-written file: chrome-work claims edge's id as one it had. The id is edge's now.
        var host = FullHost();
        host.Config.Profiles.Single(p => p.Id == "chrome-work").PreviousIds = ["edge"];
        host.PickerAnswer = _ => Select("edge", remember: true);

        Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(["edge"], RememberedProfiles(host.SavedConfig!));
    }

    [Fact]
    public void Remembering_a_choice_saves_a_rule_that_bypasses_the_picker_next_time()
    {
        var host = FullHost();
        host.PickerAnswer = _ => Select("chrome-work", remember: true);

        Handle(host, "https://login.microsoftonline.com/common");

        Assert.NotNull(host.SavedConfig);
        var index = host.SavedConfig.Rules.FindIndex(r => r.Id == "ms-login");
        var remembered = host.SavedConfig.Rules[index - 1];
        Assert.Equal("login.microsoftonline.com", Assert.IsType<HostCondition>(remembered.Condition).Pattern);

        host.Config = host.SavedConfig;
        host.PickerRequests.Clear();
        var again = Handle(host, "https://login.microsoftonline.com/other");
        Assert.Equal("chrome-work", again.ProfileId);
        Assert.Empty(host.PickerRequests);
    }

    /// <summary>Opens <paramref name="url"/>, remembers <paramref name="profileId"/> in the picker, and opens it again with the configuration as saved.</summary>
    private static HandleResult OpenAgainAfterRemembering(FakeHost host, string url, string profileId)
    {
        host.PickerAnswer = _ => Select(profileId, remember: true);
        Assert.Equal(HandleOutcome.Launched, Handle(host, url).Outcome);
        Assert.Single(host.PickerRequests);

        host.Config = host.SavedConfig!;
        host.PickerRequests.Clear();
        host.Launched.Clear();
        return Handle(host, url);
    }

    private static Rule Unwrap(params RoutingAction[] then) => new()
    {
        Id = "unwrap",
        Name = "Unwrap",
        Condition = new HostCondition { Match = MatchKind.Exact, Pattern = "wrapper.example" },
        Actions = [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, .. then],
    };

    [Fact]
    public void A_choice_remembered_after_its_rules_own_rewrite_skips_the_picker_next_time()
    {
        var host = FullHost();
        host.Config.Rules.Insert(0, Unwrap(new OpenAction { Profiles = ["edge", "chrome-work"] }));

        var again = OpenAgainAfterRemembering(host, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2Fpage", "chrome-work");

        Assert.Empty(host.PickerRequests);
        Assert.Equal("chrome-work", again.ProfileId);
        Assert.Equal(["--profile-directory=Profile 1", "https://target.example/page"], Assert.Single(host.Launched).Arguments);
    }

    [Fact]
    public void A_choice_remembered_after_several_rewrites_in_one_rule_skips_the_picker_next_time()
    {
        var host = FullHost();
        host.Config.Rules.Insert(0, Unwrap(
            new RewriteAction { Mode = RewriteMode.Regex, Pattern = "^http://", Replacement = "https://" },
            new OpenAction { Profiles = ["edge", "chrome-work"] }));

        var again = OpenAgainAfterRemembering(host, "https://wrapper.example/?url=http%3A%2F%2Ftarget.example%2Fpage", "chrome-work");

        Assert.Empty(host.PickerRequests);
        Assert.Equal("chrome-work", again.ProfileId);
        Assert.Equal("https://target.example/page", Assert.Single(host.Launched).Arguments[^1]);
    }

    [Fact]
    public void A_choice_remembered_after_the_catch_alls_rewrite_skips_the_picker_next_time()
    {
        var host = FullHost();
        host.Config.CatchAll.Actions = [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new OpenAction { Profiles = ["edge", "chrome-work"] }];

        var again = OpenAgainAfterRemembering(host, "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2Fpage", "chrome-work");

        Assert.Empty(host.PickerRequests);
        Assert.Equal("chrome-work", again.ProfileId);
        Assert.Equal("https://target.example/page", Assert.Single(host.Launched).Arguments[^1]);
    }

    [Fact]
    public void A_choice_remembered_after_a_rewrite_and_reroute_skips_the_picker_next_time()
    {
        var host = FullHost();

        var again = OpenAgainAfterRemembering(host, "https://nl.safelinks.protection.outlook.com/?url=https%3A%2F%2Flogin.microsoftonline.com%2Fcommon", "chrome-work");

        Assert.Empty(host.PickerRequests);
        Assert.Equal("chrome-work", again.ProfileId);
        Assert.Equal("https://login.microsoftonline.com/common", Assert.Single(host.Launched).Arguments[^1]);
    }

    [Fact]
    public void Cancelling_the_picker_opens_nothing()
    {
        var host = FullHost();
        host.PickerAnswer = _ => new PickerResponse { Result = PickerResult.Cancelled };

        var result = Handle(host, "https://login.microsoftonline.com/");

        Assert.Equal(HandleOutcome.Ignored, result.Outcome);
        Assert.Empty(host.Launched);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public void A_broken_picker_falls_back_to_the_default_profile()
    {
        var host = FullHost();
        host.PickerAnswer = _ => null;

        var result = Handle(host, "https://login.microsoftonline.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("edge", result.ProfileId);
        Assert.Contains(host.Log, l => l.Level >= LogLevel.Warning);
    }

    [Fact]
    public void An_unresolvable_profile_falls_back_to_the_default_profile()
    {
        var host = FullHost();
        host.Browsers.Remove("chrome");

        var result = Handle(host, "https://admin.google.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("edge", result.ProfileId);
        Assert.Equal(Edge, Assert.Single(host.Launched).Path);
    }

    [Fact]
    public void When_the_default_fails_too_the_picker_offers_every_profile_with_an_explanation()
    {
        var host = FullHost();
        host.Browsers.Remove("chrome");
        host.Browsers.Remove("edge");
        host.PickerAnswer = _ => Select("system-browser");

        var result = Handle(host, "https://admin.google.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        var request = Assert.Single(host.PickerRequests);
        Assert.Equal(host.Config.Profiles.Select(p => p.Id), request.Candidates.Select(c => c.ProfileId));
        Assert.NotNull(request.Message);
        Assert.Equal(LaunchKind.OpenWithApplication, Assert.Single(host.Launched).Kind);
    }

    [Fact]
    public void When_nothing_can_be_resolved_the_url_goes_to_the_system_handler()
    {
        var host = FullHost();
        host.Browsers.Clear();
        host.PickerAnswer = _ => null;

        var result = Handle(host, "https://admin.google.com/");

        Assert.Equal(HandleOutcome.Delegated, result.Outcome);
        Assert.Single(host.OpenedWithSystem);
    }

    [Fact]
    public void System_action_is_guarded_against_pointing_back_at_preferis()
    {
        var host = FullHost();
        host.Config.Rules.Insert(0, new Rule { Id = "sys", Name = "sys", Condition = new HostCondition { Match = MatchKind.Exact, Pattern = "sys.example.com" }, Actions = [new SystemAction()] });

        host.SelfIsDefaultHandler = false;
        Assert.Equal(HandleOutcome.Delegated, Handle(host, "https://sys.example.com/").Outcome);
        Assert.Single(host.OpenedWithSystem);

        host.SelfIsDefaultHandler = true;
        var guarded = Handle(host, "https://sys.example.com/");
        Assert.Equal(HandleOutcome.Launched, guarded.Outcome);
        Assert.Equal("edge", guarded.ProfileId);
        Assert.Single(host.OpenedWithSystem);
    }

    [Fact]
    public void None_action_ignores_the_url()
    {
        var host = FullHost();
        host.Config.Rules.Insert(0, new Rule { Id = "block", Name = "block", Condition = new HostCondition { Match = MatchKind.Exact, Pattern = "block.example.com" }, Actions = [new NoneAction()] });

        var result = Handle(host, "https://block.example.com/");

        Assert.Equal(HandleOutcome.Ignored, result.Outcome);
        Assert.Empty(host.Launched);
        Assert.Empty(host.OpenedWithSystem);
    }

    [Fact]
    public void A_broken_config_offers_the_detected_browsers()
    {
        var host = new FakeHost().WithBrowser("chrome", Chrome, "/Applications/Google Chrome.app").WithBrowser("safari", Safari, "/Applications/Safari.app");
        host.ConfigError = "config.json could not be read";
        host.PickerAnswer = _ => Select("browser:chrome");

        var result = Handle(host, "https://www.example.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        var request = Assert.Single(host.PickerRequests);
        Assert.Contains("config.json", request.Message);
        Assert.Equal(["browser:chrome", "browser:safari"], request.Candidates.Select(c => c.ProfileId));
        Assert.Equal("chrome", request.Candidates[0].Browser);
        Assert.False(request.Options.AllowRemember);
        var plan = Assert.Single(host.Launched);
        Assert.Equal(Chrome, plan.Path);
        Assert.Equal(["https://www.example.com/"], plan.Arguments);
    }

    [Fact]
    public void Without_any_configured_profile_the_detected_browsers_are_offered()
    {
        var host = new FakeHost().WithBrowser("safari", Safari, "/Applications/Safari.app");
        host.PickerAnswer = _ => Select("browser:safari");

        var result = Handle(host, "https://www.example.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        var plan = Assert.Single(host.Launched);
        Assert.Equal(LaunchKind.OpenWithApplication, plan.Kind);
        Assert.Equal("/Applications/Safari.app", plan.Path);
    }

    [Fact]
    public void A_launch_failure_falls_back_to_the_picker()
    {
        var host = FullHost();
        host.FailingLaunchPaths.Add(Chrome);
        host.PickerAnswer = _ => Select("edge");

        var result = Handle(host, "https://admin.google.com/");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("edge", result.ProfileId);
        var request = Assert.Single(host.PickerRequests);
        Assert.NotNull(request.Message);
        Assert.Equal(Edge, Assert.Single(host.Launched).Path);
    }

    [Fact]
    public void When_everything_fails_the_user_is_told_and_the_url_is_offered_for_copying()
    {
        var host = new FakeHost();
        host.SelfIsDefaultHandler = true;
        host.PickerAnswer = _ => null;

        var result = Handle(host, "https://www.example.com/");

        Assert.Equal(HandleOutcome.Failed, result.Outcome);
        var error = Assert.Single(host.Errors);
        Assert.Equal("https://www.example.com/", error.Url?.AbsoluteUri);
        Assert.Empty(host.Launched);
        Assert.Empty(host.OpenedWithSystem);
    }

    [Fact]
    public void This_machines_target_replaces_the_shared_one()
    {
        var host = FullHost();
        host.Local = new LocalConfig { ProfileTargets = new() { ["chrome-work"] = new ProfileTarget { Browser = "edge" } } };

        var result = Handle(host, "https://admin.google.com/ac/users");

        Assert.Equal("chrome-work", result.ProfileId);
        var plan = Assert.Single(host.Launched);
        Assert.Equal(Edge, plan.Path);
        Assert.Equal(["https://admin.google.com/ac/users"], plan.Arguments);
    }

    [Fact]
    public void The_picker_shows_what_this_machine_opens()
    {
        var host = FullHost();
        host.Local = new LocalConfig { ProfileTargets = new() { ["chrome-default"] = new ProfileTarget { Browser = "safari" } } };
        host.PickerAnswer = _ => Select("edge");

        Handle(host, "https://login.microsoftonline.com/common");

        var candidate = Assert.Single(host.PickerRequests).Candidates.Single(c => c.ProfileId == "chrome-default");
        Assert.Equal("safari", candidate.Browser);
        Assert.Null(candidate.BrowserProfile);
    }

    [Fact]
    public void Remembering_keeps_this_machines_targets_out_of_the_shared_configuration()
    {
        var host = FullHost();
        host.Local = new LocalConfig { ProfileTargets = new() { ["chrome-default"] = new ProfileTarget { Browser = "safari" } } };
        host.PickerAnswer = _ => Select("chrome-default", remember: true);

        var result = Handle(host, "https://login.microsoftonline.com/common");

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("/Applications/Safari.app", Assert.Single(host.Launched).Path);
        var saved = host.SavedConfig!.Profiles.Single(p => p.Id == "chrome-default");
        Assert.Equal("chrome", saved.Browser?.Default);
        Assert.Equal("Default", saved.BrowserProfile?.Directory);
    }
}
