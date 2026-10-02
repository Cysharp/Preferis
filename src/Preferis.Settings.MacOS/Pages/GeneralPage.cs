using System.Globalization;
using AppKit;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.ViewModels;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.MacOS.Pages;

/// <summary>Default browser, where config.json lives, the picker's behaviour, language and this Mac's logging.</summary>
internal sealed class GeneralPage : IPageView
{
    private readonly Bindings _bindings = new();

    public GeneralPage(GeneralPageViewModel page)
    {
        View = Form.Page(
            DefaultBrowser(page),
            ConfigurationFile(page),
            Picker(page),
            Language(page),
            ThisMac(page),
            Form.Footer($"Preferis {page.Version}"));
    }

    public NSView View { get; }

    public void Dispose() => _bindings.Dispose();

    private NSView DefaultBrowser(GeneralPageViewModel page)
    {
        var status = Ui.Wrapping("", 380, 13, NSColor.Label);
        var make = Ui.Button(L("Make Default"));
        _bindings.Command(make, page.RegisterCommand);
        var settings = Ui.Button(L("Open System Settings"));
        _bindings.Command(settings, page.OpenSystemSettingsCommand);
        _bindings.Observe(page, () =>
        {
            status.StringValue = page.RegistrationText;
            make.Hidden = page.IsDefaultBrowser;
        }, nameof(GeneralPageViewModel.RegistrationText), nameof(GeneralPageViewModel.IsDefaultBrowser));

        var icon = NSImageView.FromImage(Images.Symbol("checkmark.seal.fill", 20)!);
        _bindings.Observe(page, () => icon.ContentTintColor = page.IsDefaultBrowser ? NSColor.SystemGreen : NSColor.TertiaryLabel, nameof(GeneralPageViewModel.IsDefaultBrowser));

        return Form.Section(L("Default Browser"), L("macOS asks you to confirm the change. Preferis then receives every link and opens it according to the rules."),
            Form.Row(Ui.HStack(8, icon, status), Ui.HStack(8, settings, make)));
    }

    private NSView ConfigurationFile(GeneralPageViewModel page)
    {
        var path = Ui.Label("", 12, color: NSColor.SecondaryLabel);
        path.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        path.Selectable = true;
        _bindings.Observe(page, () =>
        {
            path.StringValue = page.ConfigPath;
            path.ToolTip = page.ConfigPath;
        }, nameof(GeneralPageViewModel.ConfigPath));
        var reveal = Ui.Button(L("Show in Finder"));
        _bindings.Command(reveal, page.RevealConfigCommand);

        var change = Ui.PullDown(L("Change Location"), []);
        var moveTo = new NSMenuItem(L("Move To…"), (_, _) => page.MoveConfigCommand.Execute(null));
        var useExisting = new NSMenuItem(L("Use Existing File…"), (_, _) => page.UseExistingConfigCommand.Execute(null));
        var backHome = new NSMenuItem(L("Move Back to the Default Location"), (_, _) => page.MoveToDefaultLocationCommand.Execute(null));
        change.Menu!.AddItem(moveTo);
        change.Menu.AddItem(useExisting);
        change.Menu.AddItem(NSMenuItem.SeparatorItem);
        change.Menu.AddItem(backHome);
        change.Menu.AutoEnablesItems = false;
        _bindings.Observe(page, () => backHome.Enabled = !page.IsAtDefaultLocation, nameof(GeneralPageViewModel.IsAtDefaultLocation));

        var location = Ui.VStack(2, Ui.Label("config.json"), path);
        location.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        return Form.Section(L("Configuration File"), page.ConfigPathHint,
            Form.Row(location, Ui.HStack(8, reveal, change)));
    }

    private NSView Picker(GeneralPageViewModel page)
    {
        var position = Ui.Popup();
        _bindings.Choice(position, page, nameof(GeneralPageViewModel.PickerPosition),
            [(PickerPosition.Cursor, L("Near the Pointer")), (PickerPosition.Center, L("Center of the Screen"))],
            () => page.PickerPosition, value => page.PickerPosition = value);

        var seconds = NumberField(page, nameof(GeneralPageViewModel.AutoSelectSeconds), 0, 60, 1, () => page.AutoSelectSeconds, value => page.AutoSelectSeconds = value);

        var remember = Ui.Switch();
        _bindings.Switch(remember, page, nameof(GeneralPageViewModel.RememberChoiceDefault), () => page.RememberChoiceDefault, value => page.RememberChoiceDefault = value);

        return Form.Section(L("Picker"), L("Shown when a rule offers several profiles. These settings are shared with every computer using this configuration."),
            Form.Row(L("Position"), position),
            Form.Row(L("Choose automatically after"), seconds, L("In seconds. 0 keeps the picker open until you choose.")),
            Form.Row(L("Remember the choice by default"), remember, L("Checks “Always open this host with this choice” when the picker opens.")));
    }

    private NSView Language(GeneralPageViewModel page)
    {
        var language = Ui.Popup();
        _bindings.Popup(language, page, nameof(GeneralPageViewModel.Language), () => page.Languages, () => page.Language, value => page.Language = value, l => l.Title);
        return Form.Section(L("Language"), Form.Row(L("Language"), language, L("The picker shows a new language from the next link on, the settings the next time they open.")));
    }

    private NSView ThisMac(GeneralPageViewModel page)
    {
        var level = Ui.Popup();
        _bindings.Choice(level, page, nameof(GeneralPageViewModel.LogLevel), page.LogLevels.Select(l => (l, GeneralPageViewModel.LogLevelTitle(l))).ToList(), () => page.LogLevel, value => page.LogLevel = value);
        var logs = Ui.Button(L("Show Logs"));
        _bindings.Command(logs, page.OpenLogFolderCommand);

        var history = Ui.Switch();
        _bindings.Switch(history, page, nameof(GeneralPageViewModel.IsHistoryEnabled), () => page.IsHistoryEnabled, value => page.IsHistoryEnabled = value);
        var size = NumberField(page, nameof(GeneralPageViewModel.HistorySize), 1, 10_000, 10, () => page.HistorySize, value => page.HistorySize = (int)value);
        _bindings.Observe(page, () => size.Subviews.OfType<NSControl>().ToList().ForEach(c => c.Enabled = page.IsHistoryEnabled), nameof(GeneralPageViewModel.IsHistoryEnabled));

        return Form.Section(L("This Mac"), L("Kept in local.json on this Mac only, never synced."),
            Form.Row(L("Log level"), Ui.HStack(8, level, logs)),
            Form.Row(L("Keep a history of opened links"), history, L("Records the URL of every link Preferis receives. The Test page shows the history to route them again.")),
            Form.Row(L("Links to keep"), size));
    }

    // A short number field with a stepper, two-way bound, like a SwiftUI stepper row.
    private NSStackView NumberField(GeneralPageViewModel page, string property, double min, double max, double step, Func<double> get, Action<double> set)
    {
        var field = Ui.Field();
        field.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        field.WidthAnchor.ConstraintEqualTo(56).Active = true;
        var stepper = new NSStepper { MinValue = min, MaxValue = max, Increment = step, ValueWraps = false };
        _bindings.Observe(page, () =>
        {
            var value = get();
            field.StringValue = value.ToString(CultureInfo.InvariantCulture);
            stepper.DoubleValue = value;
        }, property);
        field.Changed += (_, _) =>
        {
            if (double.TryParse(field.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                set(Math.Clamp(value, min, max));
            }
        };
        stepper.Activated += (_, _) => set(stepper.DoubleValue);
        return Ui.HStack(6, field, stepper);
    }
}
