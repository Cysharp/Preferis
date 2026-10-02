using AppKit;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.MacOS;

/// <summary>
/// Questions as NSAlert and file choices as NSOpenPanel, run modally. Modal rather than as sheets: the sheet APIs take
/// blocks, and a failure inside a block brought the NativeAOT picker down (docs/Platform.md §3.7).
/// </summary>
internal sealed class AppKitDialogService : IDialogService
{
    public Task<string?> PickFolderAsync(string title)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Message = title;
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.CanCreateDirectories = true;
        panel.AllowsMultipleSelection = false;
        panel.Prompt = L("Choose");
        return Task.FromResult(panel.RunModal() == 1 ? panel.Url?.Path : null);
    }

    public Task<string?> PickFileAsync(string title)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Message = title;
        panel.CanChooseDirectories = false;
        panel.CanChooseFiles = true;
        // Application bundles are folders; let them be chosen as a whole (a command profile can open one).
        panel.TreatsFilePackagesAsDirectories = false;
        panel.AllowsMultipleSelection = false;
        return Task.FromResult(panel.RunModal() == 1 ? panel.Url?.Path : null);
    }

    public Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        var alert = new NSAlert { MessageText = title, InformativeText = message, AlertStyle = NSAlertStyle.Informational };
        foreach (var button in buttons)
        {
            var added = alert.AddButton(button);
            if (button == L("Cancel"))
            {
                added.KeyEquivalent = "\u001b";
            }
        }
        // The first button answers 1000 (NSAlertFirstButtonReturn), the next 1001 and so on.
        var response = (int)alert.RunModal() - 1000;
        return Task.FromResult(response >= 0 && response < buttons.Count ? response : -1);
    }
}
