using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>Small factories so the pages read as layout rather than control setup.</summary>
internal static class Ui
{
    /// <summary>Around the content of a page, as in System Settings.</summary>
    public const double PageInset = 20;

    public static NSTextField Label(string text, double size = 13, nfloat? weight = null, NSColor? color = null)
    {
        var label = NSTextField.CreateLabel(text);
        label.Font = NSFont.SystemFontOfSize((nfloat)size, weight ?? NSFontWeight.Regular)!;
        label.TextColor = color ?? NSColor.Label;
        label.LineBreakMode = NSLineBreakMode.TruncatingTail;
        label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return label;
    }

    public static NSTextField Secondary(string text, double size = 11) => Label(text, size, color: NSColor.SecondaryLabel);

    /// <summary>A label that wraps to as many lines as it needs within <paramref name="width"/>.</summary>
    public static NSTextField Wrapping(string text, double width, double size = 11, NSColor? color = null)
    {
        var label = NSTextField.CreateWrappingLabel(text);
        label.Font = NSFont.SystemFontOfSize((nfloat)size)!;
        label.TextColor = color ?? NSColor.SecondaryLabel;
        label.PreferredMaxLayoutWidth = (nfloat)width;
        return label;
    }

    /// <summary>
    /// A label that wraps at whatever width the layout gives it; pin its width (<see cref="FillWidth"/>), since it
    /// has no width of its own.
    /// </summary>
    public static NSTextField Flowing(string text, double size = 11, NSColor? color = null)
    {
        // 0 is the automatic width: lines break at the width of the last layout pass.
        var label = Wrapping(text, 0, size, color);
        label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        label.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        return label;
    }

    /// <summary>The bold title above a group of rows.</summary>
    public static NSTextField SectionTitle(string text) => Label(text, 13, NSFontWeight.Bold);

    /// <summary>
    /// A text field as System Settings shows one in a form row: no bezel, right-aligned, taking the rest of the row
    /// (<see cref="Form.Row(NSView, NSView?, string?)"/> lets it fill). The focus ring shows it while editing.
    /// </summary>
    public static NSTextField Field(string placeholder = "", bool monospaced = false)
    {
        var field = new NSTextField
        {
            PlaceholderString = placeholder,
            Bezeled = false,
            Bordered = false,
            DrawsBackground = false,
            Alignment = NSTextAlignment.Right,
            Font = monospaced ? NSFont.MonospacedSystemFont(12, NSFontWeight.Regular)! : NSFont.SystemFontOfSize(13)!,
            LineBreakMode = NSLineBreakMode.TruncatingTail,
            UsesSingleLineMode = true,
        };
        field.Cell.Scrollable = true;
        field.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        field.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return field;
    }

    /// <summary>A <see cref="Field"/> for one item per line: Return starts a new line, and the field grows with them.</summary>
    public static NSTextField LinesField(string placeholder = "")
    {
        var field = Field(placeholder);
        field.UsesSingleLineMode = false;
        field.Cell.Scrollable = false;
        field.Cell.Wraps = true;
        field.LineBreakMode = NSLineBreakMode.CharWrapping;
        field.DoCommandBySelector = (_, textView, selector) =>
        {
            if (selector.Name != "insertNewline:")
            {
                return false;
            }
            textView.InsertText(new NSString("\n"), textView.SelectedRange);
            return true;
        };
        field.Changed += (_, _) =>
        {
            // Reading the value moves the typed text into the cell, which the size is measured from; otherwise the
            // field would only grow once editing ends.
            _ = field.StringValue;
            field.InvalidateIntrinsicContentSize();
        };
        return field;
    }

    /// <summary>The mini switch of grouped forms, as System Settings uses (Human Interface Guidelines, Toggles).</summary>
    public static NSSwitch Switch() => new() { ControlSize = NSControlSize.Mini };

    public static NSButton Button(string title, Action? action = null)
    {
        var button = action is null ? NSButton.CreateButton(title, () => { }) : NSButton.CreateButton(title, action);
        return button;
    }

    public static NSButton Checkbox(string title) => NSButton.CreateCheckbox(title, () => { });

    public static NSButton IconButton(string symbol, string tip)
    {
        var button = NSButton.CreateButton(Images.Symbol(symbol, 12)!, () => { });
        button.BezelStyle = NSBezelStyle.Rounded;
        button.ToolTip = tip;
        return button;
    }

    /// <summary>A pop-up button for a value in a form row: the value and a round chevron badge (<see cref="MenuPopUpButton"/>).</summary>
    public static NSPopUpButton Popup() => new MenuPopUpButton(pullsDown: false);

    /// <summary>
    /// A pull-down button whose menu items run actions: drawn like <see cref="Popup"/> in a form row, or as a push
    /// button (<paramref name="bordered"/>) on its own below a group.
    /// </summary>
    public static NSPopUpButton PullDown(string title, IEnumerable<(string Title, Action Action)> items, bool bordered = false)
    {
        var button = bordered ? new NSPopUpButton(CGRect.Empty, pullsDown: true) : new MenuPopUpButton(pullsDown: true);
        button.AddItem(title);
        foreach (var (itemTitle, action) in items)
        {
            button.Menu!.AddItem(new NSMenuItem(itemTitle, (_, _) => action()));
        }
        return button;
    }

    public static NSStackView VStack(double spacing, params NSView[] views)
    {
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = (nfloat)spacing };
        foreach (var view in views)
        {
            stack.AddArrangedSubview(view);
        }
        return stack;
    }

    public static NSStackView HStack(double spacing, params NSView[] views)
    {
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = (nfloat)spacing };
        foreach (var view in views)
        {
            stack.AddArrangedSubview(view);
        }
        return stack;
    }

    /// <summary>Lets the other views of a horizontal stack keep their size while this one takes the rest.</summary>
    public static NSView Spacer()
    {
        var view = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        view.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        view.WidthAnchor.ConstraintGreaterThanOrEqualTo(0).Active = true;
        return view;
    }

    public static void FillWidth(NSView view, NSView container, double inset = 0)
    {
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        view.WidthAnchor.ConstraintEqualTo(container.WidthAnchor, 1, (nfloat)(-2 * inset)).Active = true;
    }

    /// <summary>A page that scrolls vertically: content is laid out top-down at the width of the page.</summary>
    public static NSScrollView ScrollPage(NSView content)
    {
        var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
        content.TranslatesAutoresizingMaskIntoConstraints = false;
        document.AddSubview(content);
        content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor, (nfloat)PageInset).Active = true;
        content.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor, (nfloat)(-PageInset)).Active = true;
        content.TopAnchor.ConstraintEqualTo(document.TopAnchor, (nfloat)PageInset).Active = true;
        content.BottomAnchor.ConstraintEqualTo(document.BottomAnchor, (nfloat)(-PageInset)).Active = true;

        var scroll = new NSScrollView { HasVerticalScroller = true, AutohidesScrollers = true, DrawsBackground = false, BorderType = NSBorderType.NoBorder };
        scroll.DocumentView = document;
        var clip = scroll.ContentView;
        document.LeadingAnchor.ConstraintEqualTo(clip.LeadingAnchor).Active = true;
        document.TrailingAnchor.ConstraintEqualTo(clip.TrailingAnchor).Active = true;
        document.TopAnchor.ConstraintEqualTo(clip.TopAnchor).Active = true;
        return scroll;
    }

    public static void Pin(NSView view, NSView container, double inset = 0)
    {
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        view.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor, (nfloat)inset).Active = true;
        view.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor, (nfloat)(-inset)).Active = true;
        view.TopAnchor.ConstraintEqualTo(container.TopAnchor, (nfloat)inset).Active = true;
        view.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, (nfloat)(-inset)).Active = true;
    }

    /// <summary>The fill of the groups in System Settings; before macOS 14 the closest is a faint label color.</summary>
    public static NSColor GroupFill => OperatingSystem.IsMacOSVersionAtLeast(14) ? NSColor.QuaternarySystemFill : NSColor.Label.ColorWithAlphaComponent(0.03f);
}

/// <summary>
/// Grouped forms like System Settings (a SwiftUI grouped <c>Form</c>), measured on macOS 26: a bold title, a rounded
/// group whose rows put a label on the left and the control on the right, separated by thin lines, and a footnote.
/// </summary>
internal static class Form
{
    /// <summary>Between the edge of a group and its content.</summary>
    public const double Inset = 10;

    // Measured in System Settings: rows of text are 36.5 pt high, rows of 24 pt controls 37.5 pt.
    private const double TextPadding = 10.25;
    private const double ControlPadding = 6.75;

    /// <summary>Between sections on a page.</summary>
    public const double SectionSpacing = 28;

    /// <summary>Between groups of one section, or before a section without a title.</summary>
    public const double GroupSpacing = 10;

    private const string UntitledSection = "preferis.untitled-section";

    public static NSView Section(string? title, params NSView[] rows) => Section(title, null, rows);

    public static NSView Section(string? title, string? footer, params NSView[] rows)
    {
        var section = Ui.VStack(8);
        if (title is not null)
        {
            Add(section, Header(title));
        }
        else
        {
            section.Identifier = UntitledSection;
        }
        Add(section, Group(rows));
        if (footer is not null)
        {
            Add(section, Footer(footer));
        }
        return section;
    }

    /// <summary>A section title, aligned with the content of the rows; <paramref name="accessory"/> goes to the right.</summary>
    public static NSView Header(string title, NSView? accessory = null)
    {
        var header = Ui.HStack(8, Ui.SectionTitle(title));
        header.EdgeInsets = new NSEdgeInsets(0, (nfloat)Inset, 0, (nfloat)Inset);
        if (accessory is not null)
        {
            header.AddArrangedSubview(Ui.Spacer());
            header.AddArrangedSubview(accessory);
        }
        return header;
    }

    /// <summary>A footnote below a group, aligned with the content of the rows.</summary>
    public static NSView Footer(string text) => Footer(Ui.Flowing(text));

    public static NSView Footer(NSTextField label)
    {
        var footer = Ui.VStack(0, label);
        footer.EdgeInsets = new NSEdgeInsets(0, (nfloat)Inset, 0, (nfloat)Inset);
        Ui.FillWidth(label, footer, Inset);
        return footer;
    }

    /// <summary>The rounded group of rows.</summary>
    public static NSBox Group(params NSView[] rows)
    {
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var separator = new NSBox { BoxType = NSBoxType.NSBoxSeparator };
                stack.AddArrangedSubview(separator);
                Ui.FillWidth(separator, stack, Inset);
                separator.LeadingAnchor.ConstraintEqualTo(stack.LeadingAnchor, (nfloat)Inset).Active = true;
            }
            stack.AddArrangedSubview(rows[i]);
            Ui.FillWidth(rows[i], stack);
        }
        return new NSBox
        {
            BoxType = NSBoxType.NSBoxCustom,
            BorderWidth = 0,
            CornerRadius = 12,
            FillColor = Ui.GroupFill,
            ContentViewMargins = CGSize.Empty,
            ContentView = stack,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
    }

    /// <summary>A row: label (and optional detail below it) on the left, <paramref name="control"/> on the right.</summary>
    public static NSView Row(string label, NSView? control, string? detail = null) => Row(Ui.Label(label), control, detail);

    public static NSView Row(NSView label, NSView? control, string? detail = null) => Row(label, control, detail is null ? null : Ui.Flowing(detail));

    /// <summary>A row whose <paramref name="detail"/> label the caller keeps updating.</summary>
    public static NSView Row(NSView label, NSView? control, NSTextField? detail)
    {
        var left = Ui.VStack(2, label);
        if (detail is not null)
        {
            left.AddArrangedSubview(detail);
            Ui.FillWidth(detail, left);
        }
        left.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);

        // Laid out by hand: a stack view drops its insets across its axis for the tallest view, which squeezed rows
        // with a description. Rows are as short as the padding allows.
        var row = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        var shortest = row.HeightAnchor.ConstraintEqualTo(0);
        shortest.Priority = 1;
        shortest.Active = true;
        Place(row, left, TextPadding);
        left.LeadingAnchor.ConstraintEqualTo(row.LeadingAnchor, (nfloat)Inset).Active = true;
        if (control is null)
        {
            left.TrailingAnchor.ConstraintEqualTo(row.TrailingAnchor, (nfloat)(-Inset)).Active = true;
            left.CenterYAnchor.ConstraintEqualTo(row.CenterYAnchor).Active = true;
            return row;
        }

        Place(row, control, ControlPadding);
        control.LeadingAnchor.ConstraintEqualTo(left.TrailingAnchor, 16).Active = true;
        control.TrailingAnchor.ConstraintEqualTo(row.TrailingAnchor, (nfloat)(-Inset)).Active = true;
        if (Fills(control))
        {
            // Text fields take the rest of the row, as in System Settings. The label keeps its width, up to half the
            // row: a description below it wraps rather than squeezing the field.
            left.SetHuggingPriority(250, NSLayoutConstraintOrientation.Horizontal);
            left.WidthAnchor.ConstraintLessThanOrEqualTo(row.WidthAnchor, 0.5f).Active = true;
            if (control is NSStackView buttons)
            {
                buttons.Distribution = NSStackViewDistribution.Fill;
                buttons.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
            }
            if (detail is not null)
            {
                // A wrapping label has no width of its own: ask for the width of its text on one line, or for a
                // share of the row when the text comes later.
                var natural = detail.StringValue.Length > 0 ? detail.AttributedStringValue.Size.Width + 1 : 0;
                var wide = natural > 0
                    ? left.WidthAnchor.ConstraintGreaterThanOrEqualTo(natural)
                    : left.WidthAnchor.ConstraintGreaterThanOrEqualTo(row.WidthAnchor, 0.4f);
                // Below the window's own size (500), so that the text wraps instead of widening the window.
                wide.Priority = 490;
                wide.Active = true;
            }
        }
        else
        {
            left.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
            control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        }

        switch (control)
        {
            case NSTextField { Editable: true, UsesSingleLineMode: false }:
                // A field of several lines starts level with the label.
                control.FirstBaselineAnchor.ConstraintEqualTo(label.FirstBaselineAnchor).Active = true;
                break;
            case NSStackView { Orientation: NSUserInterfaceLayoutOrientation.Vertical }:
                // So does a list of checkboxes.
                control.TopAnchor.ConstraintEqualTo(left.TopAnchor).Active = true;
                break;
            default:
                left.CenterYAnchor.ConstraintEqualTo(row.CenterYAnchor).Active = true;
                control.CenterYAnchor.ConstraintEqualTo(row.CenterYAnchor).Active = true;
                break;
        }
        return row;
    }

    /// <summary>A row whose content spans the whole width (lists, editors).</summary>
    public static NSView Wide(NSView content)
    {
        var row = Ui.VStack(0, content);
        row.EdgeInsets = new NSEdgeInsets((nfloat)Inset, (nfloat)Inset, (nfloat)Inset, (nfloat)Inset);
        Ui.FillWidth(content, row, Inset);
        return row;
    }

    /// <summary>
    /// A scrolling page of sections. A section without a title follows the previous one closely, as a continuation.
    /// </summary>
    public static NSScrollView Page(params NSView[] views)
    {
        var stack = Ui.VStack(SectionSpacing, views);
        for (var i = 0; i < views.Length; i++)
        {
            Ui.FillWidth(views[i], stack);
            if (i > 0 && views[i].Identifier == UntitledSection)
            {
                stack.SetCustomSpacing((nfloat)GroupSpacing, views[i - 1]);
            }
        }
        return Ui.ScrollPage(stack);
    }

    /// <summary>
    /// Shows a separator only between visible rows of <paramref name="sectionOrGroup"/>; call it after hiding or showing rows.
    /// </summary>
    public static void UpdateSeparators(NSView sectionOrGroup)
    {
        // Arranged subviews, not subviews: a hidden view is detached from the stack's hierarchy.
        var group = sectionOrGroup as NSBox ?? (sectionOrGroup as NSStackView)?.ArrangedSubviews.OfType<NSBox>().FirstOrDefault(b => b.BoxType == NSBoxType.NSBoxCustom);
        if (group?.ContentView is not NSStackView rows)
        {
            return;
        }
        var seenVisibleRow = false;
        NSBox? before = null;
        foreach (var view in rows.ArrangedSubviews)
        {
            if (view is NSBox { BoxType: NSBoxType.NSBoxSeparator } separator)
            {
                separator.Hidden = true;
                before = separator;
            }
            else if (!view.Hidden)
            {
                if (seenVisibleRow && before is not null)
                {
                    before.Hidden = false;
                }
                seenVisibleRow = true;
                before = null;
            }
        }
    }

    // Inside the row, with at least `padding` above and below.
    private static void Place(NSView row, NSView view, double padding)
    {
        row.AddSubview(view);
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        view.TopAnchor.ConstraintGreaterThanOrEqualTo(row.TopAnchor, (nfloat)padding).Active = true;
        view.BottomAnchor.ConstraintLessThanOrEqualTo(row.BottomAnchor, (nfloat)(-padding)).Active = true;
    }

    private static void Add(NSStackView stack, NSView view)
    {
        stack.AddArrangedSubview(view);
        Ui.FillWidth(view, stack);
    }

    // Editable text fields (alone or with buttons beside them) fill the row; Ui.Field gives them the lowest hugging.
    private static bool Fills(NSView control) => control switch
    {
        NSTextField { Editable: true } field => field.GetContentHuggingPriorityForOrientation(NSLayoutConstraintOrientation.Horizontal) <= 1,
        NSStackView { Orientation: NSUserInterfaceLayoutOrientation.Horizontal } stack => stack.ArrangedSubviews.Any(Fills),
        _ => false,
    };

}

/// <summary>Top-down coordinates, so scrolled content starts at the top.</summary>
internal sealed class FlippedView : NSView
{
    public FlippedView()
    {
    }

    public FlippedView(NativeHandle handle)
        : base(handle)
    {
    }

    public override bool IsFlipped => true;
}
