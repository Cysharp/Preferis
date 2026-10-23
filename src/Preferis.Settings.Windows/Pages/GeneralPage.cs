using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Windows.Controls;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows.Pages;

/// <summary>Default browser, where config.json lives, the picker's behaviour, language and this PC's logging.</summary>
internal sealed class GeneralPage : IPageView
{
    private readonly Bindings _bindings = new();

    public GeneralPage(GeneralPageViewModel page)
    {
        var version = Form.Footer($"Preferis {page.Version}");
        version.Margin = new Thickness(1, 28, 0, 0);
        View = Form.Page(
            DefaultBrowser(page),
            ConfigurationFile(page),
            Picker(page),
            Language(page),
            ThisPC(page),
            version);
    }

    public UIElement View { get; }

    public void Dispose() => _bindings.Dispose();

    private UIElement DefaultBrowser(GeneralPageViewModel page)
    {
        var status = Ui.Label("");
        status.TextWrapping = TextWrapping.Wrap;
        var make = Ui.AccentButton(L("Make Default"));
        _bindings.Command(make, page.RegisterCommand);
        var settings = Ui.Button(L("Open Windows Settings"));
        _bindings.Command(settings, page.OpenSystemSettingsCommand);
        _bindings.Observe(page, () =>
        {
            status.Text = page.RegistrationText;
            Ui.Show(make, !page.IsDefaultBrowser);
        }, nameof(GeneralPageViewModel.RegistrationText), nameof(GeneralPageViewModel.IsDefaultBrowser));

        var icon = new Grid { Width = 32, Height = 32 };
        _bindings.Observe(page, () =>
        {
            icon.Children.Clear();
            icon.Children.Add(Ui.Glyph(page.IsDefaultBrowser ? Ui.Glyphs.Completed : Ui.Glyphs.Shield, 24,
                Ui.Brush(page.IsDefaultBrowser ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush")));
        }, nameof(GeneralPageViewModel.IsDefaultBrowser));

        var section = Form.Section(L("Default Browser"),
            L("Windows lets you choose the default browser only in its Settings: Make Default registers Preferis and opens that page. Preferis then receives every link and opens it according to the rules."),
            Form.Card(status, Ui.HStack(8, settings, make), icon: icon));
        section.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(1, 8, 0, 4));
        return section;
    }

    private UIElement ConfigurationFile(GeneralPageViewModel page)
    {
        var path = Ui.Secondary("");
        path.IsTextSelectionEnabled = true;
        _bindings.Observe(page, () =>
        {
            path.Text = page.ConfigPath;
            ToolTipService.SetToolTip(path, page.ConfigPath);
        }, nameof(GeneralPageViewModel.ConfigPath));
        var reveal = Ui.Button(L("Show in Explorer"));
        _bindings.Command(reveal, page.RevealConfigCommand);

        var change = new DropDownButton { Content = L("Change Location") };
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Bindings.MenuItem(L("Move To…"), page.MoveConfigCommand));
        menu.Items.Add(Bindings.MenuItem(L("Use Existing File…"), page.UseExistingConfigCommand));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Bindings.MenuItem(L("Move Back to the Default Location"), page.MoveToDefaultLocationCommand));
        change.Flyout = menu;

        return Form.Section(L("Configuration File"), page.ConfigPathHint,
            Form.Card(Ui.Label("config.json"), Ui.HStack(8, reveal, change), path, Ui.Glyph(Ui.Glyphs.Folder, 20, Ui.Brush("TextFillColorSecondaryBrush"))));
    }

    private UIElement Picker(GeneralPageViewModel page)
    {
        var position = Ui.ComboBox();
        _bindings.Choice(position, page, nameof(GeneralPageViewModel.PickerPosition),
            new[] { (PickerPosition.Cursor, L("Near the pointer")), (PickerPosition.Center, L("Center of the screen")) },
            () => page.PickerPosition, value => page.PickerPosition = value);

        var seconds = new NumberBox { Minimum = 0, Maximum = 60, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 120 };
        _bindings.Number(seconds, page, nameof(GeneralPageViewModel.AutoSelectSeconds), () => page.AutoSelectSeconds, value => page.AutoSelectSeconds = value);

        var remember = Ui.Switch();
        _bindings.Switch(remember, page, nameof(GeneralPageViewModel.RememberChoiceDefault), () => page.RememberChoiceDefault, value => page.RememberChoiceDefault = value);

        return Form.Section(L("Picker"), L("Shown when a rule offers several profiles. These settings are shared with every computer using this configuration."),
            Form.Card(L("Position"), position),
            Form.Card(L("Choose automatically after"), seconds, L("In seconds. 0 keeps the picker open until you choose.")),
            Form.Card(L("Remember the choice by default"), remember, L("Checks “Always open this host with this choice” when the picker opens.")));
    }

    private UIElement Language(GeneralPageViewModel page)
    {
        var language = Ui.ComboBox();
        _bindings.Popup(language, page, nameof(GeneralPageViewModel.Language), () => page.Languages, () => page.Language, value => page.Language = value, l => l.Title);
        return Form.Section(L("Language"), Form.Card(L("Language"), language, L("The picker shows a new language from the next link on, the settings the next time they open.")));
    }

    private UIElement ThisPC(GeneralPageViewModel page)
    {
        var level = Ui.ComboBox(120);
        _bindings.Choice(level, page, nameof(GeneralPageViewModel.LogLevel), page.LogLevels.Select(l => (l, GeneralPageViewModel.LogLevelTitle(l))).ToList(), () => page.LogLevel, value => page.LogLevel = value);
        var logs = Ui.Button(L("Show Logs"));
        _bindings.Command(logs, page.OpenLogFolderCommand);

        var history = Ui.Switch();
        _bindings.Switch(history, page, nameof(GeneralPageViewModel.IsHistoryEnabled), () => page.IsHistoryEnabled, value => page.IsHistoryEnabled = value);
        var size = new NumberBox { Minimum = 1, Maximum = 10_000, SmallChange = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 120 };
        _bindings.Number(size, page, nameof(GeneralPageViewModel.HistorySize), () => page.HistorySize, value => page.HistorySize = (int)value);
        _bindings.Observe(page, () => size.IsEnabled = page.IsHistoryEnabled, nameof(GeneralPageViewModel.IsHistoryEnabled));

        return Form.Section(L("This PC"), L("Kept in local.json on this PC only, never synced."),
            Form.Card(L("Log level"), Ui.HStack(8, level, logs)),
            Form.Card(L("Keep a history of opened links"), history, L("Records the URL of every link Preferis receives. The Test page shows the history to route them again.")),
            Form.Card(L("Links to keep"), size));
    }
}
