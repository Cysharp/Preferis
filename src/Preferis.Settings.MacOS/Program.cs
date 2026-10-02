using AppKit;
using Foundation;
using ObjCRuntime;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS;

internal static class Program
{
    private static int Main(string[] args)
    {
        NSApplication.Init();
        var app = NSApplication.SharedApplication;
        // Snapshots render offscreen; keep the app out of the Dock and away from the focus meanwhile.
        app.ActivationPolicy = Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 }
            ? NSApplicationActivationPolicy.Accessory
            : NSApplicationActivationPolicy.Regular;
        app.Delegate = new AppDelegate();
        app.Run();
        return 0;
    }
}

internal sealed class AppDelegate : NSApplicationDelegate
{
    private SettingsViewModel? _settings;
    private SettingsWindow? _window;

    public AppDelegate()
    {
    }

    public AppDelegate(NativeHandle handle)
        : base(handle)
    {
    }

    public override void DidFinishLaunching(NSNotification notification)
    {
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            // AppKit would log and swallow the exception, leaving a running app without a window.
            Console.Error.WriteLine($"Preferis Settings could not start: {ex}");
            Environment.Exit(1);
        }
    }

    private void Start()
    {
        var loader = SettingsConfigLoader.Create();
        _settings = new SettingsViewModel(loader, new MacSettingsPlatform(loader.LoadLocal()), new AppKitDialogService(), new TimerSaveScheduler());
        _window = new SettingsWindow(_settings);
        NSApplication.SharedApplication.MainMenu = MainMenu.Build();

        if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 } directory)
        {
            Snapshots.TakeAndExit(_window, _settings, directory);
            return;
        }

        _window.Show();
        if (OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            NSApplication.SharedApplication.Activate();
        }
        else
        {
#pragma warning disable CA1422 // The replacement exists only from macOS 14.
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422
        }
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    // Quitting saves the edits still waiting, like closing the window, and asks only when they cannot be saved.
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        if (_settings is not { IsDirty: true })
        {
            return NSApplicationTerminateReply.Now;
        }
        Answer(_settings);
        return NSApplicationTerminateReply.Later;
    }

    private static async void Answer(SettingsViewModel settings)
        => NSApplication.SharedApplication.ReplyToApplicationShouldTerminate(await settings.CanCloseAsync());
}

/// <summary>The menu bar. The Edit menu is what makes copy, paste and undo work in the text fields.</summary>
internal static class MainMenu
{
    public static NSMenu Build()
    {
        var menu = new NSMenu();

        var app = new NSMenu("Preferis Settings");
        app.AddItem(new NSMenuItem("About Preferis Settings", new Selector("orderFrontStandardAboutPanel:"), ""));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Hide Preferis Settings", new Selector("hide:"), "h"));
        var hideOthers = new NSMenuItem("Hide Others", new Selector("hideOtherApplications:"), "h") { KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask };
        app.AddItem(hideOthers);
        app.AddItem(new NSMenuItem("Show All", new Selector("unhideAllApplications:"), ""));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Quit Preferis Settings", new Selector("terminate:"), "q"));
        menu.AddItem(new NSMenuItem("Preferis Settings") { Submenu = app });

        // No Save or Revert: edits save themselves.
        var file = new NSMenu("File");
        file.AddItem(new NSMenuItem("Close", new Selector("performClose:"), "w"));
        menu.AddItem(new NSMenuItem("File") { Submenu = file });

        var edit = new NSMenu("Edit");
        edit.AddItem(new NSMenuItem("Undo", new Selector("undo:"), "z"));
        edit.AddItem(new NSMenuItem("Redo", new Selector("redo:"), "Z"));
        edit.AddItem(NSMenuItem.SeparatorItem);
        edit.AddItem(new NSMenuItem("Cut", new Selector("cut:"), "x"));
        edit.AddItem(new NSMenuItem("Copy", new Selector("copy:"), "c"));
        edit.AddItem(new NSMenuItem("Paste", new Selector("paste:"), "v"));
        edit.AddItem(new NSMenuItem("Select All", new Selector("selectAll:"), "a"));
        menu.AddItem(new NSMenuItem("Edit") { Submenu = edit });

        var window = new NSMenu("Window");
        window.AddItem(new NSMenuItem("Minimize", new Selector("performMiniaturize:"), "m"));
        window.AddItem(new NSMenuItem("Zoom", new Selector("performZoom:"), ""));
        menu.AddItem(new NSMenuItem("Window") { Submenu = window });
        NSApplication.SharedApplication.WindowsMenu = window;

        return menu;
    }
}
