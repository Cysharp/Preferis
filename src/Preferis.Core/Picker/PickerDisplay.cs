using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Picker;

/// <summary>The URL split so that the host can be emphasized (docs/Design.md §9.2). Concatenating the parts gives the displayed URL.</summary>
public sealed record UrlDisplayParts(string BeforeHost, string Host, string AfterHost);

/// <summary>Text shown by the picker UIs, shared so that every OS presents candidates the same way.</summary>
public static class PickerDisplay
{
    public static UrlDisplayParts SplitUrl(string url)
    {
        if (!RoutingUrl.TryParse(url, out var parsed) || parsed.Uri.IsFile || parsed.Host.Length == 0)
        {
            return parsed is not null && parsed.Uri.IsFile
                ? new UrlDisplayParts(parsed.Uri.Scheme + Uri.SchemeDelimiter, "", parsed.Uri.AbsolutePath + parsed.Uri.Query + parsed.Uri.Fragment)
                : new UrlDisplayParts(url, "", "");
        }

        var uri = parsed.Uri;
        var before = uri.Scheme + Uri.SchemeDelimiter + (uri.UserInfo.Length > 0 ? uri.UserInfo + "@" : "");
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // The Unicode host is what people recognise; the punycode form would hide the name.
        return new UrlDisplayParts(before, parsed.Host, port + uri.PathAndQuery + uri.Fragment);
    }

    /// <summary>Second line under a candidate's name: browser profile (name and account) and private mode.</summary>
    public static string DetailLine(PickerCandidate candidate)
    {
        var parts = new List<string>();
        if (candidate.BrowserProfile is { } profile)
        {
            if (!string.IsNullOrEmpty(profile.Name))
            {
                parts.Add(profile.Name);
            }
            if (!string.IsNullOrEmpty(profile.Email) && !string.Equals(profile.Email, profile.Name, StringComparison.Ordinal))
            {
                parts.Add(profile.Email);
            }
            if (parts.Count == 0 && !string.IsNullOrEmpty(profile.Directory))
            {
                parts.Add(profile.Directory);
            }
        }
        if (candidate.Private)
        {
            parts.Add(L("Private"));
        }
        return string.Join(" · ", parts);
    }
}
