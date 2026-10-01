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
        _catchAll.Box.HeightAnchor.ConstraintEqualTo((nfloat)(RowHeight + 12)).Active = true;
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
            _ => Empty(),
        };
        _editorHost.AddSubview(editor);
        Ui.Pin(editor, _editorHost);
    }

    private static NSView Empty()
    {
        var text = Ui.VStack(6,
            Ui.Label("No Rule Selected", 17, NSFontWeight.Semibold, NSColor.SecondaryLabel),
            Ui.Wrapping("Click + below the list to add a rule.", 320, 12));
        text.Alignment = NSLayoutAttribute.CenterX;
        var host = new NSView();
        host.AddSubview(text);
        text.TranslatesAutoresizingMaskIntoConstraints = false;
        text.CenterXAnchor.ConstraintEqualTo(host.CenterXAnchor).Active = true;
        text.CenterYAnchor.ConstraintEqualTo(host.CenterYAnchor).Active = true;
        return host;
    }

    private static NSView RuleEditor(RuleViewModel rule, Bindings bindings)
    {
        var name = Ui.Field("Name");
        name.Font = NSFont.SystemFontOfSize(17, NSFontWeight.Semibold)!;
        bindings.Text(name, rule, nameof(RuleViewModel.Name), () => rule.Name, value => rule.Name = value);
        var id = Ui.Flowing($"Id: {rule.Id}");
        var header = Ui.VStack(4, name, id);
        Ui.FillWidth(name, header);
        Ui.FillWidth(id, header);

        var enabled = new NSSwitch();
        bindings.Switch(enabled, rule, nameof(RuleViewModel.Enabled), () => rule.Enabled, value => rule.Enabled = value);
        var general = Form.Section(null, Form.Row("Enabled", enabled, "Routing skips a rule that is turned off."));

        return Page(
            header,
            Issues(rule, nameof(RuleViewModel.Issues), () => rule.Issues, bindings),
            general,
            ConditionSection(rule, bindings),
            ActionsSection(rule.Actions, "the rewritten URL goes on to the next rule.", bindings));
    }

    private static NSView CatchAllEditor(CatchAllViewModel catchAll, Bindings bindings)
    {
        var header = Ui.VStack(4,
            Ui.Label("When No Rule Matches", 17, NSFontWeight.Semibold),
            Ui.Flowing("The catch-all rule comes after every other rule and has no condition; only its actions can be changed."));
        Ui.FillWidth(header.ArrangedSubviews[1], header);
        return Page(
            header,
            Issues(catchAll, nameof(CatchAllViewModel.Issues), () => catchAll.Issues, bindings),
            ActionsSection(catchAll.Actions, "the default profile opens the rewritten URL.", bindings));
    }

    private static NSView Page(params NSView[] views)
    {
        var stack = Ui.VStack(22, views);
        foreach (var view in views)
        {
            Ui.FillWidth(view, stack);
        }
        return Ui.ScrollPage(stack);
    }

    private static NSView Issues(System.ComponentModel.INotifyPropertyChanged source, string property, Func<IReadOnlyList<ConfigIssue>> issues, Bindings bindings)
    {
        var list = Ui.VStack(4);
        bindings.Observe(source, () =>
        {
            foreach (var view in list.ArrangedSubviews)
            {
                view.RemoveFromSuperview();
            }
            foreach (var issue in issues())
            {
                var message = Ui.Flowing(issue.Message, 12, issue.Severity == IssueSeverity.Error ? NSColor.SystemRed : NSColor.SystemOrange);
                list.AddArrangedSubview(message);
                Ui.FillWidth(message, list);
            }
            list.Hidden = issues().Count == 0;
        }, property);
        return list;
    }

    private static NSView ConditionSection(RuleViewModel rule, Bindings bindings)
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
            var replace = Ui.Button("Replace with a New Condition", () => rule.SetCondition(new HostCondition { Match = MatchKind.Exact, Pattern = "" }));
            var unknown = Ui.VStack(8, Ui.Flowing("This condition comes from a newer version of Preferis and cannot be edited here. It is saved unchanged.", 12, NSColor.Label), json, replace);
            foreach (var view in unknown.ArrangedSubviews.Take(2))
            {
                Ui.FillWidth(view, unknown);
            }
            return Form.Section("If", Form.Wide(unknown));
        }

        var editor = new ConditionEditor(tree, edited => rule.SetCondition(ConditionPredicates.FromEditor(edited)));
        bindings.Add(editor.Dispose);
        return Form.Section("If", Footer, Form.Wide(editor.View));
    }

    /// <param name="afterRewrites">What happens when only rewrites ran (docs/Design.md §6.1).</param>
    private static NSView ActionsSection(ActionListViewModel actions, string afterRewrites, Bindings bindings)
    {
        var list = Ui.VStack(0);
        Bindings? itemBindings = null;
        bindings.Add(() => itemBindings?.Dispose());
        bindings.ObserveCollection(actions.Items, () =>
        {
            itemBindings?.Dispose();
            itemBindings = new Bindings();
            foreach (var view in list.ArrangedSubviews)
            {
                view.RemoveFromSuperview();
            }
            if (actions.Items.Count == 0)
            {
                var empty = Ui.Flowing("No actions yet: a rule needs at least one. Add one below.", 12, NSColor.SystemRed);
                list.AddArrangedSubview(empty);
                Ui.FillWidth(empty, list);
            }
            for (var i = 0; i < actions.Items.Count; i++)
            {
                if (i > 0)
                {
                    var separator = new NSBox { BoxType = NSBoxType.NSBoxSeparator };
                    list.AddArrangedSubview(separator);
                    Ui.FillWidth(separator, list);
                }
                var item = ActionView(actions.Items[i], itemBindings);
                list.AddArrangedSubview(item);
                Ui.FillWidth(item, list);
            }
        });

        var add = Ui.PullDown("Add Action", ActionListViewModel.Kinds.Select(k => (k.Title, (Action)(() => actions.AddCommand.Execute(k.Value)))));
        add.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        var section = Form.Section("Then", "Actions run from top to bottom. Every action but Rewrite ends the routing; when only rewrites ran, " + afterRewrites, Form.Wide(list));
        var stack = Ui.VStack(10, section, add);
        Ui.FillWidth(section, stack);
        return stack;
    }

    private static NSView ActionView(ActionViewModel action, Bindings bindings)
    {
        var title = Ui.Label($"{action.Number}  {action.Title}", 13, NSFontWeight.Semibold);
        var up = SmallButton("chevron.up", "Move up", action.MoveUpCommand, bindings);
        var down = SmallButton("chevron.down", "Move down", action.MoveDownCommand, bindings);
        var remove = SmallButton("minus.circle", "Remove this action", action.RemoveCommand, bindings);
        var header = Ui.HStack(4, title, Ui.Spacer(), up, down, remove);

        NSView? content = action switch
        {
            OpenActionViewModel open => OpenContent(open, bindings),
            RewriteActionViewModel rewrite => RewriteContent(rewrite, bindings),
            SimpleActionViewModel simple => Ui.Flowing(simple.Description),
            UnknownActionViewModel unknown => UnknownContent(unknown),
            _ => null,
        };
        var block = Ui.VStack(8, header);
        block.EdgeInsets = new NSEdgeInsets(10, 0, 12, 0);
        Ui.FillWidth(header, block);
        if (content is not null)
        {
            block.AddArrangedSubview(content);
            Ui.FillWidth(content, block, 0);
        }
        return block;
    }

    private static NSButton SmallButton(string symbol, string tip, System.Windows.Input.ICommand command, Bindings bindings)
    {
        var button = NSButton.CreateButton(Images.Symbol(symbol, 12)!, () => { });
        button.Bordered = false;
        button.ToolTip = tip;
        button.ContentTintColor = NSColor.SecondaryLabel;
        bindings.Command(button, command);
        return button;
    }

    // The checked profiles, in the order the picker shows them; none checked opens the default profile.
    private static NSView OpenContent(OpenActionViewModel open, Bindings bindings)
    {
        var stack = Ui.VStack(6);
        foreach (var toggle in open.Profiles)
        {
            var check = Ui.Checkbox(toggle.Name);
            if (toggle.IsMissing)
            {
                check.ContentTintColor = NSColor.SystemRed;
                check.ToolTip = "No profile has this id any more; uncheck it to remove it.";
            }
            bindings.Check(check, toggle, nameof(ProfileToggleViewModel.IsSelected), () => toggle.IsSelected, value => toggle.IsSelected = value);
            stack.AddArrangedSubview(check);
        }
        if (open.Profiles.Count == 0)
        {
            stack.AddArrangedSubview(Ui.Secondary("There are no profiles yet: add them on the Profiles page.", 12));
        }
        var note = Ui.Flowing("");
        // Every check and uncheck raises UsesDefaultProfile.
        bindings.Observe(open, () => note.StringValue = open.Profiles.Count(p => p.IsSelected) switch
        {
            0 => "None checked: the default profile opens the URL.",
            1 => "The checked profile opens the URL.",
            _ => "Several checked: the picker offers them in the order they were checked.",
        }, nameof(OpenActionViewModel.UsesDefaultProfile));
        stack.AddArrangedSubview(note);
        Ui.FillWidth(note, stack);
        return stack;
    }

    private static NSView RewriteContent(RewriteActionViewModel rewrite, Bindings bindings)
    {
        var mode = Ui.Popup();
        bindings.Choice(mode, rewrite, nameof(RewriteActionViewModel.Mode),
            RewriteActionViewModel.Modes.Select(m => (m.Value, m.Title)).ToList(),
            () => rewrite.Mode, value => rewrite.Mode = value);

        var pattern = Ui.Field("^http://", monospaced: true);
        bindings.Text(pattern, rewrite, nameof(RewriteActionViewModel.Pattern), () => rewrite.Pattern, value => rewrite.Pattern = value);
        var replacement = Ui.Field("https://", monospaced: true);
        bindings.Text(replacement, rewrite, nameof(RewriteActionViewModel.Replacement), () => rewrite.Replacement, value => rewrite.Replacement = value);
        var error = Ui.Flowing("", 11, NSColor.SystemRed);
        bindings.Observe(rewrite, () =>
        {
            error.StringValue = rewrite.PatternError ?? "";
            error.Hidden = rewrite.PatternError is null;
        }, nameof(RewriteActionViewModel.PatternError));
        var parameter = Ui.Field("url", monospaced: true);
        bindings.Text(parameter, rewrite, nameof(RewriteActionViewModel.Parameter), () => rewrite.Parameter, value => rewrite.Parameter = value);

        var patternRow = Labeled("Pattern", pattern);
        var replacementRow = Labeled("Replace with", replacement);
        var regexNote = Ui.Flowing("Applied to the whole URL, with a lower-case scheme and host and no default port. $1, $2… insert the groups.");
        var parameterRow = Labeled("Parameter", parameter);
        var parameterNote = Ui.Flowing("The value of this query parameter becomes the URL, as with links wrapped by mail and chat services.");
        var stack = Ui.VStack(8, mode, patternRow, error, replacementRow, regexNote, parameterRow, parameterNote);
        foreach (var view in stack.ArrangedSubviews.Skip(1))
        {
            Ui.FillWidth(view, stack);
        }
        bindings.Observe(rewrite, () =>
        {
            patternRow.Hidden = replacementRow.Hidden = regexNote.Hidden = !rewrite.IsRegex;
            error.Hidden = !rewrite.IsRegex || rewrite.PatternError is null;
            parameterRow.Hidden = parameterNote.Hidden = !rewrite.IsQueryParameter;
        }, nameof(RewriteActionViewModel.Mode));
        return stack;
    }

    private static NSView Labeled(string label, NSTextField field)
    {
        var title = Ui.Label(label);
        title.WidthAnchor.ConstraintEqualTo(96).Active = true;
        field.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        return Ui.HStack(8, title, field);
    }

    private static NSView UnknownContent(UnknownActionViewModel unknown)
    {
        var json = Ui.Flowing(unknown.Json, 11, NSColor.Label);
        json.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
        json.Selectable = true;
        var note = Ui.Flowing("This action comes from a newer version of Preferis and is saved unchanged.");
        var stack = Ui.VStack(6, note, json);
        Ui.FillWidth(note, stack);
        Ui.FillWidth(json, stack);
        return stack;
    }
}
