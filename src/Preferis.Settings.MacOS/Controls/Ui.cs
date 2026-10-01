using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>Small factories so the pages read as layout rather than control setup.</summary>
internal static class Ui
{
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

    public static NSTextField Heading(string text) => Label(text, 22, NSFontWeight.Bold);

    public static NSTextField SectionTitle(string text) => Label(text, 13, NSFontWeight.Semibold);

    public static NSTextField Field(string placeholder = "", bool monospaced = false)
    {
        var field = new NSTextField { PlaceholderString = placeholder, BezelStyle = NSTextFieldBezelStyle.Rounded };
        if (monospaced)
        {
            field.Font = NSFont.MonospacedSystemFont(12, NSFontWeight.Regular)!;
        }
        field.LineBreakMode = NSLineBreakMode.TruncatingTail;
        field.UsesSingleLineMode = true;
        field.Cell.Scrollable = true;
        return field;
    }

    /// <summary>A small multi-line text view (one argument per line) with a border.</summary>
    public static (NSScrollView Scroll, NSTextView Text) TextArea(double height = 60)
    {
        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            BorderType = NSBorderType.BezelBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var text = new NSTextView(new CGRect(0, 0, 300, height))
        {
            Font = NSFont.MonospacedSystemFont(12, NSFontWeight.Regular)!,
            RichText = false,
            AutomaticQuoteSubstitutionEnabled = false,
            AutomaticDashSubstitutionEnabled = false,
            AutomaticTextReplacementEnabled = false,
            AutomaticSpellingCorrectionEnabled = false,
            AutoresizingMask = NSViewResizingMask.WidthSizable,
            VerticallyResizable = true,
            HorizontallyResizable = false,
        };
        text.TextContainer!.WidthTracksTextView = true;
        scroll.DocumentView = text;
        scroll.HeightAnchor.ConstraintEqualTo((nfloat)height).Active = true;
        return (scroll, text);
    }

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

    public static NSPopUpButton Popup() => new() { PullsDown = false };

    /// <summary>A pull-down button ("Add Action ▾") whose menu items run actions.</summary>
    public static NSPopUpButton PullDown(string title, IEnumerable<(string Title, Action Action)> items)
    {
        var button = new NSPopUpButton { PullsDown = true };
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
    public static NSScrollView ScrollPage(NSView content, double inset = 24)
    {
        var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
        content.TranslatesAutoresizingMaskIntoConstraints = false;
        document.AddSubview(content);
        content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor, (nfloat)inset).Active = true;
        content.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor, (nfloat)(-inset)).Active = true;
        content.TopAnchor.ConstraintEqualTo(document.TopAnchor, (nfloat)20).Active = true;
        content.BottomAnchor.ConstraintEqualTo(document.BottomAnchor, (nfloat)(-24)).Active = true;

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
}

/// <summary>
/// Grouped form sections like System Settings: an optional title above a rounded box whose rows put a label on the
/// left and the control on the right, separated by thin lines.
/// </summary>
internal static class Form
{
    public static NSView Section(string? title, params NSView[] rows) => Section(title, null, rows);

    public static NSView Section(string? title, string? footer, params NSView[] rows)
    {
        var box = new NSBox
        {
            BoxType = NSBoxType.NSBoxCustom,
            BorderWidth = 0,
            CornerRadius = 10,
            // The fill of grouped form rows in System Settings; before macOS 14 the closest is a faint label color.
            FillColor = OperatingSystem.IsMacOSVersionAtLeast(14) ? NSColor.QuaternarySystemFill : NSColor.Label.ColorWithAlphaComponent(0.05f),
            ContentViewMargins = CGSize.Empty,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var separator = new NSBox { BoxType = NSBoxType.NSBoxSeparator };
                stack.AddArrangedSubview(separator);
                Ui.FillWidth(separator, stack, 12);
                separator.LeadingAnchor.ConstraintEqualTo(stack.LeadingAnchor, 12).Active = true;
            }
            stack.AddArrangedSubview(rows[i]);
            Ui.FillWidth(rows[i], stack);
        }
        box.ContentView = stack;

        var section = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 6 };
        if (title is not null)
        {
            var heading = Ui.SectionTitle(title);
            section.AddArrangedSubview(heading);
        }
        section.AddArrangedSubview(box);
        Ui.FillWidth(box, section);
        if (footer is not null)
        {
            var note = Ui.Flowing(footer);
            section.AddArrangedSubview(note);
            Ui.FillWidth(note, section);
        }
        return section;
    }

    /// <summary>A row: label (and optional detail below it) on the left, <paramref name="control"/> on the right.</summary>
    public static NSView Row(string label, NSView? control, string? detail = null) => Row(Ui.Label(label), control, detail);

    public static NSView Row(NSView label, NSView? control, string? detail = null)
    {
        var left = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 2 };
        left.AddArrangedSubview(label);
        if (detail is not null)
        {
            var details = Ui.Flowing(detail);
            left.AddArrangedSubview(details);
            Ui.FillWidth(details, left);
        }
        left.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        left.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);

        var row = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 16,
            EdgeInsets = new NSEdgeInsets(9, 12, 9, 12),
            Distribution = NSStackViewDistribution.Fill,
        };
        row.AddArrangedSubview(left);
        if (control is not null)
        {
            control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
            row.AddArrangedSubview(control);
        }
        return row;
    }

    /// <summary>
    /// Shows a separator only between visible rows of <paramref name="section"/>; call it after hiding or showing rows.
    /// </summary>
    public static void UpdateSeparators(NSView section)
    {
        // Arranged subviews, not subviews: a hidden view is detached from the stack's hierarchy.
        if (section is not NSStackView stack
            || stack.ArrangedSubviews.OfType<NSBox>().FirstOrDefault(b => b.BoxType == NSBoxType.NSBoxCustom)?.ContentView is not NSStackView rows)
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

    /// <summary>A row whose content spans the whole width (lists, editors).</summary>
    public static NSView Wide(NSView content)
    {
        var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, EdgeInsets = new NSEdgeInsets(9, 12, 9, 12) };
        row.AddArrangedSubview(content);
        Ui.FillWidth(content, row, 12);
        return row;
    }
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
