using System.Diagnostics.CodeAnalysis;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Routing;

/// <summary>
/// A <c>preferis:</c> URL: hands the web address it carries to Preferis, so that a browser (a bookmarklet, an extension)
/// or any app that can open URLs can have Preferis route a URL again (docs/Design.md §5.5). Two forms:
/// <c>preferis://open?url=&lt;percent-encoded URL&gt;</c>, and <c>preferis:&lt;URL&gt;</c> with the URL as it is.
/// </summary>
public static class ReopenUrl
{
    public const string Scheme = "preferis";

    private const string Prefix = Scheme + ":";
    private const string OpenAction = "open";
    private const string UrlParameter = "url=";

    /// <summary>Whether <paramref name="input"/> is a <c>preferis:</c> URL, well-formed or not.</summary>
    public static bool IsReopenUrl(string input) => input.AsSpan().TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The web address that a <c>preferis:</c> URL carries.</summary>
    /// <param name="problem">Why there is none, for the user; it does not repeat the input, which may hold a token.</param>
    public static bool TryUnwrap(string input, [NotNullWhen(true)] out RoutingUrl? url, [NotNullWhen(false)] out string? problem)
    {
        url = null;
        var rest = input.Trim();
        if (!rest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            problem = L("This version of Preferis knows only preferis://open?url=<URL> and preferis:<URL>.");
            return false;
        }
        rest = rest[Prefix.Length..];

        string carried;
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            if (!TryReadOpen(rest[2..], out carried, out problem))
            {
                return false;
            }
        }
        else
        {
            carried = rest.Trim();
        }

        if (carried.Length == 0)
        {
            problem = L("The preferis: URL does not say which URL to open.");
            return false;
        }

        // A web page can open preferis: URLs. A file, or a URL of another scheme, could reach the system default
        // handler through the rules, which would start a program; and another preferis: URL would loop.
        if (!RoutingUrl.TryParse(carried, out var parsed) || parsed.Uri.Scheme is not ("http" or "https") || parsed.Host.Length == 0)
        {
            problem = L("A preferis: URL can open only http and https URLs.");
            return false;
        }

        url = parsed;
        problem = null;
        return true;
    }

    // "open?url=<percent-encoded URL>", after "preferis://".
    private static bool TryReadOpen(string rest, out string carried, [NotNullWhen(false)] out string? problem)
    {
        carried = "";
        var end = rest.AsSpan().IndexOfAny('/', '?', '#');
        var action = end < 0 ? rest : rest[..end];
        rest = end < 0 ? "" : rest[end..];
        if (!action.Equals(OpenAction, StringComparison.OrdinalIgnoreCase))
        {
            problem = L("This version of Preferis knows only preferis://open?url=<URL> and preferis:<URL>.");
            return false;
        }
        if (rest.StartsWith('/'))
        {
            rest = rest[1..];
        }
        if (rest.Length == 0 || rest == "?" || rest == "?url")
        {
            problem = null;
            return true;
        }

        // An & or a # here is the URL's own when it was not encoded; opening what comes before would lose the rest of it.
        var query = rest.StartsWith('?') ? rest[1..] : null;
        if (query is null || !query.StartsWith(UrlParameter, StringComparison.Ordinal) || query.AsSpan().IndexOfAny('&', '#') >= 0)
        {
            problem = L("The preferis: URL has more than its url parameter. Percent-encode the URL it carries, as encodeURIComponent does.");
            return false;
        }

        // Percent-decoding alone: encodeURIComponent writes a space as %20, so a + is one the URL itself has.
        carried = Uri.UnescapeDataString(query[UrlParameter.Length..]);
        problem = null;
        return true;
    }
}
