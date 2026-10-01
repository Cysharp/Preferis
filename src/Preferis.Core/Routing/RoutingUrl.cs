using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Preferis.Core.Routing;

/// <summary>
/// The normalized view of an incoming URL that conditions are evaluated against:
/// lower-cased host in both Unicode and punycode form, and the canonical absolute URL.
/// </summary>
public sealed class RoutingUrl
{
    private RoutingUrl(Uri uri)
    {
        Uri = uri;
        AbsoluteUri = uri.AbsoluteUri;
        IdnHost = uri.HostNameType is UriHostNameType.Dns ? uri.IdnHost.ToLowerInvariant() : uri.Host.ToLowerInvariant();
        Host = ToUnicodeHost(IdnHost);
    }

    public Uri Uri { get; }

    /// <summary>Host in Unicode form, lower-cased. Empty for URLs without an authority.</summary>
    public string Host { get; }

    /// <summary>Host in punycode (ASCII) form, lower-cased.</summary>
    public string IdnHost { get; }

    /// <summary>Canonical absolute URL (lower-cased scheme and host, default port removed).</summary>
    public string AbsoluteUri { get; }

    /// <summary>Parses a URL. A rooted file system path is accepted and turned into a <c>file:</c> URL.</summary>
    /// <exception cref="FormatException">The input is neither an absolute URL nor a rooted path.</exception>
    public static RoutingUrl Parse(string input)
        => TryParse(input, out var url) ? url : throw new FormatException($"Not an absolute URL or a rooted file path: '{input}'");

    public static bool TryParse(string input, [NotNullWhen(true)] out RoutingUrl? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && !LooksLikeWindowsDrivePath(input, uri))
        {
            url = new RoutingUrl(uri);
            return true;
        }

        // Shells and file associations hand us paths, not URLs ("C:\page.html", "/tmp/page.html").
        if (Path.IsPathRooted(input) && Uri.TryCreate(Path.GetFullPath(input), UriKind.Absolute, out var fileUri) && fileUri.IsFile)
        {
            url = new RoutingUrl(fileUri);
            return true;
        }

        return false;
    }

    public override string ToString() => AbsoluteUri;

    // "C:\x" parses as a URL with scheme "c" on non-Windows runtimes; treat single-letter schemes as drive paths.
    private static bool LooksLikeWindowsDrivePath(string input, Uri uri)
        => uri.Scheme.Length == 1 && input.Length > 1 && input[1] == ':';

    private static string ToUnicodeHost(string idnHost)
    {
        if (idnHost.Length == 0 || !idnHost.Contains("xn--", StringComparison.Ordinal))
        {
            return idnHost;
        }

        try
        {
            return new IdnMapping().GetUnicode(idnHost).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return idnHost;
        }
    }
}
