using Preferis.Core.Configuration;

namespace Preferis.Core.Routing;

/// <summary>Evaluates a condition tree against a URL and the app that opened it. See docs/Design.md §6.2 for the semantics.</summary>
public static class ConditionEvaluator
{
    /// <param name="source">The app that opened the URL; null when the platform could not tell.</param>
    public static bool Matches(Condition? condition, RoutingUrl url, SourceApp? source = null) => condition switch
    {
        null => false,
        AndCondition and => and.Conditions.All(c => Matches(c, url, source)),
        OrCondition or => or.Conditions.Any(c => Matches(c, url, source)),
        NotCondition not => not.Condition is not null && !Matches(not.Condition, url, source),
        // Hosts are case insensitive by definition; the IDN host is matched in both of its forms.
        HostCondition host => MatchesHost(host, url),
        UrlCondition u => PatternMatcher.Create(u.Match, u.Pattern, u.IgnoreCase).IsMatch(url.AbsoluteUri),
        // Paths ignore case as the default file systems of both OSes do, and ids with them, so that one rule fits all.
        // What the platform could not tell matches no pattern, not even one that matches everything.
        AppIdCondition id => source?.Id is { } value && PatternMatcher.Create(id.Match, id.Pattern, ignoreCase: true).IsMatch(value),
        AppPathCondition path => source?.Path is { } value && PatternMatcher.Create(path.Match, path.Pattern, ignoreCase: true).IsMatch(value),
        _ => false,
    };

    private static bool MatchesHost(HostCondition condition, RoutingUrl url)
    {
        var matcher = PatternMatcher.Create(condition.Match, condition.Pattern, ignoreCase: true);
        return matcher.IsMatch(url.Host) || (url.IdnHost != url.Host && matcher.IsMatch(url.IdnHost));
    }
}
