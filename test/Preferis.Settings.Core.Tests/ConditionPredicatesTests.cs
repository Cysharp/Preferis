using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class ConditionPredicatesTests
{
    private static HostCondition Host(string pattern, MatchKind match = MatchKind.Exact) => new() { Match = match, Pattern = pattern };

    private static UrlCondition Url(string pattern, MatchKind match = MatchKind.StartsWith, bool ignoreCase = true) => new() { Match = match, Pattern = pattern, IgnoreCase = ignoreCase };

    private static Condition? RoundTrip(Condition? condition) => ConditionPredicates.FromEditor(ConditionPredicates.ToEditor(condition)!);

    private static string Json(Condition? condition)
        => condition is null ? "null" : ConfigSerializer.Serialize(new PreferisConfig { Rules = [new Rule { Id = "r", Condition = condition }] });

    [Fact]
    public void A_single_leaf_is_shown_under_an_all_row()
    {
        var root = ConditionPredicates.ToEditor(Host("login.example.com"))!;

        Assert.Equal(CompoundKind.All, root.Kind);
        var row = Assert.IsType<ComparisonPredicate>(Assert.Single(root.Children));
        Assert.Equal(new ComparisonPredicate(PredicateField.Host, MatchKind.Exact, "login.example.com"), row);
    }

    [Fact]
    public void Groups_map_to_all_any_and_none()
    {
        var condition = new AndCondition
        {
            Conditions =
            [
                new OrCondition { Conditions = [Host("a.example.com"), Host("b.example.com")] },
                new NotCondition { Condition = new OrCondition { Conditions = [Url("https://x/"), Url("https://y/")] } },
                new NotCondition { Condition = Host("c.example.com", MatchKind.Wildcard) },
            ],
        };

        var root = ConditionPredicates.ToEditor(condition)!;

        Assert.Equal(CompoundKind.All, root.Kind);
        Assert.Equal(CompoundKind.Any, Assert.IsType<CompoundPredicate>(root.Children[0]).Kind);
        var none = Assert.IsType<CompoundPredicate>(root.Children[1]);
        Assert.Equal(CompoundKind.None, none.Kind);
        Assert.Equal(2, none.Children.Count);
        var negatedLeaf = Assert.IsType<CompoundPredicate>(root.Children[2]);
        Assert.Equal(CompoundKind.None, negatedLeaf.Kind);
        Assert.Equal(PredicateField.Host, Assert.IsType<ComparisonPredicate>(Assert.Single(negatedLeaf.Children)).Field);
    }

    [Fact]
    public void Url_case_sensitivity_is_its_own_field()
    {
        var root = ConditionPredicates.ToEditor(new OrCondition { Conditions = [Url("https://A/"), Url("https://B/", ignoreCase: false)] })!;

        Assert.Equal([PredicateField.Url, PredicateField.UrlCaseSensitive], root.Children.Cast<ComparisonPredicate>().Select(c => c.Field));
    }

    [Fact]
    public void App_conditions_are_rows_of_their_own()
    {
        var root = ConditionPredicates.ToEditor(new OrCondition
        {
            Conditions =
            [
                new AppIdCondition { Match = MatchKind.Exact, Pattern = "com.tinyspeck.slackmacgap" },
                new AppPathCondition { Match = MatchKind.EndsWith, Pattern = @"\slack.exe" },
            ],
        })!;

        Assert.Equal(
            [new ComparisonPredicate(PredicateField.AppId, MatchKind.Exact, "com.tinyspeck.slackmacgap"), new ComparisonPredicate(PredicateField.AppPath, MatchKind.EndsWith, @"\slack.exe")],
            root.Children);
    }

    [Fact]
    public void A_row_turned_into_an_app_row_becomes_an_app_condition()
    {
        var root = new CompoundPredicate(CompoundKind.All, [new ComparisonPredicate(PredicateField.AppPath, MatchKind.EndsWith, @"\Code.exe")]);

        var condition = ConditionPredicates.FromEditor(root, previous: Host("example.com"));

        var path = Assert.IsType<AppPathCondition>(condition);
        Assert.Equal(MatchKind.EndsWith, path.Match);
        Assert.Equal(@"\Code.exe", path.Pattern);
    }

    [Theory]
    [MemberData(nameof(Trees))]
    public void Every_condition_tree_survives_the_editor(string name, Condition condition)
    {
        Assert.NotNull(name);
        Assert.Equal(Json(condition), Json(RoundTrip(condition)));
    }

    public static TheoryData<string, Condition> Trees() => new()
    {
        { "leaf", Host("login.example.com") },
        { "url leaf, case sensitive", Url("https://admin.example.com/", ignoreCase: false) },
        { "and", new AndCondition { Conditions = [Host("a"), Url("https://b/")] } },
        { "or", new OrCondition { Conditions = [Host("a"), Host("b")] } },
        { "not leaf", new NotCondition { Condition = Host("a", MatchKind.Regex) } },
        { "not or", new NotCondition { Condition = new OrCondition { Conditions = [Host("a"), Host("b")] } } },
        { "not and", new NotCondition { Condition = new AndCondition { Conditions = [Host("a"), Host("b")] } } },
        { "not not", new NotCondition { Condition = new NotCondition { Condition = Host("a") } } },
        { "nested", new AndCondition { Conditions = [new OrCondition { Conditions = [Host("a", MatchKind.EndsWith), Host("b", MatchKind.Contains)] }, Url("https://c/*", MatchKind.Wildcard)] } },
        { "app id leaf", new AppIdCondition { Match = MatchKind.Exact, Pattern = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" } },
        { "apps and hosts", new AndCondition { Conditions = [new NotCondition { Condition = new AppPathCondition { Match = MatchKind.Wildcard, Pattern = @"*\Teams\*" } }, Host("a")] } },
    };

    [Fact]
    public void An_empty_editor_means_no_condition()
    {
        Assert.Null(ConditionPredicates.FromEditor(new CompoundPredicate(CompoundKind.All, [])));
        Assert.Empty(ConditionPredicates.ToEditor(null)!.Children);
    }

    [Fact]
    public void A_root_with_one_row_is_stored_as_that_row()
    {
        var condition = ConditionPredicates.FromEditor(new CompoundPredicate(CompoundKind.Any, [new ComparisonPredicate(PredicateField.Url, MatchKind.Contains, "/admin/")]));

        var url = Assert.IsType<UrlCondition>(condition);
        Assert.Equal(MatchKind.Contains, url.Match);
        Assert.True(url.IgnoreCase);
    }

    [Fact]
    public void Nested_groups_keep_their_shape_even_with_one_row()
    {
        var condition = ConditionPredicates.FromEditor(new CompoundPredicate(CompoundKind.All,
        [
            new ComparisonPredicate(PredicateField.Host, MatchKind.Exact, "a"),
            new CompoundPredicate(CompoundKind.Any, [new ComparisonPredicate(PredicateField.Host, MatchKind.Exact, "b")]),
        ]));

        var and = Assert.IsType<AndCondition>(condition);
        Assert.IsType<OrCondition>(and.Conditions[1]);
    }

    [Fact]
    public void Conditions_from_a_newer_preferis_cannot_be_edited_this_way()
    {
        using var raw = System.Text.Json.JsonDocument.Parse("""{ "type": "sourceApp" }""");
        var condition = new AndCondition { Conditions = [Host("a"), new UnknownCondition("sourceApp", raw.RootElement.Clone())] };

        Assert.Null(ConditionPredicates.ToEditor(condition));
    }

    [Fact]
    public void A_rule_takes_a_condition_from_the_editor()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        var rule = settings.Rules.Rules.Single(r => r.Id == "google-admin");

        rule.SetCondition(new OrCondition { Conditions = [Host("admin.google.com"), Host("accounts.google.com")] });

        Assert.IsType<OrCondition>(settings.Config.Rules[1].Condition);
        Assert.Equal("any of 2 conditions → open Chrome (Work)", rule.Summary);
        Assert.True(settings.IsDirty);
    }
}
