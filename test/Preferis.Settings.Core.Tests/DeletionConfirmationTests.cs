using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class DeletionConfirmationTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_profile_and_its_references_remain_while_confirmation_is_pending_or_cancelled()
    {
        var settings = Create();
        var page = settings.Profiles;
        var profile = page.Profiles.Single(p => p.Id == "edge");
        page.SelectedProfile = profile;
        var before = Snapshot(settings);
        var answer = DeferAnswer();

        var deletion = page.DeleteCommand.ExecuteAsync(null);

        Assert.False(deletion.IsCompleted);
        AssertUnchanged(settings, before);
        Assert.Same(profile, page.SelectedProfile);

        answer.SetResult(1);
        await deletion;

        AssertUnchanged(settings, before);
        Assert.Same(profile, page.SelectedProfile);
    }

    [Fact]
    public async Task A_rule_remains_while_confirmation_is_pending_or_cancelled()
    {
        var settings = Create();
        var page = settings.Rules;
        var rule = page.Rules.Single(r => r.Id == "google-admin");
        page.SelectedRule = rule;
        var before = Snapshot(settings);
        var answer = DeferAnswer();

        var deletion = page.DeleteRuleCommand.ExecuteAsync(null);

        Assert.False(deletion.IsCompleted);
        AssertUnchanged(settings, before);
        Assert.Same(rule, page.SelectedRule);

        answer.SetResult(1);
        await deletion;

        AssertUnchanged(settings, before);
        Assert.Same(rule, page.SelectedRule);
    }

    [Fact]
    public async Task Confirming_a_profile_deletion_after_reload_keeps_the_reloaded_profile_references_and_local_target()
    {
        var settings = Create();
        var page = settings.Profiles;
        var profile = page.Profiles.Single(p => p.Id == "edge");
        page.SelectedProfile = profile;
        var answer = DeferAnswer();
        var deletion = page.DeleteCommand.ExecuteAsync(null);

        ReloadExternalChanges(settings);
        Assert.NotSame(page, settings.Profiles);
        var reloaded = Snapshot(settings);
        answer.SetResult(0);
        await deletion;

        AssertUnchanged(settings, reloaded);
        Assert.Contains(profile, page.Profiles);
        Assert.Same(profile, page.SelectedProfile);
        Assert.Equal("safari", settings.Local.ProfileTargets!["edge"].Browser);
    }

    [Fact]
    public async Task Confirming_a_rule_deletion_after_reload_leaves_both_rule_lists_unchanged()
    {
        var settings = Create();
        var page = settings.Rules;
        var rule = page.Rules.Single(r => r.Id == "google-admin");
        page.SelectedRule = rule;
        var answer = DeferAnswer();
        var deletion = page.DeleteRuleCommand.ExecuteAsync(null);

        ReloadExternalChanges(settings);
        Assert.NotSame(page, settings.Rules);
        var reloaded = Snapshot(settings);
        answer.SetResult(0);
        await deletion;

        AssertUnchanged(settings, reloaded);
        Assert.Contains(rule, page.Rules);
        Assert.Same(rule, page.SelectedRule);
    }

    private SettingsViewModel Create()
    {
        _harness.WithSample();
        File.WriteAllText(_harness.Paths.LocalConfigPath, """{"profileTargets":{"edge":{"browser":"safari"}}}""");
        return _harness.Create();
    }

    private TaskCompletionSource<int> DeferAnswer()
    {
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Dialogs.NextAnswer = answer.Task;
        return answer;
    }

    private void ReloadExternalChanges(SettingsViewModel settings)
    {
        var external = _harness.SavedConfig();
        external.Profiles.Single(p => p.Id == "edge").Name = "Edge externally renamed";
        external.Rules.Single(r => r.Id == "google-admin").Name = "Admin externally renamed";
        File.WriteAllText(settings.ConfigPath, ConfigSerializer.Serialize(external));
        settings.CheckForExternalChanges();
    }

    private (string Config, string Local, string ConfigFile, string LocalFile) Snapshot(SettingsViewModel settings)
        => (ConfigSerializer.Serialize(settings.Config), ConfigSerializer.SerializeLocal(settings.Local),
            File.ReadAllText(settings.ConfigPath), File.ReadAllText(_harness.Paths.LocalConfigPath));

    private void AssertUnchanged(SettingsViewModel settings, (string Config, string Local, string ConfigFile, string LocalFile) before)
    {
        Assert.Equal(before, Snapshot(settings));
        Assert.False(settings.IsDirty);
        Assert.False(_harness.Saves.HasPending);
    }
}
