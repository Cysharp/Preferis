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
            example.com:invalid,Work
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

    [Theory]
    [InlineData("example.com:8080", "http://example.com:8080/path?q=1", true)]
    [InlineData("example.com:8080", "https://example.com:8080/", true)]
    [InlineData("example.com:8080", "https://example.com:8081/", false)]
    [InlineData("example.com:8080", "https://example.com/", false)]
    [InlineData("example.com:8080", "https://other.example.com:8080/", false)]
    [InlineData("example.com:8080", "https://other.com/example.com:8080/", false)]
    [InlineData("example.com:8080", "https://example.com/path:8080/", false)]
    [InlineData("example.com:8080", "https://user:8080@example.com/", false)]
    [InlineData("example.com:8080", "https://example.com/?url=http://example.com:8080/", false)]
    [InlineData("localhost:3000", "http://localhost:3000/", true)]
    [InlineData("*.example.com:8080", "https://app.example.com:8080/", true)]
    [InlineData("*.example.com:8080", "https://example.com:8080/", false)]
    [InlineData("例え.jp:8080", "https://xn--r8jz45g.jp:8080/", true)]
    [InlineData("[::1]:8080", "http://[::1]:8080/", true)]
    [InlineData("[0:0:0:0:0:0:0:1]:8080", "http://[::1]:8080/", true)]
    [InlineData("example.com:0", "http://example.com:0/", true)]
    [InlineData("example.com:65535", "http://example.com:65535/", true)]
    [InlineData("example.com:08080", "http://example.com:8080/", true)]
    public void Host_and_port_patterns_match_the_actual_host_and_port(string pattern, string url, bool matches)
    {
        AssertImportedMatch(pattern, url, matches);
    }

    [Theory]
    [InlineData("example.com:80", "http://example.com/", true)]
    [InlineData("example.com:80", "http://example.com:80/", true)]
    [InlineData("example.com:80", "http://example.com:8080/", false)]
    [InlineData("example.com:80", "https://example.com/", false)]
    [InlineData("example.com:80", "http://user:pass@example.com/", true)]
    [InlineData("example.com:80", "http://user:pass@example.com:8080/", false)]
    [InlineData("example.com:80", "ws://example.com/", true)]
    [InlineData("example.com:443", "https://example.com/", true)]
    [InlineData("example.com:443", "https://example.com:443/", true)]
    [InlineData("example.com:443", "https://example.com:8443/", false)]
    [InlineData("example.com:443", "http://example.com/", false)]
    [InlineData("example.com:443", "http://example.com:443/", true)]
    [InlineData("example.com:443", "wss://example.com/", true)]
    [InlineData("[::1]:80", "http://[::1]/", true)]
    [InlineData("[::1]:80", "http://[::1]:8080/", false)]
    [InlineData("example.com:21", "ftp://example.com/file", true)]
    [InlineData("example.com:70", "gopher://example.com/", true)]
    [InlineData("example.com:119", "nntp://example.com/", true)]
    [InlineData("example.com:23", "telnet://example.com/", true)]
    [InlineData("example.com:389", "ldap://example.com/", true)]
    [InlineData("example.com:808", "net.tcp://example.com/", true)]
    public void Host_and_port_patterns_account_for_normalized_default_ports(string pattern, string url, bool matches)
    {
        AssertImportedMatch(pattern, url, matches);
    }

    private static void AssertImportedMatch(string pattern, string url, bool matches)
    {
        var config = Config();
        config.DefaultProfile = "profile-1";
        var result = RuleCsvImport.Import(config, pattern + ",Work");
        var rule = Assert.Single(result.Rules);
        Assert.Equal(1, result.ImportedLines);
        Assert.Empty(result.Skipped);
        config.Rules.Add(rule);
        // Imported conditions must retain their behavior in the saved configuration as well.
        config = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(config));

        var decision = Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse(url)).Decision);

        Assert.Equal(matches ? ["profile-2"] : new[] { "profile-1" }, decision.ProfileIds);
        Assert.Equal(matches ? rule.Id : null, decision.RuleId);
    }

    [Theory]
    [InlineData("example.com:")]
    [InlineData(":8080")]
    [InlineData("example.com:65536")]
    [InlineData("example.com:-1")]
    [InlineData("example.com:+80")]
    [InlineData("example.com:abc")]
    [InlineData("example.com:80:80")]
    [InlineData("::1:8080")]
    [InlineData("example.com:80/path")]
    [InlineData("example.com:80?query")]
    [InlineData("example.com:80#fragment")]
    [InlineData("example.com: 80")]
    [InlineData("example .com:80")]
    public void Invalid_host_and_port_rows_are_reported(string pattern)
    {
        var result = RuleCsvImport.Import(Config(), "URL,Profile\n" + pattern + ",Work");

        Assert.Empty(result.Rules);
        Assert.Equal(0, result.ImportedLines);
        Assert.Equal(2, Assert.Single(result.Skipped).Line);
    }

    [Theory]
    [InlineData(@"/^https?://regex\.example\.com/\d+$", "https://regex.example.com/123", true)]
    [InlineData(@"/^https?://regex\.example\.com/\d+$", "https://regex.example.com/abc", false)]
    [InlineData(@"/^https://EXAMPLE\.com/", "https://example.com/path", true)]
    [InlineData(@"/(?i)^https://example\.com/(?-i:ABC)$", "https://example.com/ABC", true)]
    [InlineData(@"/(?i)^https://example\.com/(?-i:ABC)$", "https://example.com/abc", false)]
    [InlineData(@"/example\.com/path/", "https://example.com/path/child", true)]
    [InlineData(@"/example\.com/path/", "https://example.com/pathology", false)]
    public void A_leading_slash_marks_a_url_regular_expression(string pattern, string url, bool matches)
    {
        AssertImportedMatch(pattern, url, matches);
        var rule = Assert.Single(RuleCsvImport.Import(Config(), pattern + ",Work").Rules);
        var condition = Assert.IsType<UrlCondition>(rule.Condition);
        Assert.Equal(MatchKind.Regex, condition.Match);
        Assert.Equal(pattern[1..], condition.Pattern);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/[")]
    [InlineData("/(")]
    public void Invalid_or_empty_regular_expressions_are_reported_without_losing_valid_rows(string pattern)
    {
        var result = RuleCsvImport.Import(Config(), "URL,Profile\n" + pattern + ",Work\nexample.com,Work");

        Assert.Single(result.Rules);
        Assert.Equal(1, result.ImportedLines);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(2, skipped.Line);
        Assert.Contains("regular expression", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_detection_keeps_regex_escapes_and_match_kinds_distinct()
    {
        const string Csv = """
            URL,Profile
            /https://example.com/*,Work
            https://example.com/*,Work
            /\d,Work
            /\D,Work
            /\d,Work
            HTTPS://EXAMPLE.COM/*,Work
            example.com:8080,Work
            EXAMPLE.COM:08080,Work
            """;
        var result = RuleCsvImport.Import(Config(), Csv);

        Assert.Equal(5, result.ImportedLines);
        Assert.Equal([6, 7, 9], result.Skipped.Select(s => s.Line));
        Assert.Equal(5, Assert.IsType<OrCondition>(Assert.Single(result.Rules).Condition).Conditions.Count);
    }

    [Fact]
    public void Rows_are_grouped_by_the_complete_profile_set_in_configuration_order()
    {
        const string Csv = """
            URL,Profile,Profile
            shared.example.com, work , PERSONAL,Work
            work.example.com,Work
            another.example.com,Personal,Work
            personal.example.com,Personal
            SHARED.example.com,Personal,Work
            shared.example.com,Work
            """;
        var config = Config();
        var result = RuleCsvImport.Import(config, Csv);

        Assert.Equal(5, result.ImportedLines);
        Assert.Equal(6, Assert.Single(result.Skipped).Line);
        Assert.Equal(["Imported Rule for Personal, Work", "Imported Rule for Work", "Imported Rule for Personal"], result.Rules.Select(r => r.Name));
        Assert.Equal(["profile-1", "profile-2"], Assert.IsType<OpenAction>(Assert.Single(result.Rules[0].Actions)).Profiles);
        Assert.Equal(["profile-2"], Assert.IsType<OpenAction>(Assert.Single(result.Rules[1].Actions)).Profiles);
        Assert.Equal(["profile-1"], Assert.IsType<OpenAction>(Assert.Single(result.Rules[2].Actions)).Profiles);
        Assert.Equal([(typeof(HostCondition), "shared.example.com"), (typeof(HostCondition), "another.example.com")], Patterns(result.Rules[0]));
        config.Rules.AddRange(result.Rules);

        var decision = Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse("https://shared.example.com/")).Decision);
        Assert.Equal(["profile-1", "profile-2"], decision.ProfileIds);
        Assert.Equal(OpenSource.Explicit, decision.Source);
        Assert.Equal(result.Rules[0].Id, decision.RuleId);
    }

    [Theory]
    [InlineData("Work,Nobody")]
    [InlineData("Nobody,Work")]
    [InlineData("Personal,Work,Nobody")]
    [InlineData("Work,")]
    [InlineData(",Work")]
    [InlineData("Work, ,Personal")]
    public void Every_profile_column_must_resolve_before_anything_is_imported(string names)
    {
        var result = RuleCsvImport.Import(Config(), "URL,Profile\nexample.com," + names);

        Assert.Equal(0, result.ImportedLines);
        Assert.Empty(result.Rules);
        Assert.Equal(2, Assert.Single(result.Skipped).Line);
    }

    [Fact]
    public void Quoted_profile_names_and_regex_commas_are_preserved()
    {
        var config = Config();
        config.Profiles[0].Name = "Personal, Home";
        const string Csv = """
            "/^https://example\.com/[0-9]{1,3}$","Personal, Home",work
            """;

        var rule = Assert.Single(RuleCsvImport.Import(config, Csv).Rules);

        Assert.Equal(["profile-1", "profile-2"], Assert.IsType<OpenAction>(Assert.Single(rule.Actions)).Profiles);
        Assert.True(ConditionEvaluator.Matches(rule.Condition, RoutingUrl.Parse("https://example.com/123")));
        Assert.False(ConditionEvaluator.Matches(rule.Condition, RoutingUrl.Parse("https://example.com/1234")));
    }
}
