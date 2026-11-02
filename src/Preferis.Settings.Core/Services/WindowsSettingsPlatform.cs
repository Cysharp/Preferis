using System.Diagnostics;
using System.Runtime.Versioning;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Platform.Browsers;
using Preferis.Platform.Windows;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Services;

/// <summary>
/// The settings UI's view of Windows. <c>Preferis.Settings.exe</c> sits next to <c>Preferis.exe</c> (docs/Platform.md §2.1),
/// which registration must point at and which opens URLs for the test page.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSettingsPlatform : ISettingsPlatform
{
    // Twice the 32 px the settings show, for 200 % displays.
    private const int IconPixels = 64;

    private readonly WindowsBrowserDetector _detector;
    private readonly BrowserProfileEnumerator _profiles = new();
    private readonly WindowsDefaultBrowserRegistrar _registrar;

    /// <param name="updater">The Velopack updates of an installation (docs/Platform.md §2.1); null outside one.</param>
    public WindowsSettingsPlatform(LocalConfig local, IAppUpdater? updater = null)
    {
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _detector = new WindowsBrowserDetector(local.BrowserOverrides);
        _registrar = new WindowsDefaultBrowserRegistrar(RouterPath ?? Path.Combine(AppContext.BaseDirectory, "Preferis.exe"));
        Updater = updater;
    }

    public HostPlatform Platform => HostPlatform.Windows;

    public IAppUpdater? Updater { get; }

    public IBrowserLocator BrowserLocator => _detector;

    public IBrowserProfileEnumerator BrowserProfiles => _profiles;

    public Func<string, string?> GetEnvironmentVariable => Environment.GetEnvironmentVariable;

    public string HomeDirectory { get; }

    public bool CanRegister => RouterPath is not null;

    public bool CanOpenUrls => RouterPath is not null;

    private static string? RouterPath => Path.Combine(AppContext.BaseDirectory, "Preferis.exe") is var path && File.Exists(path) ? path : null;

    public IReadOnlyList<DetectedBrowserInfo> DetectBrowsers()
        => _detector.DetectBrowsers()
            .Select(b => new DetectedBrowserInfo(b, _profiles.Enumerate(b), WindowsApplicationIcons.GetPng(b.ExecutablePath, IconPixels)))
            .ToList();

    public RegistrationStatus GetRegistrationStatus() => _registrar.GetStatus();

    /// <summary>The Router registers itself, so that the registry points at it rather than at the settings.</summary>
    public async Task<RegistrationResult> RegisterAsync()
    {
        var result = await RunRouterAsync("--register").ConfigureAwait(false);
        return new RegistrationResult(result.ExitCode == 0, string.Join(" ", result.Output));
    }

    public void OpenDefaultBrowserSettings() => _registrar.OpenSystemSettings();

    // Nothing on Windows denies reading a browser's profile data (docs/Platform.md §2.4).
    public void OpenFullDiskAccessSettings() => throw new PlatformNotSupportedException();

    public Task<RouterRunResult> OpenUrlAsync(string url) => RunRouterAsync(url);

    public void Reveal(string path)
    {
        // Explorer reads its own command line: /select wants the path quoted after the comma, not the whole argument.
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        using var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
    }

    private static Task<RouterRunResult> RunRouterAsync(string argument)
        => RouterPath is { } router
            ? RouterProcess.RunAsync(router, argument)
            : Task.FromResult(new RouterRunResult(2, [L("Preferis.exe was not found next to Preferis.Settings.exe.")]));
}
