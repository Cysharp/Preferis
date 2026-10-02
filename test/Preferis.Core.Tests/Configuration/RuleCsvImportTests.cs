using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Configuration;

public class RuleCsvImportTests
{
    private const string Sample = """
        マッチURLまたはドメイン,プロファイル名
        https://www.example.com,Personal
        https://*.slack.com,Work
        *.microsoftonline.com,Work
        https://github.com/dotnet/*,Work
        https://github.com/mayuki/*,Personal
        """;

    private static PreferisConfig Config() => new()
    {
        Profiles =
        [
            new Profile { Id = "profile-1", Name = "Personal", Browser = new BrowserReference("edge") },
            new Profile { Id = "profile-2", Name = "Work", Browser = new BrowserReference("chrome") },
        ],
    };

    private static (Type Type, string Pattern) Describe(Condition condition) => condition switch
    {
        PatternCondition pattern when pattern.Match == MatchKind.Wildcard => (condition.GetType(), pattern.Pattern),
        _ => throw new InvalidOperationException($"Not a wildcard condition: {condition.Type}"),
    };

    private static List<(Type, string)> Patterns(Rule rule) => rule.Condition is OrCondition or
        ? or.Conditions.Select(Describe).ToList()
        : [Describe(rule.Condition!)];

    [Fact]
    public void Each_profile_gets_one_rule_with_its_lines_in_order()
    {
        var result = RuleCsvImport.Import(Config(), Sample);

        Assert.Equal(5, result.ImportedLines);
        Assert.Empty(result.Skipped);
        Assert.Equal(["Imported Rule for Personal", "Imported Rule for Work"], result.Rules.Select(r => r.Name));
        Assert.Equal(["imported-rule-for-personal", "imported-rule-for-work"], result.Rules.Select(r => r.Id));
        Assert.All(result.Rules, r => Assert.True(r.Enabled));
        Assert.Equal(["profile-1"], Assert.IsType<OpenAction>(Assert.Single(result.Rules[0].Actions)).Profiles);
        Assert.Equal(["profile-2"], Assert.IsType<OpenAction>(Assert.Single(result.Rules[1].Actions)).Profiles);
        Assert.Equal(
            [(typeof(UrlCondition), "https://www.example.com/*"), (typeof(UrlCondition), "https://github.com/mayuki/*")],
            Patterns(result.Rules[0]));
        Assert.Equal(
            [(typeof(UrlCondition), "https://*.slack.com/*"), (typeof(HostCondition), "*.microsoftonline.com"), (typeof(UrlCondition), "https://github.com/dotnet/*")],
            Patterns(result.Rules[1]));
    }

    [Theory]
    [InlineData("https://www.example.com/", "profile-1")]
    [InlineData("https://www.example.com/path?q=1", "profile-1")]
    [InlineData("https://app.slack.com/client", "profile-2")]
    [InlineData("https://login.microsoftonline.com/common", "profile-2")]
    [InlineData("https://github.com/dotnet/runtime", "profile-2")]
    [InlineData("https://github.com/mayuki/Preferis", "profile-1")]
    [InlineData("https://slack.com/", null)]
    [InlineData("http://www.example.com/", null)]
    public void Imported_rules_route_as_the_lines_read(string url, string? profileId)
    {
        var config = Config();
        config.Rules.AddRange(RuleCsvImport.Import(config, Sample).Rules);

        var decision = new Router(config).Route(RoutingUrl.Parse(url)).Decision;

        // Without a matching rule the catch-all opens the default profile (none here: every profile).
        var open = Assert.IsType<RoutingDecision.Open>(decision);
        Assert.Equal(profileId is null ? ["profile-1", "profile-2"] : new[] { profileId }, open.ProfileIds);
    }

    [Fact]
    public void The_configuration_is_not_changed()
    {
        var config = Config();

        RuleCsvImport.Import(config, Sample);

        Assert.Empty(config.Rules);
    }

    [Fact]
    public void A_single_line_is_a_plain_condition_and_a_page_url_stays_a_page()
    {
        var rule = Assert.Single(RuleCsvImport.Import(Config(), "https://www.example.com/,Work").Rules);

        var condition = Assert.IsType<UrlCondition>(rule.Condition);
        Assert.Equal(MatchKind.Wildcard, condition.Match);
        Assert.Equal("https://www.example.com/", condition.Pattern);
    }

    [Fact]
    public void Lines_that_cannot_be_imported_are_skipped_with_their_line_number()
    {
        const string Csv = """
            URL,Profile
            https://a.example.com,Nobody
            example.com/path,Work
            ,Work
            only-one-column
            example.com:8080,Work
            https://b.example.com,Work
            https://B.example.com,work
            """;

        var result = RuleCsvImport.Import(Config(), Csv);

        Assert.Equal(1, result.ImportedLines);
        Assert.Equal([2, 3, 4, 5, 6, 8], result.Skipped.Select(s => s.Line));
        Assert.Contains("“Nobody”", result.Skipped[0].Reason, StringComparison.Ordinal);
        Assert.Contains("“example.com/path”", result.Skipped[1].Reason, StringComparison.Ordinal);
        Assert.Contains("already", result.Skipped[5].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_first_line_that_can_be_imported_is_not_a_header()
    {
        var result = RuleCsvImport.Import(Config(), "a.example.com,Work\nb.example.com,Work");

        Assert.Equal(2, result.ImportedLines);
    }

    [Fact]
    public void Quotes_a_byte_order_mark_spaces_and_empty_lines_are_handled()
    {
        const string Csv = "﻿\"https://x.example.com/a,b\",\"Work\"\r\n\r\n  *.example.org  , work \r\n\"https://y.example.com/\"\"q\"\"\",Work\r\n";

        var result = RuleCsvImport.Import(Config(), Csv);

        Assert.Empty(result.Skipped);
        Assert.Equal(
            [(typeof(UrlCondition), "https://x.example.com/a,b"), (typeof(HostCondition), "*.example.org"), (typeof(UrlCondition), "https://y.example.com/\"q\"")],
            Patterns(Assert.Single(result.Rules)));
    }

    [Fact]
    public void A_quoted_field_may_span_lines_and_later_line_numbers_follow_the_file()
    {
        var result = RuleCsvImport.Import(Config(), "\"a.example.com\",\"Wo\nrk\"\nb.example.com,Nobody");

        Assert.Equal(3, Assert.Single(result.Skipped).Line);
    }

    [Fact]
    public void Imported_rules_do_not_take_the_names_or_ids_of_existing_rules()
    {
        var config = Config();
        config.Rules.Add(new Rule { Id = "imported-rule-for-work", Name = "Imported Rule for Work", Actions = [new OpenAction()] });

        var rule = Assert.Single(RuleCsvImport.Import(config, "a.example.com,Work").Rules);

        Assert.Equal("Imported Rule for Work 2", rule.Name);
        Assert.Equal("imported-rule-for-work-2", rule.Id);
    }

    [Fact]
    public void Nothing_to_import_gives_no_rules()
    {
        var result = RuleCsvImport.Import(Config(), "");

        Assert.Empty(result.Rules);
        Assert.Equal(0, result.ImportedLines);
        Assert.Empty(result.Skipped);
    }
}
