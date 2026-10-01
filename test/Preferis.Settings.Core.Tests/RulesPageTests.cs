using Preferis.Core.Configuration;
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
        var leaf = Assert.IsType<LeafConditionViewModel>(rule.Condition);
        Assert.Equal(ConditionTarget.Host, leaf.Target);
        Assert.Equal(MatchKind.Exact, leaf.Match);
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
    public void Editing_a_leaf_updates_the_rule()
    {
        var page = Page(out var settings);
        var leaf = Assert.IsType<LeafConditionViewModel>(Rule(page, "google-admin").Condition);

        leaf.Target = ConditionTarget.Host;
        leaf.Match = MatchKind.EndsWith;
        leaf.Pattern = ".google.com";

        var condition = Assert.IsType<HostCondition>(settings.Config.Rules[1].Condition);
        Assert.Equal(MatchKind.EndsWith, condition.Match);
        Assert.Equal(".google.com", condition.Pattern);
        Assert.False(leaf.IsIgnoreCaseAvailable);
    }

    [Fact]
    public void Url_leaves_can_be_case_sensitive()
    {
        var page = Page(out var settings);
        var leaf = Assert.IsType<LeafConditionViewModel>(Rule(page, "google-admin").Condition);
        Assert.True(leaf.IsIgnoreCaseAvailable);

        leaf.IgnoreCase = false;

        Assert.False(Assert.IsType<UrlCondition>(settings.Config.Rules[1].Condition).IgnoreCase);
    }

    [Fact]
    public void Invalid_patterns_are_explained_on_the_leaf()
    {
        var page = Page(out _);
        var leaf = Assert.IsType<LeafConditionViewModel>(Rule(page, "ms-login").Condition);
        Assert.Null(leaf.PatternError);

        leaf.Pattern = "^login\\.(";

        Assert.NotNull(leaf.PatternError);
        Assert.NotEmpty(Rule(page, "ms-login").Issues);
    }

    [Fact]
    public void Groups_can_be_built_and_negated()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");
        var leaf = Assert.IsType<LeafConditionViewModel>(rule.Condition);

        leaf.WrapInGroupCommand.Execute(null);
        var group = Assert.IsType<GroupConditionViewModel>(rule.Condition);
        Assert.Equal(GroupKind.All, group.Kind);
        Assert.Same(leaf, Assert.Single(group.Children));

        group.AddConditionCommand.Execute(null);
        var added = Assert.IsType<LeafConditionViewModel>(group.Children[1]);
        added.Pattern = "admin.google.com";
        added.IsNegated = true;
        group.Kind = GroupKind.Any;

        var model = Assert.IsType<OrCondition>(settings.Config.Rules[1].Condition);
        Assert.IsType<UrlCondition>(model.Conditions[0]);
        var not = Assert.IsType<NotCondition>(model.Conditions[1]);
        Assert.Equal("admin.google.com", Assert.IsType<HostCondition>(not.Condition).Pattern);

        leaf.RemoveCommand.Execute(null);
        Assert.Single(Assert.IsType<OrCondition>(settings.Config.Rules[1].Condition).Conditions);
    }

    [Fact]
    public void Removing_the_root_leaves_the_rule_without_a_condition()
    {
        var page = Page(out var settings);
        var rule = Rule(page, "google-admin");

        rule.Condition!.RemoveCommand.Execute(null);

        Assert.Null(rule.Condition);
        Assert.Null(settings.Config.Rules[1].Condition);
        rule.AddConditionCommand.Execute(null);
        Assert.IsType<HostCondition>(settings.Config.Rules[1].Condition);
    }

    [Fact]
    public void Negations_load_as_a_flag_on_the_negated_node()
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
        var rule = _harness.Create().Rules.Rules[0];

        var group = Assert.IsType<GroupConditionViewModel>(rule.Condition);
        Assert.True(group.IsNegated);
        Assert.Equal(GroupKind.Any, group.Kind);
        Assert.False(Assert.Single(group.Children).IsNegated);
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

    [Fact]
    public void Unknown_conditions_and_actions_are_kept_as_they_are()
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
        var settings = _harness.Create();
        var rule = settings.Rules.Rules[0];
        var group = Assert.IsType<GroupConditionViewModel>(rule.Condition);
        Assert.Equal("sourceApp", Assert.IsType<UnknownConditionViewModel>(group.Children[0]).Type);
        Assert.Equal("teleport", Assert.IsType<UnknownActionViewModel>(rule.Actions.Items[0]).Type);

        group.AddConditionCommand.Execute(null);
        ((LeafConditionViewModel)group.Children[1]).Pattern = "example.com";
        _harness.Saves.RunPending();

        var saved = File.ReadAllText(_harness.Paths.DefaultConfigPath);
        Assert.Contains("com.tinyspeck.slackmacgap", saved, StringComparison.Ordinal);
        Assert.Contains("moon", saved, StringComparison.Ordinal);
    }
}
