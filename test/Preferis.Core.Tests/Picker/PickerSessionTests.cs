using Preferis.Core.Picker;

namespace Preferis.Core.Tests.Picker;

public class PickerSessionTests
{
    private readonly ManualTimeProvider _clock = new();

    private PickerSession Create(int candidates = 3, double autoSelectSeconds = 0, bool rememberDefault = false, bool allowRemember = true)
        => new(new PickerRequest
        {
            Url = "https://login.microsoftonline.com/common",
            Candidates = Enumerable.Range(1, candidates).Select(i => new PickerCandidate { ProfileId = $"p{i}", Name = $"Profile {i}" }).ToList(),
            Options = new PickerOptions { AutoSelectSeconds = autoSelectSeconds, RememberChoiceDefault = rememberDefault, AllowRemember = allowRemember },
        }, _clock);

    [Fact]
    public void The_first_candidate_is_selected_initially()
    {
        var session = Create();

        Assert.Equal(0, session.SelectedIndex);
        Assert.False(session.IsCompleted);
        Assert.Null(session.Response);
    }

    [Fact]
    public void Confirm_selects_the_highlighted_candidate()
    {
        var session = Create();
        session.MoveSelection(+1);

        var response = session.Confirm();

        Assert.Equal(PickerResult.Selected, response.Result);
        Assert.Equal("p2", response.ProfileId);
        Assert.False(response.Remember);
        Assert.True(session.IsCompleted);
        Assert.Same(response, session.Response);
    }

    [Fact]
    public void Moving_the_selection_stops_at_both_ends()
    {
        var session = Create(candidates: 3);

        session.MoveSelection(-1);
        Assert.Equal(0, session.SelectedIndex);

        session.MoveSelection(+1);
        session.MoveSelection(+1);
        session.MoveSelection(+1);
        Assert.Equal(2, session.SelectedIndex);
    }

    [Fact]
    public void Select_ignores_indexes_outside_the_list()
    {
        var session = Create(candidates: 2);

        session.Select(1);
        session.Select(5);
        session.Select(-1);

        Assert.Equal(1, session.SelectedIndex);
    }

    [Theory]
    [InlineData(1, "p1")]
    [InlineData(3, "p3")]
    public void A_number_key_chooses_that_candidate_directly(int number, string expected)
    {
        var session = Create(candidates: 3);

        var response = session.ChooseNumber(number);

        Assert.NotNull(response);
        Assert.Equal(expected, response.ProfileId);
        Assert.True(session.IsCompleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(10)]
    public void A_number_without_a_candidate_does_nothing(int number)
    {
        var session = Create(candidates: 3);

        Assert.Null(session.ChooseNumber(number));
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public void Cancel_answers_cancelled_without_a_profile()
    {
        var session = Create(rememberDefault: true);

        var response = session.Cancel();

        Assert.Equal(PickerResult.Cancelled, response.Result);
        Assert.Null(response.ProfileId);
        Assert.False(response.Remember);
    }

    [Fact]
    public void The_remember_choice_starts_from_the_configured_default_and_is_sent_with_the_selection()
    {
        var session = Create(rememberDefault: true);
        Assert.True(session.Remember);

        session.Remember = false;
        session.Remember = true;

        Assert.True(session.Confirm().Remember);
    }

    [Fact]
    public void Remembering_is_off_and_cannot_be_turned_on_when_the_request_does_not_allow_it()
    {
        var session = Create(rememberDefault: true, allowRemember: false);

        Assert.False(session.CanRemember);
        Assert.False(session.Remember);
        session.Remember = true;

        Assert.False(session.Confirm().Remember);
    }

    [Fact]
    public void The_first_answer_wins()
    {
        var session = Create();
        var first = session.ChooseNumber(2);

        session.MoveSelection(+1);
        var second = session.Cancel();

        Assert.Same(first, second);
        Assert.Equal("p2", session.Response!.ProfileId);
        Assert.Equal(1, session.SelectedIndex);
    }

    [Fact]
    public void An_empty_candidate_list_can_only_be_cancelled()
    {
        var session = Create(candidates: 0);

        Assert.Equal(-1, session.SelectedIndex);
        Assert.Equal(PickerResult.Cancelled, session.Confirm().Result);
    }

    [Fact]
    public void Auto_select_is_off_by_default()
    {
        var session = Create(autoSelectSeconds: 0);

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(session.AutoSelectRemaining);
        Assert.Null(session.Tick());
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public void Auto_select_chooses_the_highlighted_candidate_after_the_idle_period()
    {
        var session = Create(autoSelectSeconds: 5);
        Assert.Equal(TimeSpan.FromSeconds(5), session.AutoSelectRemaining);

        _clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Null(session.Tick());
        Assert.Equal(TimeSpan.FromSeconds(0.1), session.AutoSelectRemaining);

        _clock.Advance(TimeSpan.FromSeconds(0.1));
        var response = session.Tick();

        Assert.NotNull(response);
        Assert.Equal("p1", response.ProfileId);
    }

    [Fact]
    public void An_automatic_choice_is_never_remembered()
    {
        // Nobody chose: a permanent rule must not come out of a timeout.
        var session = Create(autoSelectSeconds: 1, rememberDefault: true);

        _clock.Advance(TimeSpan.FromSeconds(1));

        var response = session.Tick();
        Assert.Equal(PickerResult.Selected, response?.Result);
        Assert.False(response?.Remember);
    }

    [Fact]
    public void Any_interaction_restarts_the_auto_select_countdown()
    {
        var session = Create(autoSelectSeconds: 5);

        _clock.Advance(TimeSpan.FromSeconds(4));
        session.MoveSelection(+1);
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Null(session.Tick());

        _clock.Advance(TimeSpan.FromSeconds(0.5));
        session.NoteActivity();
        _clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Null(session.Tick());

        _clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal("p2", session.Tick()?.ProfileId);
    }

    [Fact]
    public void The_countdown_disappears_once_answered()
    {
        var session = Create(autoSelectSeconds: 5);

        session.Cancel();

        Assert.Null(session.AutoSelectRemaining);
        Assert.Equal(PickerResult.Cancelled, session.Tick()?.Result);
    }
}
