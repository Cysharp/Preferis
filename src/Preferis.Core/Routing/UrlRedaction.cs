using System.Text;
using System.Text.RegularExpressions;

namespace Preferis.Core.Routing;

/// <summary>
/// Masks the parts of URLs that carry secrets (tokens in the query or the fragment, credentials in the user info).
/// Errors are logged by default, while URLs are to be kept only when the history is opted into (docs/Design.md §8).
/// </summary>
public static partial class UrlRedaction
{
    public const string Mask = "***";

    /// <summary>
    /// Masks the user info, the query and the fragment of one URL. The string need not be a valid URL: what failed to
    /// parse is masked the same way.
    /// </summary>
    public static string Redact(string url)
    {
        var end = url.AsSpan().IndexOfAny('?', '#');
        var result = new StringBuilder(MaskUserInfo(end < 0 ? url : url[..end]));
        var rest = end < 0 ? [] : url.AsSpan(end);
        if (!rest.IsEmpty && rest[0] == '?')
        {
            var fragment = rest.IndexOf('#');
            AppendMasked(result, '?', fragment < 0 ? rest[1..] : rest[1..fragment]);
            rest = fragment < 0 ? [] : rest[fragment..];
        }
        if (!rest.IsEmpty)
        {
            AppendMasked(result, '#', rest[1..]);
        }
        return result.ToString();
    }

    /// <summary>Masks every URL in a message, such as the text of an exception that names the URL it failed on.</summary>
    public static string RedactUrls(string text) => UrlInText().Replace(text, match => Redact(match.Value));

    private static string MaskUserInfo(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return url;
        }

        var start = schemeEnd + 3;
        var authorityLength = url.AsSpan(start).IndexOfAny('/', '\\');
        var authority = authorityLength < 0 ? url.AsSpan(start) : url.AsSpan(start, authorityLength);
        // The last '@': browsers take what follows it for the host, so everything before it may be a credential.
        var at = authority.LastIndexOf('@');
        return at <= 0 ? url : string.Concat(url.AsSpan(0, start), Mask, url.AsSpan(start + at));
    }

    private static void AppendMasked(StringBuilder result, char delimiter, ReadOnlySpan<char> content)
    {
        result.Append(delimiter);
        if (!content.IsEmpty)
        {
            result.Append(Mask);
        }
    }

    // Schemes of two characters or more, so that drive paths (C:\...) are not taken for URLs. A trailing quote or
    // punctuation mark belongs to the sentence around the URL; one inside it (an apostrophe in the query) does not end it.
    [GeneratedRegex("""(?<![A-Za-z0-9+.\-])[A-Za-z][A-Za-z0-9+.\-]+:[^\s"<>]*[^\s"<>'.,:;)]""")]
    private static partial Regex UrlInText();
}
