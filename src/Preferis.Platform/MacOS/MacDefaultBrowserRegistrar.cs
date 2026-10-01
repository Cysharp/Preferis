using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Preferis.Core.Hosting;

namespace Preferis.Platform.MacOS;

/// <summary>Registers <c>Preferis.app</c> with Launch Services and asks macOS to make it the default browser. See docs/Platform.md §3.2.</summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacDefaultBrowserRegistrar : IDefaultBrowserRegistrar
{
    private const string LsRegister = "/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister";
    private static readonly string[] Schemes = ["http", "https"];

    // State for the completion block of setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:.
    // Registration runs one scheme at a time, so a single static slot is enough.
    private static readonly ManualResetEventSlim Completed = new(false);
    private static string? _completionError;

    private readonly string? _appPath;

    /// <param name="appPath">The <c>Preferis.app</c> bundle; null when not running from one (e.g. <c>dotnet run</c>).</param>
    public MacDefaultBrowserRegistrar(string? appPath)
    {
        _appPath = appPath is not null && appPath.EndsWith(".app", StringComparison.Ordinal) && Directory.Exists(appPath) ? appPath : null;
    }

    /// <summary>For the Router, whose own bundle is <c>Preferis.app</c>.</summary>
    public static MacDefaultBrowserRegistrar ForMainBundle()
    {
        using var pool = ObjC.AutoreleasePool();
        return new MacDefaultBrowserRegistrar(ObjC.MainBundlePath);
    }

    public string? AppPath => _appPath;

    public RegistrationStatus GetStatus()
    {
        using var pool = ObjC.AutoreleasePool();
        var selfPath = _appPath;
        var selfId = selfPath is null ? null : ObjC.BundleIdentifier(ObjC.BundleWithPath(selfPath));
        if (selfPath is null)
        {
            return new RegistrationStatus(false, false, null);
        }

        var isDefault = true;
        string? current = null;
        foreach (var scheme in Schemes)
        {
            var handler = ObjC.DefaultApplicationPathForUrl($"{scheme}://example.com/");
            var handlerId = handler is null ? null : ObjC.BundleIdentifier(ObjC.BundleWithPath(handler));
            current ??= handler;
            isDefault &= handlerId is not null && string.Equals(handlerId, selfId, StringComparison.OrdinalIgnoreCase);
        }

        var registered = selfId is not null && ObjC.ApplicationPathsForUrl("https://example.com/").Contains(selfPath, StringComparer.Ordinal);
        return new RegistrationStatus(registered, isDefault, current);
    }

    public RegistrationResult Register()
    {
        using var pool = ObjC.AutoreleasePool();
        var selfPath = _appPath;
        var selfId = selfPath is null ? null : ObjC.BundleIdentifier(ObjC.BundleWithPath(selfPath));
        if (selfPath is null || selfId is null)
        {
            return new RegistrationResult(false, "Registration must run from inside Preferis.app (see docs/Design.md §4.4).");
        }

        var bundleUrl = ObjC.NSUrlFromPath(selfPath);
        var status = ObjC.RegisterBundle(bundleUrl);
        if (status != 0)
        {
            return new RegistrationResult(false, $"LSRegisterURL failed with status {status}.");
        }

        var failure = DefaultHandlerRequest.Run(
            Schemes,
            scheme => IsDefaultHandler(scheme, selfId),
            scheme => RequestDefault(bundleUrl, scheme),
            scheme => ObjC.SetDefaultHandlerForScheme(scheme, selfId));
        return failure is null
            ? new RegistrationResult(true, "Preferis is the default browser.")
            : new RegistrationResult(false, failure);
    }

    public RegistrationResult Unregister()
    {
        var selfPath = _appPath;
        if (selfPath is null)
        {
            return new RegistrationResult(false, "Unregistration must run from inside Preferis.app.");
        }

        // There is no public API to forget a bundle; lsregister is the supported tool.
        var info = new ProcessStartInfo(LsRegister) { UseShellExecute = false };
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(selfPath);
        using var process = Process.Start(info);
        process?.WaitForExit(10_000);
        return process?.ExitCode == 0
            ? new RegistrationResult(true, "Preferis was removed from Launch Services. Pick another default browser in System Settings.")
            : new RegistrationResult(false, "lsregister -u failed.");
    }

    public void OpenSystemSettings()
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        info.ArgumentList.Add("x-apple.systempreferences:com.apple.Desktop-Settings.extension");
        using var process = Process.Start(info);
    }

    private static bool IsDefaultHandler(string scheme, string selfId)
    {
        using var pool = ObjC.AutoreleasePool();
        var handler = ObjC.DefaultApplicationPathForUrl($"{scheme}://example.com/");
        return handler is not null && string.Equals(ObjC.BundleIdentifier(ObjC.BundleWithPath(handler)), selfId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The macOS 12 API; the system shows a confirmation dialog. Returns the completion error, or null.</summary>
    private static string? RequestDefault(IntPtr bundleUrl, string scheme)
    {
        Completed.Reset();
        _completionError = null;

        var block = ObjC.CreateGlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnCompleted);
        ObjC.SendVoid(ObjC.SharedWorkspace, "setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:", bundleUrl, ObjC.NSString(scheme), block);

        // Give the user time to answer the confirmation dialog.
        return Completed.Wait(TimeSpan.FromMinutes(2)) ? _completionError : "timed out waiting for the system.";
    }

    [UnmanagedCallersOnly]
    private static void OnCompleted(IntPtr block, IntPtr error)
    {
        _completionError = error == IntPtr.Zero ? null : ObjC.FromNSString(ObjC.Send(error, "localizedDescription")) ?? "unknown error";
        Completed.Set();
    }
}
