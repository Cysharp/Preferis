using System.Text;
using System.Text.RegularExpressions;
using Preferis.Core.Configuration;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Routing;

/// <summary>
/// Compiles one (match kind, pattern, case sensitivity) triple into a predicate over strings.
/// Regular expressions use the non-backtracking engine so that user supplied patterns run in linear time;
/// constructs that engine does not support fall back to the backtracking engine with a timeout.
/// An invalid pattern yields a matcher that never matches instead of throwing at routing time.
/// </summary>
public sealed class PatternMatcher
{
    private static readonly TimeSpan BacktrackingTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Func<string, bool> _predicate;

    private PatternMatcher(Func<string, bool> predicate, string? error)
    {
        _predicate = predicate;
        Error = error;
    }

    /// <summary>Why the pattern could not be compiled, or null when it is valid.</summary>
    public string? Error { get; }

    public bool IsValid => Error is null;

    public bool IsMatch(string input) => _predicate(input);

    public static PatternMatcher Create(MatchKind kind, string pattern, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return kind switch
        {
            MatchKind.Exact => new(s => string.Equals(s, pattern, comparison), null),
            MatchKind.StartsWith => new(s => s.StartsWith(pattern, comparison), null),
            MatchKind.EndsWith => new(s => s.EndsWith(pattern, comparison), null),
            MatchKind.Contains => new(s => s.Contains(pattern, comparison), null),
            MatchKind.Wildcard => FromRegex(WildcardToRegex(pattern), ignoreCase),
            MatchKind.Regex => FromRegex(pattern, ignoreCase),
            _ => new(_ => false, UnsupportedKind(kind)),
        };
    }

    /// <summary>
    /// The <see cref="Error"/> <see cref="Create"/> would report, without building the matcher: the settings check every
    /// pattern of the configuration, and the non-backtracking engine takes about a millisecond and a megabyte to build
    /// one where the parser alone takes microseconds. Both engines parse alike; the non-backtracking one only adds
    /// <see cref="NotSupportedException"/>, which <see cref="Create"/> answers with the backtracking engine.
    /// </summary>
    public static string? ErrorOf(MatchKind kind, string pattern, bool ignoreCase) => kind switch
    {
        MatchKind.Exact or MatchKind.StartsWith or MatchKind.EndsWith or MatchKind.Contains => null,
        MatchKind.Wildcard => ParseError(WildcardToRegex(pattern), ignoreCase),
        MatchKind.Regex => ParseError(pattern, ignoreCase),
        _ => UnsupportedKind(kind),
    };

    /// <summary><c>*</c> matches any run of characters (including none), <c>?</c> exactly one; everything else is literal.</summary>
    public static string WildcardToRegex(string wildcard)
    {
        var builder = new StringBuilder("^");
        foreach (var ch in wildcard)
        {
            builder.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        return builder.Append('$').ToString();
    }

    private static RegexOptions Options(bool ignoreCase) => RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

    private static string UnsupportedKind(MatchKind kind) => L("Unsupported match kind: {0}", kind);

    private static string? ParseError(string pattern, bool ignoreCase)
    {
        try
        {
            // The backtracking engine is the cheap one to build; this one is never run.
            _ = new Regex(pattern, Options(ignoreCase));
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static PatternMatcher FromRegex(string pattern, bool ignoreCase)
    {
        var options = Options(ignoreCase);
        Regex regex;
        try
        {
            regex = new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // Lookarounds, backreferences etc.: use the classic engine, bounded by a timeout.
            try
            {
                regex = new Regex(pattern, options, BacktrackingTimeout);
            }
            catch (ArgumentException ex)
            {
                return new(_ => false, ex.Message);
            }
        }
        catch (ArgumentException ex)
        {
            return new(_ => false, ex.Message);
        }

        return new(s =>
        {
            try
            {
                return regex.IsMatch(s);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }, null);
    }
}
