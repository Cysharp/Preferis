using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class RoutingUrlTests
{
    [Fact]
    public void Normalizes_scheme_host_and_default_port()
    {
        var url = RoutingUrl.Parse("HTTPS://Example.COM:443/Admin/?q=1");

        Assert.Equal("example.com", url.Host);
        Assert.Equal("https://example.com/Admin/?q=1", url.AbsoluteUri);
    }

    [Fact]
    public void Exposes_both_unicode_and_punycode_host_forms()
    {
        var url = RoutingUrl.Parse("https://日本語.jp/");

        Assert.Equal("日本語.jp", url.Host);
        Assert.Equal("xn--wgv71a119e.jp", url.IdnHost);
    }

    [Fact]
    public void Punycode_input_also_exposes_the_unicode_form()
    {
        var url = RoutingUrl.Parse("https://xn--wgv71a119e.jp/");

        Assert.Equal("xn--wgv71a119e.jp", url.IdnHost);
        Assert.Equal("日本語.jp", url.Host);
    }

    [Fact]
    public void A_rooted_file_path_becomes_a_file_url()
    {
        var url = RoutingUrl.Parse("/tmp/page.html");

        Assert.Equal("file", url.Uri.Scheme);
        Assert.EndsWith("/tmp/page.html", url.AbsoluteUri);
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.False(RoutingUrl.TryParse("not a url at all", out var url));
        Assert.Null(url);
        Assert.Throws<FormatException>(() => RoutingUrl.Parse("not a url at all"));
    }

    [Fact]
    public void Host_is_empty_for_urls_without_authority()
    {
        var url = RoutingUrl.Parse("about:blank");

        Assert.Equal("", url.Host);
        Assert.Equal("about:blank", url.AbsoluteUri);
    }

    [Fact]
    public void Mailto_exposes_the_mail_domain_as_host()
    {
        // .NET parses the part after '@' of a mailto URL as the host; conditions can rely on that.
        var url = RoutingUrl.Parse("mailto:someone@example.com");

        Assert.Equal("example.com", url.Host);
    }
}
