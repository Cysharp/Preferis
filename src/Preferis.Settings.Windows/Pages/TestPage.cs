using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Windows.Controls;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows.Pages;

/// <summary>Routes a URL with the configuration being edited (Test) or hands it to Preferis like a clicked link (Open).</summary>
internal sealed class TestPage : IPageView
{
    private readonly Bindings _bindings = new();

    public TestPage(TestPageViewModel page)
    {
        var url = Input(page, "https://www.example.com/", "URL", nameof(TestPageViewModel.Url), () => page.Url, value => page.Url = value);
        var test = Ui.AccentButton(L("Test"));
        _bindings.Command(test, page.RunCommand);
        var open = Ui.Button(L("Open"));
        ToolTipService.SetToolTip(open, L("Open the URL with Preferis, as if a link was clicked: the picker appears when the rules say so."));
        _bindings.Command(open, page.OpenCommand);

        var appId = Input(page, L("App ID"), L("App ID"), nameof(TestPageViewModel.AppId), () => page.AppId, value => page.AppId = value);
        var appPath = Input(page, L("App path"), L("App path"), nameof(TestPageViewModel.AppPath), () => page.AppPath, value => page.AppPath = value);
        var openedBy = Ui.Secondary(L("Opened by"));
        openedBy.VerticalAlignment = VerticalAlignment.Center;
        var appHint = Ui.Wrapping(L("Test routes the URL as if this app had opened it, for the rules that look at the app. Open does not use it."));
        var input = Form.Wide(Ui.VStack(8,
            Ui.Columns(8, (url, Ui.Star), (test, Ui.Auto), (open, Ui.Auto)),
            Ui.Columns(8, (openedBy, Ui.Auto), (appId, Ui.Star), (appPath, Ui.Star)),
            appHint));

        var error = Ui.Wrapping("", 12, Ui.Brush("SystemFillColorCriticalBrush"));
        _bindings.Observe(page, () =>
        {
            error.Text = page.Error ?? "";
            Ui.Show(error, page.Error is not null);
        }, nameof(TestPageViewModel.Error));

        var intro = Form.Section(null,
            page.CanOpenUrls
                ? L("Test shows how the configuration being edited routes the URL, unsaved changes included, without opening anything. Open hands it to Preferis like a clicked link, so you can test how the picker and browser work.")
                : L("Test shows how the configuration being edited routes the URL, unsaved changes included, without opening anything. Open hands it to Preferis like a clicked link, so you can test how the picker and browser work. Open needs Preferis Settings to run next to Preferis.exe."),
            input,
            error);
        intro.Margin = new Thickness(0, 8, 0, 0);

        View = Form.Page(intro, Result(page), Opened(page), Steps(page), History(page));
    }

    public UIElement View { get; }

    public void Dispose() => _bindings.Dispose();

    // Enter in any of the boxes tests the URL, like the Test button.
    private TextBox Input(TestPageViewModel page, string placeholder, string name, string property, Func<string> get, Action<string> set)
    {
        var box = new TextBox { PlaceholderText = placeholder, FontFamily = Ui.Monospace };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, name);
        _bindings.Text(box, page, property, get, set);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter)
            {
                page.RunCommand.Execute(null);
                e.Handled = true;
            }
        };
        return box;
    }

    private UIElement Result(TestPageViewModel page)
    {
        var decision = Ui.Label("", semibold: true);
        decision.TextWrapping = TextWrapping.Wrap;
        var routed = Ui.Wrapping("");
        var candidates = new StackPanel { Spacing = 8 };
        var section = Form.Section(L("Result"), Form.Wide(Ui.VStack(8, decision, routed, candidates)));

        _bindings.Observe(page, () =>
        {
            Ui.Show(section, page.HasResult);
            decision.Text = page.Decision ?? "";
            routed.Text = page.RoutedUrl is { } url ? L("URL after rewriting: {0}", url) : "";
            Ui.Show(routed, page.RoutedUrl is not null);
        }, nameof(TestPageViewModel.Decision), nameof(TestPageViewModel.RoutedUrl));
        _bindings.Observe(page, () =>
        {
            candidates.Children.Clear();
            foreach (var candidate in page.Candidates)
            {
                var mark = Ui.Glyph(candidate.IsResolved ? Ui.Glyphs.Completed : Ui.Glyphs.Warning, 14,
                    Ui.Brush(candidate.IsResolved ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"));
                mark.VerticalAlignment = VerticalAlignment.Top;
                mark.Margin = new Thickness(0, 3, 0, 0);
                candidates.Children.Add(Ui.Columns(8, (mark, Ui.Auto), (Ui.VStack(2, Ui.Label(candidate.Name), Ui.Code(candidate.Detail)), Ui.Star)));
            }
        }, nameof(TestPageViewModel.Candidates));
        return section;
    }

    private UIElement Opened(TestPageViewModel page)
    {
        var result = Ui.Label("", semibold: true);
        var output = Ui.Code("");
        var section = Form.Section(L("Opened"), Form.Wide(Ui.VStack(6, result, output)));
        _bindings.Observe(page, () =>
        {
            Ui.Show(section, page.OpenResult is not null);
            result.Text = page.OpenResult ?? "";
            output.Text = string.Join("\n", page.OpenOutput);
            Ui.Show(output, page.OpenOutput.Count > 0);
        }, nameof(TestPageViewModel.OpenResult), nameof(TestPageViewModel.OpenOutput));
        return section;
    }

    private UIElement Steps(TestPageViewModel page)
    {
        var rows = new StackPanel { Spacing = 6 };
        var section = Form.Section(L("Steps"), Form.Wide(rows));
        _bindings.Observe(page, () =>
        {
            Ui.Show(section, page.Trace.Count > 0);
            rows.Children.Clear();
            foreach (var step in page.Trace)
            {
                var kind = Ui.Secondary(step.Kind);
                var rule = Ui.Label(step.Rule ?? "", 12, semibold: true);
                ToolTipService.SetToolTip(rule, step.Rule);
                var message = Ui.Wrapping(step.Message, 12, Ui.Brush("TextFillColorPrimaryBrush"));
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star });
                row.Children.Add(kind);
                Grid.SetColumn(rule, 1);
                row.Children.Add(rule);
                Grid.SetColumn(message, 2);
                row.Children.Add(message);
                kind.VerticalAlignment = rule.VerticalAlignment = VerticalAlignment.Top;
                rows.Children.Add(row);
            }
        }, nameof(TestPageViewModel.Trace));
        return section;
    }

    private UIElement History(TestPageViewModel page)
    {
        var rows = new StackPanel { Spacing = 4 };
        var empty = Ui.Secondary(L("Nothing yet."));
        var clear = Ui.Button(L("Clear History"));
        _bindings.Command(clear, page.ClearHistoryCommand);
        var section = Ui.VStack(Form.CardSpacing, Form.Header(L("Recently Opened"), clear), Form.Wide(Ui.VStack(4, empty, rows)));

        _bindings.Visible(section, page, () => page.IsHistoryEnabled, nameof(TestPageViewModel.IsHistoryEnabled));
        _bindings.Observe(page, () =>
        {
            Ui.Show(empty, page.History.Count == 0);
            rows.Children.Clear();
            foreach (var entry in page.History)
            {
                var url = Ui.Label(entry.Url, 12);
                url.FontFamily = Ui.Monospace;
                ToolTipService.SetToolTip(url, entry.Url);
                var app = Ui.Secondary(entry.App ?? "");
                app.MaxWidth = 160;
                app.TextTrimming = TextTrimming.CharacterEllipsis;
                if (entry.App is not null)
                {
                    ToolTipService.SetToolTip(app, string.Join("\n", new[] { entry.AppPath, entry.AppId }.OfType<string>()));
                }
                var again = Ui.Button(L("Test"));
                again.Padding = new Thickness(8, 2, 8, 2);
                var line = entry;
                again.Click += (_, _) => page.TestHistoryEntryCommand.Execute(line);
                rows.Children.Add(Ui.Columns(8, (Ui.Secondary(entry.Time), new GridLength(130)), (url, Ui.Star), (app, Ui.Auto), (Ui.Secondary(entry.Rule), Ui.Auto), (again, Ui.Auto)));
            }
        }, nameof(TestPageViewModel.History));
        return section;
    }
}
