using System.Text.Json;
using AppKit;
using Preferis.Core.Configuration;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS.Pages;

/// <summary>The rules in order, the catch-all rule below them, and the selected one's condition and actions.</summary>
internal sealed class RulesPage : IPageView
{
    private const double ListWidth = 270;
    private const double RowHeight = 44;
    private readonly RulesPageViewModel _page;
    private readonly Bindings _bindings = new();
    private readonly ListPane<RuleViewModel> _rules;
    private readonly ListPane<CatchAllViewModel> _catchAll;
    private readonly NSView _editorHost = new();
    private Bindings? _editorBindings;
    private object? _shown;

    public RulesPage(RulesPageViewModel page)
    {
        _page = page;
        _rules = new ListPane<RuleViewModel>(
            page.Rules,
            Row,
            rule => page.SelectedRule = rule,
            page.MoveRule,
            RowHeight,
            nameof(RuleViewModel.Name), nameof(RuleViewModel.Summary), nameof(RuleViewModel.Enabled), nameof(RuleViewModel.HasIssues));
        // A list of its own: it always comes last and cannot be moved or deleted.
        _catchAll = new ListPane<CatchAllViewModel>(
            [page.CatchAll],
            CatchAllRow,
            catchAll =>
            {
                if (catchAll is not null)
                {
                    page.SelectCatchAllCommand.Execute(null);
                }
            },
            null,
            RowHeight,
            nameof(CatchAllViewModel.Summary), nameof(CatchAllViewModel.HasIssues));
        _catchAll.Table.Style = NSTableViewStyle.Inset;
        // The inset table style keeps 10 pt above its first row; as much below the row makes it sit in the middle.
        _catchAll.Box.HeightAnchor.ConstraintEqualTo((nfloat)(RowHeight + 20)).Active = true;
        _rules.AddBelowRows(_catchAll.Box);

        var add = Ui.IconButton("plus", "Add a rule below the selected one");
        _bindings.Command(add, page.AddRuleCommand);
        var remove = Ui.IconButton("minus", "Delete the selected rule");
        _bindings.Command(remove, page.DeleteRuleCommand);
        _rules.Bar.AddArrangedSubview(add);
        _rules.Bar.AddArrangedSubview(remove);
        _rules.Bar.AddArrangedSubview(Ui.Spacer());
        _rules.Bar.AddArrangedSubview(MoreMenu());
        Ui.FillWidth(_rules.Bar, _rules.View);

        var root = new NSView();
        root.AddSubview(_rules.View);
        root.AddSubview(_editorHost);
        _rules.View.TranslatesAutoresizingMaskIntoConstraints = false;
        _editorHost.TranslatesAutoresizingMaskIntoConstraints = false;
        _rules.View.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20).Active = true;
        // Below the toolbar: the window's content runs under it, and only the scrolling editor insets itself.
        _rules.View.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor, 20).Active = true;
        _rules.View.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -20).Active = true;
        _rules.View.WidthAnchor.ConstraintEqualTo((nfloat)ListWidth).Active = true;
        _editorHost.LeadingAnchor.ConstraintEqualTo(_rules.View.TrailingAnchor, 4).Active = true;
        _editorHost.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
        _editorHost.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
        _editorHost.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
        View = root;

        _bindings.Observe(page, ShowSelected, nameof(RulesPageViewModel.CurrentEditor));
    }

    public NSView View { get; }

    public void Dispose()
    {
        _editorBindings?.Dispose();
        _rules.Dispose();
        _catchAll.Dispose();
        _bindings.Dispose();
    }

    private static NSView Row(RuleViewModel rule)
    {
        var enabled = Ui.Checkbox("");
        enabled.State = rule.Enabled ? NSCellStateValue.On : NSCellStateValue.Off;
        enabled.ToolTip = "Turn the rule on or off";
        enabled.Activated += (_, _) => rule.Enabled = enabled.State == NSCellStateValue.On;
        var name = Ui.Label(rule.Name.Length > 0 ? rule.Name : "(Unnamed)", color: rule.Enabled ? NSColor.Label : NSColor.TertiaryLabel);
        var summary = Ui.Secondary(rule.Summary);
        summary.ToolTip = rule.Summary;
        var texts = Ui.VStack(1, name, summary);
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        var row = Ui.HStack(6, enabled, texts);
        row.EdgeInsets = new NSEdgeInsets(0, 0, 0, 6);
        if (rule.HasIssues)
        {
            row.AddArrangedSubview(Badge());
        }
        return row;
    }

    private static NSView CatchAllRow(CatchAllViewModel catchAll)
    {
        var icon = NSImageView.FromImage(Images.Symbol("arrow.turn.down.right", 13)!);
        icon.ContentTintColor = NSColor.SecondaryLabel;
        icon.WidthAnchor.ConstraintEqualTo(18).Active = true;
        var summary = Ui.Secondary(catchAll.Summary);
        summary.ToolTip = catchAll.Summary;
        var texts = Ui.VStack(1, Ui.Label("When No Rule Matches"), summary);
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        var row = Ui.HStack(6, icon, texts);
        row.EdgeInsets = new NSEdgeInsets(0, 0, 0, 6);
        if (catchAll.HasIssues)
        {
            row.AddArrangedSubview(Badge());
        }
        return row;
    }

    private static NSImageView Badge()
    {
        var badge = NSImageView.FromImage(Images.Symbol("exclamationmark.circle.fill", 12)!);
        badge.ContentTintColor = NSColor.SystemRed;
        badge.ToolTip = "This rule has problems";
        badge.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        return badge;
    }

    private NSPopUpButton MoreMenu()
    {
        var more = new NSPopUpButton { PullsDown = true, Bordered = true };
        more.AddItem("");
        more.LastItem!.Image = Images.Symbol("ellipsis", 12);
        more.Menu!.AutoEnablesItems = false;
        var duplicate = new NSMenuItem("Duplicate", (_, _) => _page.DuplicateRuleCommand.Execute(null));
        var up = new NSMenuItem("Move Up", (_, _) => _page.MoveRuleUpCommand.Execute(null));
        var down = new NSMenuItem("Move Down", (_, _) => _page.MoveRuleDownCommand.Execute(null));
        more.Menu.AddItem(duplicate);
        more.Menu.AddItem(NSMenuItem.SeparatorItem);
        more.Menu.AddItem(up);
        more.Menu.AddItem(down);
        more.Menu.AddItem(NSMenuItem.SeparatorItem);
        more.Menu.AddItem(new NSMenuItem("Import from CSV…", (_, _) => _page.ImportRulesCommand.Execute(null)));
        void Update()
        {
            duplicate.Enabled = _page.DuplicateRuleCommand.CanExecute(null);
            up.Enabled = _page.MoveRuleUpCommand.CanExecute(null);
            down.Enabled = _page.MoveRuleDownCommand.CanExecute(null);
        }
        _bindings.Observe(_page, Update, nameof(RulesPageViewModel.SelectedRule));
        _bindings.ObserveCollection(_page.Rules, Update);
        return more;
    }

    private void ShowSelected()
    {
        _rules.Select(_page.SelectedRule);
        _catchAll.Select(_page.IsCatchAllSelected ? _page.CatchAll : null);
        if (ReferenceEquals(_shown, _page.CurrentEditor))
        {
            return;
        }
        ShowEditor();
    }

    private void ShowEditor()
    {
        _shown = _page.CurrentEditor;
        _editorBindings?.Dispose();
        foreach (var view in _editorHost.Subviews)
        {
            view.RemoveFromSuperview();
        }
        _editorBindings = new Bindings();
        var editor = _shown switch
        {
            RuleViewModel rule => RuleEditor(rule, _editorBindings),
            CatchAllViewModel catchAll => CatchAllEditor(catchAll, _editorBindings),
            _ => EditorParts.Empty("No Rule Selected", "Click + below the list to add a rule."),
        };
        _editorHost.AddSubview(editor);
        Ui.Pin(editor, _editorHost);
    }

    private NSView RuleEditor(RuleViewModel rule, Bindings bindings)
    {
        var title = Ui.Label("");
        var subtitle = Ui.Flowing("");
        bindings.Observe(rule, () =>
        {
            title.StringValue = rule.Name.Length > 0 ? rule.Name : "(Unnamed)";
            subtitle.StringValue = rule.Summary;
        }, nameof(RuleViewModel.Name), nameof(RuleViewModel.Summary));
        var header = EditorParts.Header(EditorParts.SymbolBadge("arrow.triangle.branch", NSColor.SystemOrange, 32), title, subtitle, rule, nameof(RuleViewModel.Issues), () => rule.Issues, bindings);

        var name = Ui.Field("Name");
        bindings.Text(name, rule, nameof(RuleViewModel.Name), () => rule.Name, value => rule.Name = value);
        var enabled = Ui.Switch();
        bindings.Switch(enabled, rule, nameof(RuleViewModel.Enabled), () => rule.Enabled, value => rule.Enabled = value);
        var general = Form.Section(null,
            Form.Row("Name", name, $"Id: {rule.Id}"),
            Form.Row("Enabled", enabled, "Routing skips a rule that is turned off."));

        return Form.Page(
            header,
            general,
            ConditionSection(rule, bindings),
            ActionsSection(rule.Actions, "the rewritten URL goes on to the next rule.", bindings));
    }

    private static NSView CatchAllEditor(CatchAllViewModel catchAll, Bindings bindings)
    {
        var title = Ui.Label("When No Rule Matches");
        var subtitle = Ui.Flowing("The catch-all rule comes after every other rule and has no condition; only its actions can be changed.");
        var header = EditorParts.Header(EditorParts.SymbolBadge("arrow.turn.down.right", NSColor.SystemGray, 32), title, subtitle, catchAll, nameof(CatchAllViewModel.Issues), () => catchAll.Issues, bindings);
        return Form.Page(header, ActionsSection(catchAll.Actions, "the default profile opens the rewritten URL.", bindings));
    }

    private NSView ConditionSection(RuleViewModel rule, Bindings bindings)
    {
        const string Footer = "Host is the host name alone (www.example.com) and never case-sensitive; URL is the whole URL. "
            + "“matches” takes * for any characters and ? for one; “matches regex” takes a .NET regular expression. "
            + "Hold ⌥ and click + to nest a group. A rule without a condition never matches.";
        if (ConditionPredicates.ToEditor(rule.Model.Condition) is not { } tree)
        {
            // A condition type from a newer Preferis: kept as it is, and only replaceable as a whole.
            var json = Ui.Flowing(JsonSerializer.SerializeToElement(rule.Model, PreferisJsonContext.Default.Rule).GetProperty("condition").ToString(), 11, NSColor.Label);
            json.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
            json.Selectable = true;
            var replace = Ui.Button("Replace with a New Condition", () =>
            {
                rule.SetCondition(new HostCondition { Match = MatchKind.Exact, Pattern = "" });
                ShowEditor();
            });
            return Form.Section("If",
                Form.Row("This condition comes from a newer version of Preferis", replace, "It cannot be edited here and is saved unchanged."),
                Form.Wide(json));
        }

        var editor = new ConditionEditor(tree, edited => rule.SetCondition(ConditionPredicates.FromEditor(edited)));
        bindings.Add(editor.Dispose);
        return Form.Section("If", Footer, Form.Wide(editor.View));
    }

    /// <summary>
    /// "Then": one group per action, in the order they run, with "Add Action" beside the title.
    /// </summary>
    /// <param name="afterRewrites">What happens when only rewrites ran (docs/Design.md §6.1).</param>
    private static NSView ActionsSection(ActionListViewModel actions, string afterRewrites, Bindings bindings)
    {
        var add = Ui.PullDown("Add Action", ActionListViewModel.Kinds.Select(k => (k.Title, (Action)(() => actions.AddCommand.Execute(k.Value)))), bordered: true);
        var groups = Ui.VStack(Form.GroupSpacing);
        var section = Ui.VStack(8,
            Form.Header("Then", add),
            groups,
            Form.Footer("Actions run from top to bottom. Every action but Rewrite ends the routing; when only rewrites ran, " + afterRewrites));
        foreach (var view in section.ArrangedSubviews)
        {
            Ui.FillWidth(view, section);
        }

        Bindings? itemBindings = null;
        bindings.Add(() => itemBindings?.Dispose());
        bindings.ObserveCollection(actions.Items, () =>
        {
            itemBindings?.Dispose();
            itemBindings = new Bindings();
            foreach (var view in groups.ArrangedSubviews)
            {
                view.RemoveFromSuperview();
            }
            var views = actions.Items.Count == 0
                ? [Form.Group(Form.Row(Ui.Label("No actions yet: a rule needs at least one.", color: NSColor.SystemRed), null))]
                : actions.Items.Select(action => ActionGroup(action, itemBindings)).ToArray();
            foreach (var view in views)
            {
                groups.AddArrangedSubview(view);
                Ui.FillWidth(view, groups);
            }
        });
        return section;
    }

    private static NSView ActionGroup(ActionViewModel action, Bindings bindings)
    {
        var buttons = Ui.HStack(2,
            SmallButton("chevron.up", "Move up", action.MoveUpCommand, bindings),
            SmallButton("chevron.down", "Move down", action.MoveDownCommand, bindings),
            SmallButton("minus.circle", "Remove this action", action.RemoveCommand, bindings));
        var title = $"{action.Number}  {action.Title}";
        return action switch
        {
            OpenActionViewModel open => Form.Group(Form.Row(title, buttons), OpenRow(open, bindings)),
            RewriteActionViewModel rewrite => RewriteGroup(Form.Row(title, buttons), rewrite, bindings),
            SimpleActionViewModel simple => Form.Group(Form.Row(title, buttons, simple.Description)),
            UnknownActionViewModel unknown => Form.Group(Form.Row(title, buttons, "From a newer version of Preferis; saved unchanged."), Form.Wide(Json(unknown.Json))),
            _ => Form.Group(Form.Row(title, buttons)),
        };
    }

    private static NSButton SmallButton(string symbol, string tip, System.Windows.Input.ICommand command, Bindings bindings)
    {
        var button = NSButton.CreateButton(Images.Symbol(symbol, 13)!, () => { });
        button.Bordered = false;
        button.ToolTip = tip;
        button.ContentTintColor = NSColor.SecondaryLabel;
        bindings.Command(button, command);
        return button;
    }

    // The checked profiles, in the order the picker shows them; none checked opens the default profile.
    private static NSView OpenRow(OpenActionViewModel open, Bindings bindings)
    {
        var checks = Ui.VStack(6);
        foreach (var toggle in open.Profiles)
        {
            var check = Ui.Checkbox(toggle.Name);
            if (toggle.IsMissing)
            {
                check.ContentTintColor = NSColor.SystemRed;
                check.ToolTip = "No profile has this id any more; uncheck it to remove it.";
            }
            bindings.Check(check, toggle, nameof(ProfileToggleViewModel.IsSelected), () => toggle.IsSelected, value => toggle.IsSelected = value);
            checks.AddArrangedSubview(check);
        }
        if (open.Profiles.Count == 0)
        {
            checks.AddArrangedSubview(Ui.Secondary("Add profiles on the Profiles page.", 13));
        }
        var note = Ui.Flowing("");
        // Every check and uncheck raises UsesDefaultProfile.
        bindings.Observe(open, () => note.StringValue = open.Profiles.Count(p => p.IsSelected) switch
        {
            0 => "None checked: the default profile opens the URL.",
            1 => "The checked profile opens the URL.",
            _ => "The picker offers the checked profiles, in the order of the Profiles page.",
        }, nameof(OpenActionViewModel.UsesDefaultProfile));
        return Form.Row(Ui.Label("Profiles"), checks, note);
    }

    private static NSView RewriteGroup(NSView header, RewriteActionViewModel rewrite, Bindings bindings)
    {
        var mode = Ui.Popup();
        bindings.Choice(mode, rewrite, nameof(RewriteActionViewModel.Mode),
            RewriteActionViewModel.Modes.Select(m => (m.Value, m.Title)).ToList(),
            () => rewrite.Mode, value => rewrite.Mode = value);

        var pattern = Ui.Field("^http://", monospaced: true);
        bindings.Text(pattern, rewrite, nameof(RewriteActionViewModel.Pattern), () => rewrite.Pattern, value => rewrite.Pattern = value);
        var error = Ui.Flowing("", 11, NSColor.SystemRed);
        var replacement = Ui.Field("https://", monospaced: true);
        bindings.Text(replacement, rewrite, nameof(RewriteActionViewModel.Replacement), () => rewrite.Replacement, value => rewrite.Replacement = value);
        var parameter = Ui.Field("url", monospaced: true);
        bindings.Text(parameter, rewrite, nameof(RewriteActionViewModel.Parameter), () => rewrite.Parameter, value => rewrite.Parameter = value);

        var patternRow = Form.Row(Ui.Label("Pattern"), pattern, error);
        var replacementRow = Form.Row("Replace with", replacement, "Applied to the whole URL, with a lower-case scheme and host and no default port. $1, $2… insert the groups.");
        var parameterRow = Form.Row("Parameter", parameter, "Its value becomes the URL, as with links wrapped by mail and chat services.");
        var group = Form.Group(header, Form.Row("Method", mode), patternRow, replacementRow, parameterRow);
        bindings.Observe(rewrite, () =>
        {
            error.StringValue = rewrite.PatternError ?? "";
            error.Hidden = !rewrite.IsRegex || rewrite.PatternError is null;
            patternRow.Hidden = replacementRow.Hidden = !rewrite.IsRegex;
            parameterRow.Hidden = !rewrite.IsQueryParameter;
            Form.UpdateSeparators(group);
        }, nameof(RewriteActionViewModel.Mode), nameof(RewriteActionViewModel.PatternError));
        return group;
    }

    private static NSTextField Json(string json)
    {
        var text = Ui.Flowing(json, 11, NSColor.Label);
        text.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
        text.Selectable = true;
        return text;
    }
}
