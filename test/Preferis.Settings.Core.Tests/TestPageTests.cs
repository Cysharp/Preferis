using Preferis.Core.Configuration;
using Preferis.Core.History;
using Preferis.Core.Routing;

namespace Preferis.Settings.Core.Tests;

public sealed class TestPageTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Shows_the_trace_the_decision_and_how_each_candidate_resolves()
    {
        var page = _harness.WithSample().Create().Test;

        page.Url = "https://login.microsoftonline.com/common";
        page.RunCommand.Execute(null);

        Assert.Null(page.Error);
        Assert.Contains(page.Trace, t => t.Rule == "Microsoft ログインは選択する" && t.Kind == "matched");
        Assert.Equal("Show the picker with 3 profiles (rule \"Microsoft ログインは選択する\")", page.Decision);
        Assert.Equal(["Microsoft Edge", "Chrome (Default)", "Chrome (Work)"], page.Candidates.Select(c => c.Name));
        var work = page.Candidates[2];
        Assert.True(work.IsResolved);
        Assert.Equal("\"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome\" \"--profile-directory=Profile 1\" https://login.microsoftonline.com/common", work.Detail);
    }

    [Fact]
    public void Running_publishes_complete_result_lists_once()
    {
        var settings = _harness.WithSample().Create();
        for (var i = 0; i < 1_000; i++)
        {
            settings.Config.Rules.Insert(0, new Rule
            {
                Id = $"unmatched-{i}",
                Name = $"Unmatched {i}",
                Condition = new HostCondition { Match = MatchKind.Exact, Pattern = $"unmatched-{i}.example" },
                Actions = [new NoneAction()],
            });
        }
        var page = settings.Test;
        var traces = new List<string[]>();
        var candidates = new List<string[]>();
        page.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(page.Trace))
            {
                traces.Add(page.Trace.Select(t => t.Message).ToArray());
            }
            if (change.PropertyName == nameof(page.Candidates))
            {
                candidates.Add(page.Candidates.Select(c => c.Name).ToArray());
            }
        };

        page.Url = "https://login.microsoftonline.com/common";
        page.RunCommand.Execute(null);

        Assert.Equal(page.Trace.Select(t => t.Message), Assert.Single(traces));
        Assert.Equal(1_000, page.Trace.Count(t => t.Rule?.StartsWith("Unmatched ", StringComparison.Ordinal) == true));
        Assert.Equal(["Microsoft Edge", "Chrome (Default)", "Chrome (Work)"], Assert.Single(candidates));
    }

    [Fact]
    public void Invalid_input_replaces_previous_results_with_empty_lists_once()
    {
        var page = _harness.WithSample().Create().Test;
        page.Url = "https://login.microsoftonline.com/common";
        page.RunCommand.Execute(null);
        var changed = new List<string?>();
        page.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        page.Url = "invalid";
        page.RunCommand.Execute(null);

        Assert.Empty(page.Trace);
        Assert.Empty(page.Candidates);
        Assert.Null(page.Decision);
        Assert.NotNull(page.Error);
        Assert.Single(changed, name => name == nameof(page.Trace));
        Assert.Single(changed, name => name == nameof(page.Candidates));
    }

    [Fact]
    public void Unresolvable_candidates_say_why()
    {
        var page = _harness.WithSample().Create().Test;
        _harness.Platform.Browsers.RemoveAll(b => b.Browser.Id == "chrome");

        page.Url = "https://admin.google.com/";
        page.RunCommand.Execute(null);

        var candidate = Assert.Single(page.Candidates);
        Assert.False(candidate.IsResolved);
        Assert.StartsWith("Browser 'chrome' is not installed. Uses the fallback “Microsoft Edge”: \"/Applications/Microsoft Edge.app", candidate.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fallback_that_fails_too_is_explained_from_the_start()
    {
        var page = _harness.WithSample().Create().Test;
        _harness.Platform.Browsers.RemoveAll(b => b.Browser.Id is "chrome" or "edge");

        page.Url = "https://admin.google.com/";
        page.RunCommand.Execute(null);

        Assert.Equal("Browser 'chrome' is not installed. The fallback “Microsoft Edge” cannot be used either: Browser 'edge' is not installed.", Assert.Single(page.Candidates).Detail);
    }

    [Fact]
    public void Rewrites_and_the_catch_all_are_described()
    {
        var page = _harness.WithSample().Create().Test;

        page.Url = "https://www.example.com/";
        page.RunCommand.Execute(null);

        Assert.Equal("Open with the default profile (catch-all)", page.Decision);
        Assert.Equal("Microsoft Edge", Assert.Single(page.Candidates).Name);
    }

    [Fact]
    public void A_rule_that_only_rewrites_does_not_get_credit_for_the_decision()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            {
              "version": 1,
              "profiles": [ { "id": "safari", "name": "Safari", "browser": "safari" } ],
              "rules": [
                { "id": "upgrade", "name": "Upgrade", "enabled": true,
                  "condition": { "type": "url", "match": "startsWith", "pattern": "http://" },
                  "actions": [ { "type": "rewrite", "mode": "regex", "pattern": "^http://", "replacement": "https://" } ] }
              ],
              "catchAll": { "actions": [] }
            }
            """);
        var page = _harness.Create().Test;

        page.Url = "http://www.example.com/";
        page.RunCommand.Execute(null);

        Assert.EndsWith("(catch-all)", page.Decision, StringComparison.Ordinal);
        Assert.Equal("https://www.example.com/", page.RoutedUrl);
    }

    [Fact]
    public void The_rules_evaluated_again_before_the_picker_are_shown()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            {
              "version": 1,
              "profiles": [ { "id": "a", "name": "A", "browser": "safari" }, { "id": "b", "name": "B", "browser": "safari" } ],
              "rules": [
                { "id": "pinned", "name": "Pinned", "enabled": true,
                  "condition": { "type": "host", "match": "equals", "pattern": "target.example" },
                  "actions": [ { "type": "open", "profiles": ["a"] } ] },
                { "id": "unwrap", "name": "Unwrap", "enabled": true,
                  "condition": { "type": "host", "match": "equals", "pattern": "wrapper.example" },
                  "actions": [ { "type": "rewrite", "mode": "queryParameter", "parameter": "url" }, { "type": "open", "profiles": ["a", "b"] } ] }
              ],
              "catchAll": { "actions": [ { "type": "open" } ] }
            }
            """);
        var page = _harness.Create().Test;

        page.Url = "https://wrapper.example/?url=https%3A%2F%2Ftarget.example%2F";
        page.RunCommand.Execute(null);

        Assert.Contains(page.Trace, t => t.Kind == "re-evaluated" && t.Rule == "Unwrap");
        Assert.Equal("Open with A (rule \"Pinned\")", page.Decision);
    }

    [Fact]
    public void The_configuration_being_edited_is_tested_before_it_is_saved()
    {
        var settings = _harness.WithSample().Create();
        settings.Rules.Rules.Single(r => r.Id == "google-admin").Enabled = false;

        settings.Test.Url = "https://admin.google.com/";
        settings.Test.RunCommand.Execute(null);

        Assert.Equal("Open with the default profile (catch-all)", settings.Test.Decision);
    }

    [Fact]
    public void Text_that_is_not_a_url_is_reported()
    {
        var page = _harness.WithSample().Create().Test;

        page.Url = "example";
        page.RunCommand.Execute(null);

        Assert.NotNull(page.Error);
        Assert.Empty(page.Trace);
        Assert.Null(page.Decision);
    }

    [Fact]
    public void The_history_lists_recent_urls_to_test_again()
    {
        File.WriteAllText(_harness.Paths.LocalConfigPath, """{ "logging": { "history": true } }""");
        var history = new RoutingHistory(_harness.Paths.HistoryPath);
        history.Append(new HistoryEntry { Time = DateTimeOffset.Now, Url = "https://admin.google.com/x", RuleId = "google-admin", Outcome = "Launched", ProfileId = "chrome-work" }, 50);
        var page = _harness.WithSample().Create().Test;

        page.Activate();

        var entry = Assert.Single(page.History);
        Assert.Equal("Google Admin は Chrome (Work)", entry.Rule);
        page.TestHistoryEntryCommand.Execute(entry);
        Assert.Equal("https://admin.google.com/x", page.Url);
        Assert.NotNull(page.Decision);

        page.ClearHistoryCommand.Execute(null);
        Assert.Empty(page.History);
        Assert.Empty(history.Read());
    }

    [Fact]
    public void The_url_is_tested_as_opened_by_the_app_given()
    {
        const string Slack = @"C:\Users\me\AppData\Local\slack\app-4.41.105\slack.exe";
        var settings = _harness.WithSample().Create();
        settings.Config.Rules.Insert(0, new Rule
        {
            Id = "from-slack",
            Name = "From Slack",
            Condition = new OrCondition
            {
                Conditions =
                [
                    new AppIdCondition { Match = MatchKind.Exact, Pattern = "com.tinyspeck.slackmacgap" },
                    new AppPathCondition { Match = MatchKind.EndsWith, Pattern = @"\slack.exe" },
                ],
            },
            Actions = [new NoneAction()],
        });
        var page = settings.Test;
        page.Url = "https://example.com/";

        page.RunCommand.Execute(null);
        Assert.Contains(page.Trace, t => t.Rule == "From Slack" && t.Kind == "not matched");

        page.AppPath = $"  {Slack}  ";
        page.RunCommand.Execute(null);
        Assert.Equal("Do nothing (rule \"From Slack\")", page.Decision);

        page.AppPath = "";
        page.AppId = "com.tinyspeck.slackmacgap";
        page.RunCommand.Execute(null);
        Assert.Equal("Do nothing (rule \"From Slack\")", page.Decision);
    }

    [Fact]
    public void The_history_names_the_app_and_tests_again_with_it()
    {
        const string Terminal = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.12741.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";
        File.WriteAllText(_harness.Paths.LocalConfigPath, """{ "logging": { "history": true } }""");
        var history = new RoutingHistory(_harness.Paths.HistoryPath);
        history.Append(new HistoryEntry { Time = DateTimeOffset.Now, Url = "https://example.com/a", Outcome = "Launched", AppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", AppPath = Terminal }, 50);
        history.Append(new HistoryEntry { Time = DateTimeOffset.Now, Url = "https://example.com/b", Outcome = "Launched", AppPath = "/Applications/Slack.app/" }, 50);
        history.Append(new HistoryEntry { Time = DateTimeOffset.Now, Url = "https://example.com/c", Outcome = "Launched" }, 50);
        var page = _harness.WithSample().Create().Test;

        page.Activate();

        Assert.Equal([null, "Slack.app", "WindowsTerminal.exe"], page.History.Select(h => h.App));
        page.TestHistoryEntryCommand.Execute(page.History[2]);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", page.AppId);
        Assert.Equal(Terminal, page.AppPath);
        // An entry without an app leaves none behind from the one tested before.
        page.TestHistoryEntryCommand.Execute(page.History[0]);
        Assert.Equal("", page.AppId);
        Assert.Equal("", page.AppPath);
    }

    [Fact]
    public void The_history_is_hidden_while_it_is_off()
    {
        var page = _harness.WithSample().Create().Test;

        page.Activate();

        Assert.False(page.IsHistoryEnabled);
        Assert.Empty(page.History);
    }

    [Fact]
    public void Reloading_history_publishes_one_complete_snapshot()
    {
        File.WriteAllText(_harness.Paths.LocalConfigPath, """{ "logging": { "history": true } }""");
        var history = new RoutingHistory(_harness.Paths.HistoryPath);
        for (var i = 0; i < 20; i++)
        {
            history.Append(new HistoryEntry { Time = DateTimeOffset.Now, Url = $"https://example.com/{i}", Outcome = "Launched" }, 50);
        }
        var page = _harness.WithSample().Create().Test;
        var snapshots = new List<string[]>();
        page.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(page.History))
            {
                snapshots.Add(page.History.Select(h => h.Url).ToArray());
            }
        };

        page.Activate();

        Assert.Equal(history.Read().Select(h => h.Url), Assert.Single(snapshots));
        Assert.Equal(20, page.History.Count);
    }
}

public sealed class TestPageOpenTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Opening_hands_the_url_to_preferis_and_shows_what_happened()
    {
        var page = _harness.WithSample().Create().Test;
        page.Url = "  https://login.microsoftonline.com/common ";

        await page.OpenCommand.ExecuteAsync(null);

        Assert.Equal(["https://login.microsoftonline.com/common"], _harness.Platform.OpenedUrls);
        Assert.Equal("Preferis handled the URL.", page.OpenResult);
        Assert.Equal(["[Info] Launched: Launched 'edge'. (12.3 ms)"], page.OpenOutput);
        Assert.NotNull(page.Decision);
        Assert.False(page.IsOpening);
    }

    [Fact]
    public async Task A_failure_reported_by_preferis_is_shown()
    {
        var page = _harness.WithSample().Create().Test;
        _harness.Platform.OpenResult = new(2, ["[Error] Not an absolute URL"]);
        page.Url = "https://www.example.com/";

        await page.OpenCommand.ExecuteAsync(null);

        Assert.Equal("Preferis could not open the URL.", page.OpenResult);
    }

    [Fact]
    public async Task Text_that_is_not_a_url_is_not_opened()
    {
        var page = _harness.WithSample().Create().Test;
        page.Url = "example";

        await page.OpenCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Platform.OpenedUrls);
        Assert.NotNull(page.Error);
    }

    [Fact]
    public async Task Edits_still_waiting_are_saved_before_opening()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Edited";
        settings.Test.Url = "https://www.example.com/";

        await settings.Test.OpenCommand.ExecuteAsync(null);

        Assert.Single(_harness.Platform.OpenedUrls);
        Assert.Empty(_harness.Dialogs.Asked);
        Assert.False(settings.IsDirty);
        Assert.Equal("Edited", _harness.SavedConfig().Profiles[0].Name);
    }

    [Fact]
    public Task Opening_waits_for_an_in_flight_save_after_the_edit_is_undone() => UiThread.RunAsync(async () =>
    {
        var settings = _harness.WithSample().Create();
        var original = settings.Profiles.Profiles[0].Name;
        settings.Profiles.Profiles[0].Name = "Temporary";
        settings.Test.Url = "https://www.example.com/";
        string? nameWhenOpened = null;
        _harness.Platform.OnOpen = _ => nameWhenOpened = _harness.SavedConfig().Profiles[0].Name;

        Task saving;
        Task opening;
        bool waitedForSave;
        int openedBeforeUnlock;
        using (_harness.Loader.Lock())
        {
            saving = _harness.Saves.RunPendingAsync();
            settings.Profiles.Profiles[0].Name = original;
            Assert.False(settings.IsDirty);
            Assert.True(settings.HasPendingChanges);

            opening = settings.Test.OpenCommand.ExecuteAsync(null);
            waitedForSave = !opening.IsCompleted;
            openedBeforeUnlock = _harness.Platform.OpenedUrls.Count;
        }

        await saving;
        await opening;
        Assert.True(waitedForSave);
        Assert.Equal(0, openedBeforeUnlock);
        Assert.Single(_harness.Platform.OpenedUrls);
        Assert.Equal(original, nameWhenOpened);
        Assert.Equal(original, _harness.SavedConfig().Profiles[0].Name);
        Assert.False(settings.HasPendingChanges);
    });

    [Fact]
    public async Task Edits_that_cannot_be_saved_leave_the_choice_to_open_anyway()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Edited";
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Chrome (Default)", "Theirs", StringComparison.Ordinal));
        settings.Test.Url = "https://www.example.com/";

        _harness.Dialogs.Answers.Enqueue("Cancel");
        await settings.Test.OpenCommand.ExecuteAsync(null);
        Assert.Empty(_harness.Platform.OpenedUrls);

        _harness.Dialogs.Answers.Enqueue("Open Saved Configuration");
        await settings.Test.OpenCommand.ExecuteAsync(null);
        Assert.Single(_harness.Platform.OpenedUrls);
        Assert.Equal("Microsoft Edge", _harness.SavedConfig().Profiles[0].Name);
    }

    [Fact]
    public async Task A_choice_remembered_in_the_picker_shows_up_and_the_result_stays()
    {
        var settings = _harness.WithSample().Create();
        settings.SelectedPage = settings.Test;
        var page = settings.Test;
        page.Url = "https://login.microsoftonline.com/common";
        _harness.Platform.OnOpen = _ =>
        {
            var config = _harness.Loader.Load().Config;
            RememberedRule.Insert(config, RememberedRule.Create(RoutingUrl.Parse("https://login.microsoftonline.com/"), "edge"), "ms-login");
            _harness.Loader.Save(config, _harness.Paths.DefaultConfigPath);
        };

        await page.OpenCommand.ExecuteAsync(null);

        Assert.Contains(settings.Rules.Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
        Assert.Same(page, settings.Test);
        Assert.Same(page, settings.SelectedPage);
        Assert.Equal("Preferis handled the URL.", page.OpenResult);
    }

    [Fact]
    public void Opening_needs_preferis_app()
    {
        _harness.Platform.CanOpenUrls = false;

        var page = _harness.WithSample().Create().Test;

        Assert.False(page.OpenCommand.CanExecute(null));
    }
}
