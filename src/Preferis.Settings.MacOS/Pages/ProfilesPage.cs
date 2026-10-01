using AppKit;
using CoreGraphics;
using Preferis.Core.Configuration;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS.Pages;

/// <summary>The profiles: the list (with + for the browsers found on this Mac) and the selected profile's settings.</summary>
internal sealed class ProfilesPage : IPageView
{
    private const double ListWidth = 270;
    private readonly ProfilesPageViewModel _page;
    private readonly Bindings _bindings = new();
    private readonly ListPane<ProfileViewModel> _list;
    private readonly NSView _editorHost = new();
    private Bindings? _editorBindings;
    private ProfileViewModel? _shown;

    public ProfilesPage(ProfilesPageViewModel page)
    {
        _page = page;
        _list = new ListPane<ProfileViewModel>(
            page.Profiles,
            Row,
            profile => page.SelectedProfile = profile,
            page.MoveProfile,
            44,
            nameof(ProfileViewModel.Name), nameof(ProfileViewModel.Summary), nameof(ProfileViewModel.IsDefault), nameof(ProfileViewModel.HasIssues), nameof(ProfileViewModel.IconPng));

        var add = Ui.IconButton("plus", "Add a profile");
        add.Activated += (_, _) => ShowAddMenu(add);
        var remove = Ui.IconButton("minus", "Delete the selected profile");
        _bindings.Command(remove, page.DeleteCommand);
        var more = MoreMenu();
        _list.Bar.AddArrangedSubview(add);
        _list.Bar.AddArrangedSubview(remove);
        _list.Bar.AddArrangedSubview(Ui.Spacer());
        _list.Bar.AddArrangedSubview(more);
        Ui.FillWidth(_list.Bar, _list.View);

        var root = new NSView();
        root.AddSubview(_list.View);
        root.AddSubview(_editorHost);
        _list.View.TranslatesAutoresizingMaskIntoConstraints = false;
        _editorHost.TranslatesAutoresizingMaskIntoConstraints = false;
        _list.View.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20).Active = true;
        // Below the toolbar: the window's content runs under it, and only the scrolling editor insets itself.
        _list.View.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor, 20).Active = true;
        _list.View.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -20).Active = true;
        _list.View.WidthAnchor.ConstraintEqualTo((nfloat)ListWidth).Active = true;
        _editorHost.LeadingAnchor.ConstraintEqualTo(_list.View.TrailingAnchor, 4).Active = true;
        _editorHost.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
        _editorHost.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
        _editorHost.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
        View = root;

        _bindings.Observe(page, ShowSelected, nameof(ProfilesPageViewModel.SelectedProfile));
    }

    public NSView View { get; }

    public void Dispose()
    {
        _editorBindings?.Dispose();
        _list.Dispose();
        _bindings.Dispose();
    }

    private static NSView Row(ProfileViewModel profile)
    {
        var icon = new NSImageView { Image = Images.FromPng(profile.IconPng) ?? Images.Symbol(profile.IsCommand ? "terminal" : "globe", 18)!, ImageScaling = NSImageScale.ProportionallyUpOrDown };
        icon.ContentTintColor = NSColor.SecondaryLabel;
        icon.WidthAnchor.ConstraintEqualTo(26).Active = true;
        icon.HeightAnchor.ConstraintEqualTo(26).Active = true;
        var name = Ui.Label(profile.Name.Length > 0 ? profile.Name : "(Unnamed)");
        var summary = Ui.Secondary(profile.Summary);
        var texts = Ui.VStack(1, name, summary);
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        var row = Ui.HStack(8, icon, texts);
        row.EdgeInsets = new NSEdgeInsets(0, 2, 0, 6);
        if (profile.HasIssues)
        {
            row.AddArrangedSubview(Badge("exclamationmark.circle.fill", NSColor.SystemRed, "This profile has problems"));
        }
        if (profile.IsDefault)
        {
            row.AddArrangedSubview(Badge("star.fill", NSColor.SystemYellow, "Default profile"));
        }
        return row;
    }

    private static NSImageView Badge(string symbol, NSColor color, string tip)
    {
        var badge = NSImageView.FromImage(Images.Symbol(symbol, 12)!);
        badge.ContentTintColor = color;
        badge.ToolTip = tip;
        badge.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        return badge;
    }

    // "+": the browsers and browser profiles of this Mac, then the other ways to add a profile.
    private void ShowAddMenu(NSButton anchor)
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        menu.AddItem(new NSMenuItem("Browsers on This Mac") { Enabled = false });
        foreach (var browser in _page.DetectedBrowsers)
        {
            foreach (var item in browser.Items)
            {
                var title = item.Profile is null ? item.Title : item.Subtitle is { } subtitle ? $"{item.Title} — {subtitle}" : item.Title;
                var entry = item;
                var menuItem = new NSMenuItem(title, (_, _) => _page.AddDetectedCommand.Execute(entry))
                {
                    IndentationLevel = item.Profile is null ? 0 : 1,
                };
                if (Images.FromPng(item.Profile is null ? browser.IconPng : item.AvatarPng ?? browser.IconPng) is { } image)
                {
                    menuItem.Image = Images.Resized(image, 16);
                }
                menu.AddItem(menuItem);
            }
        }
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Add All Browsers and Profiles", (_, _) => _page.AddAllDetectedCommand.Execute(null)));
        menu.AddItem(new NSMenuItem("Command…", (_, _) => _page.AddCommandProfileCommand.Execute(null)));
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Detect Browsers Again", (_, _) => _page.RefreshBrowsersCommand.Execute(null)));
        menu.PopUpMenu(null, new CGPoint(0, -4), anchor);
    }

    private NSPopUpButton MoreMenu()
    {
        var more = new NSPopUpButton { PullsDown = true, Bordered = true };
        more.AddItem("");
        more.LastItem!.Image = Images.Symbol("ellipsis", 12);
        more.Menu!.AutoEnablesItems = false;
        var duplicate = new NSMenuItem("Duplicate", (_, _) => _page.DuplicateCommand.Execute(null));
        var makeDefault = new NSMenuItem("Make Default Profile", (_, _) => _page.SetDefaultCommand.Execute(null));
        var up = new NSMenuItem("Move Up", (_, _) => _page.MoveUpCommand.Execute(null));
        var down = new NSMenuItem("Move Down", (_, _) => _page.MoveDownCommand.Execute(null));
        more.Menu.AddItem(duplicate);
        more.Menu.AddItem(makeDefault);
        more.Menu.AddItem(NSMenuItem.SeparatorItem);
        more.Menu.AddItem(up);
        more.Menu.AddItem(down);
        void Update()
        {
            duplicate.Enabled = _page.DuplicateCommand.CanExecute(null);
            makeDefault.Enabled = _page.SetDefaultCommand.CanExecute(null) && _page.SelectedProfile?.IsDefault == false;
            up.Enabled = _page.MoveUpCommand.CanExecute(null);
            down.Enabled = _page.MoveDownCommand.CanExecute(null);
        }
        _bindings.Observe(_page, Update, nameof(ProfilesPageViewModel.SelectedProfile));
        _bindings.ObserveCollection(_page.Profiles, Update);
        return more;
    }

    private void ShowSelected()
    {
        _list.Select(_page.SelectedProfile);
        if (ReferenceEquals(_shown, _page.SelectedProfile))
        {
            return;
        }
        _shown = _page.SelectedProfile;
        _editorBindings?.Dispose();
        foreach (var view in _editorHost.Subviews)
        {
            view.RemoveFromSuperview();
        }
        _editorBindings = new Bindings();
        var editor = _shown is null ? Empty() : Editor(_shown, _editorBindings);
        _editorHost.AddSubview(editor);
        Ui.Pin(editor, _editorHost);
    }

    private static NSView Empty()
    {
        var text = Ui.VStack(6,
            Ui.Label("No Profile Selected", 17, NSFontWeight.Semibold, NSColor.SecondaryLabel),
            Ui.Wrapping("Click + below the list to add the browsers found on this Mac, or a command.", 320, 12));
        text.Alignment = NSLayoutAttribute.CenterX;
        var host = new NSView();
        host.AddSubview(text);
        text.TranslatesAutoresizingMaskIntoConstraints = false;
        text.CenterXAnchor.ConstraintEqualTo(host.CenterXAnchor).Active = true;
        text.CenterYAnchor.ConstraintEqualTo(host.CenterYAnchor).Active = true;
        return host;
    }

    private NSView Editor(ProfileViewModel profile, Bindings bindings)
    {
        // Header: icon, name, id and problems.
        var icon = new NSImageView { ImageScaling = NSImageScale.ProportionallyUpOrDown };
        icon.WidthAnchor.ConstraintEqualTo(44).Active = true;
        icon.HeightAnchor.ConstraintEqualTo(44).Active = true;
        icon.ContentTintColor = NSColor.SecondaryLabel;
        bindings.Observe(profile, () => icon.Image = Images.FromPng(profile.IconPng) ?? Images.Symbol(profile.IsCommand ? "terminal" : "globe", 28), nameof(ProfileViewModel.IconPng), nameof(ProfileViewModel.TargetKind));
        var name = Ui.Field("Name");
        name.Font = NSFont.SystemFontOfSize(17, NSFontWeight.Semibold)!;
        bindings.Text(name, profile, nameof(ProfileViewModel.Name), () => profile.Name, value => profile.Name = value);
        var id = Ui.Flowing($"Id: {profile.Id} — used by rules; it does not change when you rename the profile.");
        var title = Ui.VStack(4, name, id);
        Ui.FillWidth(name, title);
        Ui.FillWidth(id, title);
        var header = Ui.HStack(14, icon, title);
        title.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        var issues = Issues(profile, bindings);

        var isDefault = new NSSwitch();
        bindings.Observe(profile, () =>
        {
            isDefault.State = profile.IsDefault ? 1 : 0;
            // There is always one default at most; it changes by choosing another profile.
            isDefault.Enabled = !profile.IsDefault;
        }, nameof(ProfileViewModel.IsDefault));
        isDefault.Activated += (_, _) => _page.SetDefaultCommand.Execute(null);

        var target = Ui.Popup();
        bindings.Choice(target, profile, nameof(ProfileViewModel.TargetKind),
            [(ProfileTargetKind.Browser, "Browser"), (ProfileTargetKind.Command, "Command")],
            () => profile.TargetKind, value => profile.TargetKind = value);

        var general = Form.Section(null,
            Form.Row("Default profile", isDefault, "Opens links that the matching rule does not assign to a profile."),
            Form.Row("Opens with", target));

        var stack = Ui.VStack(22, header, issues, general, BrowserSection(profile, bindings), CommandSection(profile, bindings), FallbackSection(profile, bindings));
        foreach (var view in stack.ArrangedSubviews)
        {
            Ui.FillWidth(view, stack);
        }
        return Ui.ScrollPage(stack);
    }

    private static NSView Issues(ProfileViewModel profile, Bindings bindings)
    {
        var list = Ui.VStack(4);
        bindings.Observe(profile, () =>
        {
            foreach (var view in list.ArrangedSubviews)
            {
                view.RemoveFromSuperview();
            }
            foreach (var issue in profile.Issues)
            {
                var message = Ui.Flowing(issue.Message, 12, issue.Severity == IssueSeverity.Error ? NSColor.SystemRed : NSColor.SystemOrange);
                list.AddArrangedSubview(message);
                Ui.FillWidth(message, list);
            }
            list.Hidden = profile.Issues.Count == 0;
        }, nameof(ProfileViewModel.Issues));
        return list;
    }

    private static NSView BrowserSection(ProfileViewModel profile, Bindings bindings)
    {
        var browser = Ui.Popup();
        bindings.Popup(browser, profile, nameof(ProfileViewModel.Browser), () => profile.BrowserChoices, () => profile.Browser, value => profile.Browser = value, c => c.Title, c => Images.FromPng(c.IconPng));
        var mac = Ui.Popup();
        bindings.Popup(mac, profile, nameof(ProfileViewModel.MacOSBrowser), () => profile.BrowserChoices, () => profile.MacOSBrowser, value => profile.MacOSBrowser = value, c => c.Title, c => Images.FromPng(c.IconPng));
        var windows = Ui.Popup();
        bindings.Popup(windows, profile, nameof(ProfileViewModel.WindowsBrowser), () => profile.WindowsBrowserChoices, () => profile.WindowsBrowser, value => profile.WindowsBrowser = value, c => c.Title);
        var perOs = new NSSwitch();
        bindings.Switch(perOs, profile, nameof(ProfileViewModel.PerOs), () => profile.PerOs, value => profile.PerOs = value);
        var browserProfile = Ui.Popup();
        bindings.Popup(browserProfile, profile, nameof(ProfileViewModel.BrowserProfile), () => profile.BrowserProfileChoices, () => profile.BrowserProfile, value => profile.BrowserProfile = value, c => c.Title);
        var isPrivate = new NSSwitch();
        bindings.Switch(isPrivate, profile, nameof(ProfileViewModel.Private), () => profile.Private, value => profile.Private = value);
        var (argsScroll, args) = Ui.TextArea(54);
        argsScroll.WidthAnchor.ConstraintEqualTo(240).Active = true;
        bindings.Text(args, profile, nameof(ProfileViewModel.ExtraArgs), () => profile.ExtraArgs, value => profile.ExtraArgs = value);

        var browserRow = Form.Row("Browser", browser);
        var macRow = Form.Row("Browser on macOS", mac);
        var windowsRow = Form.Row("Browser on Windows", windows);
        var profileRow = Form.Row("Browser profile", browserProfile);
        var section = Form.Section("Browser",
            browserRow,
            Form.Row("Different browser on each OS", perOs, "For a configuration shared with Windows computers."),
            macRow,
            windowsRow,
            profileRow,
            Form.Row("Private window", isPrivate),
            Form.Row("Extra arguments", argsScroll, "One per line, for example --new-window."));

        bindings.Observe(profile, () =>
        {
            section.Hidden = !profile.IsBrowser;
            browserRow.Hidden = profile.PerOs;
            macRow.Hidden = !profile.PerOs;
            windowsRow.Hidden = !profile.PerOs;
            profileRow.Hidden = !profile.HasBrowserProfiles;
            Form.UpdateSeparators(section);
        }, nameof(ProfileViewModel.TargetKind), nameof(ProfileViewModel.PerOs), nameof(ProfileViewModel.HasBrowserProfiles));
        return section;
    }

    private NSView CommandSection(ProfileViewModel profile, Bindings bindings)
    {
        var macPath = Ui.Field("/Applications/Foo.app/Contents/MacOS/Foo", monospaced: true);
        macPath.WidthAnchor.ConstraintEqualTo(280).Active = true;
        bindings.Text(macPath, profile, nameof(ProfileViewModel.MacOSCommandPath), () => profile.MacOSCommandPath, value => profile.MacOSCommandPath = value);
        var choose = Ui.Button("Choose…");
        bindings.Command(choose, profile.BrowseMacOSCommandCommand);
        var (macArgsScroll, macArgs) = Ui.TextArea(54);
        macArgsScroll.WidthAnchor.ConstraintEqualTo(280).Active = true;
        bindings.Text(macArgs, profile, nameof(ProfileViewModel.MacOSCommandArgs), () => profile.MacOSCommandArgs, value => profile.MacOSCommandArgs = value);
        var winPath = Ui.Field(@"%LOCALAPPDATA%\Programs\Foo\foo.exe", monospaced: true);
        winPath.WidthAnchor.ConstraintEqualTo(280).Active = true;
        bindings.Text(winPath, profile, nameof(ProfileViewModel.WindowsCommandPath), () => profile.WindowsCommandPath, value => profile.WindowsCommandPath = value);
        var (winArgsScroll, winArgs) = Ui.TextArea(54);
        winArgsScroll.WidthAnchor.ConstraintEqualTo(280).Active = true;
        bindings.Text(winArgs, profile, nameof(ProfileViewModel.WindowsCommandArgs), () => profile.WindowsCommandArgs, value => profile.WindowsCommandArgs = value);

        var section = Form.Section("Command",
            "{url} in the arguments is replaced with the URL; without it the URL is added at the end. Choosing an app runs it through /usr/bin/open.",
            Form.Row("macOS", Ui.HStack(6, macPath, choose)),
            Form.Row("Arguments", macArgsScroll, "One per line."),
            Form.Row("Windows", winPath),
            Form.Row("Arguments", winArgsScroll, "One per line."));
        bindings.Observe(profile, () => section.Hidden = !profile.IsCommand, nameof(ProfileViewModel.TargetKind));
        return section;
    }

    private static NSView FallbackSection(ProfileViewModel profile, Bindings bindings)
    {
        var fallback = Ui.Popup();
        bindings.Popup(fallback, profile, nameof(ProfileViewModel.Fallback), () => profile.FallbackChoices, () => profile.Fallback, value => profile.Fallback = value, c => c.Title);
        return Form.Section("When Not Available",
            Form.Row("Use instead", fallback, "For example when the browser is not installed on another computer that shares this configuration."));
    }
}
