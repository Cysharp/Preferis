namespace Preferis.Core.Picker;

/// <summary>
/// The picker dialog's behaviour without any UI: which candidate is highlighted, keyboard shortcuts, the
/// "remember" option and the auto-select countdown. Each OS picker (AppKit, WinUI) only renders this state and
/// forwards input to it, so the rules are the same everywhere and testable. See docs/Design.md §9.2.
/// Not thread safe: call it from the UI thread.
/// </summary>
public sealed class PickerSession
{
    private readonly TimeProvider _time;
    private readonly TimeSpan? _autoSelectAfter;
    private long _lastActivity;
    private bool _remember;

    public PickerSession(PickerRequest request, TimeProvider time)
    {
        Request = request;
        _time = time;
        _autoSelectAfter = request.Options.AutoSelectSeconds > 0 ? TimeSpan.FromSeconds(request.Options.AutoSelectSeconds) : null;
        _lastActivity = time.GetTimestamp();
        _remember = CanRemember && request.Options.RememberChoiceDefault;
        SelectedIndex = request.Candidates.Count > 0 ? 0 : -1;
    }

    public PickerRequest Request { get; }

    public IReadOnlyList<PickerCandidate> Candidates => Request.Candidates;

    /// <summary>Highlighted candidate, or -1 when there is none.</summary>
    public int SelectedIndex { get; private set; }

    public bool CanRemember => Request.Options.AllowRemember;

    /// <summary>"Always use this choice for this host". Stays false when the request does not allow remembering.</summary>
    public bool Remember
    {
        get => _remember;
        set
        {
            NoteActivity();
            _remember = value && CanRemember;
        }
    }

    /// <summary>The answer, once the user (or the countdown) decided. Later calls do not change it.</summary>
    public PickerResponse? Response { get; private set; }

    public bool IsCompleted => Response is not null;

    /// <summary>Time left until the highlighted candidate is chosen automatically, or null when auto-select is off or the session ended.</summary>
    public TimeSpan? AutoSelectRemaining
    {
        get
        {
            if (_autoSelectAfter is not { } after || IsCompleted || SelectedIndex < 0)
            {
                return null;
            }
            var remaining = after - _time.GetElapsedTime(_lastActivity);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Highlights a candidate (click, table selection). Out of range indexes are ignored.</summary>
    public void Select(int index)
    {
        NoteActivity();
        if (!IsCompleted && index >= 0 && index < Candidates.Count)
        {
            SelectedIndex = index;
        }
    }

    /// <summary>Arrow keys: moves the highlight, stopping at the first and last candidate.</summary>
    public void MoveSelection(int delta)
    {
        NoteActivity();
        if (!IsCompleted && Candidates.Count > 0)
        {
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Candidates.Count - 1);
        }
    }

    /// <summary>Enter, the Open button or a double click: chooses the highlighted candidate (cancels when there is none).</summary>
    public PickerResponse Confirm()
        => SelectedIndex < 0
            ? Complete(new PickerResponse { Result = PickerResult.Cancelled })
            : Complete(new PickerResponse { Result = PickerResult.Selected, ProfileId = Candidates[SelectedIndex].ProfileId, Remember = Remember });

    /// <summary>Keys 1 to 9: chooses that candidate directly. Returns null when there is no such candidate.</summary>
    public PickerResponse? ChooseNumber(int number)
    {
        if (IsCompleted)
        {
            return Response;
        }
        if (number < 1 || number > Math.Min(9, Candidates.Count))
        {
            NoteActivity();
            return null;
        }
        SelectedIndex = number - 1;
        return Confirm();
    }

    /// <summary>Esc, the Cancel button or closing the window.</summary>
    public PickerResponse Cancel() => Complete(new PickerResponse { Result = PickerResult.Cancelled });

    /// <summary>Any other user activity (mouse movement, scrolling) restarts the auto-select countdown.</summary>
    public void NoteActivity() => _lastActivity = _time.GetTimestamp();

    /// <summary>Called periodically by the UI. Returns the answer once the session ended (including by auto-select).</summary>
    /// <remarks>An automatic choice is never remembered: nobody chose, so it must not become a permanent rule.</remarks>
    public PickerResponse? Tick()
        => AutoSelectRemaining == TimeSpan.Zero
            ? Complete(new PickerResponse { Result = PickerResult.Selected, ProfileId = Candidates[SelectedIndex].ProfileId, Remember = false })
            : Response;

    private PickerResponse Complete(PickerResponse response)
    {
        Response ??= response;
        return Response;
    }
}
