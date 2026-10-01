using System.Runtime.Versioning;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;

namespace Preferis.Platform.MacOS;

/// <summary>
/// Finds browsers through Launch Services. <see cref="TryResolve"/> performs exactly one lookup (fast path);
/// <see cref="DetectBrowsers"/> lists every application that claims https URLs. See docs/Platform.md §3.3.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacBrowserDetector : IBrowserDetector
{
    private const string ProbeUrl = "https://example.com/";

    private readonly IReadOnlyDictionary<string, BrowserOverride>? _overrides;
    private readonly string _homeDirectory;

    public MacBrowserDetector(IReadOnlyDictionary<string, BrowserOverride>? overrides, string homeDirectory)
    {
        _overrides = overrides;
        _homeDirectory = homeDirectory;
    }

    public DetectedBrowser? TryResolve(string browserId)
    {
        using var pool = ObjC.AutoreleasePool();
        var entry = BrowserCatalog.Find(browserId);
        var overridePath = _overrides?.GetValueOrDefault(browserId)?.Path;

        var appPath = overridePath is not null && Directory.Exists(overridePath) ? overridePath
            : entry?.MacBundleId is { } bundleId ? ObjC.ApplicationPathForBundleId(bundleId)
            : browserId.StartsWith("mac:", StringComparison.Ordinal) ? ObjC.ApplicationPathForBundleId(browserId[4..])
            : null;

        return appPath is null ? null : Describe(appPath, browserId, entry);
    }

    public IReadOnlyList<DetectedBrowser> DetectBrowsers()
    {
        using var pool = ObjC.AutoreleasePool();
        var selfId = ObjC.MainBundleIdentifier;
        var found = new Dictionary<string, (string Path, BrowserCatalogEntry? Entry)>(StringComparer.Ordinal);

        foreach (var path in ObjC.ApplicationPathsForUrl(ProbeUrl))
        {
            var bundleId = ObjC.BundleIdentifier(ObjC.BundleWithPath(path));
            if (bundleId is null || IsSelf(bundleId, selfId))
            {
                continue;
            }
            var entry = BrowserCatalog.Entries.FirstOrDefault(e => string.Equals(e.MacBundleId, bundleId, StringComparison.OrdinalIgnoreCase));
            var id = entry?.Id ?? "mac:" + bundleId;
            // Launch Services may list several copies (e.g. updater caches); prefer the one under /Applications.
            if (!found.TryGetValue(id, out var existing) || (!existing.Path.StartsWith("/Applications/", StringComparison.Ordinal) && path.StartsWith("/Applications/", StringComparison.Ordinal)))
            {
                found[id] = (path, entry);
            }
        }

        var catalogOrder = BrowserCatalog.Entries.Select((entry, index) => (entry.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        return found
            .OrderBy(kv => catalogOrder.GetValueOrDefault(kv.Key, int.MaxValue))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => Describe(kv.Value.Path, kv.Key, kv.Value.Entry))
            .ToList();
    }

    private DetectedBrowser Describe(string appPath, string id, BrowserCatalogEntry? entry)
    {
        var bundle = ObjC.BundleWithPath(appPath);
        var executable = ObjC.InfoString(bundle, "CFBundleExecutable") ?? Path.GetFileNameWithoutExtension(appPath);
        var name = entry?.DisplayName ?? ObjC.InfoString(bundle, "CFBundleDisplayName") ?? ObjC.InfoString(bundle, "CFBundleName") ?? Path.GetFileNameWithoutExtension(appPath);
        var userData = _overrides?.GetValueOrDefault(id)?.UserDataDir
            ?? (entry?.MacUserDataDir is { } relative ? Path.Combine(_homeDirectory, "Library", "Application Support", relative) : null);

        return new DetectedBrowser(
            id,
            name,
            entry?.Family ?? BrowserFamily.Other,
            Path.Combine(appPath, "Contents", "MacOS", executable),
            appPath,
            userData,
            entry);
    }

    private static bool IsSelf(string bundleId, string? selfId)
        => selfId is not null && (string.Equals(bundleId, selfId, StringComparison.OrdinalIgnoreCase) || bundleId.StartsWith(selfId + ".", StringComparison.OrdinalIgnoreCase));
}
