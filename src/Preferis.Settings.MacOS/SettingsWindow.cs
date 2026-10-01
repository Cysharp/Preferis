using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.MacOS.Pages;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS;

/// <summary>
/// The settings window, laid out like System Settings: a sidebar with the pages, the page on the right, Revert and Save
/// in the toolbar. The close button shows the unsaved-changes dot; the subtitle summarizes the state.
/// </summary>
internal sealed class SettingsWindow
{
    private static readonly (string Symbol, NSColor Color)[] PageIcons =
    [
        ("person.2.fill", NSColor.SystemBlue),
        ("arrow.triangle.branch", NSColor.SystemOrange),
        ("play.fill", NSColor.SystemGreen),
        ("gearshape.fill", NSColor.SystemGray),
    ];

    private readonly SettingsViewModel _settings;
    private readonly Bindings _bindings = new();
    private readonly NSTableView _sidebar;
    private readonly NSView _pageContainer;
    private readonly WindowEvents _events;
    private IPageView? _page;
    private PageViewModel? _shownPage;
    private bool _closeConfirmed;

    public SettingsWindow(SettingsViewModel settings)
    {
        _settings = settings;

        Window = new NSWindow(
            new CGRect(0, 0, 1080, 720),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView,
            NSBackingStore.Buffered,
            false)
        {
            MinSize = new CGSize(820, 520),
            ToolbarStyle = NSWindowToolbarStyle.Unified,
            FrameAutosaveName = "PreferisSettings",
        };

        _sidebar = BuildSidebar();
        var sidebarScroll = new NSScrollView { DocumentView = _sidebar, DrawsBackground = false, HasVerticalScroller = true, AutohidesScrollers = true };
        var sidebar = new NSViewController { View = sidebarScroll };
        var sidebarItem = NSSplitViewItem.CreateSidebar(sidebar);
        sidebarItem.MinimumThickness = 190;
        sidebarItem.MaximumThickness = 260;
        sidebarItem.CanCollapse = false;

        _pageContainer = new NSView();
        var content = new NSView();
        var banner = new Banner(settings, _bindings);
        var column = Ui.VStack(0, banner.View, _pageContainer);
        column.Alignment = NSLayoutAttribute.Leading;
        // Fill: the page container has no intrinsic height and must take all the room the banner leaves.
        column.Distribution = NSStackViewDistribution.Fill;
        content.AddSubview(column);
        Ui.Pin(column, content);
        Ui.FillWidth(banner.View, column);
        Ui.FillWidth(_pageContainer, column);
        _pageContainer.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
        var contentItem = NSSplitViewItem.FromViewController(new NSViewController { View = content });

        var split = new NSSplitViewController();
        split.AddSplitViewItem(sidebarItem);
        split.AddSplitViewItem(contentItem);
        Window.ContentViewController = split;
        Window.SetContentSize(new CGSize(1080, 720));

        Window.Toolbar = new NSToolbar("PreferisSettingsToolbar") { Delegate = new Toolbar(settings, _bindings), DisplayMode = NSToolbarDisplayMode.Icon };

        _events = new WindowEvents(this);
        Window.Delegate = _events;

        _bindings.Observe(settings, ShowPage, nameof(SettingsViewModel.SelectedPage), nameof(SettingsViewModel.Pages));
        _bindings.Observe(settings, UpdateTitle, nameof(SettingsViewModel.SelectedPage), nameof(SettingsViewModel.IsDirty), nameof(SettingsViewModel.IssueSummary));
    }

    public NSWindow Window { get; }

    public void Show()
    {
        if (Window.Frame.Width < 820)
        {
            Window.Center();
        }
        Window.MakeKeyAndOrderFront(null);
    }

    /// <summary>The page view currently shown, for snapshots.</summary>
    public NSView PageView => _pageContainer;

    private NSTableView BuildSidebar()
    {
        var table = new NSTableView
        {
            HeaderView = null,
            Style = NSTableViewStyle.SourceList,
            RowHeight = 30,
            AllowsEmptySelection = false,
            FocusRingType = NSFocusRingType.None,
        };
        table.AddColumn(new NSTableColumn("page") { ResizingMask = NSTableColumnResizing.Autoresizing });
        table.Source = new SidebarSource(this);
        return table;
    }

    private void ShowPage()
    {
        var page = _settings.SelectedPage;
        var index = page is null ? -1 : _settings.Pages.ToList().IndexOf(page);
        if (index >= 0 && _sidebar.SelectedRow != index)
        {
            _sidebar.SelectRow(index, false);
        }
        // Reloading the configuration replaces most page view models; the view follows the instance.
        if (ReferenceEquals(page, _shownPage))
        {
            return;
        }
        _shownPage = page;
        _page?.Dispose();
        foreach (var subview in _pageContainer.Subviews)
        {
            subview.RemoveFromSuperview();
        }
        _page = page switch
        {
            ProfilesPageViewModel profiles => new ProfilesPage(profiles),
            RulesPageViewModel rules => new RulesPage(rules),
            TestPageViewModel test => new TestPage(test),
            GeneralPageViewModel general => new GeneralPage(general),
            _ => null,
        };
        if (_page is not null)
        {
            _pageContainer.AddSubview(_page.View);
            Ui.Pin(_page.View, _pageContainer);
        }
    }

    private void UpdateTitle()
    {
        Window.Title = _settings.SelectedPage?.Title ?? "Preferis Settings";
        var parts = new List<string>();
        if (_settings.IsDirty)
        {
            parts.Add("Edited");
        }
        if (_settings.IssueSummary.Length > 0)
        {
            parts.Add(_settings.IssueSummary);
        }
        Window.Subtitle = string.Join(" — ", parts);
        Window.DocumentEdited = _settings.IsDirty;
    }

    private async void OnCloseRequested()
    {
        if (await _settings.CanCloseAsync())
        {
            _closeConfirmed = true;
            Window.Close();
        }
    }

    private sealed class SidebarSource(SettingsWindow owner) : NSTableViewSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._settings.Pages.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var page = owner._settings.Pages[(int)row];
            var (symbol, color) = PageIcons[Math.Min((int)row, PageIcons.Length - 1)];

            // White symbol on a colored rounded square, like the icons of System Settings.
            var badge = new NSView(new CGRect(0, 0, 22, 22)) { WantsLayer = true, TranslatesAutoresizingMaskIntoConstraints = false };
            badge.Layer!.BackgroundColor = color.CGColor;
            badge.Layer.CornerRadius = 6;
            badge.WidthAnchor.ConstraintEqualTo(22).Active = true;
            badge.HeightAnchor.ConstraintEqualTo(22).Active = true;
            var icon = NSImageView.FromImage(Images.Symbol(symbol, 12, NSFontWeight.Semibold)!);
            icon.ContentTintColor = NSColor.White;
            icon.Frame = new CGRect(0, 0, 22, 22);
            badge.AddSubview(icon);

            var stack = Ui.HStack(8, badge, Ui.Label(page.Title));
            stack.EdgeInsets = new NSEdgeInsets(0, 4, 0, 4);
            return stack;
        }

        // The table comes from the notification: the first one arrives while the window is still being built.
        public override void SelectionDidChange(NSNotification notification)
        {
            if (notification.Object is not NSTableView table)
            {
                return;
            }
            var row = (int)table.SelectedRow;
            if (row >= 0 && row < owner._settings.Pages.Count)
            {
                owner._settings.SelectedPage = owner._settings.Pages[row];
            }
        }
    }

    private sealed class Toolbar : NSToolbarDelegate
    {
        private const string Revert = "revert";
        private const string Save = "save";
        private readonly SettingsViewModel _settings;
        private readonly Bindings _bindings;

        public Toolbar(SettingsViewModel settings, Bindings bindings)
        {
            _settings = settings;
            _bindings = bindings;
        }

        public Toolbar(NativeHandle handle)
            : base(handle)
        {
            _settings = null!;
            _bindings = null!;
        }

        public override string[] DefaultItemIdentifiers(NSToolbar toolbar) => [NSToolbar.NSToolbarFlexibleSpaceItemIdentifier, Revert, Save];

        public override string[] AllowedItemIdentifiers(NSToolbar toolbar) => DefaultItemIdentifiers(toolbar);

        public override NSToolbarItem WillInsertItem(NSToolbar toolbar, string itemIdentifier, bool willBeInserted)
        {
            var item = new NSToolbarItem(itemIdentifier);
            var button = Ui.Button(itemIdentifier == Save ? "Save" : "Revert");
            if (itemIdentifier == Save)
            {
                button.KeyEquivalent = "s";
                button.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask;
                _bindings.Command(button, _settings.SaveCommand);
            }
            else
            {
                _bindings.Command(button, _settings.RevertCommand);
            }
            _bindings.Observe(_settings, () => button.Enabled = _settings.IsDirty, nameof(SettingsViewModel.IsDirty));
            item.View = button;
            item.Label = button.Title;
            return item;
        }
    }

    private sealed class WindowEvents : NSWindowDelegate
    {
        private readonly SettingsWindow _owner;

        public WindowEvents(SettingsWindow owner)
        {
            _owner = owner;
        }

        public WindowEvents(NativeHandle handle)
            : base(handle)
        {
            _owner = null!;
        }

        // Coming back to the window is when a change made elsewhere (the Router, a sync client) should show up.
        public override void DidBecomeKey(NSNotification notification) => _owner._settings.CheckForExternalChanges();

        public override bool WindowShouldClose(NSObject sender)
        {
            if (_owner._closeConfirmed || !_owner._settings.IsDirty)
            {
                return true;
            }
            _owner.OnCloseRequested();
            return false;
        }

        public override void WillClose(NSNotification notification) => NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
    }
}

/// <summary>A page of the window. Disposing it releases its bindings to the view models.</summary>
internal interface IPageView : IDisposable
{
    NSView View { get; }
}

/// <summary>The notice above the page: changes on disk, a configuration that could not be read, a failed operation.</summary>
internal sealed class Banner
{
    public Banner(SettingsViewModel settings, Bindings bindings)
    {
        var icon = NSImageView.FromImage(Images.Symbol("exclamationmark.triangle.fill", 14)!);
        icon.ContentTintColor = NSColor.SystemOrange;
        var text = Ui.Wrapping("", 520, 12, NSColor.Label);
        var reload = Ui.Button("Reload");
        var overwrite = Ui.Button("Save Anyway");
        bindings.Command(reload, settings.RevertCommand);
        bindings.Command(overwrite, settings.SaveAnywayCommand);
        var row = Ui.HStack(10, icon, text, Ui.Spacer(), reload, overwrite);
        row.Alignment = NSLayoutAttribute.CenterY;
        row.EdgeInsets = new NSEdgeInsets(10, 16, 10, 16);

        var box = new NSBox
        {
            BoxType = NSBoxType.NSBoxCustom,
            BorderWidth = 0,
            CornerRadius = 0,
            FillColor = NSColor.SystemYellow.ColorWithAlphaComponent(0.18f),
            ContentViewMargins = CGSize.Empty,
            ContentView = row,
        };
        View = box;

        bindings.Observe(settings, () =>
        {
            var message = settings.HasExternalChanges
                ? "config.json was changed outside the settings (for example by “Always open … with this choice” or a sync client). Reload to see those changes, or save yours over them."
                : settings.StatusMessage ?? settings.LoadError;
            text.StringValue = message ?? "";
            reload.Hidden = !settings.HasExternalChanges;
            overwrite.Hidden = !settings.HasExternalChanges;
            box.Hidden = message is null;
        }, nameof(SettingsViewModel.HasExternalChanges), nameof(SettingsViewModel.StatusMessage), nameof(SettingsViewModel.LoadError));
    }

    public NSView View { get; }
}
