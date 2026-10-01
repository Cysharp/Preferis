using System.Text.RegularExpressions;
using Preferis.Core.Configuration;

namespace Preferis.Core.Routing;

/// <summary>Applies a <see cref="RewriteAction"/> to a URL.</summary>
public static class UrlRewriter
{
    private static readonly TimeSpan BacktrackingTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Returns the rewritten URL, or an error message when the action is misconfigured or produced an invalid URL.</summary>
    public static (RoutingUrl? Url, string? Error) Rewrite(RewriteAction action, RoutingUrl url) => action.Mode switch
    {
        RewriteMode.Regex => RewriteWithRegex(action, url),
        RewriteMode.QueryParameter => RewriteFromQueryParameter(action, url),
        _ => (null, $"Unsupported rewrite mode: {action.Mode}"),
    };

    private static (RoutingUrl?, string?) RewriteWithRegex(RewriteAction action, RoutingUrl url)
    {
        if (string.IsNullOrEmpty(action.Pattern))
        {
            return (null, "Regex rewrite has no pattern.");
        }

        Regex regex;
        try
        {
            try
            {
                regex = new Regex(action.Pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            }
            catch (NotSupportedException)
            {
                regex = new Regex(action.Pattern, RegexOptions.CultureInvariant, BacktrackingTimeout);
            }
        }
        catch (ArgumentException ex)
        {
            return (null, $"Invalid rewrite pattern: {ex.Message}");
        }

        string rewritten;
        try
        {
            rewritten = regex.Replace(url.AbsoluteUri, action.Replacement ?? "");
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, "Rewrite pattern timed out.");
        }

        return Finish(rewritten);
    }

    private static (RoutingUrl?, string?) RewriteFromQueryParameter(RewriteAction action, RoutingUrl url)
    {
        if (string.IsNullOrEmpty(action.Parameter))
        {
            return (null, "Query parameter rewrite has no parameter name.");
        }

        var query = url.Uri.Query;
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        // Plain '+' is kept as is: values that embed URLs are percent-encoded, and a literal '+' would be lost otherwise.
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (!string.Equals(name, action.Parameter, StringComparison.Ordinal))
            {
                continue;
            }

            var value = separator < 0 ? "" : Uri.UnescapeDataString(pair[(separator + 1)..]);
            return Finish(value);
        }

        return (null, $"Query parameter '{action.Parameter}' not found.");
    }

    private static (RoutingUrl?, string?) Finish(string candidate)
        => RoutingUrl.TryParse(candidate, out var url) ? (url, null) : (null, $"Rewrite produced an invalid URL: '{candidate}'");
}
