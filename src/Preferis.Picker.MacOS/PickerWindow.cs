using System.Globalization;
using AppKit;
using CoreGraphics;
using Foundation;
using Preferis.Core.Picker;

namespace Preferis.Picker.MacOS;

/// <summary>
/// The picker panel. Renders <see cref="PickerSession"/> and forwards input to it; the session decides
/// (docs/Design.md §9.2, §11). Built in code: a nib would need ibtool at build time and gains nothing here.
/// </summary>
internal sealed class PickerWindow
{
    private const double ContentWidth = 460;
    private const double Margin = 20;
    private const double RowHeight = 44;
    private const int MaxVisibleRows = 7;

    private readonly PickerSession _session;
    private readonly Action<PickerResponse> _answer;
    private readonly CandidateImages _images = new();
    private readonly NSPanel _panel;
    private readonly NSTableView _table;
    private readonly NSButton _openButton;
    private readonly NSButton _copyButton;
    private readonly List<NSView> _rows;
    private NSObject? _eventMonitor;
    private NSTimer? _timer;
    private bool _answered;

    public PickerWindow(PickerRequest request, Action<PickerResponse> answer)
    {
        _session = new PickerSession(request, TimeProvider.System);
        _answer = answer;
        _rows = request.Candidates.Select(BuildRow).ToList();

        var root = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 10,
            EdgeInsets = new NSEdgeInsets(16, (nfloat)Margin, 16, (nfloat)Margin),
        };

        if (request.Message is { Length: > 0 } message)
        {
            AddFullWidth(root, BuildMessage(message));
        }

        root.AddArrangedSubview(Label("Open this link with:", NSFont.BoldSystemFontOfSize(13), NSColor.Label));
        AddFullWidth(root, BuildUrlLabel(request.Url));

        _table = new NSTableView
        {
            HeaderView = null,
            RowHeight = (nfloat)RowHeight,
            AllowsEmptySelection = false,
            AllowsMultipleSelection = false,
            Style = NSTableViewStyle.Inset,
            ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
        };
        _table.AddColumn(new NSTableColumn("candidate") { ResizingMask = NSTableColumnResizing.Autoresizing });
        _table.DoubleClick += (_, _) => OnDoubleClick();

        var scroll = new NSScrollView
        {
            DocumentView = _table,
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            BorderType = NSBorderType.LineBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var visibleRows = Math.Clamp(request.Candidates.Count, 1, MaxVisibleRows);
        // The inset style pads the list by about 5 points above and below.
        scroll.HeightAnchor.ConstraintEqualTo((nfloat)(visibleRows * (RowHeight + _table.IntercellSpacing.Height) + 12)).Active = true;
        AddFullWidth(root, scroll);

        if (_session.CanRemember)
        {
            NSButton? remember = null;
            remember = NSButton.CreateCheckbox($"Always open {PickerDisplay.SplitUrl(request.Url).Host} with this choice", () =>
                _session.Remember = remember!.State == NSCellStateValue.On);
            remember.State = _session.Remember ? NSCellStateValue.On : NSCellStateValue.Off;
            root.AddArrangedSubview(remember);
        }

        _copyButton = NSButton.CreateButton("Copy URL", CopyUrl);
        var cancel = NSButton.CreateButton("Cancel", () => Answer(_session.Cancel()));
        cancel.KeyEquivalent = "\u001b";
        _openButton = NSButton.CreateButton("Open", () => Answer(_session.Confirm()));
        _openButton.KeyEquivalent = "\r";
        _openButton.Enabled = request.Candidates.Count > 0;
        var buttons = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 8 };
        buttons.AddView(_copyButton, NSStackViewGravity.Leading);
        buttons.AddView(cancel, NSStackViewGravity.Trailing);
        buttons.AddView(_openButton, NSStackViewGravity.Trailing);
        root.SetCustomSpacing(16, root.ArrangedSubviews[^1]);
        AddFullWidth(root, buttons);
        // Loading the rows selects the first one and reports it, so every control must exist by now.
        _table.Source = new CandidateSource(this);

        _panel = new NSPanel(new CGRect(0, 0, ContentWidth, 300), NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
        {
            Title = "Preferis",
            FloatingPanel = true,
            // A panel hides when its app deactivates by default; the pending URL should stay visible instead.
            HidesOnDeactivate = false,
            BecomesKeyOnlyIfNeeded = false,
            AcceptsMouseMovedEvents = true,
            CollectionBehavior = NSWindowCollectionBehavior.MoveToActiveSpace | NSWindowCollectionBehavior.FullScreenAuxiliary,
            ContentView = root,
        };
        root.WidthAnchor.ConstraintEqualTo((nfloat)ContentWidth).Active = true;
        _panel.SetContentSize(new CGSize(ContentWidth, root.FittingSize.Height));
        _panel.WillClose += (_, _) => Answer(_session.Cancel());
        Position(request.Options.Position);
    }

    public void Show()
    {
        if (_session.SelectedIndex >= 0)
        {
            _table.SelectRow(_session.SelectedIndex, byExtendingSelection: false);
        }
        _panel.MakeKeyAndOrderFront(null);
        _panel.MakeFirstResponder(_table);
        Activate();

        _eventMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.KeyDown | NSEventMask.MouseMoved | NSEventMask.ScrollWheel | NSEventMask.LeftMouseDown,
            OnEvent);
        if (_session.AutoSelectRemaining is not null)
        {
            UpdateOpenButton();
            _timer = NSTimer.CreateRepeatingScheduledTimer(0.1, _ => OnTick());
        }
        if (Environment.GetEnvironmentVariable("PREFERIS_PICKER_SNAPSHOT") is { Length: > 0 } snapshot)
        {
            NSTimer.CreateScheduledTimer(0.5, _ => Snapshot(snapshot));
        }
    }

    /// <summary>Developer aid: writes the panel's content as PNG, for checking the layout without screen recording rights.</summary>
    private void Snapshot(string path)
    {
        var view = _panel.ContentView!;
        var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
        if (bitmap is null)
        {
            return;
        }
        view.CacheDisplay(view.Bounds, bitmap);
        if (bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png) is { } png)
        {
            File.WriteAllBytes(path, png.ToArray());
        }
    }

    private static void Activate()
    {
        var app = NSApplication.SharedApplication;
        if (OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            app.Activate();
        }
        // Cooperative activation (macOS 14+) is only a request that the frontmost app may decline. Spike 2 showed
        // the deprecated call reliably brings a helper started by the Router to the front, so use it when needed.
        NSTimer.CreateScheduledTimer(0.05, _ =>
        {
            if (!app.Active)
            {
#pragma warning disable CA1422 // Obsolete on macOS 14, still functional and the only forceful option.
                app.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422
            }
        });
    }

    // Returning null swallows the event; the binding's delegate is not annotated for that.
    private NSEvent OnEvent(NSEvent e)
    {
        if (e.Type == NSEventType.KeyDown && e.Window == _panel)
        {
            var modifiers = e.ModifierFlags & (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask | NSEventModifierMask.AlternateKeyMask);
            var key = e.CharactersIgnoringModifiers;
            if (modifiers == 0 && key is { Length: 1 } && key[0] is >= '1' and <= '9' && _session.ChooseNumber(key[0] - '0') is { } chosen)
            {
                Answer(chosen);
                return null!;
            }
            if (modifiers == NSEventModifierMask.CommandKeyMask && key == "c" && _panel.FirstResponder == _table)
            {
                CopyUrl();
                return null!;
            }
        }
        _session.NoteActivity();
        return e;
    }

    private void OnTick()
    {
        if (_session.Tick() is { } response)
        {
            Answer(response);
            return;
        }
        UpdateOpenButton();
    }

    private void UpdateOpenButton()
        => _openButton.Title = _session.AutoSelectRemaining is { } remaining
            ? $"Open ({Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture)})"
            : "Open";

    private void OnSelectionChanged()
    {
        if (_table.SelectedRow >= 0)
        {
            _session.Select((int)_table.SelectedRow);
            UpdateOpenButton();
        }
    }

    private void OnDoubleClick()
    {
        if (_table.ClickedRow >= 0)
        {
            _session.Select((int)_table.ClickedRow);
            Answer(_session.Confirm());
        }
    }

    private void CopyUrl()
    {
        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetStringForType(_session.Request.Url, NSPasteboardType.String.GetConstant()!);
        _copyButton.Title = "Copied";
        NSTimer.CreateScheduledTimer(1.5, _ => _copyButton.Title = "Copy URL");
        _session.NoteActivity();
    }

    private void Answer(PickerResponse response)
    {
        if (_answered)
        {
            return;
        }
        _answered = true;
        _timer?.Invalidate();
        if (_eventMonitor is not null)
        {
            NSEvent.RemoveMonitor(_eventMonitor);
        }
        _answer(response);
    }

    private void Position(string position)
    {
        if (position == "center")
        {
            _panel.Center();
            return;
        }

        // Near the pointer: centered horizontally on it, title bar just above it, kept inside the visible screen area.
        var mouse = NSEvent.CurrentMouseLocation;
        var screen = NSScreen.Screens.FirstOrDefault(s => s.Frame.Contains(mouse)) ?? NSScreen.MainScreen;
        if (screen is null)
        {
            _panel.Center();
            return;
        }
        var area = screen.VisibleFrame;
        var frame = _panel.Frame;
        var x = Math.Clamp((double)(mouse.X - frame.Width / 2), (double)area.GetMinX(), (double)(area.GetMaxX() - frame.Width));
        var y = Math.Clamp((double)(mouse.Y + 24 - frame.Height), (double)area.GetMinY(), (double)(area.GetMaxY() - frame.Height));
        _panel.SetFrameOrigin(new CGPoint(x, y));
    }

    private static void AddFullWidth(NSStackView stack, NSView view)
    {
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        stack.AddArrangedSubview(view);
        view.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, (nfloat)(-2 * Margin)).Active = true;
    }

    private static NSView BuildMessage(string message)
    {
        var icon = NSImageView.FromImage(NSImage.GetSystemSymbol("exclamationmark.triangle.fill", null)!);
        icon.ContentTintColor = NSColor.SystemOrange;
        icon.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        var label = NSTextField.CreateWrappingLabel(message);
        label.PreferredMaxLayoutWidth = (nfloat)(ContentWidth - 2 * Margin - 28);
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.Top, Spacing = 8 };
        stack.AddArrangedSubview(icon);
        stack.AddArrangedSubview(label);
        return stack;
    }

    private static NSTextField BuildUrlLabel(string url)
    {
        var parts = PickerDisplay.SplitUrl(url);
        var paragraph = new NSMutableParagraphStyle { LineBreakMode = NSLineBreakMode.TruncatingMiddle };
        var dim = new NSStringAttributes { ForegroundColor = NSColor.SecondaryLabel, Font = NSFont.SystemFontOfSize(12), ParagraphStyle = paragraph };
        var strong = new NSStringAttributes { ForegroundColor = NSColor.Label, Font = NSFont.BoldSystemFontOfSize(12), ParagraphStyle = paragraph };
        var text = new NSMutableAttributedString();
        text.Append(new NSAttributedString(parts.BeforeHost, dim));
        text.Append(new NSAttributedString(parts.Host, strong));
        text.Append(new NSAttributedString(parts.AfterHost, dim));

        var label = NSTextField.CreateLabel(text);
        label.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        label.UsesSingleLineMode = true;
        label.MaximumNumberOfLines = 1;
        label.ToolTip = url;
        label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return label;
    }

    private NSView BuildRow(PickerCandidate candidate, int index)
    {
        var name = Label(candidate.Name, NSFont.SystemFontOfSize(13), NSColor.Label);
        var texts = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 1 };
        texts.AddArrangedSubview(name);
        if (PickerDisplay.DetailLine(candidate) is { Length: > 0 } detail)
        {
            texts.AddArrangedSubview(Label(detail, NSFont.SystemFontOfSize(11), NSColor.SecondaryLabel));
        }
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);

        var shortcut = Label(index < 9 ? (index + 1).ToString(CultureInfo.InvariantCulture) : "", NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular), NSColor.TertiaryLabel);

        var row = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 10,
            EdgeInsets = new NSEdgeInsets(0, 4, 0, 8),
        };
        row.AddArrangedSubview(BuildIcon(candidate));
        row.AddArrangedSubview(texts);
        row.AddArrangedSubview(shortcut);
        return row;
    }

    // Browser icon with the browser profile's picture as a small badge in the corner, like the browsers' own profile menus.
    private NSView BuildIcon(PickerCandidate candidate)
    {
        const double Size = 36;
        var container = new NSView(new CGRect(0, 0, Size, Size)) { TranslatesAutoresizingMaskIntoConstraints = false };
        container.WidthAnchor.ConstraintEqualTo((nfloat)Size).Active = true;
        container.HeightAnchor.ConstraintEqualTo((nfloat)Size).Active = true;

        var icon = NSImageView.FromImage(_images.Icon(candidate));
        icon.Frame = new CGRect(0, 2, 32, 32);
        icon.ImageScaling = NSImageScale.ProportionallyUpOrDown;
        container.AddSubview(icon);

        if (_images.Avatar(candidate) is { } picture)
        {
            var avatar = NSImageView.FromImage(picture);
            avatar.Frame = new CGRect(Size - 18, 0, 18, 18);
            avatar.ImageScaling = NSImageScale.ProportionallyUpOrDown;
            avatar.WantsLayer = true;
            avatar.Layer!.CornerRadius = 9;
            avatar.Layer.MasksToBounds = true;
            avatar.Layer.BorderWidth = 1.5f;
            avatar.Layer.BorderColor = NSColor.WindowBackground.CGColor;
            container.AddSubview(avatar);
        }
        return container;
    }

    private static NSTextField Label(string text, NSFont? font, NSColor color)
    {
        var label = NSTextField.CreateLabel(text);
        if (font is not null)
        {
            label.Font = font;
        }
        label.TextColor = color;
        label.LineBreakMode = NSLineBreakMode.TruncatingTail;
        return label;
    }

    private sealed class CandidateSource(PickerWindow owner) : NSTableViewSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._rows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row) => owner._rows[(int)row];

        public override void SelectionDidChange(NSNotification notification) => owner.OnSelectionChanged();
    }
}
