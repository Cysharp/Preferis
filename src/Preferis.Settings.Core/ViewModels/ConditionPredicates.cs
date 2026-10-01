using Preferis.Core.Configuration;

namespace Preferis.Settings.ViewModels;

/// <summary>A row of a predicate editor (Mail rules, NSPredicateEditor): a compound row or a comparison row.</summary>
public abstract record PredicateNode;

public enum CompoundKind
{
    /// <summary>"All of the following are true" (<c>and</c>).</summary>
    All,

    /// <summary>"Any of the following are true" (<c>or</c>).</summary>
    Any,

    /// <summary>"None of the following are true" (<c>not</c> of <c>or</c>).</summary>
    None,
}

/// <summary>What a comparison row looks at. Case sensitive URLs are a field of their own, since a row has no other place for it.</summary>
public enum PredicateField
{
    Host,
    Url,
    UrlCaseSensitive,
}

public sealed record CompoundPredicate(CompoundKind Kind, IReadOnlyList<PredicateNode> Children) : PredicateNode;

public sealed record ComparisonPredicate(PredicateField Field, MatchKind Match, string Pattern) : PredicateNode;

/// <summary>
/// Converts condition trees to the shape of a predicate editor and back, for settings UIs that edit conditions with one
/// (the AppKit UI uses NSPredicateEditor). The editor's root is always a compound row; a lone condition is shown under
/// "all of", and a root with one row is stored as that row so that simple rules stay simple. Negation is "none of".
/// </summary>
public static class ConditionPredicates
{
    /// <summary>The editor tree, or null when the condition cannot be edited this way (it comes from a newer Preferis).</summary>
    public static CompoundPredicate? ToEditor(Condition? condition)
    {
        if (condition is null)
        {
            return new CompoundPredicate(CompoundKind.All, []);
        }
        return ToNode(condition) switch
        {
            null => null,
            CompoundPredicate compound => compound,
            var row => new CompoundPredicate(CompoundKind.All, [row]),
        };
    }

    /// <summary>The condition for an editor tree; null (no condition, never matches) when the editor is empty.</summary>
    public static Condition? FromEditor(CompoundPredicate root) => root switch
    {
        { Children.Count: 0 } => null,
        { Kind: CompoundKind.All or CompoundKind.Any, Children.Count: 1 } => FromNode(root.Children[0]),
        _ => FromNode(root),
    };

    private static PredicateNode? ToNode(Condition condition)
    {
        switch (condition)
        {
            case AndCondition and:
                return Compound(CompoundKind.All, and.Conditions);
            case OrCondition or:
                return Compound(CompoundKind.Any, or.Conditions);
            case NotCondition { Condition: OrCondition or }:
                return Compound(CompoundKind.None, or.Conditions);
            case NotCondition { Condition: null }:
                return new CompoundPredicate(CompoundKind.None, []);
            case NotCondition not:
                return ToNode(not.Condition) is { } negated ? new CompoundPredicate(CompoundKind.None, [negated]) : null;
            case HostCondition host:
                return new ComparisonPredicate(PredicateField.Host, host.Match, host.Pattern);
            case UrlCondition url:
                return new ComparisonPredicate(url.IgnoreCase ? PredicateField.Url : PredicateField.UrlCaseSensitive, url.Match, url.Pattern);
            default:
                return null;
        }
    }

    private static CompoundPredicate? Compound(CompoundKind kind, List<Condition> conditions)
    {
        var children = new List<PredicateNode>(conditions.Count);
        foreach (var condition in conditions)
        {
            if (ToNode(condition) is not { } child)
            {
                return null;
            }
            children.Add(child);
        }
        return new CompoundPredicate(kind, children);
    }

    private static Condition FromNode(PredicateNode node) => node switch
    {
        CompoundPredicate { Kind: CompoundKind.All } all => new AndCondition { Conditions = all.Children.Select(FromNode).ToList() },
        CompoundPredicate { Kind: CompoundKind.Any } any => new OrCondition { Conditions = any.Children.Select(FromNode).ToList() },
        CompoundPredicate { Children.Count: 1 } none => new NotCondition { Condition = FromNode(none.Children[0]) },
        CompoundPredicate none => new NotCondition { Condition = new OrCondition { Conditions = none.Children.Select(FromNode).ToList() } },
        ComparisonPredicate { Field: PredicateField.Host } host => new HostCondition { Match = host.Match, Pattern = host.Pattern },
        ComparisonPredicate url => new UrlCondition { Match = url.Match, Pattern = url.Pattern, IgnoreCase = url.Field == PredicateField.Url },
        _ => throw new ArgumentOutOfRangeException(nameof(node), node, "Unsupported predicate."),
    };
}
