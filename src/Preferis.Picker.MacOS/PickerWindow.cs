using System.Globalization;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Preferis.Core.Picker;

namespace Preferis.Picker.MacOS;

/// <summary>
/// The picker panel. Renders <see cref="PickerSession"/> and forwards input to it; the session decides
/// (docs/Design.md §9.2, §11). Built in code: a nib would need ibtool at build time and gains nothing here.
/// Looks like Spotlight: a borderless Liquid Glass panel (macOS 26; a vibrant material before), the URL as a large
/// header, the candidates as a list with a neutral rounded selection.
/// </summary>
internal sealed class PickerWindow
{
    private const double ContentWidth = 540;
    private const double Margin = 20;
    private const double RowHeight = 52;
    private const int MaxVisibleRows = 6;
    private const double CornerRadius = 26;

    // Room around the glass for the shadow it draws itself; the window is transparent there and lets clicks through.
    private const double ShadowMargin = 32;

    private readonly PickerSession _session;
    private readonly Action<PickerResponse> _answer;
    private readonly Action _quit;
    private readonly CandidateImages _images = new();
    private readonly NSPanel _panel;
    private readonly NSView _content;
    private readonly NSTableView _table;
    private readonly NSButton _openButton;
    private readonly NSButton _copyButton;
    private readonly List<NSView> _rows;
    private NSObject? _eventMonitor;
    private NSTimer? _timer;
    private bool _answered;

    /// <param name="answer">Delivers the answer to the Router.</param>
    /// <param name="quit">Ends the process, called once the window has animated out.</param>
    public PickerWindow(PickerRequest request, Action<PickerResponse> answer, Action quit)
    {
        _session = new PickerSession(request, TimeProvider.System);
        _answer = answer;
        _quit = quit;
        _rows = request.Candidates.Select(BuildRow).ToList();

        var root = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 12,
            EdgeInsets = new NSEdgeInsets(20, (nfloat)Margin, 20, (nfloat)Margin),
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        _copyButton = IconButton("doc.on.doc", "Copy URL", CopyUrl);
        AddFullWidth(root, BuildHeader(request.Url, _copyButton));
        if (request.Message is { Length: > 0 } message)
        {
            AddFullWidth(root, BuildMessage(message));
        }
        AddFullWidth(root, new NSBox { BoxType = NSBoxType.NSBoxSeparator });

        _table = new NSTableView
        {
            HeaderView = null,
            RowHeight = (nfloat)RowHeight,
            AllowsEmptySelection = false,
            AllowsMultipleSelection = false,
            Style = NSTableViewStyle.Plain,
            BackgroundColor = NSColor.Clear,
            IntercellSpacing = new CGSize(0, 2),
            FocusRingType = NSFocusRingType.None,
            ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
        };
        _table.AddColumn(new NSTableColumn("candidate") { ResizingMask = NSTableColumnResizing.Autoresizing });
        _table.DoubleClick += (_, _) => OnDoubleClick();

        var scroll = new NSScrollView
        {
            DocumentView = _table,
            DrawsBackground = false,
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            BorderType = NSBorderType.NoBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var visibleRows = Math.Clamp(request.Candidates.Count, 1, MaxVisibleRows);
        scroll.HeightAnchor.ConstraintEqualTo((nfloat)(visibleRows * (RowHeight + _table.IntercellSpacing.Height))).Active = true;
        // The list's selection is inset from the edges like Spotlight's; let it use the full width.
        root.AddArrangedSubview(scroll);
        scroll.WidthAnchor.ConstraintEqualTo(root.WidthAnchor, 1, (nfloat)(-2 * (Margin - 8))).Active = true;
        root.SetCustomSpacing(4, root.ArrangedSubviews[^2]);

        _openButton = NSButton.CreateButton("Open", () => Answer(_session.Confirm()));
        _openButton.KeyEquivalent = "\r";
        _openButton.ControlSize = NSControlSize.Large;
        _openButton.Enabled = request.Candidates.Count > 0;
        var cancel = NSButton.CreateButton("Cancel", () => Answer(_session.Cancel()));
        cancel.KeyEquivalent = "\u001b";
        cancel.ControlSize = NSControlSize.Large;

        var footer = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 8 };
        if (_session.CanRemember)
        {
            NSButton? remember = null;
            remember = NSButton.CreateCheckbox($"Always open {PickerDisplay.SplitUrl(request.Url).Host} with this choice", () =>
                _session.Remember = remember!.State == NSCellStateValue.On);
            remember.State = _session.Remember ? NSCellStateValue.On : NSCellStateValue.Off;
            remember.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
            remember.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
            footer.AddView(remember, NSStackViewGravity.Leading);
        }
        footer.AddView(cancel, NSStackViewGravity.Trailing);
        footer.AddView(_openButton, NSStackViewGravity.Trailing);
        AddFullWidth(root, footer);
        // Loading the rows selects the first one and reports it, so every control must exist by now.
        _table.Source = new CandidateSource(this);

        var width = root.WidthAnchor.ConstraintEqualTo((nfloat)ContentWidth);
        width.Active = true;
        var size = new CGSize(ContentWidth, root.FittingSize.Height);

        _content = Backdrop(root);
        _content.WantsLayer = true;
        _content.Frame = new CGRect(ShadowMargin, ShadowMargin, size.Width, size.Height);
        var windowSize = new CGSize(size.Width + 2 * ShadowMargin, size.Height + 2 * ShadowMargin);
        var container = new NSView(new CGRect(CGPoint.Empty, windowSize));
        container.AddSubview(_content);

        _panel = new KeyablePanel(new CGRect(CGPoint.Empty, windowSize))
        {
            FloatingPanel = true,
            // A panel hides when its app deactivates by default; the pending URL should stay visible instead.
            HidesOnDeactivate = false,
            BecomesKeyOnlyIfNeeded = false,
            AcceptsMouseMovedEvents = true,
            MovableByWindowBackground = true,
            // Liquid Glass draws its shape and its own shadow; a window shadow would be computed for the whole rectangle
            // (and cut off at it). The older material is flat and keeps the window shadow.
            IsOpaque = false,
            BackgroundColor = NSColor.Clear,
            HasShadow = !OperatingSystem.IsMacOSVersionAtLeast(26),
            CollectionBehavior = NSWindowCollectionBehavior.MoveToActiveSpace | NSWindowCollectionBehavior.FullScreenAuxiliary,
            ContentView = container,
        };
        Position(request.Options.Position);
    }

    public void Show()
    {
        if (_session.SelectedIndex >= 0)
        {
            _table.SelectRow(_session.SelectedIndex, byExtendingSelection: false);
        }
        AnimateIn();
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
    /// <remarks>The glass is composited by the window server and does not appear in the image; the layout does.</remarks>
    private void Snapshot(string path)
    {
        var view = _content;
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
        _copyButton.Image = Symbol("checkmark", 15);
        NSTimer.CreateScheduledTimer(1.5, _ => _copyButton.Image = Symbol("doc.on.doc", 15));
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
        // Answer first: the Router opens the browser while the window fades away.
        _answer(response);
        AnimateOut();
    }

    // Like Spotlight: the panel fades in while growing slightly with a soft spring.
    private void AnimateIn()
    {
        var reduceMotion = NSWorkspace.SharedWorkspace.AccessibilityDisplayShouldReduceMotion;
        _panel.AlphaValue = 0;
        _panel.MakeKeyAndOrderFront(null);
        FadeTo(1, reduceMotion ? 0.12 : 0.18, CAMediaTimingFunction.EaseOut);
        if (!reduceMotion && _content.Layer is { } layer)
        {
            var spring = new CASpringAnimation { KeyPath = "transform" };
            spring.From = NSValue.FromCATransform3D(ScaledAroundCenter(0.94));
            spring.To = NSValue.FromCATransform3D(CATransform3D.Identity);
            spring.Mass = 1;
            spring.Stiffness = 320;
            spring.Damping = 24;
            spring.Duration = spring.SettlingDuration;
            layer.AddAnimation(spring, "appear");
        }
    }

    // A quick fade while shrinking a little, then the process ends. The answer has already gone to the Router.
    private void AnimateOut()
    {
        const double Duration = 0.15;
        var reduceMotion = NSWorkspace.SharedWorkspace.AccessibilityDisplayShouldReduceMotion;
        if (!reduceMotion && _content.Layer is { } layer)
        {
            var shrink = CABasicAnimation.FromKeyPath("transform");
            shrink.From = NSValue.FromCATransform3D(CATransform3D.Identity);
            shrink.To = NSValue.FromCATransform3D(ScaledAroundCenter(0.97));
            shrink.Duration = Duration;
            shrink.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseIn);
            shrink.FillMode = CAFillMode.Forwards;
            shrink.RemovedOnCompletion = false;
            layer.AddAnimation(shrink, "disappear");
        }
        FadeTo(0, Duration, CAMediaTimingFunction.EaseIn);
        NSTimer.CreateScheduledTimer(Duration + 0.03, _ =>
        {
            _panel.OrderOut(null);
            _quit();
        });
    }

    // Explicit grouping rather than NSAnimationContext.RunAnimation: an exception inside that block took the whole
    // process down (EXC_ARM_SP_ALIGN in runAnimationGroup:) instead of reaching the handler in AppDelegate.
    private void FadeTo(double alpha, double duration, NSString timing)
    {
        NSAnimationContext.BeginGrouping();
        NSAnimationContext.CurrentContext.Duration = duration;
        NSAnimationContext.CurrentContext.TimingFunction = CAMediaTimingFunction.FromName(timing);
        ((NSWindow)_panel.Animator).AlphaValue = (nfloat)alpha;
        NSAnimationContext.EndGrouping();
    }

    // A layer-backed view's layer is anchored at its corner; scale about the middle so the panel grows from its center.
    private CATransform3D ScaledAroundCenter(double scale)
    {
        var center = new CGPoint(_content.Bounds.Width / 2, _content.Bounds.Height / 2);
        return CATransform3D.MakeTranslation(-center.X, -center.Y, 0)
            .Concat(CATransform3D.MakeScale((nfloat)scale, (nfloat)scale, 1))
            .Concat(CATransform3D.MakeTranslation(center.X, center.Y, 0));
    }

    private void Position(string position)
    {
        if (position == "center")
        {
            _panel.Center();
            return;
        }

        // Near the pointer: centered horizontally on it, the top of the glass just above it, the glass kept inside the
        // visible screen area (the transparent shadow margin around it may go beyond).
        var mouse = NSEvent.CurrentMouseLocation;
        var screen = NSScreen.Screens.FirstOrDefault(s => s.Frame.Contains(mouse)) ?? NSScreen.MainScreen;
        if (screen is null)
        {
            _panel.Center();
            return;
        }
        var area = screen.VisibleFrame;
        var frame = _panel.Frame;
        var x = Math.Clamp((double)(mouse.X - frame.Width / 2), (double)area.GetMinX() - ShadowMargin, (double)(area.GetMaxX() - frame.Width) + ShadowMargin);
        var y = Math.Clamp((double)(mouse.Y + 16 + ShadowMargin - frame.Height), (double)area.GetMinY() - ShadowMargin, (double)(area.GetMaxY() - frame.Height) + ShadowMargin);
        _panel.SetFrameOrigin(new CGPoint(x, y));
    }

    /// <summary>Liquid Glass on macOS 26; the vibrant material Spotlight used before on older systems.</summary>
    private static NSView Backdrop(NSView content)
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            return new NSGlassEffectView { CornerRadius = (nfloat)CornerRadius, ContentView = content };
        }

        var effect = new NSVisualEffectView
        {
            Material = NSVisualEffectMaterial.Popover,
            BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
            State = NSVisualEffectState.Active,
            WantsLayer = true,
        };
        effect.Layer!.CornerRadius = (nfloat)CornerRadius;
        effect.Layer.MasksToBounds = true;
        effect.AddSubview(content);
        content.LeadingAnchor.ConstraintEqualTo(effect.LeadingAnchor).Active = true;
        content.TrailingAnchor.ConstraintEqualTo(effect.TrailingAnchor).Active = true;
        content.TopAnchor.ConstraintEqualTo(effect.TopAnchor).Active = true;
        content.BottomAnchor.ConstraintEqualTo(effect.BottomAnchor).Active = true;
        return effect;
    }

    private static void AddFullWidth(NSStackView stack, NSView view)
    {
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        stack.AddArrangedSubview(view);
        view.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, (nfloat)(-2 * Margin)).Active = true;
    }

    // Like Spotlight's query: the host large, the whole URL small below it, the copy button at the end.
    private static NSView BuildHeader(string url, NSButton copy)
    {
        var parts = PickerDisplay.SplitUrl(url);
        var icon = NSImageView.FromImage(Symbol(parts.Host.Length > 0 ? "link" : "doc", 22)!);
        icon.ContentTintColor = NSColor.SecondaryLabel;
        icon.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);

        var host = Label(parts.Host.Length > 0 ? parts.Host : Path.GetFileName(parts.AfterHost.TrimEnd('/')), NSFont.SystemFontOfSize(24, NSFontWeight.Semibold), NSColor.Label);
        var full = Label(url, NSFont.SystemFontOfSize(12), NSColor.SecondaryLabel);
        full.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        full.ToolTip = url;
        host.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        full.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);

        var texts = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 2 };
        texts.AddArrangedSubview(host);
        texts.AddArrangedSubview(full);
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);

        var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.CenterY, Spacing = 12 };
        row.AddArrangedSubview(icon);
        row.AddArrangedSubview(texts);
        row.AddArrangedSubview(copy);
        return row;
    }

    private static NSView BuildMessage(string message)
    {
        var icon = NSImageView.FromImage(Symbol("exclamationmark.triangle.fill", 13)!);
        icon.ContentTintColor = NSColor.SystemOrange;
        icon.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        var label = NSTextField.CreateWrappingLabel(message);
        label.Font = NSFont.SystemFontOfSize(12)!;
        label.TextColor = NSColor.SecondaryLabel;
        label.PreferredMaxLayoutWidth = (nfloat)(ContentWidth - 2 * Margin - 24);
        var stack = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Alignment = NSLayoutAttribute.Top, Spacing = 8 };
        stack.AddArrangedSubview(icon);
        stack.AddArrangedSubview(label);
        return stack;
    }

    private NSView BuildRow(PickerCandidate candidate, int index)
    {
        var texts = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 1 };
        texts.AddArrangedSubview(Label(candidate.Name, NSFont.SystemFontOfSize(14, NSFontWeight.Medium), NSColor.Label));
        if (PickerDisplay.DetailLine(candidate) is { Length: > 0 } detail)
        {
            texts.AddArrangedSubview(Label(detail, NSFont.SystemFontOfSize(12), NSColor.SecondaryLabel));
        }
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);

        var row = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 12,
            EdgeInsets = new NSEdgeInsets(0, 14, 0, 14),
        };
        row.AddArrangedSubview(BuildIcon(candidate));
        row.AddArrangedSubview(texts);
        if (index < 9)
        {
            row.AddArrangedSubview(new KeycapView((index + 1).ToString(CultureInfo.InvariantCulture)));
        }
        return row;
    }

    // Browser icon with the browser profile's picture as a small badge in the corner, like the browsers' own profile menus.
    private NSView BuildIcon(PickerCandidate candidate)
    {
        const double Size = 38;
        var container = new NSView(new CGRect(0, 0, Size, Size)) { TranslatesAutoresizingMaskIntoConstraints = false };
        container.WidthAnchor.ConstraintEqualTo((nfloat)Size).Active = true;
        container.HeightAnchor.ConstraintEqualTo((nfloat)Size).Active = true;

        var image = _images.Icon(candidate);
        var icon = NSImageView.FromImage(image);
        icon.Frame = new CGRect(0, 2, 36, 36);
        icon.ImageScaling = NSImageScale.ProportionallyUpOrDown;
        if (image.Template)
        {
            // A symbol (command, browser not installed here) rather than an app icon.
            icon.SymbolConfiguration = NSImageSymbolConfiguration.Create(24, NSFontWeight.Regular);
            icon.ContentTintColor = NSColor.SecondaryLabel;
        }
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
            avatar.Layer.BorderColor = NSColor.White.CGColor;
            container.AddSubview(avatar);
        }
        return container;
    }

    private static NSButton IconButton(string symbol, string tip, Action action)
    {
        var button = NSButton.CreateButton(Symbol(symbol, 15)!, action);
        button.Bordered = false;
        button.ContentTintColor = NSColor.SecondaryLabel;
        button.ToolTip = tip;
        button.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        return button;
    }

    private static NSImage? Symbol(string name, double pointSize)
        => NSImage.GetSystemSymbol(name, null)?.GetImage(NSImageSymbolConfiguration.Create((nfloat)pointSize, NSFontWeight.Regular));

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

    /// <summary>Borderless windows cannot become key by default; the picker needs the keyboard.</summary>
    private sealed class KeyablePanel : NSPanel
    {
        public KeyablePanel(CGRect frame)
            : base(frame, NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
        {
        }

        // Needed to wrap native objects of this class that were not created here: Animator returns a proxy that
        // reports this class, and without this constructor reading it fails ("Failed to marshal the Objective-C object").
        public KeyablePanel(NativeHandle handle)
            : base(handle)
        {
        }

        public override bool CanBecomeKeyWindow => true;

        public override bool CanBecomeMainWindow => true;
    }

    /// <summary>Spotlight-like selection: a neutral rounded highlight, text keeps its normal colors.</summary>
    private sealed class CandidateRowView : NSTableRowView
    {
        public override NSBackgroundStyle InteriorBackgroundStyle => NSBackgroundStyle.Normal;

        public override void DrawSelection(CGRect dirtyRect)
        {
            NSColor.Label.ColorWithAlphaComponent(0.1f).SetFill();
            NSBezierPath.FromRoundedRect(Bounds.Inset(6, 1), 12, 12).Fill();
        }
    }

    /// <summary>The 1-9 shortcut, drawn like a key cap. Drawn rather than layered so it follows light and dark mode.</summary>
    private sealed class KeycapView : NSView
    {
        private readonly string _text;

        public KeycapView(string text)
        {
            _text = text;
            TranslatesAutoresizingMaskIntoConstraints = false;
            WidthAnchor.ConstraintEqualTo(22).Active = true;
            HeightAnchor.ConstraintEqualTo(22).Active = true;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var path = NSBezierPath.FromRoundedRect(Bounds.Inset(0.5f, 0.5f), 6, 6);
            path.LineWidth = 1;
            NSColor.TertiaryLabel.SetStroke();
            path.Stroke();

            var text = new NSAttributedString(_text, new NSStringAttributes
            {
                Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Medium),
                ForegroundColor = NSColor.SecondaryLabel,
            });
            var size = text.Size;
            text.DrawAtPoint(new CGPoint((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2));
        }
    }

    private sealed class CandidateSource(PickerWindow owner) : NSTableViewSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._rows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row) => owner._rows[(int)row];

        public override NSTableRowView GetRowView(NSTableView tableView, nint row) => new CandidateRowView();

        public override void SelectionDidChange(NSNotification notification) => owner.OnSelectionChanged();
    }
}
