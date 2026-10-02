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
            _ => new(_ => false, L("Unsupported match kind: {0}", kind)),
        };
    }

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

    private static PatternMatcher FromRegex(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
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
