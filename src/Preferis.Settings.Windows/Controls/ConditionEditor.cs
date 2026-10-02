using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Windows.Controls;

/// <summary>
/// A rule's condition as rows, in the shape of Outlook's and Mail's rules (the AppKit version uses NSPredicateEditor):
/// "All / Any / None of the following are true" with "Host / URL — is / starts with / … — pattern" rows under it, groups
/// nested at any depth. Edits the UI-neutral tree of <see cref="ConditionPredicates"/> and reports it after every change.
/// </summary>
internal sealed class ConditionEditor
{
    private const double Indent = 28;

    private static readonly (PredicateField Field, string Title)[] Fields =
    [
        (PredicateField.Host, "Host"),
        (PredicateField.Url, "URL"),
        (PredicateField.UrlCaseSensitive, "URL (case-sensitive)"),
    ];

    // The titles follow the rule summaries of the list (RuleSummary.Verb).
    private static readonly (MatchKind Match, string Title)[] Operators =
    [
        (MatchKind.Exact, "is"),
        (MatchKind.StartsWith, "starts with"),
        (MatchKind.EndsWith, "ends with"),
        (MatchKind.Contains, "contains"),
        (MatchKind.Wildcard, "matches"),
        (MatchKind.Regex, "matches regex"),
    ];

    private static readonly (CompoundKind Kind, string Title)[] Compounds =
    [
        (CompoundKind.All, "All"),
        (CompoundKind.Any, "Any"),
        (CompoundKind.None, "None"),
    ];

    private readonly Node _root;
    private readonly Action<CompoundPredicate> _changed;
    private readonly StackPanel _rows = new() { Spacing = 6 };

    /// <param name="changed">Called with the edited tree after every change, typing included.</param>
    public ConditionEditor(CompoundPredicate tree, Action<CompoundPredicate> changed)
    {
        _root = Node.From(tree);
        _changed = changed;
        Render();
    }

    public UIElement View => _rows;

    private void Render()
    {
        _rows.Children.Clear();
        AddRows(_root, null, 0);
    }

    private void Publish() => _changed((CompoundPredicate)_root.ToPredicate());

    // Structure changed: show the new tree and report it.
    private void Restructure()
    {
        Render();
        Publish();
    }

    private void AddRows(Node node, Node? parent, int depth)
    {
        _rows.Children.Add(node.IsCompound ? CompoundRow(node, parent, depth) : ComparisonRow(node, parent!, depth));
        if (node.IsCompound)
        {
            foreach (var child in node.Children)
            {
                AddRows(child, node, depth + 1);
            }
            if (node.Children.Count == 0 && parent is null)
            {
                var empty = Ui.Secondary("No conditions: the rule never matches. Add one with +.");
                empty.Margin = new Thickness(Indent, 0, 0, 0);
                _rows.Children.Add(empty);
            }
        }
    }

    private FrameworkElement CompoundRow(Node node, Node? parent, int depth)
    {
        var kind = Ui.ComboBox(100);
        kind.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var (_, title) in Compounds)
        {
            kind.Items.Add(title);
        }
        kind.SelectedIndex = Array.FindIndex(Compounds, c => c.Kind == node.Compound);
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex >= 0 && Compounds[kind.SelectedIndex].Kind != node.Compound)
            {
                node.Compound = Compounds[kind.SelectedIndex].Kind;
                Publish();
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(kind, "Which conditions must be true");

        var add = AddButton(node, insertAfter: null);
        var buttons = Ui.HStack(0, add);
        if (parent is not null)
        {
            buttons.Children.Add(RemoveButton(node, parent));
        }
        var row = Ui.Columns(8,
            (kind, Ui.Auto),
            (Ui.Label("of the following are true"), Ui.Star),
            (buttons, Ui.Auto));
        row.Margin = new Thickness(depth * Indent, 0, 0, 0);
        return row;
    }

    private FrameworkElement ComparisonRow(Node node, Node parent, int depth)
    {
        var field = Ui.ComboBox(170);
        field.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var (_, title) in Fields)
        {
            field.Items.Add(title);
        }
        field.SelectedIndex = Array.FindIndex(Fields, f => f.Field == node.Field);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, "What to look at");

        var op = Ui.ComboBox(140);
        op.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var (_, title) in Operators)
        {
            op.Items.Add(title);
        }
        op.SelectedIndex = Array.FindIndex(Operators, o => o.Match == node.Match);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(op, "How to compare");

        var pattern = new TextBox { Text = node.Pattern, PlaceholderText = "Pattern", FontFamily = Ui.Monospace };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pattern, "Pattern");
        var error = Ui.Wrapping("", 12, Ui.Brush("SystemFillColorCriticalBrush"));
        void ShowError()
        {
            var message = node.Pattern.Length == 0 ? null : PatternMatcher.Create(node.Match, node.Pattern, ignoreCase: true).Error;
            error.Text = message ?? "";
            Ui.Show(error, message is not null);
        }
        ShowError();

        field.SelectionChanged += (_, _) =>
        {
            if (field.SelectedIndex >= 0)
            {
                node.Field = Fields[field.SelectedIndex].Field;
                Publish();
            }
        };
        op.SelectionChanged += (_, _) =>
        {
            if (op.SelectedIndex >= 0)
            {
                node.Match = Operators[op.SelectedIndex].Match;
                ShowError();
                Publish();
            }
        };
        pattern.TextChanged += (_, _) =>
        {
            node.Pattern = pattern.Text;
            ShowError();
            Publish();
        };

        var buttons = Ui.HStack(0, AddButton(parent, insertAfter: node), RemoveButton(node, parent));
        var row = Ui.Columns(8, (field, Ui.Auto), (op, Ui.Auto), (pattern, Ui.Star), (buttons, Ui.Auto));
        row.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        row.RowSpacing = 6;
        // In a narrow editor the pattern gets a line of its own instead of a sliver between the drop-downs.
        row.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 560;
            Grid.SetRow(pattern, narrow ? 1 : 0);
            Grid.SetColumn(pattern, narrow ? 0 : 2);
            Grid.SetColumnSpan(pattern, narrow ? 3 : 1);
        };
        var stack = Ui.VStack(2, row, error);
        stack.Margin = new Thickness(depth * Indent, 0, 0, 0);
        return stack;
    }

    // "+": a condition, or a group of conditions, below this row (or at the end of the group).
    private Button AddButton(Node group, Node? insertAfter)
    {
        var button = Ui.IconButton(Ui.Glyphs.Add, "Add a condition or a group");
        var menu = new MenuFlyout();
        var condition = new MenuFlyoutItem { Text = "Add Condition" };
        condition.Click += (_, _) => Insert(group, insertAfter, Node.Comparison(PredicateField.Host, MatchKind.Exact, ""));
        var nested = new MenuFlyoutItem { Text = "Add Group" };
        nested.Click += (_, _) => Insert(group, insertAfter, Node.Group(CompoundKind.Any, [Node.Comparison(PredicateField.Host, MatchKind.Exact, "")]));
        menu.Items.Add(condition);
        menu.Items.Add(nested);
        button.Flyout = menu;
        return button;
    }

    private Button RemoveButton(Node node, Node parent)
    {
        var button = Ui.IconButton(Ui.Glyphs.Remove, "Remove");
        button.Click += (_, _) =>
        {
            parent.Children.Remove(node);
            Restructure();
        };
        return button;
    }

    private void Insert(Node group, Node? after, Node node)
    {
        var index = after is null ? group.Children.Count : group.Children.IndexOf(after) + 1;
        group.Children.Insert(index, node);
        Restructure();
    }

    /// <summary>The editor's own, mutable copy of the tree; turned back into predicates for every report.</summary>
    private sealed class Node
    {
        public bool IsCompound { get; private init; }

        public CompoundKind Compound { get; set; }

        public List<Node> Children { get; } = [];

        public PredicateField Field { get; set; }

        public MatchKind Match { get; set; }

        public string Pattern { get; set; } = "";

        public static Node Group(CompoundKind kind, IEnumerable<Node> children)
        {
            var node = new Node { IsCompound = true, Compound = kind };
            node.Children.AddRange(children);
            return node;
        }

        public static Node Comparison(PredicateField field, MatchKind match, string pattern) => new() { Field = field, Match = match, Pattern = pattern };

        public static Node From(PredicateNode predicate) => predicate switch
        {
            CompoundPredicate compound => Group(compound.Kind, compound.Children.Select(From)),
            ComparisonPredicate comparison => Comparison(comparison.Field, comparison.Match, comparison.Pattern),
            _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate, "Unsupported predicate."),
        };

        public PredicateNode ToPredicate() => IsCompound
            ? new CompoundPredicate(Compound, Children.Select(c => c.ToPredicate()).ToList())
            : new ComparisonPredicate(Field, Match, Pattern);
    }
}
