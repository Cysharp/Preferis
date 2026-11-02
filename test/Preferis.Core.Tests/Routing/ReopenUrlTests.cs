using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class ReopenUrlTests
{
    private static string Unwrap(string input)
    {
        Assert.True(ReopenUrl.TryUnwrap(input, out var url, out var problem), problem);
        return url.AbsoluteUri;
    }

    private static string Problem(string input)
    {
        Assert.False(ReopenUrl.TryUnwrap(input, out var url, out var problem));
        Assert.Null(url);
        return problem;
    }

    [Theory]
    [InlineData("preferis://open?url=https%3A%2F%2Fexample.com%2F")]
    [InlineData("preferis:https://example.com/")]
    [InlineData("PREFERIS://open?url=https%3A%2F%2Fexample.com%2F")]
    [InlineData("Preferis:https://example.com/")]
    public void Preferis_urls_are_told_apart_from_the_urls_they_carry(string input)
    {
        Assert.True(ReopenUrl.IsReopenUrl(input));
    }

    [Theory]
    [InlineData("https://example.com/?next=preferis://open")]
    [InlineData("preferisx:https://example.com/")]
    [InlineData("preferis")]
    [InlineData("/tmp/preferis:page.html")]
    public void Other_input_is_not_a_preferis_url(string input)
    {
        Assert.False(ReopenUrl.IsReopenUrl(input));
    }

    [Fact]
    public void The_open_form_carries_a_percent_encoded_url()
    {
        // As encodeURIComponent writes it: the query, the ampersand and the fragment belong to the URL carried.
        Assert.Equal(
            "https://example.com/path?q=1&r=2#top",
            Unwrap("preferis://open?url=https%3A%2F%2Fexample.com%2Fpath%3Fq%3D1%26r%3D2%23top"));
    }

    [Theory]
    [InlineData("PREFERIS://OPEN?url=https%3A%2F%2Fexample.com%2F")]
    [InlineData("preferis://open/?url=https%3A%2F%2Fexample.com%2F")]
    [InlineData(" preferis://open?url=https%3A%2F%2Fexample.com%2F ")]
    public void The_open_form_is_read_leniently_where_nothing_is_lost(string input)
    {
        Assert.Equal("https://example.com/", Unwrap(input));
    }

    [Fact]
    public void A_plus_sign_stays_a_plus_sign()
    {
        // Not form encoding: encodeURIComponent writes a space as %20, so a + is one the URL itself has.
        Assert.Equal("https://example.com/?q=a+b", Unwrap("preferis://open?url=https%3A%2F%2Fexample.com%2F%3Fq%3Da+b"));
    }

    [Fact]
    public void The_short_form_carries_the_url_as_it_is()
    {
        Assert.Equal("https://example.com/path?q=1&r=%26#top", Unwrap("preferis:https://example.com/path?q=1&r=%26#top"));
    }

    [Fact]
    public void The_url_carried_is_normalized_as_any_other()
    {
        Assert.Equal("http://example.com/", Unwrap("preferis:HTTP://Example.COM:80/"));
    }

    [Theory]
    [InlineData("preferis://open")]
    [InlineData("preferis://open?url=")]
    [InlineData("preferis://open?url")]
    [InlineData("preferis:")]
    [InlineData("preferis:  ")]
    public void A_preferis_url_must_carry_a_url(string input)
    {
        Assert.Contains("does not say which URL to open", Problem(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preferis://open?url=file%3A%2F%2F%2FC%3A%2FWindows%2Fnotepad.exe")]
    [InlineData("preferis:file:///Applications/Calculator.app")]
    [InlineData("preferis:/etc/hosts")]
    [InlineData("preferis:javascript:alert(1)")]
    [InlineData("preferis://open?url=mailto%3Ame%40example.com")]
    [InlineData("preferis:preferis:https://example.com/")]
    [InlineData("preferis://open?url=preferis%3A%2F%2Fopen%3Furl%3Dhttps%253A%252F%252Fexample.com%252F")]
    public void Only_web_addresses_are_carried(string input)
    {
        // A web page can open preferis: URLs; a file or another scheme would reach the system default handler.
        Assert.Contains("only http and https", Problem(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preferis:example.com")]
    [InlineData("preferis:https://")]
    [InlineData("preferis://open?url=%ZZ")]
    public void What_is_not_a_url_is_rejected(string input)
    {
        Assert.Contains("only http and https", Problem(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preferis://open?url=https://example.com/?a=1&b=2")]
    [InlineData("preferis://open?url=https://example.com/#top")]
    [InlineData("preferis://open?url=https%3A%2F%2Fexample.com%2F&url=https%3A%2F%2Fexample.org%2F")]
    [InlineData("preferis://open?from=bookmarklet&url=https%3A%2F%2Fexample.com%2F")]
    public void A_url_that_was_not_encoded_is_not_opened_cut_short(string input)
    {
        // Without encodeURIComponent the URL ends at its first & or #, and the rest would be lost without a word.
        Assert.Contains("encodeURIComponent", Problem(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preferis://pick?url=https%3A%2F%2Fexample.com%2F")]
    [InlineData("preferis://https://example.com/")]
    [InlineData("preferis://?url=https%3A%2F%2Fexample.com%2F")]
    public void Only_the_open_action_is_known(string input)
    {
        Assert.Contains("preferis://open?url=", Problem(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preferis:file:///tmp/a?token=secret")]
    [InlineData("preferis://me:secret@example.com/?url=https%3A%2F%2Fexample.com%2F")]
    public void The_problem_does_not_repeat_the_input(string input)
    {
        // The problem is shown and logged: a token in the input must not reach the log through it.
        Assert.DoesNotContain("secret", Problem(input), StringComparison.Ordinal);
    }
}
