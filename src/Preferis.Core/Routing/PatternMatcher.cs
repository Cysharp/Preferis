using System.Text;
using System.Text.RegularExpressions;
using Preferis.Core.Configuration;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Routing;

/// <summary>
/// Compiles one (match kind, pattern, case sensitivity) triple into a predicate over strings.
/// The Router builds one for every condition it evaluates, once per URL (docs/Design.md §5.2), so building is kept cheap:
/// wildcards are matched without a regular expression, and regular expressions start on the backtracking engine, which
/// takes microseconds to build where the non-backtracking one takes about a millisecond and 0.7 MiB. A user supplied
/// pattern still runs in bounded time: when the backtracking engine takes too long, the non-backtracking engine, linear in
/// the input, answers instead. Constructs that engine does not support stay on the backtracking engine with a timeout.
/// An invalid pattern yields a matcher that never matches instead of throwing at routing time.
/// </summary>
public sealed class PatternMatcher
{
    private static readonly TimeSpan BacktrackingTimeout = TimeSpan.FromMilliseconds(100);

    // Matching a URL takes microseconds; only a pattern that backtracks catastrophically gets anywhere near this.
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromMilliseconds(10);

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
            MatchKind.Wildcard => new(s => WildcardMatches(pattern, s, ignoreCase), null),
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

    /// <summary>
    /// Whether <paramref name="input"/> matches <paramref name="pattern"/> as <see cref="WildcardToRegex"/> would, without
    /// building a regular expression. URLs and hosts have no line breaks, which the regex's <c>.</c> would not match.
    /// </summary>
    internal static bool WildcardMatches(string pattern, string input, bool ignoreCase)
    {
        // The usual greedy match: a star first takes nothing and, when what follows fails, one more character. Only the
        // last star ever needs to take more (an earlier one could only give what the last one can), so this takes at
        // most pattern × input steps, and allocates nothing.
        int p = 0, i = 0, star = -1, starInput = 0;
        while (i < input.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                starInput = i;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], input[i], ignoreCase)))
            {
                p++;
                i++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                i = ++starInput;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }
        return p == pattern.Length;
    }

    // Ordinal, like the other match kinds; the regex's invariant case table differs only for a few letters that no host
    // or (percent-encoded) URL contains.
    private static bool Same(char a, char b, bool ignoreCase) => a == b || (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

    private static PatternMatcher FromRegex(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        Regex quick;
        try
        {
            quick = new Regex(pattern, options, QuickTimeout);
        }
        catch (ArgumentException ex)
        {
            return new(_ => false, ex.Message);
        }

        // Built only for a pattern the quick one gives up on. Either engine finds a match exactly when the other does.
        Regex? linear = null;
        return new(s =>
        {
            try
            {
                return quick.IsMatch(s);
            }
            catch (RegexMatchTimeoutException)
            {
            }
            linear ??= Linear(pattern, options);
            try
            {
                return linear.IsMatch(s);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }, null);
    }

    /// <summary>
    /// The non-backtracking engine, which takes time linear in the input whatever the pattern. Lookarounds,
    /// backreferences and the like it does not support: those get the backtracking engine again, with a longer timeout.
    /// </summary>
    private static Regex Linear(string pattern, RegexOptions options)
    {
        try
        {
            return new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, options, BacktrackingTimeout);
        }
    }
}
