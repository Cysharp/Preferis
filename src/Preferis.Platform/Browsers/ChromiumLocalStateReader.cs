using System.Text.Json;
using Preferis.Core.Browsers;

namespace Preferis.Platform.Browsers;

/// <summary>
/// Reads the profile list of a Chromium based browser from its <c>Local State</c> file
/// (<c>profile.info_cache</c>, ordered by <c>profile.profiles_order</c>). See docs/HostPlatform.md §2.4.
/// </summary>
public static class ChromiumLocalStateReader
{
    /// <param name="userDataDir">Used to build avatar paths; null when unknown.</param>
    public static IReadOnlyList<BrowserProfileInfo> Read(Stream localState, string? userDataDir)
    {
        // The file can be several hundred KB; JsonDocument parses it without materializing objects.
        using var document = JsonDocument.Parse(localState);
        var root = document.RootElement;
        if (!root.TryGetProperty("profile", out var profile)
            || !profile.TryGetProperty("info_cache", out var cache)
            || cache.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var byDirectory = new Dictionary<string, BrowserProfileInfo>(StringComparer.Ordinal);
        var appearance = new List<string>();
        foreach (var entry in cache.EnumerateObject())
        {
            var name = NonEmpty(GetString(entry.Value, "name")) ?? NonEmpty(GetString(entry.Value, "gaia_name"));
            var email = NonEmpty(GetString(entry.Value, "user_name"));
            var picture = NonEmpty(GetString(entry.Value, "gaia_picture_file_name"));
            var avatar = userDataDir is not null && picture is not null ? Path.Combine(userDataDir, entry.Name, picture) : null;
            byDirectory[entry.Name] = new BrowserProfileInfo(entry.Name, name, email, avatar);
            appearance.Add(entry.Name);
        }

        var ordered = new List<BrowserProfileInfo>(byDirectory.Count);
        if (profile.TryGetProperty("profiles_order", out var order) && order.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in order.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && byDirectory.Remove(item.GetString()!, out var info))
                {
                    ordered.Add(info);
                }
            }
        }
        // Profiles the order list does not mention keep their file order.
        ordered.AddRange(appearance.Where(byDirectory.ContainsKey).Select(d => byDirectory[d]));
        return ordered;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
