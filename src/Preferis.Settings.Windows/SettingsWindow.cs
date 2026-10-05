using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Preferis.Core.Localization;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Windows.Controls;
using Preferis.Settings.Windows.Pages;
using Windows.Graphics;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows;

/// <summary>
/// The settings window, laid out like the Windows 11 Settings app: the title bar drawn over the Mica material, the pages
/// in a navigation pane on the left, the page with its title on the right. Edits save themselves, so there is no Save
/// button; the line under the title counts the problems of the configuration.
/// </summary>
internal sealed class SettingsWindow
{
    private readonly SettingsViewModel _settings;
    private readonly Bindings _bindings = new();
    private readonly NavigationView _navigation;
    private readonly ContentControl _pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _title = new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _subtitle;
    private IPageView? _page;
    private PageViewModel? _shownPage;
    private bool _updatingNavigation;
    private bool _closeConfirmed;

    public SettingsWindow(SettingsViewModel settings)
    {
        _settings = settings;
        _subtitle = Ui.Secondary("", 13);

        Window = new Window { Title = Program.Title };
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = 900;
        presenter.PreferredMinimumHeight = 600;
        Window.AppWindow.SetPresenter(presenter);
        Window.ExtendsContentIntoTitleBar = true;
        Window.SystemBackdrop = new MicaBackdrop();

        var titleBar = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
        titleBar.Children.Add(new TextBlock { Text = Program.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        Window.SetTitleBar(titleBar);

        // Like the Settings app, the pane folds to its icons when the window is narrow, leaving the room to the page.
        _navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            OpenPaneLength = 260,
        };
        // The button that opens a folded pane; an open pane has nothing to toggle.
        _navigation.DisplayModeChanged += (_, e) => _navigation.IsPaneToggleButtonVisible = e.DisplayMode != NavigationViewDisplayMode.Expanded;
        _navigation.SelectionChanged += (_, e) =>
        {
            if (!_updatingNavigation && e.SelectedItem is NavigationViewItem { Tag: int index } && index < _settings.Pages.Count)
            {
                _settings.SelectedPage = _settings.Pages[index];
            }
        };

        var heading = Ui.VStack(2, _title, _subtitle);
        heading.Margin = new Thickness(32, 4, 32, 12);
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = Ui.Star });
        content.Children.Add(heading);
        var banner = Banner(settings, _bindings);
        Grid.SetRow(banner, 1);
        content.Children.Add(banner);
        Grid.SetRow(_pageHost, 2);
        content.Children.Add(_pageHost);
        _navigation.Content = content;

        var root = new Grid();
        if (UiLanguages.Current == UiLanguage.Japanese)
        {
            // Text falls back to the fonts of this language: kanji in a Chinese font on an English Windows otherwise.
            root.Language = "ja-JP";
        }
        root.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = Ui.Star });
        root.Children.Add(titleBar);
        Grid.SetRow(_navigation, 1);
        root.Children.Add(_navigation);
        Window.Content = root;
        Root = root;

        // Coming back to the window is when a change made elsewhere (the Router, a sync client) should show up.
        Window.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _settings.CheckForExternalChanges();
            }
        };
        Window.AppWindow.Closing += OnClosing;

        _bindings.Observe(settings, ShowPage, nameof(SettingsViewModel.SelectedPage), nameof(SettingsViewModel.Pages));
        _bindings.Observe(settings, UpdateTitle, nameof(SettingsViewModel.SelectedPage), nameof(SettingsViewModel.IssueSummary));
    }

    public Window Window { get; }

    /// <summary>The window's content, for snapshots.</summary>
    public Grid Root { get; }

    /// <summary>The size the window opens with, in device independent pixels.</summary>
    public static readonly (double Width, double Height) DefaultSize = (1120, 780);

    public void Show()
    {
        Window.AppWindow.Resize(PhysicalDefaultSize());
        Window.Activate();
    }

    /// <summary><see cref="DefaultSize"/> on the window's current display.</summary>
    public SizeInt32 PhysicalDefaultSize()
    {
        var scale = Win32Scale();
        return new SizeInt32((int)(DefaultSize.Width * scale), (int)(DefaultSize.Height * scale));
    }

    private double Win32Scale()
    {
        var dpi = Win32.GetDpiForWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(Window.AppWindow.Id));
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private void ShowPage()
    {
        RebuildNavigation();
        var page = _settings.SelectedPage;
        // Reloading the configuration replaces most page view models; the view follows the instance.
        if (ReferenceEquals(page, _shownPage))
        {
            return;
        }
        _shownPage = page;
        _page?.Dispose();
        _page = page switch
        {
            ProfilesPageViewModel profiles => new ProfilesPage(profiles),
            RulesPageViewModel rules => new RulesPage(rules),
            TestPageViewModel test => new TestPage(test),
            GeneralPageViewModel general => new GeneralPage(general),
            _ => null,
        };
        _pageHost.Content = _page?.View;
    }

    // The items stay; only the selection follows the view model (whose page objects change on every reload).
    private void RebuildNavigation()
    {
        _updatingNavigation = true;
        if (_navigation.MenuItems.Count != _settings.Pages.Count)
        {
            _navigation.MenuItems.Clear();
            for (var i = 0; i < _settings.Pages.Count; i++)
            {
                _navigation.MenuItems.Add(new NavigationViewItem
                {
                    Content = _settings.Pages[i].Title,
                    Icon = new FontIcon { Glyph = PageGlyph(_settings.Pages[i]) },
                    Tag = i,
                });
            }
        }
        var index = _settings.SelectedPage is { } selected ? _settings.Pages.ToList().IndexOf(selected) : -1;
        _navigation.SelectedItem = index >= 0 ? _navigation.MenuItems[index] : null;
        _updatingNavigation = false;
    }

    // By page rather than by position in the pane, which may change.
    private static string PageGlyph(PageViewModel page) => page switch
    {
        GeneralPageViewModel => Ui.Glyphs.Settings,
        ProfilesPageViewModel => Ui.Glyphs.People,
        RulesPageViewModel => Ui.Glyphs.Branch,
        _ => Ui.Glyphs.Play,
    };

    private void UpdateTitle()
    {
        _title.Text = _settings.SelectedPage?.Title ?? Program.Title;
        _subtitle.Text = _settings.IssueSummary;
        Ui.Show(_subtitle, _settings.IssueSummary.Length > 0);
    }

    // Edits still waiting for the pause are saved first; closing asks only when they cannot be.
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_settings.HasPendingChanges)
        {
            return;
        }
        args.Cancel = true;
        if (await _settings.CanCloseAsync())
        {
            _closeConfirmed = true;
            Window.Close();
        }
    }

    /// <summary>
    /// The notice above the page: edits held back because <c>config.json</c> changed elsewhere or could not be read, or a
    /// failed operation.
    /// </summary>
    private static InfoBar Banner(SettingsViewModel settings, Bindings bindings)
    {
        var theirs = Ui.Button(L("Use Theirs"));
        bindings.Command(theirs, settings.UseTheirsCommand);
        var mine = Ui.Button(L("Keep Mine"));
        bindings.Command(mine, settings.KeepMineCommand);
        var actions = Ui.HStack(8, theirs, mine);
        var bar = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            Margin = new Thickness(32, 0, 32, 12),
            Content = actions,
        };
        bindings.Observe(settings, () =>
        {
            var message = settings.HasExternalChanges
                ? L("config.json was changed outside the settings (for example by “Always open … with this choice” or a sync client) before your latest changes were saved. Use the file as it is now, or save your changes over it.")
                : settings.IsHoldingEditsForUnreadableFile
                    ? L("{0} Your changes to it are not saved until you replace the file with them.", settings.LoadError)
                    : settings.StatusMessage ?? settings.LoadError;
            bar.Message = message ?? "";
            Ui.Show(theirs, settings.HasExternalChanges);
            Ui.Show(mine, settings.HasExternalChanges || settings.IsHoldingEditsForUnreadableFile);
            mine.Content = settings.HasExternalChanges ? L("Keep Mine") : L("Replace");
            Ui.Show(actions, settings.HasExternalChanges || settings.IsHoldingEditsForUnreadableFile);
            bar.IsOpen = message is not null;
            Ui.Show(bar, message is not null);
        }, nameof(SettingsViewModel.HasExternalChanges), nameof(SettingsViewModel.IsHoldingEditsForUnreadableFile), nameof(SettingsViewModel.StatusMessage), nameof(SettingsViewModel.LoadError));
        return bar;
    }
}

/// <summary>A page of the window. Disposing it releases its bindings to the view models.</summary>
internal interface IPageView : IDisposable
{
    UIElement View { get; }
}
