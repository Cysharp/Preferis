using Preferis.Core.Routing;

namespace Preferis.Core.Configuration;

/// <summary>
/// The errors of the patterns of one configuration, kept from one <see cref="ConfigValidator.Validate"/> to the next:
/// the settings validate the whole configuration after every edit, most edits leave its patterns as they were, and it
/// may have thousands of them (an imported list of sites). Results are keyed by everything they depend on (the match
/// kind, the pattern and the case sensitivity), so an edited pattern is checked again and the others are not. Only what
/// the last validation looked up is kept, so the cache follows the configuration rather than collecting every text
/// typed into a pattern box. Not thread safe: a settings session uses it on its UI thread.
/// </summary>
public sealed class PatternErrorCache
{
    private readonly Func<MatchKind, string, bool, string?> _check;
    private Dictionary<(MatchKind Kind, string Pattern, bool IgnoreCase), string?> _current = [];
    private Dictionary<(MatchKind Kind, string Pattern, bool IgnoreCase), string?> _previous = [];

    /// <param name="check">Finds the error of a pattern: <see cref="PatternMatcher.ErrorOf"/>, or what a test counts.</param>
    public PatternErrorCache(Func<MatchKind, string, bool, string?>? check = null)
    {
        _check = check ?? PatternMatcher.ErrorOf;
    }

    /// <summary>The error <see cref="PatternMatcher.ErrorOf"/> reports, checked only when neither this validation nor the previous one did.</summary>
    public string? ErrorOf(MatchKind kind, string pattern, bool ignoreCase)
    {
        var key = (kind, pattern, ignoreCase);
        if (!_current.TryGetValue(key, out var error))
        {
            if (!_previous.Remove(key, out error))
            {
                error = _check(kind, pattern, ignoreCase);
            }
            _current.Add(key, error);
        }
        return error;
    }

    /// <summary>A validation is over: what it did not look up is no longer in the configuration and is forgotten.</summary>
    internal void EndValidation()
    {
        (_previous, _current) = (_current, _previous);
        _current.Clear();
    }
}
