using Preferis.Core.Configuration;
using Preferis.Core.History;

namespace Preferis.Settings.Tests;

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
        Assert.Equal(["Microsoft Edge", "Chrome (Work)", "Chrome (Default)"], page.Candidates.Select(c => c.Name));
        var work = page.Candidates[1];
        Assert.True(work.IsResolved);
        Assert.Equal("\"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome\" \"--profile-directory=Profile 1\" https://login.microsoftonline.com/common", work.Detail);
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
    public void The_history_is_hidden_while_it_is_off()
    {
        var page = _harness.WithSample().Create().Test;

        page.Activate();

        Assert.False(page.IsHistoryEnabled);
        Assert.Empty(page.History);
    }
}
