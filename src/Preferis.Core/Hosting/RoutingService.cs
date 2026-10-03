using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

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
        // The language of the picker and of the messages (docs/Design.md §9.4) is config.json's; while it loads, and when it
        // cannot be read, the system's rather than that of the previous URL.
        var systemLanguages = _services.SystemLanguages;
        if (systemLanguages is not null)
        {
            UiLanguages.SetLazily(() => UiLanguages.Resolve(null, systemLanguages));
        }
        var loaded = _services.Config.Load();
        if (systemLanguages is not null && loaded.Config.Settings?.Language is { } setting)
        {
            UiLanguages.SetLazily(() => UiLanguages.Resolve(setting, systemLanguages));
        }
        if (loaded.Error is not null)
        {
            Log(LogLevel.Warning, loaded.Error);
        }

        // What this machine opens: its own targets in place of the shared ones (docs/Design.md §7.4). Remembering a
        // choice writes to the file as loaded again, so these never reach config.json.
        var config = LocalProfileTargets.Apply(loaded.Config, loaded.Local);
        var result = new Router(config).Route(url);
        foreach (var entry in result.Trace)
        {
            Log(LogLevel.Trace, entry.RuleId is null ? $"[{entry.Kind}] {entry.Message}" : $"[{entry.Kind}] {entry.RuleId}: {entry.Message}");
        }

        // Everything from here on (launching, the picker, remembering, the fallbacks) works on the URL the rules
        // produced: after a rewrite (e.g. an unwrapped Safe Link) that is no longer the URL that came in.
        var context = new Context(result.Decision.Url, loaded, config, new ProfileResolver(_services.HostPlatform, _services.Browsers, _services.BrowserProfiles, _services.GetEnvironmentVariable, _services.HomeDirectory));
        return Execute(context, result.Decision, loaded.Error) with { RuleId = result.RuleId };
    }

    private HandleResult Execute(Context context, RoutingDecision decision, string? configError)
    {
        var url = context.Url;
        switch (decision)
        {
            case RoutingDecision.Open { ProfileIds.Count: 0 }:
                return OfferDetectedBrowsers(context, configError ?? L("No profiles are configured yet."));
            case RoutingDecision.Open { ProfileIds.Count: 1 } single:
                return LaunchProfile(context, single.ProfileIds[0]);
            case RoutingDecision.Open several:
                return ShowProfilePicker(context, several.ProfileIds, several.RuleId, message: null, allowFallbackPicker: true)
                    ?? Fallback(context, L("The picker could not be shown."), failedProfileId: null, allowPicker: false);
            case RoutingDecision.System when _services.System.IsSelfDefaultHandler(url.Uri.Scheme):
                Log(LogLevel.Warning, "The system default handler is Preferis itself; not delegating.");
                return Fallback(context, L("The system default handler is Preferis itself."), failedProfileId: null, allowPicker: true);
            case RoutingDecision.System:
                _services.System.OpenWithSystemDefault(url);
                return new HandleResult(HandleOutcome.Delegated, null, "Handed to the system default handler.");
            case RoutingDecision.None:
                return new HandleResult(HandleOutcome.Ignored, null, "Rule says: do nothing.");
            case RoutingDecision.Failed failed:
                Log(LogLevel.Error, $"Routing failed: {failed.Reason}");
                return Fallback(context, failed.Reason, failedProfileId: null, allowPicker: true);
            default:
                return Fallback(context, L("Unexpected decision {0}.", decision), failedProfileId: null, allowPicker: true);
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
                if (TryLaunch(context, resolved.Plan, resolved.ProfileId, out var error))
                {
                    return Launched(context, resolved.ProfileId);
                }
                // A launch failure goes straight to the picker (docs/Design.md §8 step 3): the user should see
                // which profile failed and choose, rather than silently landing in the default browser.
                var launchFailure = L("Could not start {0}: {1}", Describe(context, resolved.ProfileId), error);
                return ShowProfilePicker(context, context.Config.Profiles.Select(p => p.Id).ToList(), ruleId: null, message: launchFailure, allowFallbackPicker: false)
                    ?? Fallback(context, launchFailure, resolved.ProfileId, allowPicker: false);
            case ResolveResult.Unresolved unresolved:
                Log(LogLevel.Warning, unresolved.Reason);
                return Fallback(context, unresolved.Reason, profileId, allowPicker: true);
            default:
                return Fallback(context, L("Unexpected resolution result."), profileId, allowPicker: true);
        }
    }

    /// <summary>The fallback chain: default profile, picker with every profile, system handler, user notification.</summary>
    private HandleResult Fallback(Context context, string reason, string? failedProfileId, bool allowPicker)
    {
        var config = context.Config;

        if (config.DefaultProfile is { } defaultId && defaultId != failedProfileId && config.Profiles.Any(p => p.Id == defaultId))
        {
            Log(LogLevel.Info, $"Falling back to the default profile {Describe(context, defaultId)}.");
            if (context.Resolver.ResolveWithFallback(config, defaultId, context.Url) is ResolveResult.Resolved resolved
                && TryLaunch(context, resolved.Plan, resolved.ProfileId, out _))
            {
                return Launched(context, resolved.ProfileId);
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

        _services.Notifier.ShowError("Preferis", L("The URL could not be opened: {0}", reason), context.Url);
        return new HandleResult(HandleOutcome.Failed, null, reason);
    }

    /// <summary>Shows the picker for configured profiles. Returns null when the picker is unavailable, so the caller can continue the chain.</summary>
    private HandleResult? ShowProfilePicker(Context context, IReadOnlyList<string> profileIds, string? ruleId, string? message, bool allowFallbackPicker)
    {
        var decision = new RoutingDecision.Open(context.Url, profileIds, OpenSource.Explicit, ruleId);
        var request = PickerMessages.CreateRequest(context.Config, decision, _services.HostPlatform, message);
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
            case ResolveResult.Resolved resolved when TryLaunch(context, resolved.Plan, resolved.ProfileId, out _):
                return Launched(context, resolved.ProfileId);
            case ResolveResult.Resolved resolved:
                return Fallback(context, L("Could not start {0}.", Describe(context, resolved.ProfileId)), resolved.ProfileId, allowFallbackPicker);
            case ResolveResult.Unresolved unresolved:
                Log(LogLevel.Warning, unresolved.Reason);
                return Fallback(context, unresolved.Reason, chosen, allowFallbackPicker);
            default:
                return Fallback(context, L("Unexpected resolution result."), chosen, allowFallbackPicker);
        }
    }

    private void Remember(Context context, string profileId, string? ruleId)
    {
        // The picker may have been open for minutes: add the rule to the file as it is now, so that changes saved
        // meanwhile (settings UI, another picker) are kept rather than replaced by the copy loaded before.
        var current = _services.Config.Load();
        if (current.Error is not null || current.ConfigPath != context.Loaded.ConfigPath)
        {
            Log(LogLevel.Warning, $"Not remembering '{context.Url.Host}': the configuration changed in a way that cannot be merged. {current.Error}");
            return;
        }

        // The picker answers with the id the profile had when it was shown. If it was renamed meanwhile, the rule must use
        // the id it has now; if it was deleted, a rule would name no profile and quietly send this host elsewhere.
        if (ProfileIds.Current(current.Config, profileId) is not { } currentId)
        {
            Log(LogLevel.Warning, $"Not remembering '{context.Url.Host}': the profile '{profileId}' no longer exists, or which profile has that id now cannot be told.");
            return;
        }

        try
        {
            RememberedRule.Insert(current.Config, RememberedRule.Create(context.Url, currentId), ruleId);
            _services.Config.Save(current.Config, current.ConfigPath);
            Log(LogLevel.Info, $"Remembered '{context.Url.Host}' -> {ProfileNames.Describe(current.Config, currentId)}.");
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
                    AllowRemember = false,
                    Language = UiLanguages.Current.Code(),
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
                var plan = ProfileResolver.PlanForBrowser(browser, context.Url, _services.HostPlatform);
                if (TryLaunch(context, plan, chosen, out var error))
                {
                    return Launched(context, chosen);
                }
                message = L("Could not start '{0}': {1}", browser.DisplayName, error);
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

        _services.Notifier.ShowError("Preferis", L("The URL could not be opened: {0}", message), context.Url);
        return new HandleResult(HandleOutcome.Failed, null, message);
    }

    private bool TryLaunch(Context context, LaunchPlan plan, string profileId, out string? error)
    {
        try
        {
            _services.Launcher.Launch(plan);
            Log(LogLevel.Info, $"Launched {Describe(context, profileId)}: {plan.Kind} {plan.Path} {string.Join(' ', plan.Arguments)}");
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(LogLevel.Error, $"Launch of {Describe(context, profileId)} failed: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    private static HandleResult Launched(Context context, string profileId) => new(HandleOutcome.Launched, profileId, $"Launched {Describe(context, profileId)}.");

    // Ids are numbers unless someone chose one (docs/Configuration.md §2.10): logs say which profile by its name too.
    private static string Describe(Context context, string profileId) => ProfileNames.Describe(context.Config, profileId);

    private void Log(LogLevel level, string message) => _services.Log.Write(level, message);

    /// <param name="Config">The configuration with this machine's targets applied; <see cref="Loaded"/> has it as the file has it.</param>
    private sealed record Context(RoutingUrl Url, ConfigLoadResult Loaded, PreferisConfig Config, ProfileResolver Resolver);
}
