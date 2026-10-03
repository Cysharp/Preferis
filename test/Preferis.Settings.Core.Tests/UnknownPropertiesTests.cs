using System.Text.Json;
using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

/// <summary>
/// Properties a newer Preferis added to conditions and actions of the types this version knows are written back
/// (docs/Configuration.md §4), even though the editors rebuild those objects on every edit.
/// </summary>
public sealed class UnknownPropertiesTests : IDisposable
{
    private const string Profiles = """
        [ { "id": "edge", "name": "Edge", "browser": "edge" }, { "id": "chrome", "name": "Chrome", "browser": "chrome" } ]
        """;

    // Every node is marked with what it is, so that a mark on the wrong node shows up as well as a lost one.
    private const string Tree = """
        { "type": "and", "futureSetting": "and", "conditions": [
          { "type": "host", "match": "equals", "pattern": "a.example.com", "futureSetting": "a" },
          { "type": "or", "futureSetting": "or", "conditions": [
            { "type": "url", "match": "startsWith", "pattern": "https://b.example.com/", "futureSetting": "b" },
            { "type": "host", "match": "endsWith", "pattern": ".c.example.com", "futureSetting": "c" } ] },
          { "type": "not", "futureSetting": "not", "condition": { "type": "or", "futureSetting": "none of", "conditions": [
            { "type": "host", "match": "contains", "pattern": "d", "futureSetting": "d" },
            { "type": "host", "match": "contains", "pattern": "e", "futureSetting": "e" } ] } } ] }
        """;

    private static readonly string[] AllMarks = ["and", "a", "or", "b", "c", "not", "none of", "d", "e"];

    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private SettingsViewModel Open(string json)
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, json);
        return _harness.Create();
    }

    private SettingsViewModel OpenRule(string condition, params string[] actions) => Open($$"""
        { "version": 1, "profiles": {{Profiles}},
          "rules": [ { "id": "r", "name": "r", "condition": {{condition}}, "actions": [ {{string.Join(", ", actions)}} ] } ],
          "catchAll": { "actions": [ { "type": "open" } ] } }
        """);

    /// <summary>The file once the pending save has run.</summary>
    private JsonElement Saved()
    {
        _harness.Saves.RunPending();
        using var document = JsonDocument.Parse(File.ReadAllText(_harness.Paths.DefaultConfigPath));
        return document.RootElement.Clone();
    }

    private Condition? SavedCondition()
    {
        _harness.Saves.RunPending();
        return _harness.SavedConfig().Rules[0].Condition;
    }

    /// <summary>What the condition editors of both UIs do on every change (RulesPage): the edited tree goes back into the rule.</summary>
    private static void Edit(RuleViewModel rule, Func<CompoundPredicate, CompoundPredicate> change)
        => rule.SetCondition(ConditionPredicates.FromEditor(change(ConditionPredicates.ToEditor(rule.Model.Condition)!), rule.Model.Condition));

    /// <summary>The group with one row changed, or removed when <paramref name="change"/> gives null.</summary>
    private static CompoundPredicate Row(CompoundPredicate group, int index, Func<PredicateNode, PredicateNode?> change)
    {
        var children = group.Children.ToList();
        if (change(children[index]) is { } changed)
        {
            children[index] = changed;
        }
        else
        {
            children.RemoveAt(index);
        }
        return group with { Children = children };
    }

    private static CompoundPredicate Group(CompoundPredicate root, int index, Func<CompoundPredicate, CompoundPredicate> change)
        => Row(root, index, node => change((CompoundPredicate)node));

    private static ComparisonPredicate NewRow => new(PredicateField.Host, MatchKind.Exact, "");

    /// <summary>The <c>futureSetting</c> of every node, parents before their children.</summary>
    private static IEnumerable<string> Marks(Condition? condition)
    {
        if (condition is null)
        {
            return [];
        }
        IEnumerable<string> own = condition.ExtensionData?.TryGetValue("futureSetting", out var mark) == true ? [mark.GetString()!] : [];
        IEnumerable<Condition> children = condition switch
        {
            AndCondition and => and.Conditions,
            OrCondition or => or.Conditions,
            NotCondition { Condition: { } negated } => [negated],
            _ => [],
        };
        return own.Concat(children.SelectMany(Marks));
    }

    [Fact]
    public void Adding_an_action_keeps_what_a_newer_preferis_added_to_the_others()
    {
        var settings = Open("""
            { "version": 1, "profiles": [], "rules": [],
              "catchAll": { "actions": [
                { "type": "rewrite", "mode": "queryParameter", "parameter": "url", "futureSetting": true },
                { "type": "open" } ] } }
            """);

        settings.Rules.CatchAll.Actions.AddCommand.Execute(ActionKind.None);

        var actions = Saved().GetProperty("catchAll").GetProperty("actions");
        Assert.Equal(["rewrite", "open", "none"], actions.EnumerateArray().Select(a => a.GetProperty("type").GetString()));
        Assert.True(actions[0].GetProperty("futureSetting").GetBoolean());
    }

    [Theory]
    [InlineData("""{ "type": "open", "profiles": ["edge"], "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "system", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "none", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "reroute", "futureSetting": { "level": 2 } }""")]
    public void Every_kind_of_action_keeps_what_a_newer_preferis_added_when_the_list_changes(string action)
    {
        var actions = OpenRule(Tree, action).Rules.Rules[0].Actions;

        actions.AddCommand.Execute(ActionKind.System);
        actions.Items[0].MoveDownCommand.Execute(null);

        var saved = Saved().GetProperty("rules")[0].GetProperty("actions");
        Assert.Equal(2, saved[1].GetProperty("futureSetting").GetProperty("level").GetInt32());
        Assert.False(saved[0].TryGetProperty("futureSetting", out _));

        actions.Items[0].RemoveCommand.Execute(null);

        saved = Saved().GetProperty("rules")[0].GetProperty("actions");
        Assert.Equal(2, Assert.Single(saved.EnumerateArray()).GetProperty("futureSetting").GetProperty("level").GetInt32());
    }

    [Fact]
    public void Editing_an_action_keeps_what_a_newer_preferis_added_to_it()
    {
        var actions = OpenRule(Tree,
            """{ "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://", "futureSetting": "rewrite" }""",
            """{ "type": "open", "profiles": ["edge"], "futureSetting": "open" }""").Rules.Rules[0].Actions;
        var rewrite = Assert.IsType<RewriteActionViewModel>(actions.Items[0]);
        var open = Assert.IsType<OpenActionViewModel>(actions.Items[1]);

        rewrite.Mode = RewriteMode.QueryParameter;
        rewrite.Parameter = "url";
        open.Profiles.Single(p => p.Id == "chrome").IsSelected = true;

        var saved = Saved().GetProperty("rules")[0].GetProperty("actions");
        Assert.Equal("url", saved[0].GetProperty("parameter").GetString());
        Assert.Equal("rewrite", saved[0].GetProperty("futureSetting").GetString());
        Assert.Equal(["edge", "chrome"], saved[1].GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("open", saved[1].GetProperty("futureSetting").GetString());
    }

    [Fact]
    public void Moving_and_renaming_rules_keeps_what_a_newer_preferis_added_to_their_conditions_and_actions()
    {
        var settings = Open($$"""
            { "version": 1, "profiles": {{Profiles}},
              "rules": [
                { "id": "first", "name": "first", "condition": {{Tree}}, "actions": [ { "type": "open", "futureSetting": "open" } ] },
                { "id": "second", "name": "second", "condition": { "type": "host", "match": "equals", "pattern": "x", "futureSetting": "x" }, "actions": [ { "type": "none" } ] } ],
              "catchAll": { "actions": [ { "type": "open" } ] } }
            """);

        settings.Rules.MoveRule(settings.Rules.Rules[0], 1);
        settings.Rules.Rules[1].Name = "renamed";

        _harness.Saves.RunPending();
        var saved = _harness.SavedConfig();
        Assert.Equal(["second", "first"], saved.Rules.Select(r => r.Id));
        Assert.Equal(AllMarks, Marks(saved.Rules[1].Condition));
        Assert.Equal("open", saved.Rules[1].Actions[0].ExtensionData?["futureSetting"].GetString());
    }

    [Fact]
    public void Typing_into_a_condition_keeps_what_a_newer_preferis_added()
    {
        var rule = OpenRule(Tree, """{ "type": "open" }""").Rules.Rules[0];

        Edit(rule, root => Group(root, 1, any => Row(any, 0, b => ((ComparisonPredicate)b) with { Pattern = "https://b.example.org/" })));
        Edit(rule, root => Group(root, 2, none => Row(none, 1, e => ((ComparisonPredicate)e) with { Match = MatchKind.StartsWith })));

        var saved = SavedCondition();
        Assert.Equal(AllMarks, Marks(saved));
        var any = Assert.IsType<OrCondition>(Assert.IsType<AndCondition>(saved).Conditions[1]);
        Assert.Equal("https://b.example.org/", Assert.IsType<UrlCondition>(any.Conditions[0]).Pattern);
    }

    [Fact]
    public void A_row_typed_into_the_same_as_another_keeps_its_own()
    {
        var rule = OpenRule("""
            { "type": "or", "conditions": [
              { "type": "host", "match": "equals", "pattern": "a", "futureSetting": "a" },
              { "type": "host", "match": "equals", "pattern": "b", "futureSetting": "b" } ] }
            """, """{ "type": "open" }""").Rules.Rules[0];

        Edit(rule, root => Row(root, 0, a => ((ComparisonPredicate)a) with { Pattern = "b" }));

        Assert.Equal(["a", "b"], Marks(SavedCondition()));
    }

    [Fact]
    public void Adding_and_removing_conditions_keeps_what_a_newer_preferis_added_to_the_others()
    {
        var rule = OpenRule(Tree, """{ "type": "open" }""").Rules.Rules[0];

        Edit(rule, root => Group(root, 1, any => any with { Children = [NewRow, .. any.Children] }));
        Assert.Equal(AllMarks, Marks(SavedCondition()));

        Edit(rule, root => Row(root, 0, _ => null));
        Assert.Equal(AllMarks.Except(["a"]), Marks(SavedCondition()));

        Edit(rule, root => Group(root, 1, none => Row(none, 0, _ => null)));
        Assert.Equal(["and", "or", "b", "c", "not", "e"], Marks(SavedCondition()));
    }

    [Fact]
    public void A_lone_condition_keeps_what_a_newer_preferis_added_when_rows_come_and_go()
    {
        var rule = OpenRule("""{ "type": "host", "match": "equals", "pattern": "a.example.com", "futureSetting": "a" }""", """{ "type": "open" }""").Rules.Rules[0];

        Edit(rule, root => root with { Children = [.. root.Children, NewRow] });
        Assert.Equal(["a"], Marks(Assert.IsType<AndCondition>(SavedCondition())));

        Edit(rule, root => Row(root, 1, _ => null));
        Assert.Equal(["a"], Marks(Assert.IsType<HostCondition>(SavedCondition())));
    }

    [Fact]
    public void A_condition_turned_into_another_type_loses_what_a_newer_preferis_added_to_it()
    {
        var rule = OpenRule(Tree, """{ "type": "open" }""").Rules.Rules[0];

        // Host to URL: the property was written for a host condition and may mean nothing, or something else, on a URL condition.
        Edit(rule, root => Group(root, 1, any => Row(any, 1, c => ((ComparisonPredicate)c) with { Field = PredicateField.Url })));
        Assert.Equal(AllMarks.Except(["c"]), Marks(SavedCondition()));

        // Any to all: the group's own property goes, its rows keep theirs.
        Edit(rule, root => Group(root, 1, any => any with { Kind = CompoundKind.All }));
        Assert.Equal(AllMarks.Except(["c", "or"]), Marks(SavedCondition()));

        Edit(rule, root => Group(root, 2, none => none with { Kind = CompoundKind.Any }));
        Assert.Equal(["and", "a", "b", "d", "e"], Marks(SavedCondition()));
    }

    [Theory]
    [InlineData("""{ "type": "host", "match": "equals", "pattern": "a", "futureSetting": 1 }""")]
    [InlineData("""{ "type": "url", "match": "startsWith", "pattern": "https://a/", "ignoreCase": false, "futureSetting": 1 }""")]
    [InlineData("""{ "type": "or", "futureSetting": 1, "conditions": [ { "type": "host", "match": "equals", "pattern": "a", "futureSetting": 2 }, { "type": "host", "match": "equals", "pattern": "a", "futureSetting": 3 } ] }""")]
    [InlineData("""{ "type": "not", "futureSetting": 1, "condition": { "type": "host", "match": "regex", "pattern": "a", "futureSetting": 2 } }""")]
    [InlineData("""{ "type": "not", "futureSetting": 1, "condition": { "type": "not", "futureSetting": 2, "condition": { "type": "host", "match": "equals", "pattern": "a", "futureSetting": 3 } } }""")]
    [InlineData("""{ "type": "not", "futureSetting": 1, "condition": { "type": "and", "futureSetting": 2, "conditions": [ { "type": "host", "match": "equals", "pattern": "a", "futureSetting": 3 }, { "type": "host", "match": "equals", "pattern": "b", "futureSetting": 4 } ] } }""")]
    [InlineData(Tree)]
    public void Every_condition_tree_keeps_what_a_newer_preferis_added_through_the_editor(string json)
    {
        var condition = OpenRule(json, """{ "type": "open" }""").Config.Rules[0].Condition;

        var edited = ConditionPredicates.FromEditor(ConditionPredicates.ToEditor(condition)!, condition);

        Assert.Equal(Serialize(condition), Serialize(edited));
    }

    // A root of one row is stored as that row (ConditionPredicates), so the "and" goes; what is under it stays.
    [Fact]
    public void A_root_of_one_group_keeps_what_a_newer_preferis_added_under_it()
    {
        var rule = OpenRule("""
            { "type": "and", "futureSetting": "and", "conditions": [ { "type": "or", "futureSetting": "or", "conditions": [
              { "type": "host", "match": "equals", "pattern": "a", "futureSetting": "a" },
              { "type": "host", "match": "equals", "pattern": "b", "futureSetting": "b" } ] } ] }
            """, """{ "type": "open" }""").Rules.Rules[0];

        Edit(rule, root => Group(root, 0, any => Row(any, 1, b => ((ComparisonPredicate)b) with { Pattern = "b2" })));

        Assert.Equal(["or", "a", "b"], Marks(Assert.IsType<OrCondition>(SavedCondition())));
    }

    [Fact]
    public void Conditions_and_actions_of_a_newer_preferis_are_still_written_back_as_they_are()
    {
        const string condition = """{ "bundleId": "com.tinyspeck.slackmacgap", "type": "sourceApp", "nested": { "a": [1, 2] } }""";
        const string action = """{ "type": "teleport", "where": "moon" }""";
        var settings = OpenRule($$"""{ "type": "and", "conditions": [ {{condition}}, { "type": "host", "match": "equals", "pattern": "a" } ] }""", action, """{ "type": "open" }""");

        settings.Rules.Rules[0].Actions.AddCommand.Execute(ActionKind.None);
        settings.Rules.CatchAll.Actions.AddCommand.Execute(ActionKind.None);

        var rule = Saved().GetProperty("rules")[0];
        using var expectedCondition = JsonDocument.Parse(condition);
        using var expectedAction = JsonDocument.Parse(action);
        Assert.True(JsonElement.DeepEquals(expectedCondition.RootElement, rule.GetProperty("condition").GetProperty("conditions")[0]));
        Assert.True(JsonElement.DeepEquals(expectedAction.RootElement, rule.GetProperty("actions")[0]));
    }

    private static string Serialize(Condition? condition)
        => ConfigSerializer.Serialize(new PreferisConfig { Rules = [new Rule { Id = "r", Condition = condition }] });
}
