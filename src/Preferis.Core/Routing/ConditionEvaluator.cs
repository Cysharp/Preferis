using Preferis.Core.Configuration;

namespace Preferis.Core.Routing;

/// <summary>Evaluates a condition tree against a URL. See docs/Design.md §6.2 for the semantics.</summary>
public static class ConditionEvaluator
{
    public static bool Matches(Condition? condition, RoutingUrl url) => condition switch
    {
        null => false,
        AndCondition and => and.Conditions.All(c => Matches(c, url)),
        OrCondition or => or.Conditions.Any(c => Matches(c, url)),
        NotCondition not => not.Condition is not null && !Matches(not.Condition, url),
        // Hosts are case insensitive by definition; the IDN host is matched in both of its forms.
        HostCondition host => MatchesHost(host, url),
        UrlCondition u => PatternMatcher.Create(u.Match, u.Pattern, u.IgnoreCase).IsMatch(url.AbsoluteUri),
        _ => false,
    };

    private static bool MatchesHost(HostCondition condition, RoutingUrl url)
    {
        var matcher = PatternMatcher.Create(condition.Match, condition.Pattern, ignoreCase: true);
        return matcher.IsMatch(url.Host) || (url.IdnHost != url.Host && matcher.IsMatch(url.IdnHost));
    }
}
