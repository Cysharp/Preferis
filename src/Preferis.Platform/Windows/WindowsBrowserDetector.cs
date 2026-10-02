using System.Runtime.Versioning;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Profiles;

namespace Preferis.Platform.Windows;

/// <summary>
/// Finds browsers through the <c>StartMenuInternet</c> registrations (per-user first, then machine-wide and 32-bit) and
/// the catalog's known install paths. <see cref="TryResolve"/> reads only the keys of the one browser asked for on the
/// fast path; <see cref="DetectBrowsers"/> reads them all. See docs/Platform.md §2.3.
/// </summary>
public sealed class WindowsBrowserDetector : IBrowserDetector
{
    private const string StartMenuInternet = @"Clients\StartMenuInternet";

    // Per-user registrations take precedence over machine-wide ones of the same name.
    private static readonly string[] Roots = [@"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node"];

    // Shared by unrelated programs: matching on the name alone would misidentify them.
    private static readonly HashSet<string> GenericExecutableNames = new(["launcher.exe"], StringComparer.OrdinalIgnoreCase);

    private readonly IRegistry _registry;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _localAppData;
    private readonly string _roamingAppData;
    private readonly IReadOnlyDictionary<string, BrowserOverride>? _overrides;

    [SupportedOSPlatform("windows")]
    public WindowsBrowserDetector(IReadOnlyDictionary<string, BrowserOverride>? overrides)
        : this(
            WindowsRegistry.Instance,
            File.Exists,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            overrides)
    {
    }

    internal WindowsBrowserDetector(IRegistry registry, Func<string, bool> fileExists, Func<string, string?> getEnvironmentVariable, string localAppData, string roamingAppData, IReadOnlyDictionary<string, BrowserOverride>? overrides)
    {
        _registry = registry;
        _fileExists = fileExists;
        _getEnvironmentVariable = getEnvironmentVariable;
        _localAppData = localAppData;
        _roamingAppData = roamingAppData;
        _overrides = overrides;
    }

    public DetectedBrowser? TryResolve(string browserId)
    {
        var entry = BrowserCatalog.Find(browserId);
        if (Override(browserId) is { } overridden)
        {
            return Describe(browserId, entry, overridden, null);
        }

        if (browserId.StartsWith("win:", StringComparison.Ordinal))
        {
            var key = browserId[4..];
            foreach (var root in Roots)
            {
                if (ReadExecutable(root, key) is { } executable && Classify(key, executable) is { Excluded: false, Entry: null })
                {
                    return Describe(browserId, null, executable, ApplicationName(root, key));
                }
            }
            return null;
        }

        if (entry is null || (entry.WindowsStartMenuKey is null && entry.WindowsStartMenuKeyPrefix is null && entry.WindowsKnownPaths.Count == 0))
        {
            // Not a Windows browser (Safari, mac: ids).
            return null;
        }

        // The keys named like this browser; the fast path normally ends here.
        foreach (var root in Roots)
        {
            foreach (var key in _registry.GetSubKeyNames($@"{root}\{StartMenuInternet}").Where(k => NameMatches(entry, k)))
            {
                if (ReadExecutable(root, key) is { } executable && Classify(key, executable).Entry == entry)
                {
                    return Describe(entry.Id, entry, executable, null);
                }
            }
        }
        if (KnownPath(entry) is { } known)
        {
            return Describe(entry.Id, entry, known, null);
        }

        // A key named otherwise (custom installs): only recognizable by its executable, so every key has to be read.
        return DetectBrowsers().FirstOrDefault(b => b.Id == browserId);
    }

    public IReadOnlyList<DetectedBrowser> DetectBrowsers()
    {
        var found = new Dictionary<string, DetectedBrowser>(StringComparer.Ordinal);
        foreach (var root in Roots)
        {
            foreach (var key in _registry.GetSubKeyNames($@"{root}\{StartMenuInternet}"))
            {
                if (ReadExecutable(root, key) is not { } executable)
                {
                    continue;
                }
                var (entry, excluded) = Classify(key, executable);
                if (excluded)
                {
                    continue;
                }
                var id = entry?.Id ?? "win:" + key;
                if (!found.ContainsKey(id))
                {
                    found[id] = Describe(id, entry, Override(id) ?? executable, entry is null ? ApplicationName(root, key) : null);
                }
            }
        }

        foreach (var entry in BrowserCatalog.Entries.Where(e => !found.ContainsKey(e.Id)))
        {
            if ((Override(entry.Id) ?? KnownPath(entry)) is { } path)
            {
                found[entry.Id] = Describe(entry.Id, entry, path, null);
            }
        }

        var catalogOrder = BrowserCatalog.Entries.Select((entry, index) => (entry.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        return found.Values
            .OrderBy(b => catalogOrder.GetValueOrDefault(b.Id, int.MaxValue))
            .ThenBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The program of a <c>shell\open\command</c> value: quoted, or unquoted up to <c>.exe</c> (Internet Explorer registers
    /// <c>C:\Program Files\Internet Explorer\iexplore.exe</c> without quotes). Environment variables are expanded.
    /// </summary>
    internal static string? ExecutableFromCommand(string command, Func<string, string?> getEnvironmentVariable)
    {
        command = command.Trim();
        string path;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            path = end < 0 ? command[1..] : command[1..end];
        }
        else
        {
            var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe < 0 ? command.Split(' ')[0] : command[..(exe + 4)];
        }
        path = EnvironmentExpander.Expand(path.Trim(), HostPlatform.Windows, getEnvironmentVariable, "");
        return path.Length == 0 ? null : path;
    }

    private string? ReadExecutable(string root, string key)
    {
        var command = _registry.GetString($@"{root}\{StartMenuInternet}\{key}\shell\open\command", null);
        var executable = command is null ? null : ExecutableFromCommand(command, _getEnvironmentVariable);
        return executable is not null && _fileExists(executable) ? executable : null;
    }

    // Capabilities\ApplicationName, unless it is a resource reference ("@file,-id"), then the key's own name.
    private string ApplicationName(string root, string key)
    {
        var path = $@"{root}\{StartMenuInternet}\{key}";
        var name = _registry.GetString($@"{path}\Capabilities", "ApplicationName");
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
        {
            return name;
        }
        var fallback = _registry.GetString(path, null);
        return !string.IsNullOrWhiteSpace(fallback) && !fallback.StartsWith('@') ? fallback : key;
    }

    /// <summary>Which catalog browser a registration is, or null for one outside the catalog.</summary>
    private static (BrowserCatalogEntry? Entry, bool Excluded) Classify(string key, string executable)
    {
        var fileName = FileName(executable);
        // Preferis must not offer itself (that would loop), whatever its key is called. Internet Explorer is retired on
        // Windows 11 and only opens Edge.
        if (key.Equals("Preferis", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Preferis.exe", StringComparison.OrdinalIgnoreCase)
            || key.Equals("IEXPLORE.EXE", StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        if (BrowserCatalog.Entries.FirstOrDefault(e => NameMatches(e, key)) is { } byName)
        {
            return (byName, false);
        }

        // Channels share the executable name (msedge.exe, chrome.exe); their install folders tell them apart.
        var candidates = BrowserCatalog.Entries.Where(e => e.WindowsExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)).ToList();
        var byFolder = candidates.FirstOrDefault(e => InstallFolder(e) is { } folder && executable.Contains($@"\{folder}\", StringComparison.OrdinalIgnoreCase));
        if (byFolder is not null)
        {
            return (byFolder, false);
        }
        return (GenericExecutableNames.Contains(fileName) ? null : candidates.FirstOrDefault(), false);
    }

    // Registry paths are Windows paths whatever runs this (the tests also run on macOS), and Path splits at the host's
    // separators only.
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static bool NameMatches(BrowserCatalogEntry entry, string key)
    {
        if (entry.WindowsStartMenuKey is { } name
            && (key.Equals(name, StringComparison.OrdinalIgnoreCase)
                // Per-user installs next to a machine-wide one append ".<suffix>" (Chrome).
                || key.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (entry.WindowsStartMenuKeyPrefix is { } prefix && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Firefox before hashed keys, and ESR.
        return entry.Id == "firefox" && key.Equals("FIREFOX.EXE", StringComparison.OrdinalIgnoreCase);
    }

    // "Microsoft\Edge Dev\User Data" -> "Microsoft\Edge Dev": the vendor folder the channel installs to.
    private static string? InstallFolder(BrowserCatalogEntry entry)
    {
        const string Suffix = @"\User Data";
        return entry.WindowsUserDataDir is { } dir && dir.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) ? dir[..^Suffix.Length] : null;
    }

    private string? KnownPath(BrowserCatalogEntry entry)
        => entry.WindowsKnownPaths
            .Select(p => EnvironmentExpander.Expand(p, HostPlatform.Windows, _getEnvironmentVariable, ""))
            .FirstOrDefault(_fileExists);

    private string? Override(string id)
        => _overrides?.GetValueOrDefault(id)?.Path is { Length: > 0 } path && _fileExists(path) ? path : null;

    private DetectedBrowser Describe(string id, BrowserCatalogEntry? entry, string executable, string? name)
    {
        var userData = _overrides?.GetValueOrDefault(id)?.UserDataDir
            ?? (entry?.WindowsUserDataDir is { } relative
                ? Path.Combine(entry.WindowsUserDataRoot == WindowsKnownFolder.RoamingAppData ? _roamingAppData : _localAppData, relative)
                : null);
        return new DetectedBrowser(
            id,
            entry?.DisplayName ?? name ?? Path.GetFileNameWithoutExtension(FileName(executable)),
            entry?.Family ?? BrowserFamily.Other,
            executable,
            null,
            userData,
            entry);
    }
}
