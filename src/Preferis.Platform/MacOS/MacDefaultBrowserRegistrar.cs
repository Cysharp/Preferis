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

    public RegistrationStatus GetStatus()
    {
        using var pool = ObjC.AutoreleasePool();
        var selfPath = ObjC.MainBundlePath;
        var selfId = ObjC.MainBundleIdentifier;
        if (selfPath is null || !selfPath.EndsWith(".app", StringComparison.Ordinal))
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
        var selfPath = ObjC.MainBundlePath;
        var selfId = ObjC.MainBundleIdentifier;
        if (selfPath is null || selfId is null || !selfPath.EndsWith(".app", StringComparison.Ordinal))
        {
            return new RegistrationResult(false, "Registration must run from inside Preferis.app (see docs/Design.md §4.4).");
        }

        var bundleUrl = ObjC.NSUrlFromPath(selfPath);
        var status = ObjC.RegisterBundle(bundleUrl);
        if (status != 0)
        {
            return new RegistrationResult(false, $"LSRegisterURL failed with status {status}.");
        }

        foreach (var scheme in Schemes)
        {
            var error = SetDefault(bundleUrl, scheme, selfId);
            if (error is not null)
            {
                return new RegistrationResult(false, $"Could not become the default handler for {scheme}: {error}");
            }
        }

        return new RegistrationResult(true, "Preferis is registered. macOS may have asked you to confirm the default browser change.");
    }

    public RegistrationResult Unregister()
    {
        var selfPath = ObjC.MainBundlePath;
        if (selfPath is null || !selfPath.EndsWith(".app", StringComparison.Ordinal))
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

    /// <summary>Uses the macOS 12 API with a completion block; falls back to the deprecated LS call when it fails.</summary>
    private static string? SetDefault(IntPtr bundleUrl, string scheme, string selfId)
    {
        Completed.Reset();
        _completionError = null;

        var block = ObjC.CreateGlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnCompleted);
        ObjC.SendVoid(ObjC.SharedWorkspace, "setDefaultApplicationAtURL:toOpenURLsWithScheme:completionHandler:", bundleUrl, ObjC.NSString(scheme), block);

        // The system shows a confirmation dialog; give the user time to answer it.
        if (!Completed.Wait(TimeSpan.FromMinutes(2)))
        {
            return "timed out waiting for the system.";
        }
        if (_completionError is null)
        {
            return null;
        }

        var status = ObjC.SetDefaultHandlerForScheme(scheme, selfId);
        return status == 0 ? null : $"{_completionError} (fallback LSSetDefaultHandlerForURLScheme returned {status})";
    }

    [UnmanagedCallersOnly]
    private static void OnCompleted(IntPtr block, IntPtr error)
    {
        _completionError = error == IntPtr.Zero ? null : ObjC.FromNSString(ObjC.Send(error, "localizedDescription")) ?? "unknown error";
        Completed.Set();
    }
}
