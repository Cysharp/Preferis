using System.Text.Json;
using Preferis.Core.Configuration;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Services;

/// <summary>
/// The editable configuration and its persistence state, independent of pages and controls. Call from the UI thread;
/// only immutable JSON snapshots cross to the worker doing file I/O. Operations are ordered so a delayed save cannot
/// write to a configuration after the user has switched away from it.
/// </summary>
internal sealed class SettingsSession
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private string? _diskText;
    private string _savedConfigJson = "";
    private string _savedLocalJson = "";
    private bool _localUnreadable;

    public SettingsSession(ConfigLoader loader)
    {
        Loader = loader;
        ApplyLoaded(ReadState(path: null), keepLocalEdits: false);
    }

    public bool IsBusy => _operations.CurrentCount == 0;
    /// <summary>Published synchronously when the edited models change, before another operation can use them.</summary>
    public event Action? ModelsReplaced;
    public ConfigLoader Loader { get; }
    public PreferisConfig Config { get; private set; } = null!;
    public LocalConfig Local { get; private set; } = null!;
    public string ConfigPath { get; private set; } = "";
    public bool ConfigExists { get; private set; }
    public bool ConfigUnreadable { get; private set; }
    public string? LoadError { get; private set; }
    public string? SaveError { get; private set; }
    public bool HasExternalChanges { get; private set; }
    public bool IsHoldingEditsForUnreadableFile { get; private set; }
    public bool ConfigEdited => ConfigSerializer.Serialize(Config) != _savedConfigJson;
    public bool LocalEdited => ConfigSerializer.SerializeLocal(Local) != _savedLocalJson;
    public bool IsDirty => ConfigEdited || LocalEdited;
    public bool CanAutoSave => (ConfigEdited && !HasExternalChanges && !IsHoldingEditsForUnreadableFile) || LocalEdited;

    /// <summary>Activation checks never race an in-flight write; that operation checks the disk itself.</summary>
    public void CheckForExternalChanges()
    {
        if (_operations.CurrentCount == 0 || ReadDisk(ConfigPath, _diskText) == _diskText)
        {
            return;
        }
        if (ConfigEdited)
        {
            HasExternalChanges = true;
        }
        else
        {
            ApplyLoaded(ReadState(ConfigPath), keepLocalEdits: true);
        }
    }

    public async Task UseTheirsAsync()
    {
        var requested = Capture();
        await _operations.WaitAsync();
        try
        {
            var loaded = await Task.Run(() => ReadState(requested.Path));
            if (ConfigPath != requested.Path || ConfigSerializer.Serialize(Config) != requested.ConfigJson)
            {
                SaveError = L("The settings changed while the file was being read. Try again.");
                return;
            }
            SaveError = null;
            ApplyLoaded(loaded, keepLocalEdits: true);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>Saves one snapshot. Newer edits remain dirty and can be saved by the next operation.</summary>
    public async Task<bool> SaveAsync(bool force)
    {
        await _operations.WaitAsync();
        try
        {
            var snapshot = Capture();
            var result = await Task.Run(() => Write(snapshot, force));
            // A successful config write still counts when writing local.json subsequently failed.
            if (result.ConfigSaved)
            {
                _savedConfigJson = _diskText = snapshot.ConfigJson;
                ConfigUnreadable = false;
                HasExternalChanges = IsHoldingEditsForUnreadableFile = false;
            }
            if (result.LocalSaved)
            {
                _savedLocalJson = snapshot.LocalJson;
                _localUnreadable = false;
            }
            HasExternalChanges |= result.Conflict;
            IsHoldingEditsForUnreadableFile |= result.Unreadable;
            ConfigExists = result.ConfigExists;
            SaveError = result.Error;
            if (result.Reload is { } reload)
            {
                // Typing remains possible while the worker reads: only replace a model that is still unchanged.
                if (ConfigSerializer.Serialize(Config) == snapshot.ConfigJson)
                {
                    ApplyLoaded(reload, keepLocalEdits: true);
                }
                else
                {
                    HasExternalChanges = true;
                }
            }
            if (!ConfigUnreadable && !_localUnreadable)
            {
                LoadError = null;
            }
            return result.Error is null && !HasExternalChanges && !IsHoldingEditsForUnreadableFile;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<string?> RelocateAsync(string target, bool overwrite)
    {
        await _operations.WaitAsync();
        try
        {
            var snapshot = Capture();
            var result = await Task.Run(() => Move(snapshot, target, overwrite));
            if (result.Conflict)
            {
                HasExternalChanges = true;
                throw new IOException(L("config.json changed on disk while it was being edited here. Reload or save first, then move it."));
            }
            if (result.Unreadable)
            {
                ConfigUnreadable = true;
                throw new IOException(L("The current config.json cannot be read. Save a working configuration before moving it."));
            }
            if (result.Reload is { } reload)
            {
                if (ConfigSerializer.Serialize(Config) == snapshot.ConfigJson)
                {
                    ApplyLoaded(reload, keepLocalEdits: true);
                }
                else
                {
                    HasExternalChanges = true;
                }
            }
            _savedConfigJson = _diskText = result.ConfigJson;
            ConfigPath = Path.GetFullPath(target);
            ConfigExists = true;
            Local.ConfigPath = result.Local!.ConfigPath;
            _savedLocalJson = ConfigSerializer.SerializeLocal(result.Local);
            return result.Warning;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task AdoptAsync(string path)
    {
        var requested = Capture();
        await _operations.WaitAsync();
        try
        {
            var state = await Task.Run(() =>
            {
                // Validate without redirecting local.json: an edit during this read can still cancel the switch.
                try
                {
                    ConfigSerializer.Deserialize(File.ReadAllText(path));
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(ex.Message, ex);
                }
                var loaded = ReadState(path);
                if (loaded.ConfigUnreadable)
                {
                    throw new InvalidDataException(loaded.Result.Error);
                }
                return loaded;
            });
            if (ConfigPath != requested.Path || ConfigSerializer.Serialize(Config) != requested.ConfigJson
                || ConfigSerializer.SerializeLocal(Local) != requested.LocalJson)
            {
                throw new IOException(L("The settings changed while the file was being read. Try again."));
            }
            // The view observes the new models synchronously. The local pointer is then an ordinary pending edit,
            // saved asynchronously by the caller; a failed write stays dirty instead of losing the new session.
            ApplyLoaded(state, keepLocalEdits: false, adoptedPath: Path.GetFullPath(path));
        }
        finally
        {
            _operations.Release();
        }
    }

    private Snapshot Capture() => new(ConfigSerializer.Serialize(Config), ConfigSerializer.SerializeLocal(Local),
        ConfigPath, _diskText, _savedConfigJson, _savedLocalJson, ConfigUnreadable);

    private WriteResult Write(Snapshot snapshot, bool force)
    {
        var result = new WriteResult();
        try
        {
            using var exclusive = snapshot.ConfigEdited ? Loader.Lock() : null;
            var changed = ReadDisk(snapshot.Path, snapshot.DiskText) != snapshot.DiskText;
            result.Conflict = snapshot.ConfigEdited && !force && changed;
            result.Unreadable = snapshot.ConfigEdited && !force && !changed && snapshot.Unreadable;
            if (snapshot.ConfigEdited && !result.Conflict && !result.Unreadable)
            {
                Loader.Save(ConfigSerializer.Deserialize(snapshot.ConfigJson), snapshot.Path);
                result.ConfigSaved = true;
            }
            if (snapshot.LocalJson != snapshot.SavedLocalJson)
            {
                Loader.SaveLocal(ConfigSerializer.DeserializeLocal(snapshot.LocalJson));
                result.LocalSaved = true;
            }
            if (!snapshot.ConfigEdited && changed)
            {
                result.Reload = ReadState(snapshot.Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error = L("Could not save: {0}", ex.Message);
        }
        result.ConfigExists = File.Exists(snapshot.Path);
        return result;
    }

    private MoveResult Move(Snapshot snapshot, string target, bool overwrite)
    {
        using var exclusive = Loader.Lock();
        var changed = ReadDisk(snapshot.Path, snapshot.DiskText) != snapshot.DiskText;
        if (changed && snapshot.ConfigEdited)
        {
            return new(true, false, snapshot.ConfigJson, null, null);
        }
        var reload = changed ? ReadState(snapshot.Path) : null;
        if (reload?.ConfigUnreadable ?? snapshot.Unreadable)
        {
            return new(false, true, snapshot.ConfigJson, null, null);
        }
        var config = reload?.Result.Config ?? ConfigSerializer.Deserialize(snapshot.ConfigJson);
        var warning = Loader.RelocateConfig(config, snapshot.Path, target, overwrite);
        return new(false, false, ConfigSerializer.Serialize(config), Loader.LoadLocal(), reload, warning);
    }

    // The active path can precede the local.json pointer when its write failed; reloads must keep that session.
    private LoadedState ReadState(string? path)
    {
        var result = Loader.Load(path);
        return new(result,
            ReadDisk(result.ConfigPath, null),
            result.Exists && !IsParsable(result.ConfigPath, ConfigSerializer.Deserialize),
            File.Exists(Loader.Paths.LocalConfigPath) && !IsParsable(Loader.Paths.LocalConfigPath, ConfigSerializer.DeserializeLocal));
    }

    private void ApplyLoaded(LoadedState state, bool keepLocalEdits, string? adoptedPath = null)
    {
        Config = state.Result.Config;
        ConfigPath = state.Result.ConfigPath;
        ConfigExists = state.Result.Exists;
        ConfigUnreadable = state.ConfigUnreadable;
        if (!keepLocalEdits)
        {
            Local = state.Result.Local;
            _savedLocalJson = ConfigSerializer.SerializeLocal(Local);
            _localUnreadable = state.LocalUnreadable;
        }
        if (adoptedPath is not null)
        {
            Local.ConfigPath = adoptedPath == Path.GetFullPath(Loader.Paths.DefaultConfigPath) ? null : adoptedPath;
        }
        LocalProfileTargets.MigrateKeys(Config, Local);
        LoadError = state.Result.Error;
        HasExternalChanges = IsHoldingEditsForUnreadableFile = false;
        _diskText = state.DiskText;
        _savedConfigJson = ConfigSerializer.Serialize(Config);
        ModelsReplaced?.Invoke();
    }

    private static string? ReadDisk(string path, string? unchanged)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return unchanged;
        }
    }

    private static bool IsParsable<T>(string path, Func<string, T> parse)
    {
        try
        {
            parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Snapshot(string ConfigJson, string LocalJson, string Path, string? DiskText,
        string SavedConfigJson, string SavedLocalJson, bool Unreadable)
    {
        public bool ConfigEdited => ConfigJson != SavedConfigJson;
    }

    private sealed record LoadedState(ConfigLoadResult Result, string? DiskText, bool ConfigUnreadable, bool LocalUnreadable);
    private sealed record MoveResult(bool Conflict, bool Unreadable, string ConfigJson, LocalConfig? Local, LoadedState? Reload, string? Warning = null);

    private sealed class WriteResult
    {
        public bool ConfigSaved { get; set; }
        public bool LocalSaved { get; set; }
        public bool ConfigExists { get; set; }
        public bool Conflict { get; set; }
        public bool Unreadable { get; set; }
        public string? Error { get; set; }
        public LoadedState? Reload { get; set; }
    }
}
