using AppKit;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS.Pages;

/// <summary>Routes a URL with the configuration being edited (Test) or hands it to Preferis like a clicked link (Open).</summary>
internal sealed class TestPage : IPageView
{
    private const double ContentWidth = 640;
    private readonly Bindings _bindings = new();

    public TestPage(TestPageViewModel page)
    {
        var url = Ui.Field("https://www.example.com/", monospaced: true);
        _bindings.Text(url, page, nameof(TestPageViewModel.Url), () => page.Url, value => page.Url = value);
        // Return in the field tests the URL, like the Test button.
        url.Activated += (_, _) => page.RunCommand.Execute(null);
        var test = Ui.Button("Test");
        _bindings.Command(test, page.RunCommand);
        var open = Ui.Button("Open");
        open.ToolTip = "Open the URL with Preferis, as if a link was clicked: the picker appears when the rules say so.";
        _bindings.Command(open, page.OpenCommand);
        var input = Ui.HStack(8, url, test, open);
        url.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

        var error = Ui.Label("", 12, color: NSColor.SystemRed);
        _bindings.Observe(page, () =>
        {
            error.StringValue = page.Error ?? "";
            error.Hidden = page.Error is null;
        }, nameof(TestPageViewModel.Error));

        var intro = Form.Section(null,
            "Test shows how the configuration being edited routes the URL, unsaved changes included, without opening anything. Open hands it to Preferis like a clicked link, so the picker and the browser can be tried for real."
                + (page.CanOpenUrls ? "" : " Open needs Preferis Settings to run from inside Preferis.app."),
            Form.Wide(input));

        var stack = Ui.VStack(22, intro, error, Result(page), Opened(page), Steps(page), History(page));
        foreach (var view in stack.ArrangedSubviews)
        {
            Ui.FillWidth(view, stack);
        }
        View = Ui.ScrollPage(stack);
    }

    public NSView View { get; }

    public void Dispose() => _bindings.Dispose();

    private NSView Result(TestPageViewModel page)
    {
        var decision = Ui.Wrapping("", ContentWidth, 13, NSColor.Label);
        decision.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold)!;
        var routed = Ui.Wrapping("", ContentWidth, 11);
        var candidates = Ui.VStack(8);
        var section = Form.Section("Result", Form.Wide(Ui.VStack(8, decision, routed, candidates)));

        _bindings.Observe(page, () =>
        {
            section.Hidden = !page.HasResult;
            decision.StringValue = page.Decision ?? "";
            routed.StringValue = page.RoutedUrl is { } url ? $"URL after rewriting: {url}" : "";
            routed.Hidden = page.RoutedUrl is null;
        }, nameof(TestPageViewModel.Decision), nameof(TestPageViewModel.RoutedUrl));
        _bindings.ObserveCollection(page.Candidates, () =>
        {
            Clear(candidates);
            foreach (var candidate in page.Candidates)
            {
                var mark = NSImageView.FromImage(Images.Symbol(candidate.IsResolved ? "checkmark.circle.fill" : "exclamationmark.triangle.fill", 13)!);
                mark.ContentTintColor = candidate.IsResolved ? NSColor.SystemGreen : NSColor.SystemOrange;
                var detail = Ui.Wrapping(candidate.Detail, ContentWidth - 30, 11);
                detail.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
                detail.Selectable = true;
                var row = Ui.HStack(8, mark, Ui.VStack(2, Ui.Label(candidate.Name), detail));
                row.Alignment = NSLayoutAttribute.Top;
                candidates.AddArrangedSubview(row);
            }
        });
        return section;
    }

    private NSView Opened(TestPageViewModel page)
    {
        var result = Ui.Label("", 13, NSFontWeight.Semibold);
        var output = Ui.Wrapping("", ContentWidth, 11);
        output.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
        output.Selectable = true;
        var section = Form.Section("Opened", Form.Wide(Ui.VStack(6, result, output)));
        _bindings.Observe(page, () =>
        {
            section.Hidden = page.OpenResult is null;
            result.StringValue = page.OpenResult ?? "";
            output.StringValue = string.Join("\n", page.OpenOutput);
            output.Hidden = page.OpenOutput.Count == 0;
        }, nameof(TestPageViewModel.OpenResult), nameof(TestPageViewModel.OpenOutput));
        return section;
    }

    private NSView Steps(TestPageViewModel page)
    {
        var rows = Ui.VStack(6);
        var section = Form.Section("Steps", Form.Wide(rows));
        _bindings.ObserveCollection(page.Trace, () =>
        {
            section.Hidden = page.Trace.Count == 0;
            Clear(rows);
            foreach (var step in page.Trace)
            {
                var kind = Ui.Secondary(step.Kind);
                kind.WidthAnchor.ConstraintEqualTo(84).Active = true;
                var rule = Ui.Label(step.Rule ?? "", 11, NSFontWeight.Semibold);
                rule.WidthAnchor.ConstraintEqualTo(190).Active = true;
                rule.ToolTip = step.Rule;
                var message = Ui.Wrapping(step.Message, ContentWidth - 290, 11, NSColor.Label);
                message.Selectable = true;
                var row = Ui.HStack(8, kind, rule, message);
                row.Alignment = NSLayoutAttribute.Top;
                rows.AddArrangedSubview(row);
            }
        });
        return section;
    }

    private NSView History(TestPageViewModel page)
    {
        var rows = Ui.VStack(4);
        var empty = Ui.Secondary("Nothing yet.", 12);
        var clear = Ui.Button("Clear History");
        _bindings.Command(clear, page.ClearHistoryCommand);
        var header = Ui.HStack(8, Ui.SectionTitle("Recently Opened"), Ui.Spacer(), clear);
        var box = Form.Section(null, Form.Wide(Ui.VStack(4, empty, rows)));
        var section = Ui.VStack(6, header, box);
        Ui.FillWidth(header, section);
        Ui.FillWidth(box, section);

        _bindings.Observe(page, () => section.Hidden = !page.IsHistoryEnabled, nameof(TestPageViewModel.IsHistoryEnabled));
        _bindings.ObserveCollection(page.History, () =>
        {
            empty.Hidden = page.History.Count > 0;
            Clear(rows);
            foreach (var entry in page.History)
            {
                var time = Ui.Secondary(entry.Time);
                time.WidthAnchor.ConstraintEqualTo(124).Active = true;
                var url = Ui.Label(entry.Url, 12);
                url.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular)!;
                url.ToolTip = entry.Url;
                url.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
                var rule = Ui.Secondary(entry.Rule);
                var again = Ui.Button("Test");
                again.ControlSize = NSControlSize.Small;
                var line = entry;
                again.Activated += (_, _) => page.TestHistoryEntryCommand.Execute(line);
                var row = Ui.HStack(8, time, url, rule, again);
                rows.AddArrangedSubview(row);
                Ui.FillWidth(row, rows);
            }
        });
        return section;
    }

    private static void Clear(NSStackView stack)
    {
        foreach (var view in stack.ArrangedSubviews)
        {
            stack.RemoveArrangedSubview(view);
            view.RemoveFromSuperview();
        }
    }
}
