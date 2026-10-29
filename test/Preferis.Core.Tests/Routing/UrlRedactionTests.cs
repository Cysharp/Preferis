using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class UrlRedactionTests
{
    [Theory]
    [InlineData("https://example.com/login?token=abc", "https://example.com/login?***")]
    [InlineData("https://example.com/callback#access_token=abc", "https://example.com/callback#***")]
    [InlineData("https://user:secret@example.com/", "https://***@example.com/")]
    [InlineData("https://user:secret@example.com:8443/a?b=c#d", "https://***@example.com:8443/a?***#***")]
    public void Masks_the_user_info_the_query_and_the_fragment(string url, string expected)
    {
        Assert.Equal(expected, UrlRedaction.Redact(url));
    }

    [Theory]
    [InlineData("https://example.com/path/to/page")]
    [InlineData("https://example.com/p@th")]
    [InlineData("https://example.com/?")]
    [InlineData("https://example.com/#")]
    [InlineData("about:blank")]
    public void Leaves_urls_without_those_parts_as_they_are(string url)
    {
        Assert.Equal(url, UrlRedaction.Redact(url));
    }

    [Fact]
    public void A_question_mark_in_the_fragment_does_not_start_a_query()
    {
        Assert.Equal("https://example.com/#***", UrlRedaction.Redact("https://example.com/#state?token=abc"));
    }

    [Theory]
    [InlineData("https://exa mple.com/?token=abc", "https://exa mple.com/?***")]
    [InlineData("example.com/login?token=abc#frag", "example.com/login?***#***")]
    [InlineData("ht!tp://user:secret@example.com/x", "ht!tp://***@example.com/x")]
    public void Strings_that_are_not_valid_urls_are_masked_too(string input, string expected)
    {
        Assert.Equal(expected, UrlRedaction.Redact(input));
    }

    [Fact]
    public void Urls_in_a_message_are_masked_and_the_quotes_around_them_kept()
    {
        var message = "An error occurred trying to start process 'https://example.com/?token=abc' with working directory 'C:\\Users\\me'.";

        Assert.Equal(
            "An error occurred trying to start process 'https://example.com/?***' with working directory 'C:\\Users\\me'.",
            UrlRedaction.RedactUrls(message));
    }

    [Fact]
    public void Every_url_in_a_message_is_masked()
    {
        var message = "Unhandled failure while routing https://user:secret@a.example/?x=1: Reroute loop detected at 'file:///tmp/page.html#top'.";

        Assert.Equal(
            "Unhandled failure while routing https://***@a.example/?***: Reroute loop detected at 'file:///tmp/page.html#***'.",
            UrlRedaction.RedactUrls(message));
    }

    [Fact]
    public void An_apostrophe_inside_a_quoted_url_is_masked_with_the_rest_of_the_query()
    {
        Assert.Equal("open 'https://example.com/?***' now", UrlRedaction.RedactUrls("open 'https://example.com/?q=it's' now"));
    }

    [Fact]
    public void Text_without_urls_is_left_as_it_is()
    {
        var message = "System.IO.IOException: Access denied?\n   at Preferis.Router.FileLog.Write() in C:\\src\\FileLog.cs:line 46 (user@host)";

        Assert.Equal(message, UrlRedaction.RedactUrls(message));
    }
}
