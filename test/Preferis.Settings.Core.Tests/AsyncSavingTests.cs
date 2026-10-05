using Preferis.Core;
using Preferis.Core.Configuration;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class AsyncSavingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reading_another_version_does_not_discard_edits_typed_after_the_request(bool adopt)
    {
        using var harness = new SettingsHarness().WithSample();
        var other = Path.Combine(harness.Root, "other.json");
        File.WriteAllText(other, "{}");
        var session = new SettingsSession(harness.Loader);
        session.Config.Profiles[0].Name = "Initial edit";
        var ui = new PausedContext();
        Task operation;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            operation = adopt ? session.AdoptAsync(other) : session.UseTheirsAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var resume = await ui.Continuation.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.Config.Profiles[0].Name = "Typed while reading";
        resume();
        // A rejected switch is reported to the page; a rejected reload leaves its conflict banner in place.
        try { await operation; }
        catch (IOException) when (adopt) { }

        Assert.Equal("Typed while reading", session.Config.Profiles[0].Name);
        Assert.Equal(harness.Paths.DefaultConfigPath, session.ConfigPath);
        Assert.Equal(harness.Paths.DefaultConfigPath, harness.Loader.Load().ConfigPath);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Closing_yields_while_another_process_holds_the_configuration_lock()
    {
        using var harness = new SettingsHarness().WithSample();
        var loader = new ConfigLoader(harness.Paths, HostPlatform.MacOS, _ => null, harness.Root)
        {
            LockTimeout = TimeSpan.FromMilliseconds(300),
        };
        var settings = new SettingsViewModel(loader, harness.Platform, harness.Dialogs, harness.Saves);
        settings.Profiles.Profiles[0].Name = "Changed";

        Task<bool> closing;
        using (harness.Loader.Lock())
        {
            closing = settings.CanCloseAsync();
            Assert.False(closing.IsCompleted);
        }

        Assert.True(await closing);
        Assert.Equal("Changed", harness.SavedConfig().Profiles[0].Name);
        Assert.Empty(harness.Dialogs.Asked);
    }

    [Fact]
    public async Task Closing_also_saves_edits_made_while_the_first_snapshot_is_waiting()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        settings.Profiles.Profiles[0].Name = "First edit";

        Task<bool> closing;
        using (harness.Loader.Lock())
        {
            closing = settings.CanCloseAsync();
            Assert.False(closing.IsCompleted);
            settings.Profiles.Profiles[0].Name = "Latest edit";
            settings.General.LogLevel = "trace";
        }

        Assert.True(await closing);
        Assert.Equal("Latest edit", harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("trace", harness.Loader.LoadLocal().Logging?.Level);
        Assert.False(settings.IsDirty);
    }

    [Fact]
    public async Task Undoing_an_edit_during_a_save_does_not_let_the_window_close_before_it_is_persisted()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        var original = settings.Profiles.Profiles[0].Name;
        settings.Profiles.Profiles[0].Name = "Temporary";

        Task saving;
        Task<bool> closing;
        using (harness.Loader.Lock())
        {
            saving = harness.Saves.RunPendingAsync();
            settings.Profiles.Profiles[0].Name = original;
            Assert.False(settings.IsDirty);
            Assert.True(settings.HasPendingChanges);
            closing = settings.CanCloseAsync();
            Assert.False(closing.IsCompleted);
        }

        await saving;
        Assert.True(await closing);
        Assert.Equal(original, harness.SavedConfig().Profiles[0].Name);
        Assert.False(settings.HasPendingChanges);
    }

    [Fact]
    public async Task A_failed_local_write_does_not_turn_a_successful_config_save_into_an_external_conflict()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        settings.Profiles.Profiles[0].Name = "First";
        settings.General.LogLevel = "trace";
        // A directory at the file path makes only the second write fail.
        Directory.CreateDirectory(harness.Paths.LocalConfigPath);

        await harness.Saves.RunPendingAsync();

        Assert.Equal("First", harness.SavedConfig().Profiles[0].Name);
        Assert.True(settings.IsDirty);
        Assert.NotNull(settings.StatusMessage);
        Assert.False(harness.Saves.HasPending);
        Directory.Delete(harness.Paths.LocalConfigPath);
        settings.Profiles.Profiles[0].Name = "Second";

        await harness.Saves.RunPendingAsync();

        Assert.False(settings.HasExternalChanges);
        Assert.False(settings.IsDirty);
        Assert.Equal("Second", harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("trace", harness.Loader.LoadLocal().Logging?.Level);
    }

    [Fact]
    public async Task A_switched_session_keeps_its_pointer_pending_when_local_json_cannot_be_written()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        var target = Path.Combine(harness.Root, "other.json");
        File.WriteAllText(target, "{}");
        Directory.CreateDirectory(harness.Paths.LocalConfigPath);
        harness.Dialogs.Files.Enqueue(target);

        await settings.General.UseExistingConfigCommand.ExecuteAsync(null);

        Assert.Equal(target, settings.ConfigPath);
        Assert.Empty(settings.Profiles.Profiles);
        Assert.True(settings.IsDirty);
        Assert.NotNull(settings.StatusMessage);
        Assert.Equal(harness.Paths.DefaultConfigPath, harness.Loader.Load().ConfigPath);
        Directory.Delete(harness.Paths.LocalConfigPath);

        Assert.True(await settings.CanCloseAsync());
        Assert.Equal(target, harness.Loader.Load().ConfigPath);
        Assert.False(settings.IsDirty);
    }

    [Fact]
    public async Task Switching_publishes_the_new_editor_before_saving_its_pointer_and_keeps_new_input()
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        var originalEditor = settings.Profiles;
        var target = Path.Combine(harness.Root, "other.json");
        File.WriteAllText(target, SettingsHarness.SampleJson);
        harness.Dialogs.Files.Enqueue(target);
        var editedNewModel = false;
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.Profiles))
            {
                Assert.NotSame(originalEditor, settings.Profiles);
                Assert.Equal(target, settings.ConfigPath);
                Assert.Equal(harness.Paths.DefaultConfigPath, harness.Loader.Load().ConfigPath);
                settings.Profiles.Profiles[0].Name = "Typed in the new editor";
                editedNewModel = true;
            }
        };

        await settings.General.UseExistingConfigCommand.ExecuteAsync(null);

        Assert.True(editedNewModel);
        Assert.Equal(target, harness.Loader.Load().ConfigPath);
        Assert.Equal("Typed in the new editor", harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("Microsoft Edge", ConfigSerializer.Deserialize(File.ReadAllText(harness.Paths.DefaultConfigPath)).Profiles[0].Name);
        Assert.False(settings.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reloading_a_switched_session_uses_its_current_file_while_the_pointer_is_pending(bool explicitReload)
    {
        using var harness = new SettingsHarness().WithSample();
        var settings = harness.Create();
        var target = Path.Combine(harness.Root, "other.json");
        File.WriteAllText(target, SettingsHarness.SampleJson);
        Directory.CreateDirectory(harness.Paths.LocalConfigPath);
        harness.Dialogs.Files.Enqueue(target);
        await settings.General.UseExistingConfigCommand.ExecuteAsync(null);
        Assert.Equal(harness.Paths.DefaultConfigPath, harness.Loader.Load().ConfigPath);

        var external = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        external.Profiles[0].Name = "Updated externally in the new file";
        File.WriteAllText(target, ConfigSerializer.Serialize(external));
        if (explicitReload)
        {
            settings.Profiles.Profiles[0].Name = "An edit to discard";
        }
        settings.CheckForExternalChanges();
        if (explicitReload)
        {
            Assert.True(settings.HasExternalChanges);
            await settings.UseTheirsCommand.ExecuteAsync(null);
        }

        Assert.Equal(target, settings.ConfigPath);
        Assert.Equal(target, settings.Local.ConfigPath);
        Assert.Equal("Updated externally in the new file", settings.Profiles.Profiles[0].Name);
        Assert.True(settings.IsDirty);
        Directory.Delete(harness.Paths.LocalConfigPath);
        Assert.True(await settings.CanCloseAsync());
        Assert.Equal(target, harness.Loader.Load().ConfigPath);
    }

    [Fact]
    public async Task Edits_follow_the_new_location_when_only_the_old_file_could_not_be_removed()
    {
        using var harness = new SettingsHarness();
        var source = Directory.CreateDirectory(Path.Combine(harness.Root, "source")).FullName;
        var paths = harness.Paths with { DefaultConfigPath = Path.Combine(source, "config.json") };
        var loader = new ConfigLoader(paths, HostPlatform.MacOS, _ => null, harness.Root);
        File.WriteAllText(paths.DefaultConfigPath, SettingsHarness.SampleJson);
        var settings = new SettingsViewModel(loader, harness.Platform, harness.Dialogs, harness.Saves);
        var cloud = Path.Combine(harness.Root, "cloud");
        harness.Dialogs.Folders.Enqueue(cloud);

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(paths.DefaultConfigPath, FileAttributes.ReadOnly);
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        else
        {
            return;
        }
        try
        {
            await settings.General.MoveConfigCommand.ExecuteAsync(null);

            Assert.Equal(Path.Combine(cloud, "config.json"), settings.ConfigPath);
            Assert.Equal(settings.ConfigPath, loader.Load().ConfigPath);
            Assert.Contains("was moved, but the old file could not be removed", settings.StatusMessage, StringComparison.Ordinal);
            settings.Profiles.Profiles[0].Name = "At the new location";
            await harness.Saves.RunPendingAsync();
            Assert.Equal("At the new location", loader.Load().Config.Profiles[0].Name);
            Assert.Equal("Microsoft Edge", ConfigSerializer.Deserialize(File.ReadAllText(paths.DefaultConfigPath)).Profiles[0].Name);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(paths.DefaultConfigPath, FileAttributes.Normal);
            }
            else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    private sealed class PausedContext : SynchronizationContext
    {
        public TaskCompletionSource<Action> Continuation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
            => Continuation.SetResult(() => callback(state));
    }
}
