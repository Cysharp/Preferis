using System.Text.Json;
using System.Windows.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Windows.Controls;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows.Pages;

/// <summary>The rules in order, the catch-all rule below them, and the selected one's condition and actions.</summary>
internal sealed class RulesPage : IPageView
{
    private readonly RulesPageViewModel _page;
    private readonly Bindings _bindings = new();
    private readonly ListPane<RuleViewModel> _rules;
    private readonly ListView _catchAll;
    private readonly ContentControl _editorHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private Bindings? _editorBindings;
    private object? _shown;
    private bool _updatingCatchAll;

    public RulesPage(RulesPageViewModel page)
    {
        _page = page;
        _rules = new ListPane<RuleViewModel>(page.Rules, Row, rule => page.SelectedRule = rule, page.MoveRule);

        // A list of its own: it always comes last and cannot be moved or deleted.
        _catchAll = new ListView { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0, 4, 0, 4) };
        var catchAllRow = CatchAllRow(page.CatchAll, _bindings);
        _catchAll.Items.Add(new ListViewItem { Content = catchAllRow, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 12, 8) });
        _catchAll.SelectionChanged += (_, _) =>
        {
            if (!_updatingCatchAll && _catchAll.SelectedIndex == 0)
            {
                page.SelectCatchAllCommand.Execute(null);
            }
        };
        var catchAllBox = Form.Box(_catchAll);
        catchAllBox.Padding = new Thickness(0);
        catchAllBox.MinHeight = 0;

        // The rules fill the height; the catch-all sits under them, above the buttons.
        _rules.View.RowDefinitions.Insert(1, new RowDefinition { Height = Ui.Auto });
        Grid.SetRow(_rules.Bar, 2);
        Grid.SetRow(catchAllBox, 1);
        _rules.View.Children.Add(catchAllBox);

        var add = Ui.IconButton(Ui.Glyphs.Add, L("Add a rule below the selected one"));
        _bindings.Command(add, page.AddRuleCommand);
        var remove = Ui.IconButton(Ui.Glyphs.Remove, L("Delete the selected rule"));
        _bindings.Command(remove, page.DeleteRuleCommand);
        var more = Ui.IconButton(Ui.Glyphs.More, L("More"));
        var menu = new MenuFlyout();
        menu.Items.Add(Bindings.MenuItem(L("Duplicate"), page.DuplicateRuleCommand));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Move Up"), page.MoveRuleUpCommand));
        menu.Items.Add(Bindings.MenuItem(L("Move Down"), page.MoveRuleDownCommand));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Import from CSV…"), page.ImportRulesCommand));
        more.Flyout = menu;
        _rules.Bar.Children.Add(add);
        _rules.Bar.Children.Add(remove);
        _rules.Bar.Children.Add(more);

        View = ProfilesPage.Split(_rules.View, _editorHost);
        _bindings.Observe(page, ShowSelected, nameof(RulesPageViewModel.CurrentEditor));
    }

    public UIElement View { get; }

    public void Dispose()
    {
        _editorBindings?.Dispose();
        _rules.Dispose();
        _bindings.Dispose();
    }

    private static UIElement Row(RuleViewModel rule, Bindings bindings)
    {
        var enabled = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(enabled, L("Turn the rule on or off"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(enabled, L("Enabled"));
        bindings.Check(enabled, rule, nameof(RuleViewModel.Enabled), () => rule.Enabled, value => rule.Enabled = value);

        var name = Ui.Label("");
        var summary = Ui.Secondary("");
        bindings.Observe(rule, () =>
        {
            name.Text = rule.Name.Length > 0 ? rule.Name : L("(Unnamed)");
            name.Foreground = Ui.Brush(rule.Enabled ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");
            summary.Text = rule.Summary;
            ToolTipService.SetToolTip(summary, rule.Summary);
        }, nameof(RuleViewModel.Name), nameof(RuleViewModel.Summary), nameof(RuleViewModel.Enabled));

        var problem = ProblemBadge();
        bindings.Visible(problem, rule, () => rule.HasIssues, nameof(RuleViewModel.HasIssues));
        return Ui.Columns(4, (enabled, Ui.Auto), (Ui.VStack(0, name, summary), Ui.Star), (problem, Ui.Auto));
    }

    private static UIElement CatchAllRow(CatchAllViewModel catchAll, Bindings bindings)
    {
        var summary = Ui.Secondary("");
        bindings.Observe(catchAll, () =>
        {
            summary.Text = catchAll.Summary;
            ToolTipService.SetToolTip(summary, catchAll.Summary);
        }, nameof(CatchAllViewModel.Summary));
        var problem = ProblemBadge();
        bindings.Visible(problem, catchAll, () => catchAll.HasIssues, nameof(CatchAllViewModel.HasIssues));
        var icon = Ui.Glyph(Ui.Glyphs.Next, 14, Ui.Brush("TextFillColorSecondaryBrush"));
        icon.Width = 28;
        return Ui.Columns(4, (icon, Ui.Auto), (Ui.VStack(0, Ui.Label(L("When No Rule Matches")), summary), Ui.Star), (problem, Ui.Auto));
    }

    private static FontIcon ProblemBadge()
    {
        var badge = Ui.Glyph(Ui.Glyphs.Error, 14, Ui.Brush("SystemFillColorCriticalBrush"));
        ToolTipService.SetToolTip(badge, L("This rule has problems"));
        return badge;
    }

    private void ShowSelected()
    {
        _rules.Select(_page.SelectedRule);
        _updatingCatchAll = true;
        _catchAll.SelectedIndex = _page.IsCatchAllSelected ? 0 : -1;
        _updatingCatchAll = false;
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
        _editorBindings = new Bindings();
        _editorHost.Content = _shown switch
        {
            RuleViewModel rule => RuleEditor(rule, _editorBindings),
            CatchAllViewModel catchAll => CatchAllEditor(catchAll, _editorBindings),
            _ => Form.Empty(L("No Rule Selected"), L("Click + below the list to add a rule.")),
        };
    }

    private UIElement RuleEditor(RuleViewModel rule, Bindings bindings)
    {
        var title = Ui.Label("");
        var subtitle = Ui.Secondary("");
        bindings.Observe(rule, () =>
        {
            title.Text = rule.Name.Length > 0 ? rule.Name : L("(Unnamed)");
            subtitle.Text = rule.Summary;
        }, nameof(RuleViewModel.Name), nameof(RuleViewModel.Summary));
        var header = EditorParts.Header(Ui.Badge(Ui.Glyphs.Branch, Colors.DarkOrange), title, subtitle, rule, nameof(RuleViewModel.Issues), () => rule.Issues, bindings);

        var name = Ui.TextBox(L("Name"));
        bindings.Text(name, rule, nameof(RuleViewModel.Name), () => rule.Name, value => rule.Name = value);
        var enabled = Ui.Switch();
        bindings.Switch(enabled, rule, nameof(RuleViewModel.Enabled), () => rule.Enabled, value => rule.Enabled = value);
        var general = Form.Section(null,
            Form.Card(L("Name"), name, L("ID: {0}", rule.Id)),
            Form.Card(L("Enabled"), enabled, L("Disabled rules are skipped during routing.")));

        return EditorPage(
            header,
            general,
            ConditionSection(rule),
            ActionsSection(rule.Actions, L("Actions run from top to bottom. Every action but Rewrite ends the routing; when only rewrites ran, the rewritten URL goes on to the next rule."), bindings));
    }

    private static UIElement CatchAllEditor(CatchAllViewModel catchAll, Bindings bindings)
    {
        var title = Ui.Label(L("When No Rule Matches"));
        var subtitle = Ui.Secondary(L("The catch-all rule comes after every other rule and has no condition; only its actions can be changed."));
        var header = EditorParts.Header(Ui.Badge(Ui.Glyphs.Next, Colors.SlateGray), title, subtitle, catchAll, nameof(CatchAllViewModel.Issues), () => catchAll.Issues, bindings);
        return EditorPage(header, ActionsSection(catchAll.Actions, L("Actions run from top to bottom. Every action but Rewrite ends the routing; when only rewrites ran, the default profile opens the rewritten URL."), bindings));
    }

    private static ScrollViewer EditorPage(params UIElement[] sections)
    {
        var page = Form.Page(sections);
        page.Padding = new Thickness(16, 0, 32, 0);
        return page;
    }

    private UIElement ConditionSection(RuleViewModel rule)
    {
        var footer = L("Host is the host name alone (www.example.com) and never case-sensitive; URL is the whole URL. “matches” takes * for any characters and ? for one; “matches regex” takes a .NET regular expression. A rule without a condition never matches.");
        if (ConditionPredicates.ToEditor(rule.Model.Condition) is not { } tree)
        {
            // A condition type from a newer Preferis: kept as it is, and only replaceable as a whole.
            var json = Ui.Code(JsonSerializer.SerializeToElement(rule.Model, PreferisJsonContext.Default.Rule).GetProperty("condition").ToString());
            var replace = Ui.Button(L("Replace with a New Condition"), () =>
            {
                rule.SetCondition(new HostCondition { Match = MatchKind.Exact, Pattern = "" });
                ShowEditor();
            });
            return Form.Section(L("If"),
                Form.Card(L("This condition comes from a newer version of Preferis"), replace, L("It cannot be edited here and is saved unchanged.")),
                Form.Wide(json));
        }

        var editor = new ConditionEditor(tree, edited => rule.SetCondition(ConditionPredicates.FromEditor(edited, rule.Model.Condition)));
        return Form.Section(L("If"), footer, Form.Wide(editor.View));
    }

    /// <summary>"Then": one card per action, in the order they run, with "Add Action" beside the title.</summary>
    /// <param name="footer">How the actions run, ending with what happens when only rewrites ran (docs/Design.md §6.1).</param>
    private static UIElement ActionsSection(ActionListViewModel actions, string footer, Bindings bindings)
    {
        var add = new DropDownButton { Content = L("Add Action") };
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var kind in ActionListViewModel.Kinds)
        {
            menu.Items.Add(Bindings.MenuItem(kind.Title, actions.AddCommand, kind.Value));
        }
        add.Flyout = menu;

        var cards = new StackPanel { Spacing = Form.CardSpacing };
        var section = Ui.VStack(Form.CardSpacing,
            Form.Header(L("Then"), add),
            cards,
            Form.Footer(footer));

        Bindings? itemBindings = null;
        bindings.Add(() => itemBindings?.Dispose());
        bindings.ObserveCollection(actions.Items, () =>
        {
            itemBindings?.Dispose();
            itemBindings = new Bindings();
            cards.Children.Clear();
            if (actions.Items.Count == 0)
            {
                cards.Children.Add(Form.Card(Ui.Label(L("No actions yet: a rule needs at least one action."), color: Ui.Brush("SystemFillColorCriticalBrush")), null));
            }
            foreach (var action in actions.Items)
            {
                cards.Children.Add(ActionCard(action, itemBindings));
            }
        });
        return section;
    }

    private static UIElement ActionCard(ActionViewModel action, Bindings bindings)
    {
        var buttons = Ui.HStack(0,
            SmallButton(Ui.Glyphs.Up, L("Move up"), action.MoveUpCommand, bindings),
            SmallButton(Ui.Glyphs.Down, L("Move down"), action.MoveDownCommand, bindings),
            SmallButton(Ui.Glyphs.Delete, L("Remove this action"), action.RemoveCommand, bindings));
        var title = Ui.Label($"{action.Number}  {action.Title}", semibold: true);
        var header = Ui.Columns(8, (title, Ui.Star), (buttons, Ui.Auto));

        var body = action switch
        {
            OpenActionViewModel open => OpenBody(open, bindings),
            RewriteActionViewModel rewrite => RewriteBody(rewrite, bindings),
            SimpleActionViewModel simple => Ui.Wrapping(simple.Description),
            UnknownActionViewModel unknown => Ui.VStack(6, Ui.Wrapping(L("From a newer version of Preferis; saved unchanged.")), Ui.Code(unknown.Json)),
            _ => null,
        };
        var content = body is null ? (UIElement)header : Ui.VStack(10, header, body);
        return Form.Box(content);
    }

    private static Button SmallButton(string glyph, string tip, ICommand command, Bindings bindings)
    {
        var button = Ui.IconButton(glyph, tip, 12);
        bindings.Command(button, command);
        return button;
    }

    // The checked profiles, in the order the picker shows them; none checked opens the default profile.
    private static UIElement OpenBody(OpenActionViewModel open, Bindings bindings)
    {
        var checks = new StackPanel { Spacing = 0 };
        foreach (var toggle in open.Profiles)
        {
            var check = new CheckBox { Content = toggle.Name };
            if (toggle.IsMissing)
            {
                check.Foreground = Ui.Brush("SystemFillColorCriticalBrush");
                ToolTipService.SetToolTip(check, L("No profile has this id any more; uncheck it to remove it."));
            }
            bindings.Check(check, toggle, nameof(ProfileToggleViewModel.IsSelected), () => toggle.IsSelected, value => toggle.IsSelected = value);
            checks.Children.Add(check);
        }
        if (open.Profiles.Count == 0)
        {
            checks.Children.Add(Ui.Secondary(L("Add profiles on the Profiles page."), 14));
        }
        var note = Ui.Wrapping("");
        // Every check and uncheck raises UsesDefaultProfile.
        bindings.Observe(open, () => note.Text = open.Profiles.Count(p => p.IsSelected) switch
        {
            0 => L("None checked: the default profile opens the URL."),
            1 => L("The checked profile opens the URL."),
            _ => L("The picker shows the checked profiles."),
        }, nameof(OpenActionViewModel.UsesDefaultProfile));
        return Ui.VStack(4, checks, note);
    }

    private static UIElement RewriteBody(RewriteActionViewModel rewrite, Bindings bindings)
    {
        var mode = Ui.ComboBox(300);
        bindings.Choice(mode, rewrite, nameof(RewriteActionViewModel.Mode),
            RewriteActionViewModel.Modes.Select(m => (m.Value, m.Title)).ToList(),
            () => rewrite.Mode, value => rewrite.Mode = value);

        var pattern = Ui.TextBox("^http://", monospaced: true, width: 300);
        bindings.Text(pattern, rewrite, nameof(RewriteActionViewModel.Pattern), () => rewrite.Pattern, value => rewrite.Pattern = value);
        var error = Ui.Wrapping("", 12, Ui.Brush("SystemFillColorCriticalBrush"));
        var replacement = Ui.TextBox("https://", monospaced: true, width: 300);
        bindings.Text(replacement, rewrite, nameof(RewriteActionViewModel.Replacement), () => rewrite.Replacement, value => rewrite.Replacement = value);
        var parameter = Ui.TextBox("url", monospaced: true, width: 300);
        bindings.Text(parameter, rewrite, nameof(RewriteActionViewModel.Parameter), () => rewrite.Parameter, value => rewrite.Parameter = value);

        var patternRow = Ui.VStack(2, Line(L("Pattern"), pattern, null), error);
        var replacementRow = Line(L("Replace with"), replacement, L("Applied to the whole URL, with a lower-case scheme and host and no default port. $1, $2… insert the groups."));
        var parameterRow = Line(L("Parameter"), parameter, L("Its value becomes the URL, as with links wrapped by mail and chat services."));
        bindings.Observe(rewrite, () =>
        {
            error.Text = rewrite.PatternError ?? "";
            Ui.Show(error, rewrite.IsRegex && rewrite.PatternError is not null);
            Ui.Show(patternRow, rewrite.IsRegex);
            Ui.Show(replacementRow, rewrite.IsRegex);
            Ui.Show(parameterRow, rewrite.IsQueryParameter);
        }, nameof(RewriteActionViewModel.Mode), nameof(RewriteActionViewModel.PatternError));
        return Ui.VStack(8, Line(L("Method"), mode, null), patternRow, replacementRow, parameterRow);
    }

    // A label and its control inside an action card.
    private static UIElement Line(string label, FrameworkElement control, string? detail)
    {
        var texts = Ui.VStack(2, Ui.Label(label));
        if (detail is not null)
        {
            texts.Children.Add(Ui.Wrapping(detail));
        }
        return Ui.Columns(16, (texts, Ui.Star), (control, Ui.Auto));
    }
}
