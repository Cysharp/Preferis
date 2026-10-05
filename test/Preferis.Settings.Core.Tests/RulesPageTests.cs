using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class RulesPageTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private RulesPageViewModel Page(out SettingsViewModel settings)
    {
        settings = _harness.WithSample().Create();
        return settings.Rules;
    }

    private static RuleViewModel Rule(RulesPageViewModel page, string id) => page.Rules.Single(r => r.Id == id);

    /// <summary>
    /// An edit in the condition editor, as both UIs make it: the editor shows the rule's condition as an editor tree
    /// and hands every change back as a whole condition.
    /// </summary>
    private static void EditCondition(RuleViewModel rule, Func<CompoundPredicate, CompoundPredicate> edit)
        => rule.SetCondition(ConditionPredicates.FromEditor(edit(ConditionPredicates.ToEditor(rule.Model.Condition)!)));

    private static ComparisonPredicate HostRow(string pattern, MatchKind match = MatchKind.Exact) => new(PredicateField.Host, match, pattern);

    [Fact]
    public void Rules_are_listed_in_order_with_a_summary()
    {
        var page = Page(out _);

        Assert.Equal(["unwrap-safelinks", "google-admin", "app-split", "ms-login"], page.Rules.Select(r => r.Id));
        Assert.Equal("host matches *.safelinks.protection.outlook.com → rewrite (query parameter url), reroute", Rule(page, "unwrap-safelinks").Summary);
        Assert.Equal(@"host matches regex ^login\.microsoftonline\.com$ → open Microsoft Edge, Chrome (Work), Chrome (Default)", Rule(page, "ms-login").Summary);
        Assert.Equal("url starts with https://admin.google.com/ → open Chrome (Work)", Rule(page, "google-admin").Summary);
        Assert.Equal("any of 2 conditions → open Chrome (Default)", Rule(page, "app-split").Summary);
        Assert.Equal("open the default profile", page.CatchAll.Summary);
    }

    [Fact]
    public void A_new_rule_starts_with_a_host_condition_and_opens_the_default_profile()
    {
        var page = Page(out var settings);

        page.AddRuleCommand.Execute(null);

        // The first rule is selected when the page opens, and new rules go right after the selection.
        var rule = page.SelectedRule!;
        Assert.Same(rule, page.CurrentEditor);
        Assert.Equal("new-rule", rule.Id);
        Assert.Same(rule.Model, settings.Config.Rules[1]);
        var root = ConditionPredicates.ToEditor(rule.Model.Condition)!;
        Assert.Equal(CompoundKind.All, root.Kind);
        Assert.Equal(HostRow(""), Assert.Single(root.Children));
        Assert.IsType<OpenActionViewModel>(Assert.Single(rule.Actions.Items));
    }

    [Fact]
    public void A_new_rule_is_inserted_after_the_selected_one()
    {
        var page = Page(out var settings);
        page.SelectedRule = page.Rules[1];

        page.AddRuleCommand.Execute(null);

        Assert.Equal(2, page.Rules.IndexOf(page.SelectedRule!));
        Assert.Equal("new-rule", settings.Config.Rules[2].Id);
    }

    [Fact]
    public void Rules_can_be_duplicated_moved_and_deleted()
    {
        var page = Page(out var settings);
        page.SelectedRule = Rule(page, "google-admin");

        page.DuplicateRuleCommand.Execute(null);
        Assert.Equal("google-admin-2", page.SelectedRule!.Id);
        Assert.Equal(2, page.Rules.IndexOf(page.SelectedRule));

        page.MoveRuleUpCommand.Execute(null);
        page.MoveRuleUpCommand.Execute(null);
        Assert.Equal("google-admin-2", settings.Config.Rules[0].Id);
        Assert.False(page.MoveRuleUpCommand.CanExecute(null));

        page.DeleteRuleCommand.Execute(null);
        Assert.Equal(["unwrap-safelinks", "google-admin", "app-split", "ms-login"], settings.Config.Rules.Select(r => r.Id));
        Assert.Equal("unwrap-safelinks", page.SelectedRule?.Id);
    }

    [Fact]
    public void A_rule_can_be_moved_to_any_position()
    {
        var page = Page(out var settings);
        page.SelectedRule = Rule(page, "ms-login");

        page.MoveRule(page.SelectedRule, 0);

        Assert.Equal(["ms-login", "unwrap-safelinks", "google-admin", "app-split"], settings.Config.Rules.Select(r => r.Id));
        Assert.Equal(["ms-login", "unwrap-safelinks", "google-admin", "app-split"], page.Rules.Select(r => r.Id));
        Assert.False(page.MoveRuleUpCommand.CanExecute(null));
        Assert.True(settings.IsDirty);

        page.MoveRule(Rule(page, "unwrap-safelinks"), 3);
        Assert.Equal(["ms-login", "google-admin", "app-split", "unwrap-safelinks"], settings.Config.Rules.Select(r => r.Id));
    }

    [Fact]
    public void Enabling_and_renaming_write_through()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "ms-login");

        rule.Enabled = false;
        rule.Name = "Sign-in";

        Assert.False(settings.Config.Rules[3].Enabled);
        Assert.Equal("Sign-in", settings.Config.Rules[3].Name);
        Assert.True(settings.IsDirty);
    }

    [Fact]
    public void The_catch_all_can_be_selected_but_not_moved_or_deleted()
    {
        var page = Page(out _);

        page.SelectCatchAllCommand.Execute(null);

        Assert.Null(page.SelectedRule);
        Assert.Same(page.CatchAll, page.CurrentEditor);
        Assert.False(page.DeleteRuleCommand.CanExecute(null));
        Assert.False(page.MoveRuleDownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editing_a_row_updates_the_rule_and_is_saved()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");

        EditCondition(rule, root => root with { Children = [HostRow(".google.com", MatchKind.EndsWith)] });

        var condition = Assert.IsType<HostCondition>(settings.Config.Rules[1].Condition);
        Assert.Equal(MatchKind.EndsWith, condition.Match);
        Assert.Equal(".google.com", condition.Pattern);
        Assert.Equal("host ends with .google.com → open Chrome (Work)", rule.Summary);
        Assert.True(settings.IsDirty);

        await _harness.Saves.RunPendingAsync();
        Assert.Equal(".google.com", Assert.IsType<HostCondition>(_harness.SavedConfig().Rules[1].Condition).Pattern);
    }

    [Fact]
    public void Url_rows_can_be_case_sensitive()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");
        var row = Assert.IsType<ComparisonPredicate>(Assert.Single(ConditionPredicates.ToEditor(rule.Model.Condition)!.Children));
        Assert.Equal(PredicateField.Url, row.Field);

        EditCondition(rule, root => root with { Children = [row with { Field = PredicateField.UrlCaseSensitive }] });

        var url = Assert.IsType<UrlCondition>(settings.Config.Rules[1].Condition);
        Assert.False(url.IgnoreCase);
        Assert.Equal("https://admin.google.com/", url.Pattern);
    }

    [Fact]
    public void Invalid_patterns_are_reported_on_the_rule()
    {
        var page = Page(out _);
        var rule = Rule(page, "ms-login");
        Assert.Empty(rule.Issues);

        EditCondition(rule, root => root with { Children = [HostRow(@"^login\.(", MatchKind.Regex)] });

        Assert.NotEmpty(rule.Issues);
    }

    [Fact]
    public async Task Conditions_can_be_added_and_removed()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");

        EditCondition(rule, root => root with { Children = [.. root.Children, HostRow("admin.google.com")] });

        var and = Assert.IsType<AndCondition>(settings.Config.Rules[1].Condition);
        Assert.IsType<UrlCondition>(and.Conditions[0]);
        Assert.Equal("admin.google.com", Assert.IsType<HostCondition>(and.Conditions[1]).Pattern);
        Assert.Equal("all of 2 conditions → open Chrome (Work)", rule.Summary);

        // A root with one row left is stored as that row.
        EditCondition(rule, root => root with { Children = root.Children.Skip(1).ToList() });

        Assert.Equal("admin.google.com", Assert.IsType<HostCondition>(settings.Config.Rules[1].Condition).Pattern);
        await _harness.Saves.RunPendingAsync();
        Assert.Equal("admin.google.com", Assert.IsType<HostCondition>(_harness.SavedConfig().Rules[1].Condition).Pattern);
    }

    [Fact]
    public void Removing_every_row_leaves_the_rule_without_a_condition()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");

        EditCondition(rule, root => root with { Children = [] });

        Assert.Null(settings.Config.Rules[1].Condition);
        Assert.Equal("no condition → open Chrome (Work)", rule.Summary);
        var empty = ConditionPredicates.ToEditor(rule.Model.Condition)!;
        Assert.Equal(CompoundKind.All, empty.Kind);
        Assert.Empty(empty.Children);

        EditCondition(rule, root => root with { Children = [HostRow("")] });
        Assert.IsType<HostCondition>(settings.Config.Rules[1].Condition);
    }

    [Fact]
    public void Groups_can_be_nested_and_negated()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");

        EditCondition(rule, root => new CompoundPredicate(CompoundKind.Any,
        [
            .. root.Children,
            new CompoundPredicate(CompoundKind.None, [HostRow("admin.google.com"), HostRow("accounts.google.com")]),
        ]));

        var or = Assert.IsType<OrCondition>(settings.Config.Rules[1].Condition);
        Assert.IsType<UrlCondition>(or.Conditions[0]);
        var none = Assert.IsType<OrCondition>(Assert.IsType<NotCondition>(or.Conditions[1]).Condition);
        Assert.Equal(["admin.google.com", "accounts.google.com"], none.Conditions.Cast<HostCondition>().Select(c => c.Pattern));
        Assert.Equal("any of 2 conditions → open Chrome (Work)", rule.Summary);
    }

    [Fact]
    public void Negations_load_as_none_of()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            {
              "version": 1,
              "profiles": [],
              "rules": [
                { "id": "r", "name": "r", "enabled": true,
                  "condition": { "type": "not", "condition": { "type": "or", "conditions": [ { "type": "host", "match": "equals", "pattern": "a" } ] } },
                  "actions": [ { "type": "none" } ] }
              ],
              "catchAll": { "actions": [ { "type": "open" } ] }
            }
            """);
        var settings = _harness.Create();
        var rule = settings.Rules.Rules[0];

        var shown = ConditionPredicates.ToEditor(rule.Model.Condition)!;
        Assert.Equal(CompoundKind.None, shown.Kind);
        Assert.Equal(HostRow("a"), Assert.Single(shown.Children));

        EditCondition(rule, root => root with { Children = [HostRow("a"), HostRow("b")] });

        var or = Assert.IsType<OrCondition>(Assert.IsType<NotCondition>(settings.Config.Rules[0].Condition).Condition);
        Assert.Equal(["a", "b"], or.Conditions.Cast<HostCondition>().Select(c => c.Pattern));
    }

    [Fact]
    public void Actions_can_be_added_configured_and_reordered()
    {
        var page = Page(out var settings);
        var actions = Rule(page, "google-admin").Actions;

        actions.AddCommand.Execute(ActionKind.Rewrite);
        var rewrite = Assert.IsType<RewriteActionViewModel>(actions.Items[^1]);
        rewrite.Pattern = "^http://";
        rewrite.Replacement = "https://";
        rewrite.MoveUpCommand.Execute(null);

        var model = settings.Config.Rules[1].Actions;
        Assert.Equal(["rewrite", "open"], model.Select(a => a.Type));
        var stored = Assert.IsType<RewriteAction>(model[0]);
        Assert.Equal(RewriteMode.Regex, stored.Mode);
        Assert.Equal("https://", stored.Replacement);

        rewrite.Mode = RewriteMode.QueryParameter;
        rewrite.Parameter = "url";
        Assert.Equal("url", Assert.IsType<RewriteAction>(settings.Config.Rules[1].Actions[0]).Parameter);

        rewrite.RemoveCommand.Execute(null);
        Assert.Equal(["open"], settings.Config.Rules[1].Actions.Select(a => a.Type));
    }

    [Fact]
    public void Open_actions_offer_every_profile_and_keep_missing_ones_visible()
    {
        var page = Page(out var settings);
        var open = Assert.IsType<OpenActionViewModel>(Rule(page, "ms-login").Actions.Items[0]);
        Assert.Equal(6, open.Profiles.Count);
        Assert.Equal(["chrome-default", "chrome-work", "edge"], open.Profiles.Where(p => p.IsSelected).Select(p => p.Id).Order());

        open.Profiles.Single(p => p.Id == "chrome-default").IsSelected = false;
        Assert.Equal(["edge", "chrome-work"], Assert.IsType<OpenAction>(settings.Config.Rules[3].Actions[0]).Profiles);

        settings.Config.Rules[3].Actions[0] = new OpenAction { Profiles = ["edge", "gone"] };
        settings.Rules.Activate();
        open = Assert.IsType<OpenActionViewModel>(Rule(settings.Rules, "ms-login").Actions.Items[0]);
        var missing = open.Profiles.Single(p => p.Id == "gone");
        Assert.True(missing.IsMissing);
        Assert.Equal("gone (missing)", missing.Name);
    }

    [Fact]
    public void Open_actions_store_the_checked_profiles_in_the_order_of_the_profile_list()
    {
        var page = Page(out var settings);
        var open = Assert.IsType<OpenActionViewModel>(Rule(page, "google-admin").Actions.Items[0]);

        // Checked after chrome-work, but listed before it.
        open.Profiles.Single(p => p.Id == "edge").IsSelected = true;

        Assert.Equal(["edge", "chrome-work"], Assert.IsType<OpenAction>(settings.Config.Rules[1].Actions[0]).Profiles);
    }

    [Fact]
    public void An_open_action_without_profiles_means_the_default_profile()
    {
        var page = Page(out var settings);
        var open = Assert.IsType<OpenActionViewModel>(Rule(page, "google-admin").Actions.Items[0]);

        open.Profiles.Single(p => p.Id == "chrome-work").IsSelected = false;

        Assert.Null(Assert.IsType<OpenAction>(settings.Config.Rules[1].Actions[0]).Profiles);
        Assert.True(open.UsesDefaultProfile);
    }

    [Fact]
    public void Profiles_added_elsewhere_show_up_when_the_page_is_activated()
    {
        var page = Page(out var settings);
        settings.Profiles.AddCommandProfileCommand.Execute(null);

        settings.SelectedPage = settings.Rules;

        var open = Assert.IsType<OpenActionViewModel>(Rule(settings.Rules, "ms-login").Actions.Items[0]);
        Assert.Equal(7, open.Profiles.Count);
    }

    [Fact]
    public void The_catch_all_actions_are_editable()
    {
        var page = Page(out var settings);

        page.CatchAll.Actions.AddCommand.Execute(ActionKind.System);
        page.CatchAll.Actions.Items[0].RemoveCommand.Execute(null);

        Assert.Equal(["system"], settings.Config.CatchAll.Actions.Select(a => a.Type));
        Assert.Equal("hand to the system default", page.CatchAll.Summary);
    }

    private RuleViewModel RuleFromANewerPreferis()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            {
              "version": 1,
              "profiles": [],
              "rules": [
                { "id": "r", "name": "r", "enabled": true,
                  "condition": { "type": "and", "conditions": [ { "type": "sourceApp", "bundleId": "com.tinyspeck.slackmacgap" } ] },
                  "actions": [ { "type": "teleport", "where": "moon" } ] }
              ],
              "catchAll": { "actions": [ { "type": "open" } ] }
            }
            """);
        return _harness.Create().Rules.Rules[0];
    }

    [Fact]
    public async Task Unknown_conditions_and_actions_are_kept_as_they_are()
    {
        var rule = RuleFromANewerPreferis();
        // The condition editor cannot show it; the UIs show its JSON instead.
        Assert.Null(ConditionPredicates.ToEditor(rule.Model.Condition));
        Assert.Equal("teleport", Assert.IsType<UnknownActionViewModel>(rule.Actions.Items[0]).Type);

        rule.Name = "Slack";
        await _harness.Saves.RunPendingAsync();

        var saved = File.ReadAllText(_harness.Paths.DefaultConfigPath);
        Assert.Contains("Slack", saved, StringComparison.Ordinal);
        Assert.Contains("com.tinyspeck.slackmacgap", saved, StringComparison.Ordinal);
        Assert.Contains("moon", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_condition_can_be_replaced_with_a_new_one()
    {
        var rule = RuleFromANewerPreferis();

        // "Replace with a New Condition".
        rule.SetCondition(new HostCondition { Match = MatchKind.Exact, Pattern = "" });

        Assert.Equal(HostRow(""), Assert.Single(ConditionPredicates.ToEditor(rule.Model.Condition)!.Children));
        await _harness.Saves.RunPendingAsync();
        Assert.DoesNotContain("sourceApp", File.ReadAllText(_harness.Paths.DefaultConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rules_imported_from_csv_go_after_the_others_and_the_result_is_shown()
    {
        var page = Page(out var settings);
        var csv = Path.Combine(_harness.Root, "rules.csv");
        File.WriteAllText(csv, "URL,Profile\nhttps://www.example.com,Microsoft Edge\n*.slack.com,Chrome (Work)\nhttps://github.com/*,microsoft edge\nfoo.example.com,Nobody\n", new System.Text.UTF8Encoding(true));
        _harness.Dialogs.Files.Enqueue(csv);

        await page.ImportRulesCommand.ExecuteAsync(null);

        Assert.Equal(["Imported Rule for Microsoft Edge", "Imported Rule for Chrome (Work)"], settings.Config.Rules.Skip(4).Select(r => r.Name));
        Assert.Equal(settings.Config.Rules.Select(r => r.Id), page.Rules.Select(r => r.Id));
        Assert.Same(page.Rules[4], page.SelectedRule);
        Assert.Equal("any of 2 conditions → open Microsoft Edge", page.Rules[4].Summary);
        var (title, message, buttons) = _harness.Dialogs.Asked[^1];
        Assert.Equal("Imported 3 lines.", title);
        Assert.Contains("• Imported Rule for Microsoft Edge (2 lines)", message, StringComparison.Ordinal);
        Assert.Contains("• Line 5: No profile is named “Nobody”.", message, StringComparison.Ordinal);
        Assert.Equal(["OK"], buttons);

        await _harness.Saves.RunPendingAsync();
        Assert.Equal(6, _harness.SavedConfig().Rules.Count);
    }

    [Fact]
    public async Task Csv_ports_regexes_and_multiple_profiles_are_saved_and_route_as_imported()
    {
        var page = Page(out var settings);
        var csv = Path.Combine(_harness.Root, "rules.csv");
        File.WriteAllText(csv, """
            URL,Profile,Profile
            intranet.example.com:8080,Chrome (Work),Microsoft Edge
            /^https?://regex\.example\.com\//,Chrome (Work)
            rejected.example.com,Chrome (Work),Nobody
            """);
        _harness.Dialogs.Files.Enqueue(csv);

        await page.ImportRulesCommand.ExecuteAsync(null);

        Assert.Equal(6, settings.Config.Rules.Count);
        Assert.Equal(settings.Config.Rules.Select(r => r.Id), page.Rules.Select(r => r.Id));
        Assert.Same(page.Rules[4], page.SelectedRule);
        Assert.Empty(page.Rules[4].Issues);
        Assert.Empty(page.Rules[5].Issues);
        var (title, message, buttons) = _harness.Dialogs.Asked[^1];
        Assert.Equal("Imported 2 lines.", title);
        Assert.Contains("• Line 4: No profile is named “Nobody”.", message, StringComparison.Ordinal);
        Assert.Equal(["OK"], buttons);

        await _harness.Saves.RunPendingAsync();
        var saved = _harness.SavedConfig();
        Assert.Equal(6, saved.Rules.Count);
        Assert.Equal(["edge", "chrome-work"], Assert.IsType<OpenAction>(Assert.Single(saved.Rules[4].Actions)).Profiles);
        Assert.Equal(["chrome-work"], Assert.IsType<OpenAction>(Assert.Single(saved.Rules[5].Actions)).Profiles);
        Assert.Equal(@"^https?://regex\.example\.com\/", Assert.IsType<UrlCondition>(saved.Rules[5].Condition).Pattern);
        var router = new Router(saved);

        void RoutesTo(string url, string? ruleId, params string[] profileIds)
        {
            var result = router.Route(RoutingUrl.Parse(url));
            Assert.Equal(ruleId, result.RuleId);
            Assert.Equal(profileIds, Assert.IsType<RoutingDecision.Open>(result.Decision).ProfileIds);
        }

        RoutesTo("http://intranet.example.com:8080/path", saved.Rules[4].Id, "edge", "chrome-work");
        RoutesTo("https://intranet.example.com:8080/path", saved.Rules[4].Id, "edge", "chrome-work");
        RoutesTo("https://intranet.example.com:8081/path", null, "edge");
        RoutesTo("https://intranet.example.com/path", null, "edge");
        RoutesTo("http://regex.example.com/path", saved.Rules[5].Id, "chrome-work");
        RoutesTo("https://regex.example.com/path", saved.Rules[5].Id, "chrome-work");
        RoutesTo("https://regex.example.com.evil.test/path", null, "edge");
        RoutesTo("https://rejected.example.com/path", null, "edge");
    }

    [Fact]
    public async Task Nothing_changes_when_no_file_is_chosen()
    {
        var page = Page(out var settings);

        await page.ImportRulesCommand.ExecuteAsync(null);

        Assert.Equal(4, settings.Config.Rules.Count);
        Assert.Empty(_harness.Dialogs.Asked);
        Assert.False(settings.IsDirty);
    }

    [Fact]
    public async Task A_file_without_anything_to_import_says_so()
    {
        var page = Page(out var settings);
        var csv = Path.Combine(_harness.Root, "rules.csv");
        File.WriteAllText(csv, "URL,Profile\nfoo.example.com,Nobody\n");
        _harness.Dialogs.Files.Enqueue(csv);

        await page.ImportRulesCommand.ExecuteAsync(null);

        Assert.Equal(4, settings.Config.Rules.Count);
        Assert.Equal("No rules were imported.", _harness.Dialogs.Asked[^1].Title);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_reported()
    {
        var page = Page(out _);
        _harness.Dialogs.Files.Enqueue(Path.Combine(_harness.Root, "missing.csv"));

        await page.ImportRulesCommand.ExecuteAsync(null);

        Assert.Equal("The file could not be read.", _harness.Dialogs.Asked[^1].Title);
    }
}
