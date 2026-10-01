using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Core.Hosting;

/// <summary>Reads and writes the configuration. Implemented by <see cref="ConfigLoader"/>; faked in tests.</summary>
public interface IConfigStore
{
    ConfigLoadResult Load();

    void Save(PreferisConfig config, string configPath);
}

/// <summary>Finds installed browsers. <see cref="DetectBrowsers"/> is for the settings UI and fallbacks; the fast path uses <see cref="IBrowserLocator.TryResolve"/>.</summary>
public interface IBrowserDetector : IBrowserLocator
{
    IReadOnlyList<DetectedBrowser> DetectBrowsers();
}

/// <summary>Executes a <see cref="LaunchPlan"/>. Throws when the process cannot be started.</summary>
public interface IBrowserLauncher
{
    void Launch(LaunchPlan plan);
}

/// <summary>The OS default URL handler (<c>system</c> action).</summary>
public interface ISystemOpener
{
    /// <summary>True when the default handler for <paramref name="scheme"/> is Preferis itself, which would loop.</summary>
    bool IsSelfDefaultHandler(string scheme);

    void OpenWithSystemDefault(RoutingUrl url);
}

/// <summary>Shows the picker (a separate process, docs/Design.md §9.3). Returns null when it could not be shown or answered nonsense.</summary>
public interface IPickerLauncher
{
    PickerResponse? Show(PickerRequest request);
}

/// <summary>Last resort: tell the user, offering the URL for copying.</summary>
public interface IUserNotifier
{
    void ShowError(string title, string message, RoutingUrl? url);
}

public enum LogLevel
{
    Trace,
    Info,
    Warning,
    Error,
}

public interface IRoutingLog
{
    void Write(LogLevel level, string message);
}

/// <summary>Everything <see cref="RoutingService"/> depends on, supplied by the Router executable.</summary>
public sealed record RoutingServices(
    IConfigStore Config,
    HostPlatform HostPlatform,
    IBrowserDetector Browsers,
    IBrowserProfileEnumerator BrowserProfiles,
    IBrowserLauncher Launcher,
    ISystemOpener System,
    IPickerLauncher Picker,
    IUserNotifier Notifier,
    IRoutingLog Log,
    Func<string, string?> GetEnvironmentVariable,
    string HomeDirectory);

public enum HandleOutcome
{
    /// <summary>A browser (or command) was started.</summary>
    Launched,

    /// <summary>The URL was handed to the OS default handler.</summary>
    Delegated,

    /// <summary>Nothing was opened on purpose (none action, picker cancelled).</summary>
    Ignored,

    /// <summary>Nothing could be done; the user was notified.</summary>
    Failed,
}

/// <param name="ProfileId">The profile that was launched, or <c>browser:&lt;id&gt;</c> for a detected browser without a profile.</param>
public sealed record HandleResult(HandleOutcome Outcome, string? ProfileId, string Summary);
