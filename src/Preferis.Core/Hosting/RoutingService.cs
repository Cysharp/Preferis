using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Core.Hosting;

/// <summary>
/// What the Router does with one URL: load the configuration, route, resolve, launch, and when anything
/// fails walk the fallback chain of docs/Design.md §8 so that the URL is never silently dropped.
/// OS specifics are behind the <see cref="RoutingServices"/> interfaces.
/// </summary>
public sealed class RoutingService
{
    private const string DetectedBrowserPrefix = "browser:";

    private readonly RoutingServices _services;

    public RoutingService(RoutingServices services)
    {
        _services = services;
    }

    public HandleResult Handle(RoutingUrl url)
    {
        var loaded = _services.Config.Load();
        if (loaded.Error is not null)
        {
            Log(LogLevel.Warning, loaded.Error);
        }

        var context = new Context(url, loaded, new ProfileResolver(_services.Platform, _services.Browsers, _services.BrowserProfiles, _services.GetEnvironmentVariable, _services.HomeDirectory));
        var result = new Router(loaded.Config).Route(url);
        foreach (var entry in result.Trace)
        {
            Log(LogLevel.Trace, entry.RuleId is null ? $"[{entry.Kind}] {entry.Message}" : $"[{entry.Kind}] {entry.RuleId}: {entry.Message}");
        }

        switch (result.Decision)
        {
            case RoutingDecision.Open { ProfileIds.Count: 0 }:
                return OfferDetectedBrowsers(context, loaded.Error ?? "No profiles are configured yet.");
            case RoutingDecision.Open { ProfileIds.Count: 1 } single:
                return LaunchProfile(context, single.ProfileIds[0]);
            case RoutingDecision.Open several:
                return ShowProfilePicker(context, several.ProfileIds, several.RuleId, message: null, allowFallbackPicker: true)
                    ?? Fallback(context, "The picker could not be shown.", failedProfileId: null, allowPicker: false);
            case RoutingDecision.System when _services.System.IsSelfDefaultHandler(url.Uri.Scheme):
                Log(LogLevel.Warning, "The system default handler is Preferis itself; not delegating.");
                return Fallback(context, "The system default handler is Preferis itself.", failedProfileId: null, allowPicker: true);
            case RoutingDecision.System:
                _services.System.OpenWithSystemDefault(url);
                return new HandleResult(HandleOutcome.Delegated, null, "Handed to the system default handler.");
            case RoutingDecision.None:
                return new HandleResult(HandleOutcome.Ignored, null, "Rule says: do nothing.");
            case RoutingDecision.Failed failed:
                Log(LogLevel.Error, $"Routing failed: {failed.Reason}");
                return Fallback(context, failed.Reason, failedProfileId: null, allowPicker: true);
            default:
                return Fallback(context, $"Unexpected decision {result.Decision}.", failedProfileId: null, allowPicker: true);
        }
    }

    private HandleResult LaunchProfile(Context context, string profileId)
    {
        switch (context.Resolver.ResolveWithFallback(context.Config, profileId, context.Url))
        {
            case ResolveResult.Resolved resolved:
                foreach (var warning in resolved.Warnings)
                {
                    Log(LogLevel.Warning, warning);
                }
                if (TryLaunch(resolved.Plan, resolved.ProfileId, out var error))
                {
                    return Launched(resolved.ProfileId);
                }
                // A launch failure goes straight to the picker (docs/Design.md §8 step 3): the user should see
                // which profile failed and choose, rather than silently landing in the default browser.
                var launchFailure = $"Could not start '{resolved.ProfileId}': {error}";
                return ShowProfilePicker(context, context.Config.Profiles.Select(p => p.Id).ToList(), ruleId: null, message: launchFailure, allowFallbackPicker: false)
                    ?? Fallback(context, launchFailure, resolved.ProfileId, allowPicker: false);
            case ResolveResult.Unresolved unresolved:
                Log(LogLevel.Warning, unresolved.Reason);
                return Fallback(context, unresolved.Reason, profileId, allowPicker: true);
            default:
                return Fallback(context, "Unexpected resolution result.", profileId, allowPicker: true);
        }
    }

    /// <summary>The fallback chain: default profile, picker with every profile, system handler, user notification.</summary>
    private HandleResult Fallback(Context context, string reason, string? failedProfileId, bool allowPicker)
    {
        var config = context.Config;

        if (config.DefaultProfile is { } defaultId && defaultId != failedProfileId && config.Profiles.Any(p => p.Id == defaultId))
        {
            Log(LogLevel.Info, $"Falling back to the default profile '{defaultId}'.");
            if (context.Resolver.ResolveWithFallback(config, defaultId, context.Url) is ResolveResult.Resolved resolved
                && TryLaunch(resolved.Plan, resolved.ProfileId, out _))
            {
                return Launched(resolved.ProfileId);
            }
        }

        if (allowPicker && config.Profiles.Count > 0)
        {
            var picked = ShowProfilePicker(context, config.Profiles.Select(p => p.Id).ToList(), ruleId: null, message: reason, allowFallbackPicker: false);
            if (picked is not null)
            {
                return picked;
            }
        }

        if (!_services.System.IsSelfDefaultHandler(context.Url.Uri.Scheme))
        {
            Log(LogLevel.Info, "Falling back to the system default handler.");
            _services.System.OpenWithSystemDefault(context.Url);
            return new HandleResult(HandleOutcome.Delegated, null, $"Handed to the system default handler after: {reason}");
        }

        _services.Notifier.ShowError("Preferis", $"The URL could not be opened: {reason}", context.Url);
        return new HandleResult(HandleOutcome.Failed, null, reason);
    }

    /// <summary>Shows the picker for configured profiles. Returns null when the picker is unavailable, so the caller can continue the chain.</summary>
    private HandleResult? ShowProfilePicker(Context context, IReadOnlyList<string> profileIds, string? ruleId, string? message, bool allowFallbackPicker)
    {
        var decision = new RoutingDecision.Open(context.Url, profileIds, OpenSource.Explicit, ruleId);
        var request = PickerMessages.CreateRequest(context.Config, decision, _services.Platform, message);
        var response = _services.Picker.Show(request);
        if (response is null)
        {
            Log(LogLevel.Warning, "The picker did not return a usable answer.");
            return null;
        }

        if (response.Result == PickerResult.Cancelled)
        {
            return new HandleResult(HandleOutcome.Ignored, null, "Picker cancelled.");
        }

        var chosen = response.ProfileId!;
        if (response.Remember)
        {
            Remember(context, chosen, ruleId);
        }

        switch (context.Resolver.ResolveWithFallback(context.Config, chosen, context.Url))
        {
            case ResolveResult.Resolved resolved when TryLaunch(resolved.Plan, resolved.ProfileId, out _):
                return Launched(resolved.ProfileId);
            case ResolveResult.Resolved resolved:
                return Fallback(context, $"Could not start '{resolved.ProfileId}'.", resolved.ProfileId, allowFallbackPicker);
            case ResolveResult.Unresolved unresolved:
                Log(LogLevel.Warning, unresolved.Reason);
                return Fallback(context, unresolved.Reason, chosen, allowFallbackPicker);
            default:
                return Fallback(context, "Unexpected resolution result.", chosen, allowFallbackPicker);
        }
    }

    private void Remember(Context context, string profileId, string? ruleId)
    {
        try
        {
            RememberedRule.Insert(context.Config, RememberedRule.Create(context.Url, profileId), ruleId);
            _services.Config.Save(context.Config, context.Loaded.ConfigPath);
            Log(LogLevel.Info, $"Remembered '{context.Url.Host}' -> '{profileId}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Error, $"Could not save the remembered rule: {ex.Message}");
        }
    }

    /// <summary>No usable configuration: let the user pick one of the browsers installed on this machine.</summary>
    private HandleResult OfferDetectedBrowsers(Context context, string message)
    {
        var browsers = _services.Browsers.DetectBrowsers();
        if (browsers.Count > 0)
        {
            var picker = context.Config.Settings?.Picker;
            var request = new PickerRequest
            {
                Url = context.Url.AbsoluteUri,
                Candidates = browsers.Select(b => new PickerCandidate { ProfileId = DetectedBrowserPrefix + b.Id, Name = b.DisplayName, Browser = b.Id }).ToList(),
                Options = new PickerOptions
                {
                    Position = picker?.Position ?? "cursor",
                    AutoSelectSeconds = picker?.AutoSelectSeconds ?? 0,
                    RememberChoiceDefault = false,
                },
                Message = message,
            };

            var response = _services.Picker.Show(request);
            if (response?.Result == PickerResult.Cancelled)
            {
                return new HandleResult(HandleOutcome.Ignored, null, "Picker cancelled.");
            }
            if (response?.ProfileId is { } chosen && browsers.FirstOrDefault(b => DetectedBrowserPrefix + b.Id == chosen) is { } browser)
            {
                var plan = ProfileResolver.PlanForBrowser(browser, context.Url, _services.Platform);
                if (TryLaunch(plan, chosen, out var error))
                {
                    return Launched(chosen);
                }
                message = $"Could not start '{browser.DisplayName}': {error}";
            }
        }
        else
        {
            Log(LogLevel.Warning, "No browser was detected on this machine.");
        }

        if (!_services.System.IsSelfDefaultHandler(context.Url.Uri.Scheme))
        {
            _services.System.OpenWithSystemDefault(context.Url);
            return new HandleResult(HandleOutcome.Delegated, null, $"Handed to the system default handler after: {message}");
        }

        _services.Notifier.ShowError("Preferis", $"The URL could not be opened: {message}", context.Url);
        return new HandleResult(HandleOutcome.Failed, null, message);
    }

    private bool TryLaunch(LaunchPlan plan, string profileId, out string? error)
    {
        try
        {
            _services.Launcher.Launch(plan);
            Log(LogLevel.Info, $"Launched '{profileId}': {plan.Kind} {plan.Path} {string.Join(' ', plan.Arguments)}");
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(LogLevel.Error, $"Launch of '{profileId}' failed: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    private static HandleResult Launched(string profileId) => new(HandleOutcome.Launched, profileId, $"Launched '{profileId}'.");

    private void Log(LogLevel level, string message) => _services.Log.Write(level, message);

    private sealed record Context(RoutingUrl Url, ConfigLoadResult Loaded, ProfileResolver Resolver)
    {
        public PreferisConfig Config => Loaded.Config;
    }
}
