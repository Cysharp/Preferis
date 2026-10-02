using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

/// <summary>An in-memory registry. Key paths and value names are case-insensitive, as in the real one.</summary>
internal sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every key path read, to check that the fast path stays narrow.</summary>
    public List<string> Reads { get; } = [];

    public FakeRegistry Set(string path, string? name, string value)
    {
        SetString(path, name, value);
        return this;
    }

    /// <summary>A browser under <c>StartMenuInternet</c> the way browsers register themselves.</summary>
    public FakeRegistry WithBrowser(string root, string key, string command, string? applicationName = null)
    {
        var path = $@"{root}\Clients\StartMenuInternet\{key}";
        Set(path, null, key);
        Set($@"{path}\shell\open\command", null, command);
        if (applicationName is not null)
        {
            Set($@"{path}\Capabilities", "ApplicationName", applicationName);
        }
        return this;
    }

    public bool KeyExists(string path)
    {
        Reads.Add(path);
        return _keys.ContainsKey(Normalize(path));
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        Reads.Add(path);
        var prefix = Normalize(path) + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? GetString(string path, string? name)
    {
        Reads.Add(path);
        return _keys.TryGetValue(Normalize(path), out var values) && values.TryGetValue(name ?? "", out var value) ? value : null;
    }

    public void SetString(string path, string? name, string value)
    {
        // Parents exist implicitly, as CreateSubKey creates them.
        var normalized = Normalize(path);
        var parts = normalized.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var key = string.Join('\\', parts[..i]);
            if (!_keys.ContainsKey(key))
            {
                _keys[key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        _keys[normalized][name ?? ""] = value;
    }

    public void DeleteTree(string path)
    {
        var normalized = Normalize(path);
        foreach (var key in _keys.Keys.Where(k => k.Equals(normalized, StringComparison.OrdinalIgnoreCase) || k.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _keys.Remove(key);
        }
    }

    public void DeleteValue(string path, string name)
    {
        if (_keys.TryGetValue(Normalize(path), out var values))
        {
            values.Remove(name);
        }
    }

    /// <summary>All values under <paramref name="path"/>, for assertions.</summary>
    public IReadOnlyDictionary<string, string> Values(string path)
        => _keys.TryGetValue(Normalize(path), out var values) ? values : new Dictionary<string, string>();

    private static string Normalize(string path) => path.TrimEnd('\\');
}
