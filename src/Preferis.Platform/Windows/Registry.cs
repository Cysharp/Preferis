using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Preferis.Platform.Windows;

/// <summary>
/// The registry as far as Preferis uses it: string values, sub key names, and whole trees to delete. Paths start with
/// <c>HKCU\</c> or <c>HKLM\</c>; a null value name is the key's default value. Faked in the tests.
/// </summary>
public interface IRegistry
{
    bool KeyExists(string path);

    /// <summary>The names of the sub keys, or empty when the key does not exist.</summary>
    IReadOnlyList<string> GetSubKeyNames(string path);

    /// <summary>A string value, or null when the key or the value does not exist or is not a string.</summary>
    string? GetString(string path, string? name);

    /// <summary>Writes a string value, creating the key and its parents as needed.</summary>
    void SetString(string path, string? name, string value);

    /// <summary>Deletes the key with everything under it. Nothing happens when it does not exist.</summary>
    void DeleteTree(string path);

    /// <summary>Deletes one value. Nothing happens when it does not exist.</summary>
    void DeleteValue(string path, string name);
}

/// <summary>The real registry, in the process's own view (64-bit Preferis sees <c>WOW6432Node</c> as a plain key).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public static WindowsRegistry Instance { get; } = new();

    public bool KeyExists(string path)
    {
        using var key = Open(path, writable: false);
        return key is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        using var key = Open(path, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    public string? GetString(string path, string? name)
    {
        using var key = Open(path, writable: false);
        // Expandable strings come back expanded, which is what every caller wants.
        return key?.GetValue(name) as string;
    }

    public void SetString(string path, string? name, string value)
    {
        var (root, subKey) = Split(path);
        using var key = root.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteTree(string path)
    {
        var (root, subKey) = Split(path);
        root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    public void DeleteValue(string path, string name)
    {
        using var key = Open(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey? Open(string path, bool writable)
    {
        var (root, subKey) = Split(path);
        try
        {
            return root.OpenSubKey(subKey, writable);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static (RegistryKey Root, string SubKey) Split(string path)
    {
        var separator = path.IndexOf('\\', StringComparison.Ordinal);
        var hive = separator < 0 ? path : path[..separator];
        var subKey = separator < 0 ? "" : path[(separator + 1)..];
        RegistryKey root = hive.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
            _ => throw new ArgumentException($"Unsupported registry hive in '{path}'.", nameof(path)),
        };
        return (root, subKey);
    }
}
