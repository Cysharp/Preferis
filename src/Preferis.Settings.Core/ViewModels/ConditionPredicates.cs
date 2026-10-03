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
    /// <param name="previous">
    /// The condition the editor showed before this change. The editor's tree has no place for what a newer Preferis added to
    /// the conditions (<see cref="Condition.ExtensionData"/>), so it is carried over from the previous conditions to the
    /// rows that stand for them (docs/Configuration.md §4).
    /// </param>
    public static Condition? FromEditor(CompoundPredicate root, Condition? previous = null)
    {
        // A lone condition is shown as the only row under an "all of" of the editor's own (ToEditor).
        var previousGroup = previous is AndCondition or OrCondition or NotCondition ? previous : null;
        IReadOnlyList<Condition> previousRows = previousGroup is not null ? Rows(previousGroup) : previous is null ? [] : [previous];
        return root switch
        {
            { Children.Count: 0 } => null,
            { Kind: CompoundKind.All or CompoundKind.Any, Children.Count: 1 } => FromRows(root.Children, previousRows)[0],
            _ => FromGroup(root, previousGroup, previousRows),
        };
    }

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

    /// <summary>The conditions a group shows as its rows (<see cref="ToNode"/>): those of the "or" that "none of" negates.</summary>
    private static IReadOnlyList<Condition> Rows(Condition? group) => group switch
    {
        AndCondition and => and.Conditions,
        OrCondition or => or.Conditions,
        NotCondition { Condition: OrCondition or } => or.Conditions,
        NotCondition { Condition: { } negated } => [negated],
        _ => [],
    };

    /// <param name="previous">The previous condition the node stands for, if any.</param>
    private static Condition FromNode(PredicateNode node, Condition? previous) => node switch
    {
        CompoundPredicate group => FromGroup(group, previous, Rows(previous)),
        ComparisonPredicate { Field: PredicateField.Host } host => Keep(previous, new HostCondition { Match = host.Match, Pattern = host.Pattern }),
        ComparisonPredicate url => Keep(previous, new UrlCondition { Match = url.Match, Pattern = url.Pattern, IgnoreCase = url.Field == PredicateField.Url }),
        _ => throw new ArgumentOutOfRangeException(nameof(node), node, "Unsupported predicate."),
    };

    private static Condition FromGroup(CompoundPredicate group, Condition? previous, IReadOnlyList<Condition> previousRows)
    {
        var rows = FromRows(group.Children, previousRows);
        return Keep(previous, group switch
        {
            { Kind: CompoundKind.All } => new AndCondition { Conditions = rows },
            { Kind: CompoundKind.Any } => new OrCondition { Conditions = rows },
            { Children.Count: 1 } => new NotCondition { Condition = rows[0] },
            _ => new NotCondition { Condition = Keep((previous as NotCondition)?.Condition, new OrCondition { Conditions = rows }) },
        });
    }

    /// <summary>
    /// The rows of a group, each built with the previous row it stands for. The editors report every change on its own, so
    /// a row equal to the previous row at its place is that row, then one equal to a previous row elsewhere (rows were
    /// added or removed before it), and the rest take the previous rows left over in order: usually the one row being
    /// typed into. Nothing tells two equal rows apart, so they may swap what they carry.
    /// </summary>
    private static List<Condition> FromRows(IReadOnlyList<PredicateNode> rows, IReadOnlyList<Condition> previousRows)
    {
        var previousNodes = previousRows.Select(ToNode).ToList();
        var unpaired = Enumerable.Range(0, previousRows.Count).ToList();
        var pairs = new Condition?[rows.Count];
        Pair((row, previous) => previous == row && Same(previousNodes[previous], rows[row]));
        Pair((row, previous) => Same(previousNodes[previous], rows[row]));
        Pair((_, _) => true);
        return rows.Select((row, index) => FromNode(row, pairs[index])).ToList();

        void Pair(Func<int, int, bool> fits)
        {
            for (var row = 0; row < rows.Count; row++)
            {
                var found = pairs[row] is null ? unpaired.FindIndex(previous => fits(row, previous)) : -1;
                if (found >= 0)
                {
                    pairs[row] = previousRows[unpaired[found]];
                    unpaired.RemoveAt(found);
                }
            }
        }
    }

    // The records compare their lists of children by reference, so groups are compared row by row here.
    private static bool Same(PredicateNode? previous, PredicateNode row) => (previous, row) switch
    {
        (CompoundPredicate a, CompoundPredicate b) => a.Kind == b.Kind && a.Children.Count == b.Children.Count && a.Children.Zip(b.Children).All(p => Same(p.First, p.Second)),
        _ => Equals(previous, row),
    };

    /// <summary>
    /// Gives a condition built from the editor what a newer Preferis added to the previous one, unless it is now another
    /// type (a host row became a URL row, "any of" became "all of"): what was written for one type may mean nothing, or
    /// something else, on another.
    /// </summary>
    private static Condition Keep(Condition? previous, Condition condition)
    {
        if (previous is not null && previous.GetType() == condition.GetType())
        {
            condition.ExtensionData = previous.ExtensionData;
        }
        return condition;
    }
}
