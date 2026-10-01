using System.Diagnostics;
using System.Runtime.Versioning;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Platform.Browsers;
using Preferis.Platform.MacOS;

namespace Preferis.Settings.Services;

/// <summary>
/// The settings UI's view of macOS. It runs as <c>Preferis.app/Contents/Helpers/Preferis Settings.app</c>
/// (docs/Platform.md §3.1), so the Router — which registration must point at — is found relative to it.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacSettingsPlatform : ISettingsPlatform
{
    private readonly MacBrowserDetector _detector;
    private readonly BrowserProfileEnumerator _profiles = new();
    private readonly MacDefaultBrowserRegistrar _registrar;

    public MacSettingsPlatform(LocalConfig local)
    {
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _detector = new MacBrowserDetector(local.BrowserOverrides, HomeDirectory);
        _registrar = new MacDefaultBrowserRegistrar(FindPreferisApp());
    }

    public HostPlatform Platform => HostPlatform.MacOS;

    public IBrowserLocator BrowserLocator => _detector;

    public IBrowserProfileEnumerator BrowserProfiles => _profiles;

    public Func<string, string?> GetEnvironmentVariable => Environment.GetEnvironmentVariable;

    public string HomeDirectory { get; }

    public bool CanRegister => RouterPath is not null;

    public bool CanOpenUrls => RouterPath is not null;

    private string? RouterPath => _registrar.AppPath is { } app && File.Exists(Path.Combine(app, "Contents", "MacOS", "Preferis")) ? Path.Combine(app, "Contents", "MacOS", "Preferis") : null;

    public IReadOnlyList<DetectedBrowserInfo> DetectBrowsers()
        => _detector.DetectBrowsers()
            .Select(b => new DetectedBrowserInfo(b, _profiles.Enumerate(b), b.ApplicationPath is { } app ? MacApplicationIcons.GetPng(app, 64) : null))
            .ToList();

    public RegistrationStatus GetRegistrationStatus() => _registrar.GetStatus();

    public async Task<RegistrationResult> RegisterAsync()
    {
        if (RouterPath is not { } router)
        {
            return new RegistrationResult(false, "Preferis.app was not found next to Preferis Settings.");
        }

        // The Router registers itself so that Launch Services records the bundle URLs are delivered to.
        var info = new ProcessStartInfo(router) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--register");
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("The Router could not be started.");
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var message = string.Join(" ", new[] { output.Trim(), error.Trim() }.Where(s => s.Length > 0));
            return new RegistrationResult(process.ExitCode == 0, message);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new RegistrationResult(false, $"Could not run Preferis --register: {ex.Message}");
        }
    }

    public void OpenDefaultBrowserSettings() => _registrar.OpenSystemSettings();

    public async Task<RouterRunResult> OpenUrlAsync(string url)
    {
        if (RouterPath is not { } router)
        {
            return new RouterRunResult(2, ["Preferis.app was not found next to Preferis Settings."]);
        }

        var info = new ProcessStartInfo(router) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(url);
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("The Router could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var lines = (await output.ConfigureAwait(false) + "\n" + errors)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new RouterRunResult(process.ExitCode, lines);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new RouterRunResult(2, [$"Could not run Preferis: {ex.Message}"]);
        }
    }

    public void Reveal(string path)
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        if (File.Exists(path))
        {
            info.ArgumentList.Add("-R");
        }
        info.ArgumentList.Add(path);
        using var process = Process.Start(info);
    }

    // Contents/Helpers/Preferis Settings.app/Contents/MacOS/ -> the enclosing Preferis.app, when there is one.
    private static string? FindPreferisApp()
    {
        var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return candidate.EndsWith(".app", StringComparison.Ordinal) && Directory.Exists(candidate) ? candidate : null;
    }
}
