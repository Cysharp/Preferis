using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Preferis.Settings.Services;

namespace Preferis.Settings.Windows;

/// <summary>
/// Questions as a ContentDialog and file choices with the Windows App SDK pickers, which work in an unpackaged app
/// (the older Windows.Storage pickers need a window handle wired in through COM).
/// </summary>
internal sealed class WinUIDialogService : IDialogService
{
    private readonly Func<Window?> _window;
    private bool _asking;

    public WinUIDialogService(Func<Window?> window)
    {
        _window = window;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (_window() is not { } window)
        {
            return null;
        }
        var picker = new FolderPicker(window.AppWindow.Id) { CommitButtonText = "Choose" };
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    public async Task<string?> PickFileAsync(string title)
    {
        if (_window() is not { } window)
        {
            return null;
        }
        var picker = new FileOpenPicker(window.AppWindow.Id) { CommitButtonText = "Choose" };
        picker.FileTypeFilter.Add("*");
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    /// <remarks>
    /// A ContentDialog has three buttons at most, which is all the view models ask with. The last one is the close button,
    /// so Esc answers it; the view models always put Cancel last.
    /// </remarks>
    public async Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        // Only one ContentDialog may be open per window.
        if (_asking || _window()?.Content?.XamlRoot is not { } root || buttons.Count is 0 or > 3)
        {
            return -1;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            XamlRoot = root,
            CloseButtonText = buttons[^1],
            DefaultButton = buttons.Count > 1 ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };
        if (buttons.Count > 1)
        {
            dialog.PrimaryButtonText = buttons[0];
        }
        if (buttons.Count > 2)
        {
            dialog.SecondaryButtonText = buttons[1];
        }

        _asking = true;
        try
        {
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => 0,
                ContentDialogResult.Secondary => 1,
                _ => buttons.Count - 1,
            };
        }
        finally
        {
            _asking = false;
        }
    }
}

/// <summary>Saves after a pause in editing: every edit restarts the wait (<see cref="ISaveScheduler"/>).</summary>
internal sealed class DispatcherSaveScheduler : ISaveScheduler
{
    // Long enough for a word typed into a field, short enough that the file is current by the time anyone looks.
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(0.6);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private Action? _save;

    public void Schedule(Action save)
    {
        _save = save;
        if (_timer is null)
        {
            _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.Interval = Pause;
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) =>
            {
                var pending = _save;
                _save = null;
                pending?.Invoke();
            };
        }
        _timer.Stop();
        _timer.Start();
    }
}
