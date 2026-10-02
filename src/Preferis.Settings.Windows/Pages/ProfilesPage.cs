using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Windows.Controls;
using static Preferis.Settings.Localization.SettingsText;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Preferis.Settings.Windows.Pages;

/// <summary>The profiles: the list (with + for the browsers found on this PC) and the selected profile's settings.</summary>
internal sealed class ProfilesPage : IPageView
{
    private const double ListWidth = 320;
    private const double MinListWidth = 240;
    private readonly ProfilesPageViewModel _page;
    private readonly Bindings _bindings = new();
    private readonly ListPane<ProfileViewModel> _list;
    private readonly ContentControl _editorHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private Bindings? _editorBindings;
    private ProfileViewModel? _shown;

    public ProfilesPage(ProfilesPageViewModel page)
    {
        _page = page;
        _list = new ListPane<ProfileViewModel>(page.Profiles, Row, profile => page.SelectedProfile = profile, page.MoveProfile);

        var add = Ui.IconButton(Ui.Glyphs.Add, L("Add a profile"));
        var addMenu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        // Built when it opens: the detected browsers change with "Detect Browsers Again".
        addMenu.Opening += (_, _) => FillAddMenu(addMenu);
        add.Flyout = addMenu;
        var remove = Ui.IconButton(Ui.Glyphs.Remove, L("Delete the selected profile"));
        _bindings.Command(remove, page.DeleteCommand);
        var more = Ui.IconButton(Ui.Glyphs.More, L("More"));
        more.Flyout = MoreMenu();
        _list.Bar.Children.Add(add);
        _list.Bar.Children.Add(remove);
        _list.Bar.Children.Add(more);

        View = Split(_list.View, _editorHost);
        _bindings.Observe(page, ShowSelected, nameof(ProfilesPageViewModel.SelectedProfile));
    }

    public UIElement View { get; }

    public void Dispose()
    {
        _editorBindings?.Dispose();
        _list.Dispose();
        _bindings.Dispose();
    }

    /// <summary>The list on the left, the editor of the selected item on the right.</summary>
    public static Grid Split(UIElement list, UIElement editor)
    {
        var grid = new Grid();
        var listColumn = new ColumnDefinition { Width = new GridLength(ListWidth) };
        grid.ColumnDefinitions.Add(listColumn);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star });
        // A narrower list in a narrow window, so that the editor keeps room for its cards.
        grid.SizeChanged += (_, e) => listColumn.Width = new GridLength(Math.Clamp(e.NewSize.Width * 0.36, MinListWidth, ListWidth));
        ((FrameworkElement)list).Margin = new Thickness(32, 0, 0, 24);
        grid.Children.Add(list);
        Grid.SetColumn((FrameworkElement)editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static UIElement Row(ProfileViewModel profile, Bindings bindings)
    {
        var icon = EditorParts.IconHost();
        icon.Width = icon.Height = 24;
        bindings.Observe(profile, () =>
        {
            icon.Children.Clear();
            icon.Children.Add(Ui.Image(profile.IconPng, 24) ?? (UIElement)Ui.Glyph(profile.OpensCommand ? Ui.Glyphs.Command : Ui.Glyphs.Globe, 20, Ui.Brush("TextFillColorSecondaryBrush")));
        }, nameof(ProfileViewModel.IconPng), nameof(ProfileViewModel.OpensCommand));

        var name = Ui.Label("");
        var summary = Ui.Secondary("");
        bindings.Observe(profile, () =>
        {
            name.Text = profile.Name.Length > 0 ? profile.Name : L("(Unnamed)");
            summary.Text = profile.Summary;
        }, nameof(ProfileViewModel.Name), nameof(ProfileViewModel.Summary));

        var problem = Ui.Glyph(Ui.Glyphs.Error, 14, Ui.Brush("SystemFillColorCriticalBrush"));
        ToolTipService.SetToolTip(problem, L("This profile has problems"));
        bindings.Visible(problem, profile, () => profile.HasErrors, nameof(ProfileViewModel.HasErrors));
        // Warnings only, typically a browser that this PC does not have: it still opens something.
        var warning = Ui.Glyph(Ui.Glyphs.Warning, 14, Ui.Brush("SystemFillColorCautionBrush"));
        ToolTipService.SetToolTip(warning, L("This profile cannot be used as configured on this computer"));
        bindings.Visible(warning, profile, () => profile.HasIssues && !profile.HasErrors, nameof(ProfileViewModel.Issues));
        var star = Ui.Glyph(Ui.Glyphs.Star, 14, Ui.Brush("SystemFillColorCautionBrush"));
        ToolTipService.SetToolTip(star, L("Default profile"));
        bindings.Visible(star, profile, () => profile.IsDefault, nameof(ProfileViewModel.IsDefault));

        return Ui.Columns(12, (icon, Ui.Auto), (Ui.VStack(0, name, summary), Ui.Star), (Ui.HStack(6, problem, warning, star), Ui.Auto));
    }

    // "+": the browsers and browser profiles of this PC, then the other ways to add a profile.
    private void FillAddMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuFlyoutItem { Text = L("Browsers on This PC"), IsEnabled = false });
        foreach (var browser in _page.DetectedBrowsers)
        {
            var icon = Images.FromPng(browser.IconPng);
            if (browser.Items.Count == 1 && browser.Note is null)
            {
                menu.Items.Add(DetectedItem(browser.Items[0], browser.Name, icon));
                continue;
            }
            // A browser with browser profiles: a sub menu with the browser itself and each of its profiles.
            var sub = new MenuFlyoutSubItem { Text = browser.Name };
            if (icon is not null)
            {
                sub.Icon = new ImageIcon { Source = icon };
            }
            foreach (var item in browser.Items)
            {
                var title = item.Profile is null ? L("{0} (no specific profile)", item.Title) : item.Subtitle is { } subtitle ? $"{item.Title} — {subtitle}" : item.Title;
                sub.Items.Add(DetectedItem(item, title, Images.FromPng(item.Profile is null ? browser.IconPng : item.AvatarPng ?? browser.IconPng)));
            }
            if (browser.Note is { } note)
            {
                sub.Items.Add(new MenuFlyoutSeparator());
                sub.Items.Add(new MenuFlyoutItem { Text = note, IsEnabled = false });
            }
            menu.Items.Add(sub);
        }
        if (_page.DetectedBrowsers.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = L("No browser was found"), IsEnabled = false });
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Add All Browsers and Profiles"), _page.AddAllDetectedCommand));
        menu.Items.Add(Bindings.MenuItem(L("Command…"), _page.AddCommandProfileCommand));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Detect Browsers Again"), _page.RefreshBrowsersCommand));
    }

    private MenuFlyoutItem DetectedItem(DetectedItemViewModel item, string title, Microsoft.UI.Xaml.Media.ImageSource? icon)
    {
        var entry = Bindings.MenuItem(title, _page.AddDetectedCommand, item);
        if (icon is not null)
        {
            entry.Icon = new ImageIcon { Source = icon };
        }
        return entry;
    }

    private MenuFlyout MoreMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Bindings.MenuItem(L("Duplicate"), _page.DuplicateCommand));
        var makeDefault = Bindings.MenuItem(L("Make Default Profile"), _page.SetDefaultCommand);
        makeDefault.Loaded += (_, _) => makeDefault.IsEnabled &= _page.SelectedProfile?.IsDefault == false;
        menu.Items.Add(makeDefault);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Move Up"), _page.MoveUpCommand));
        menu.Items.Add(Bindings.MenuItem(L("Move Down"), _page.MoveDownCommand));
        return menu;
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
        _editorBindings = new Bindings();
        _editorHost.Content = _shown is null
            ? Form.Empty(L("No Profile Selected"), L("Click + below the list to add the browsers found on this PC, or a command."))
            : Editor(_shown, _editorBindings);
    }

    private UIElement Editor(ProfileViewModel profile, Bindings bindings)
    {
        var icon = EditorParts.IconHost();
        bindings.Observe(profile, () => EditorParts.SetIcon(icon, profile.IconPng, profile.OpensCommand ? Ui.Glyphs.Command : Ui.Glyphs.Globe), nameof(ProfileViewModel.IconPng), nameof(ProfileViewModel.OpensCommand));
        var title = Ui.Label("");
        var subtitle = Ui.Secondary("");
        bindings.Observe(profile, () =>
        {
            title.Text = profile.Name.Length > 0 ? profile.Name : L("(Unnamed)");
            subtitle.Text = profile.Summary;
        }, nameof(ProfileViewModel.Name), nameof(ProfileViewModel.Summary));
        var header = EditorParts.Header(icon, title, subtitle, profile, nameof(ProfileViewModel.Issues), () => profile.Issues, bindings);

        var name = Ui.TextBox(L("Name"));
        bindings.Text(name, profile, nameof(ProfileViewModel.Name), () => profile.Name, value => profile.Name = value);

        var isDefault = Ui.Switch();
        bindings.Observe(profile, () =>
        {
            isDefault.IsOn = profile.IsDefault;
            // There is always one default at most; it changes by choosing another profile.
            isDefault.IsEnabled = !profile.IsDefault;
        }, nameof(ProfileViewModel.IsDefault));
        isDefault.Toggled += (_, _) =>
        {
            if (isDefault.IsOn && !profile.IsDefault)
            {
                _page.SetDefaultCommand.Execute(null);
            }
        };

        var target = Ui.ComboBox(160);
        bindings.Choice(target, profile, nameof(ProfileViewModel.TargetKind),
            [(ProfileTargetKind.Browser, L("Browser")), (ProfileTargetKind.Command, L("Command"))],
            () => profile.TargetKind, value => profile.TargetKind = value);

        var general = Form.Section(null,
            Form.Card(L("Name"), name, L("The name shown in the picker. Each profile needs a name of its own.")),
            Form.Card(L("Default profile"), isDefault, L("Opens links that the matching rule does not assign to a profile.")),
            Form.Card(L("Opens with"), target));

        var page = Form.Page(
            header,
            general,
            BrowserSection(profile, bindings),
            CommandSection(profile, bindings),
            LocalSection(profile, bindings),
            FallbackSection(profile, bindings),
            // Ids are numbers by default; most people never need to see them.
            Form.Section(L("Advanced"), IdCard(profile, bindings)));
        ((FrameworkElement)page.Content).Margin = new Thickness(0, 0, 0, 0);
        page.Padding = new Thickness(16, 0, 32, 0);
        return page;
    }

    /// <summary>The id, changed with a button (or Enter) rather than as typed: every reference to it is rewritten.</summary>
    private static UIElement IdCard(ProfileViewModel profile, Bindings bindings)
    {
        var explanation = L("How rules in config.json refer to the profile, for example “work” instead of a number. Changing it updates them, and other computers still find their own targets under the old one.");
        var id = Ui.TextBox("work", monospaced: true, width: 200);
        bindings.Text(id, profile, nameof(ProfileViewModel.IdDraft), () => profile.IdDraft, value => profile.IdDraft = value);
        id.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && profile.ChangeIdCommand.CanExecute(null))
            {
                profile.ChangeIdCommand.Execute(null);
                e.Handled = true;
            }
        };
        var change = Ui.Button(L("Change"));
        bindings.Command(change, profile.ChangeIdCommand);

        // The explanation gives way to what is wrong with the id being typed.
        var description = Ui.Wrapping(explanation);
        bindings.Observe(profile, () =>
        {
            description.Text = profile.IdError ?? explanation;
            description.Foreground = Ui.Brush(profile.IdError is null ? "TextFillColorSecondaryBrush" : "SystemFillColorCriticalBrush");
        }, nameof(ProfileViewModel.IdError));
        return Form.Card(Ui.Label("ID"), Ui.HStack(8, id, change), description);
    }

    private static UIElement BrowserSection(ProfileViewModel profile, Bindings bindings)
    {
        var browser = Ui.ComboBox(260);
        bindings.Popup(browser, profile, nameof(ProfileViewModel.Browser), () => profile.BrowserChoices, () => profile.Browser, value => profile.Browser = value, c => c.Title, c => c.IconPng);
        var windows = Ui.ComboBox(260);
        bindings.Popup(windows, profile, nameof(ProfileViewModel.WindowsBrowser), () => profile.WindowsBrowserChoices, () => profile.WindowsBrowser, value => profile.WindowsBrowser = value, c => c.Title, c => c.IconPng);
        var mac = Ui.ComboBox(260);
        bindings.Popup(mac, profile, nameof(ProfileViewModel.MacOSBrowser), () => profile.MacOSBrowserChoices, () => profile.MacOSBrowser, value => profile.MacOSBrowser = value, c => c.Title, c => c.IconPng);
        var perOs = Ui.Switch();
        bindings.Switch(perOs, profile, nameof(ProfileViewModel.PerOs), () => profile.PerOs, value => profile.PerOs = value);
        var browserProfile = Ui.ComboBox(260);
        bindings.Popup(browserProfile, profile, nameof(ProfileViewModel.BrowserProfile), () => profile.BrowserProfileChoices, () => profile.BrowserProfile, value => profile.BrowserProfile = value, c => c.Title);
        var isPrivate = Ui.Switch();
        bindings.Switch(isPrivate, profile, nameof(ProfileViewModel.Private), () => profile.Private, value => profile.Private = value);
        var args = Ui.LinesBox("--new-window");
        bindings.Text(args, profile, nameof(ProfileViewModel.ExtraArgs), () => profile.ExtraArgs, value => profile.ExtraArgs = value);

        var browserCard = Form.Card(L("Browser"), browser);
        var windowsCard = Form.Card(L("Browser on Windows"), windows);
        var macCard = Form.Card(L("Browser on macOS"), mac);
        var profileCard = Form.Card(L("Browser profile"), browserProfile, L("Opens the browser with this profile signed in."));
        var section = Form.Section(L("Browser"),
            browserCard,
            Form.Card(L("Different browser on each OS"), perOs, L("For a configuration shared with Mac computers.")),
            windowsCard,
            macCard,
            profileCard,
            Form.Card(L("Private window"), isPrivate, L("InPrivate in Edge, incognito in Chrome.")),
            Form.Card(L("Extra arguments"), args, L("One per line.")));

        bindings.Observe(profile, () =>
        {
            Ui.Show(section, profile.IsBrowser);
            Ui.Show(browserCard, !profile.PerOs);
            Ui.Show(windowsCard, profile.PerOs);
            Ui.Show(macCard, profile.PerOs);
            Ui.Show(profileCard, profile.HasBrowserProfiles);
        }, nameof(ProfileViewModel.TargetKind), nameof(ProfileViewModel.PerOs), nameof(ProfileViewModel.HasBrowserProfiles));
        return section;
    }

    private static UIElement CommandSection(ProfileViewModel profile, Bindings bindings)
    {
        var windowsPath = Ui.TextBox(@"%LOCALAPPDATA%\Programs\Foo\foo.exe");
        bindings.Text(windowsPath, profile, nameof(ProfileViewModel.WindowsCommandPath), () => profile.WindowsCommandPath, value => profile.WindowsCommandPath = value);
        var browse = Ui.Button(L("Browse…"));
        bindings.Command(browse, profile.BrowseWindowsCommandCommand);
        var windowsArgs = Ui.LinesBox("{url}");
        bindings.Text(windowsArgs, profile, nameof(ProfileViewModel.WindowsCommandArgs), () => profile.WindowsCommandArgs, value => profile.WindowsCommandArgs = value);
        var macPath = Ui.TextBox("/Applications/Foo.app/Contents/MacOS/Foo");
        bindings.Text(macPath, profile, nameof(ProfileViewModel.MacOSCommandPath), () => profile.MacOSCommandPath, value => profile.MacOSCommandPath = value);
        var macArgs = Ui.LinesBox("{url}");
        bindings.Text(macArgs, profile, nameof(ProfileViewModel.MacOSCommandArgs), () => profile.MacOSCommandArgs, value => profile.MacOSCommandArgs = value);

        var section = Form.Section(L("Command"),
            L("{url} in the arguments is replaced with the URL; without it the URL is added at the end. Environment variables such as %LOCALAPPDATA% are expanded."),
            Form.Card(L("Program on Windows"), Ui.HStack(8, windowsPath, browse)),
            Form.Card(L("Arguments on Windows"), windowsArgs, L("One per line.")),
            Form.Card(L("Program on macOS"), macPath, L("For a configuration shared with Mac computers.")),
            Form.Card(L("Arguments on macOS"), macArgs, L("One per line.")));
        bindings.Observe(profile, () => Ui.Show(section, profile.IsCommand), nameof(ProfileViewModel.TargetKind));
        return section;
    }

    /// <summary>
    /// What this PC opens instead of the shared target (local.json). The cards belong to the target's own view model,
    /// which is replaced when the switch turns it on again, so they are rebuilt with it.
    /// </summary>
    private static UIElement LocalSection(ProfileViewModel profile, Bindings bindings)
    {
        var uses = Ui.Switch();
        bindings.Switch(uses, profile, nameof(ProfileViewModel.UsesLocalTarget), () => profile.UsesLocalTarget, value => profile.UsesLocalTarget = value);

        var cards = new StackPanel { Spacing = Form.CardSpacing };
        Bindings? targetBindings = null;
        bindings.Observe(profile, () =>
        {
            targetBindings?.Dispose();
            cards.Children.Clear();
            if (profile.LocalTarget is { } target)
            {
                targetBindings = new Bindings();
                foreach (var card in LocalTargetCards(target, targetBindings))
                {
                    cards.Children.Add(card);
                }
            }
        }, nameof(ProfileViewModel.LocalTarget));
        bindings.Add(() => targetBindings?.Dispose());

        return Form.Section(L("This Computer"),
            Form.Card(L("Open with another browser or command on this computer"), uses, L("In place of the browser or command above, for example another browser profile. Saved on this computer only; rules and other computers are not affected.")),
            cards);
    }

    private static UIElement[] LocalTargetCards(LocalTargetViewModel target, Bindings bindings)
    {
        var kind = Ui.ComboBox(160);
        bindings.Choice(kind, target, nameof(LocalTargetViewModel.TargetKind),
            [(ProfileTargetKind.Browser, L("Browser")), (ProfileTargetKind.Command, L("Command"))],
            () => target.TargetKind, value => target.TargetKind = value);
        var browser = Ui.ComboBox(260);
        bindings.Popup(browser, target, nameof(LocalTargetViewModel.Browser), () => target.BrowserChoices, () => target.Browser, value => target.Browser = value, c => c.Title, c => c.IconPng);
        var browserProfile = Ui.ComboBox(260);
        bindings.Popup(browserProfile, target, nameof(LocalTargetViewModel.BrowserProfile), () => target.BrowserProfileChoices, () => target.BrowserProfile, value => target.BrowserProfile = value, c => c.Title);
        var isPrivate = Ui.Switch();
        bindings.Switch(isPrivate, target, nameof(LocalTargetViewModel.Private), () => target.Private, value => target.Private = value);
        var args = Ui.LinesBox("--new-window");
        bindings.Text(args, target, nameof(LocalTargetViewModel.ExtraArgs), () => target.ExtraArgs, value => target.ExtraArgs = value);
        var path = Ui.TextBox(@"%LOCALAPPDATA%\Programs\Foo\foo.exe");
        bindings.Text(path, target, nameof(LocalTargetViewModel.CommandPath), () => target.CommandPath, value => target.CommandPath = value);
        var browse = Ui.Button(L("Browse…"));
        bindings.Command(browse, target.BrowseCommandCommand);
        var commandArgs = Ui.LinesBox("{url}");
        bindings.Text(commandArgs, target, nameof(LocalTargetViewModel.CommandArgs), () => target.CommandArgs, value => target.CommandArgs = value);

        var browserCard = Form.Card(L("Browser"), browser);
        var profileCard = Form.Card(L("Browser profile"), browserProfile, L("Opens the browser with this profile signed in."));
        var privateCard = Form.Card(L("Private window"), isPrivate, L("InPrivate in Edge, incognito in Chrome."));
        var argsCard = Form.Card(L("Extra arguments"), args, L("One per line."));
        var pathCard = Form.Card(L("Program"), Ui.HStack(8, path, browse));
        var commandArgsCard = Form.Card(L("Arguments"), commandArgs, L("One per line. {url} is replaced with the URL; without it the URL is added at the end."));
        bindings.Observe(target, () =>
        {
            foreach (var card in new[] { browserCard, privateCard, argsCard })
            {
                Ui.Show(card, target.IsBrowser);
            }
            Ui.Show(profileCard, target.IsBrowser && target.HasBrowserProfiles);
            Ui.Show(pathCard, target.IsCommand);
            Ui.Show(commandArgsCard, target.IsCommand);
        }, nameof(LocalTargetViewModel.TargetKind), nameof(LocalTargetViewModel.HasBrowserProfiles));

        return [Form.Card(L("Opens with"), kind), browserCard, profileCard, privateCard, argsCard, pathCard, commandArgsCard];
    }

    private static UIElement FallbackSection(ProfileViewModel profile, Bindings bindings)
    {
        var fallback = Ui.ComboBox(260);
        bindings.Popup(fallback, profile, nameof(ProfileViewModel.Fallback), () => profile.FallbackChoices, () => profile.Fallback, value => profile.Fallback = value, c => c.Title);
        return Form.Section(L("When Not Available"),
            Form.Card(L("Profile to use instead"), fallback, L("For example when the browser is not installed on another computer that shares this configuration.")));
    }
}
