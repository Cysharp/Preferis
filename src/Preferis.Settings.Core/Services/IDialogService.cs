namespace Preferis.Settings.Services;

/// <summary>Dialogs the view models need. Implemented with Avalonia's storage provider and a small message window; faked in the tests.</summary>
public interface IDialogService
{
    /// <summary>Returns the chosen folder, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Returns the chosen file, or null when cancelled.</summary>
    Task<string?> PickFileAsync(string title);

    /// <summary>Shows a message with one button per entry. Returns the index of the button pressed, or -1 when the dialog was dismissed.</summary>
    Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons);
}
