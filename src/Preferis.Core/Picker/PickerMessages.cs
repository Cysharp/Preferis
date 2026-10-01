using System.Text.Json;
using System.Text.Json.Serialization;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Picker;

/// <summary>What the Router writes to the picker's stdin. See docs/Design.md §9.3. Deliberately unversioned.</summary>
public sealed class PickerRequest
{
    public string Url { get; set; } = "";

    public List<PickerCandidate> Candidates { get; set; } = [];

    public PickerOptions Options { get; set; } = new();

    /// <summary>Explanation shown when the picker is a fallback ("the configuration could not be read"). Null normally.</summary>
    public string? Message { get; set; }
}

public sealed class PickerCandidate
{
    public string ProfileId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Catalog browser id for this platform, for icon lookup.</summary>
    public string? Browser { get; set; }

    public BrowserProfileRef? BrowserProfile { get; set; }

    public bool Private { get; set; }
}

public sealed class PickerOptions
{
    public string Position { get; set; } = "cursor";

    public double AutoSelectSeconds { get; set; }

    public bool RememberChoiceDefault { get; set; }
}

public enum PickerResult
{
    [JsonStringEnumMemberName("selected")]
    Selected,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}

/// <summary>What the picker writes to stdout.</summary>
public sealed class PickerResponse
{
    public PickerResult Result { get; set; }

    public string? ProfileId { get; set; }

    /// <summary>Create a rule so that this host always opens with the chosen profile.</summary>
    public bool Remember { get; set; }
}

public static class PickerMessages
{
    public static PickerRequest CreateRequest(PreferisConfig config, RoutingDecision.Open decision, HostPlatform platform, string? message)
    {
        var profiles = config.Profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var picker = config.Settings?.Picker;
        return new PickerRequest
        {
            Url = decision.Url.AbsoluteUri,
            Candidates = decision.ProfileIds
                .Select(id => profiles.GetValueOrDefault(id))
                .Where(p => p is not null)
                .Select(p => new PickerCandidate
                {
                    ProfileId = p!.Id,
                    Name = p.Name,
                    Browser = p.Browser?.For(platform),
                    BrowserProfile = p.BrowserProfile,
                    Private = p.Private,
                })
                .ToList(),
            Options = new PickerOptions
            {
                Position = picker?.Position ?? "cursor",
                AutoSelectSeconds = picker?.AutoSelectSeconds ?? 0,
                RememberChoiceDefault = picker?.RememberChoiceDefault ?? false,
            },
            Message = message,
        };
    }

    public static string Serialize(PickerRequest request) => JsonSerializer.Serialize(request, PreferisJsonContext.Default.PickerRequest);

    public static string Serialize(PickerResponse response) => JsonSerializer.Serialize(response, PreferisJsonContext.Default.PickerResponse);

    /// <summary>Returns null when the text is not a request.</summary>
    public static PickerRequest? DeserializeRequest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PickerRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the picker's answer. Returns null when it is not valid JSON, has an unknown result, or selects a
    /// profile that was not offered. The Router treats null as "picker failed" and falls back (docs/Design.md §8).
    /// </summary>
    public static PickerResponse? TryParseResponse(string json, PickerRequest request)
    {
        PickerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PickerResponse);
        }
        catch (JsonException)
        {
            return null;
        }

        switch (response?.Result)
        {
            case PickerResult.Selected when response.ProfileId is not null && request.Candidates.Any(c => c.ProfileId == response.ProfileId):
                return response;
            case PickerResult.Cancelled:
                response.ProfileId = null;
                return response;
            default:
                return null;
        }
    }
}
