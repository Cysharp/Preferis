using System.Text.Json;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class ConditionEvaluatorTests
{
    private static bool Matches(Condition condition, string url) => ConditionEvaluator.Matches(condition, RoutingUrl.Parse(url));

    private static HostCondition Host(MatchKind match, string pattern) => new() { Match = match, Pattern = pattern };

    private static UrlCondition Url(MatchKind match, string pattern, bool ignoreCase = true) => new() { Match = match, Pattern = pattern, IgnoreCase = ignoreCase };

    [Theory]
    [InlineData("https://www.example.com/", true)]
    [InlineData("https://a.b.example.com/path", true)]
    [InlineData("https://example.com/", false)]
    [InlineData("https://example.com.evil.net/", false)]
    [InlineData("https://WWW.EXAMPLE.COM/", true)]
    public void Host_wildcard_matches_subdomains_only(string url, bool expected)
    {
        Assert.Equal(expected, Matches(Host(MatchKind.Wildcard, "*.example.com"), url));
    }

    [Fact]
    public void Wildcard_question_mark_matches_exactly_one_character()
    {
        var condition = Host(MatchKind.Wildcard, "app?.example.com");

        Assert.True(Matches(condition, "https://app1.example.com/"));
        Assert.False(Matches(condition, "https://app10.example.com/"));
        Assert.False(Matches(condition, "https://app.example.com/"));
    }

    [Fact]
    public void Wildcard_escapes_regex_metacharacters()
    {
        Assert.True(Matches(Url(MatchKind.Wildcard, "https://example.com/a+b/(c)/*"), "https://example.com/a+b/(c)/d"));
        Assert.False(Matches(Url(MatchKind.Wildcard, "https://example.com/a+b/*"), "https://example.com/aab/"));
    }

    [Fact]
    public void Host_comparisons_ignore_case()
    {
        Assert.True(Matches(Host(MatchKind.Exact, "Login.MicrosoftOnline.com"), "https://login.microsoftonline.com/x"));
        Assert.True(Matches(Host(MatchKind.StartsWith, "WWW.EXAMPLE"), "https://www.example.com/"));
        Assert.True(Matches(Host(MatchKind.EndsWith, ".Example.com"), "https://www.example.com/"));
        Assert.True(Matches(Host(MatchKind.Contains, "EXAMPLE"), "https://www.example.com/"));
        Assert.False(Matches(Host(MatchKind.Contains, "sample"), "https://www.example.org/"));
    }

    [Fact]
    public void Host_regex_matches_the_documented_example()
    {
        var condition = Host(MatchKind.Regex, @"^[^.]+\.example\.(org|com)$");

        Assert.True(Matches(condition, "https://www.example.com/"));
        Assert.True(Matches(condition, "https://api.example.org/"));
        Assert.False(Matches(condition, "https://a.b.example.com/"));
        Assert.False(Matches(condition, "https://example.com/"));
    }

    [Fact]
    public void Host_matches_either_unicode_or_punycode_form()
    {
        Assert.True(Matches(Host(MatchKind.Exact, "日本語.jp"), "https://日本語.jp/"));
        Assert.True(Matches(Host(MatchKind.Exact, "xn--wgv71a119e.jp"), "https://日本語.jp/"));
        Assert.True(Matches(Host(MatchKind.Exact, "日本語.jp"), "https://xn--wgv71a119e.jp/"));
    }

    [Fact]
    public void Url_starts_with_is_case_insensitive_by_default()
    {
        Assert.True(Matches(Url(MatchKind.StartsWith, "https://admin.google.com/"), "https://admin.google.com/ac/users"));
        Assert.True(Matches(Url(MatchKind.StartsWith, "https://example.com/admin/"), "https://example.com/Admin/x"));
        Assert.False(Matches(Url(MatchKind.StartsWith, "https://example.com/admin/", ignoreCase: false), "https://example.com/Admin/x"));
    }

    [Fact]
    public void Url_wildcard_matches_the_documented_example()
    {
        var condition = Url(MatchKind.Wildcard, "https://example.com/signin/*");

        Assert.True(Matches(condition, "https://example.com/signin/foo?x=1"));
        Assert.False(Matches(condition, "https://example.com/signup/"));
    }

    [Fact]
    public void Url_regex_matches_the_documented_example()
    {
        var condition = Url(MatchKind.Regex, @"^https?://[^.]+\.example\.(org|com)/");

        Assert.True(Matches(condition, "http://www.example.com/x"));
        Assert.False(Matches(condition, "ftp://www.example.com/x"));
    }

    [Fact]
    public void Regex_constructs_unsupported_by_the_non_backtracking_engine_still_work()
    {
        // Lookahead is not supported by RegexOptions.NonBacktracking; the evaluator must fall back.
        var condition = Url(MatchKind.Regex, @"^(?=https://)\S+\.example\.com/");

        Assert.True(Matches(condition, "https://www.example.com/"));
        Assert.False(Matches(condition, "http://www.example.com/"));
    }

    [Fact]
    public void Invalid_regex_never_matches_and_never_throws()
    {
        Assert.False(Matches(Host(MatchKind.Regex, "("), "https://example.com/"));
        Assert.False(Matches(Url(MatchKind.Regex, "[unclosed"), "https://example.com/"));
    }

    [Fact]
    public void Groups_compose()
    {
        var and = new AndCondition { Conditions = [Host(MatchKind.EndsWith, ".example.com"), Url(MatchKind.Contains, "/admin/")] };
        var or = new OrCondition { Conditions = [Host(MatchKind.Exact, "app1.example.com"), Host(MatchKind.Exact, "app2.example.com")] };
        var not = new NotCondition { Condition = Host(MatchKind.Exact, "www.example.com") };

        Assert.True(Matches(and, "https://www.example.com/admin/users"));
        Assert.False(Matches(and, "https://www.example.com/profile"));
        Assert.True(Matches(or, "https://app2.example.com/"));
        Assert.False(Matches(or, "https://app3.example.com/"));
        Assert.True(Matches(not, "https://api.example.com/"));
        Assert.False(Matches(not, "https://www.example.com/"));
    }

    [Fact]
    public void Empty_groups_follow_boolean_identities()
    {
        Assert.True(Matches(new AndCondition(), "https://example.com/"));
        Assert.False(Matches(new OrCondition(), "https://example.com/"));
        Assert.False(Matches(new NotCondition(), "https://example.com/"));
    }

    [Fact]
    public void Unknown_conditions_never_match()
    {
        using var document = JsonDocument.Parse("""{ "type": "geo", "country": "JP" }""");
        var unknown = new UnknownCondition("geo", document.RootElement.Clone());

        Assert.False(Matches(unknown, "https://example.com/"));
    }
}
