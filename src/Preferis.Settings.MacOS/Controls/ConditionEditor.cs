using AppKit;
using CoreData;
using Foundation;
using Preferis.Core.Configuration;
using Preferis.Core.Localization;
using Preferis.Settings.ViewModels;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>
/// A rule's condition in an NSPredicateEditor, as in the rules of Mail and the smart folders of Finder: rows of
/// "Host / URL / App — is / starts with / … — pattern" under "All / Any / None of the following are true", nested with ⌥+.
/// Translates between NSPredicate and the UI-neutral tree of <see cref="ConditionPredicates"/>.
/// </summary>
internal sealed class ConditionEditor : IDisposable
{
    private static readonly (PredicateField Field, string KeyPath, string Title)[] Fields =
    [
        (PredicateField.Host, "host", L("Host")),
        (PredicateField.Url, "url", L("URL")),
        (PredicateField.UrlCaseSensitive, "urlCaseSensitive", L("URL (case-sensitive)")),
        (PredicateField.AppId, "appId", L("App ID")),
        (PredicateField.AppPath, "appPath", L("App path")),
    ];

    // The titles follow the rule summaries of the list (RuleSummary). The compound rows are AppKit's own, in the language
    // of the system when the bundle lists it (CFBundleLocalizations).
    private static readonly (MatchKind Match, NSPredicateOperatorType Operator, string Title)[] Operators =
    [
        (MatchKind.Exact, NSPredicateOperatorType.EqualTo, L("is")),
        (MatchKind.StartsWith, NSPredicateOperatorType.BeginsWith, L("starts with")),
        (MatchKind.EndsWith, NSPredicateOperatorType.EndsWith, L("ends with")),
        (MatchKind.Contains, NSPredicateOperatorType.Contains, L("contains")),
        (MatchKind.Wildcard, NSPredicateOperatorType.Like, L("matches")),
        (MatchKind.Regex, NSPredicateOperatorType.Matches, L("matches regex")),
    ];

    private readonly NSPredicateEditor _editor;
    private readonly NSLayoutConstraint _height;
    private readonly Action<CompoundPredicate> _changed;
    private readonly List<NSObject> _observers = [];
    private string? _last;

    /// <param name="changed">Called with the edited tree after every change, typing included.</param>
    public ConditionEditor(CompoundPredicate tree, Action<CompoundPredicate> changed)
    {
        _changed = changed;
        _editor = new NSPredicateEditor
        {
            NestingMode = NSRuleEditorNestingMode.Compound,
            CanRemoveAllRows = false,
            RowHeight = 28,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        if (CompoundFormatting() is { } formatting)
        {
            _editor.FormattingDictionary = formatting;
        }
        _editor.RowTemplates = [CompoundTemplate(), ComparisonTemplate()];
        _editor.ObjectValue = ToPredicate(tree);
        _last = _editor.Predicate?.PredicateFormat;
        _height = _editor.HeightAnchor.ConstraintEqualTo(Height());
        _height.Active = true;

        // The editor is its own NSRuleEditor delegate, so changes are followed through the action and notifications.
        _editor.Activated += (_, _) => Publish();
        _observers.Add(NSRuleEditor.Notifications.ObserveRowsDidChange(_editor, (_, _) =>
        {
            _height.Constant = Height();
            Publish();
        }));
        // The action comes when a pattern field ends editing; follow the typing too, so Save never misses the last edit.
        _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(NSControl.TextDidChangeNotification, notification =>
        {
            if (notification.Object is NSView field && field.IsDescendantOf(_editor))
            {
                _editor.ReloadPredicate();
                Publish();
            }
        }));
    }

    public NSView View => _editor;

    public void Dispose()
    {
        foreach (var observer in _observers)
        {
            observer.Dispose();
        }
        _observers.Clear();
    }

    private nfloat Height() => (nfloat)(Math.Max(1, (double)_editor.NumberOfRows) * (double)_editor.RowHeight);

    private void Publish()
    {
        var predicate = _editor.Predicate;
        var format = predicate?.PredicateFormat;
        if (predicate is null || format == _last)
        {
            return;
        }
        _last = format;
        _changed(FromPredicate(predicate) switch
        {
            CompoundPredicate compound => compound,
            { } row => new CompoundPredicate(CompoundKind.All, [row]),
            null => new CompoundPredicate(CompoundKind.All, []),
        });
    }

    /// <summary>
    /// The compound rows in the language of the UI. AppKit writes "[Any] of the following are true" in English whatever the
    /// language (it has no translations of it), and the words around the choice differ by language, so in Japanese the
    /// choice becomes the whole sentence, as in the Windows editor. The keys are the rows as AppKit formats them.
    /// </summary>
    private static NSDictionary? CompoundFormatting()
    {
        if (UiLanguages.Current == UiLanguage.English)
        {
            return null;
        }
        var rows = new (string Choice, string Sentence)[]
        {
            ("All", L("All of the following are true")),
            ("Any", L("Any of the following are true")),
            ("None", L("None of the following are true")),
        };
        return NSDictionary.FromObjectsAndKeys(
            rows.Select(r => (NSObject)new NSString($"%[{r.Sentence}]@")).ToArray(),
            rows.Select(r => (NSObject)new NSString($"%[{r.Choice}]@ of the following are true")).ToArray());
    }

    private static NSPredicateEditorRowTemplate CompoundTemplate()
        => new([NSCompoundPredicateType.And, NSCompoundPredicateType.Or, NSCompoundPredicateType.Not]);

    private static NSPredicateEditorRowTemplate ComparisonTemplate()
    {
        var template = new NSPredicateEditorRowTemplate(
            Fields.Select(f => NSExpression.FromKeyPath(f.KeyPath)),
            NSAttributeType.String,
            Operators.Select(o => o.Operator),
            NSComparisonPredicateModifier.Direct,
            NSComparisonPredicateOptions.None);

        // The popups show key paths and operator names ("host", "like") until retitled. The editor copies these views
        // for every row, and the titles are display only: the items keep the expressions and operators they stand for.
        var views = template.TemplateViews;
        if (views.Length >= 3 && views[0] is NSPopUpButton fields && views[1] is NSPopUpButton operators && views[2] is NSTextField pattern)
        {
            foreach (var item in fields.Items())
            {
                if (item.RepresentedObject is NSExpression expression && Fields.FirstOrDefault(f => f.KeyPath == expression.KeyPath).Title is { } title)
                {
                    item.Title = title;
                }
            }
            foreach (var item in operators.Items())
            {
                if (item.RepresentedObject is NSNumber number && Operators.FirstOrDefault(o => (ulong)o.Operator == number.UInt64Value).Title is { } title)
                {
                    item.Title = title;
                }
            }
            pattern.PlaceholderString = L("Pattern");
        }
        return template;
    }

    private static NSPredicate ToPredicate(PredicateNode node) => node switch
    {
        CompoundPredicate { Kind: CompoundKind.All } all => NSCompoundPredicate.CreateAndPredicate(all.Children.Select(ToPredicate).ToArray()),
        CompoundPredicate { Kind: CompoundKind.Any } any => NSCompoundPredicate.CreateOrPredicate(any.Children.Select(ToPredicate).ToArray()),
        // "None of" is how the editor writes a negation: NOT over the OR of its rows.
        CompoundPredicate none => NSCompoundPredicate.CreateNotPredicate(NSCompoundPredicate.CreateOrPredicate(none.Children.Select(ToPredicate).ToArray())),
        ComparisonPredicate row => new NSComparisonPredicate(
            NSExpression.FromKeyPath(Fields.First(f => f.Field == row.Field).KeyPath),
            NSExpression.FromConstant(new NSString(row.Pattern)),
            NSComparisonPredicateModifier.Direct,
            Operators.First(o => o.Match == row.Match).Operator,
            NSComparisonPredicateOptions.None),
        _ => throw new ArgumentOutOfRangeException(nameof(node), node, "Unsupported predicate."),
    };

    private static PredicateNode? FromPredicate(NSPredicate predicate)
    {
        switch (predicate)
        {
            case NSCompoundPredicate { Type: NSCompoundPredicateType.And } and:
                return new CompoundPredicate(CompoundKind.All, Children(and.Subpredicates));
            case NSCompoundPredicate { Type: NSCompoundPredicateType.Or } or:
                return new CompoundPredicate(CompoundKind.Any, Children(or.Subpredicates));
            case NSCompoundPredicate not:
                return not.Subpredicates is [NSCompoundPredicate { Type: NSCompoundPredicateType.Or } rows]
                    ? new CompoundPredicate(CompoundKind.None, Children(rows.Subpredicates))
                    : new CompoundPredicate(CompoundKind.None, Children(not.Subpredicates));
            case NSComparisonPredicate comparison:
                var field = Fields.FirstOrDefault(f => f.KeyPath == comparison.LeftExpression.KeyPath);
                var match = Operators.FirstOrDefault(o => o.Operator == comparison.PredicateOperatorType);
                if (field.KeyPath is null || match.Title is null)
                {
                    return null;
                }
                var pattern = comparison.RightExpression.ConstantValue?.ToString() ?? "";
                return new ComparisonPredicate(field.Field, match.Match, pattern);
            default:
                return null;
        }
    }

    private static List<PredicateNode> Children(NSPredicate[] predicates)
        => predicates.Select(FromPredicate).OfType<PredicateNode>().ToList();
}
