using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Preferis.Platform.MacOS;

/// <summary>
/// Receives URLs from Launch Services. macOS delivers them as Apple Events to a running NSApplication,
/// so the Router runs a minimal application with a delegate implementing <c>application:openURLs:</c>.
/// Measured in spike 1: about 55 ms for NSApplication init plus 20 ms for delivery (docs/Platform.md §5).
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe class MacUrlActivationSource
{
    private static Action<IReadOnlyList<string>>? _onUrls;
    private static Action? _onLaunchedWithoutUrls;
    private static int _urlsReceived;
    private static IntPtr _app;

    /// <summary>True when Launch Services started this process, i.e. URLs will arrive as events.</summary>
    public static bool IsLaunchedByLaunchServices => IsLaunchedByLaunchServicesWith(Environment.GetEnvironmentVariable("__CFBundleIdentifier"));

    /// <summary>
    /// Launch Services sets <c>__CFBundleIdentifier</c> to the bundle it launches, but every child process inherits it:
    /// a shell in Terminal carries Terminal's id, the Router started by the settings app carries the settings app's.
    /// Only our own id means Launch Services started the Router.
    /// </summary>
    internal static bool IsLaunchedByLaunchServicesWith(string? cfBundleIdentifier)
        => string.Equals(cfBundleIdentifier, MacBrowserDetector.ProductBundleId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the application loop. Does not return; call <see cref="Terminate"/> to exit.</summary>
    /// <param name="onUrls">Invoked on the main thread for every batch of URLs. Should return quickly (hand off to a worker).</param>
    /// <param name="onLaunchedWithoutUrls">Invoked when no URL arrived shortly after launch (the user opened the app itself).</param>
    public static void Run(Action<IReadOnlyList<string>> onUrls, Action onLaunchedWithoutUrls)
    {
        _onUrls = onUrls;
        _onLaunchedWithoutUrls = onLaunchedWithoutUrls;
        ObjC.EnsureAppKit();

        var delegateClass = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "PreferisAppDelegate");
        ObjC.AddMethod(delegateClass, "application:openURLs:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnOpenUrls, "v@:@@");
        ObjC.AddMethod(delegateClass, "applicationDidFinishLaunching:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnDidFinishLaunching, "v@:@");
        ObjC.RegisterClassPair(delegateClass);

        _app = ObjC.Send(ObjC.GetClass("NSApplication"), "sharedApplication");
        ObjC.SendVoid(_app, "setDelegate:", ObjC.New(delegateClass));
        ObjC.SendVoid(_app, "run");
    }

    /// <summary>Ends the application loop from any thread.</summary>
    public static void Terminate()
    {
        if (_app == IntPtr.Zero)
        {
            Environment.Exit(0);
            return;
        }
        // terminate: must run on the main thread.
        ObjC.SendVoid(_app, "performSelectorOnMainThread:withObject:waitUntilDone:", ObjC.Sel("terminate:"), IntPtr.Zero, false);
    }

    [UnmanagedCallersOnly]
    private static void OnOpenUrls(IntPtr self, IntPtr cmd, IntPtr application, IntPtr urls)
    {
        try
        {
            var list = ObjC.Enumerate(urls).Select(ObjC.UrlAbsoluteString).OfType<string>().ToList();
            Interlocked.Add(ref _urlsReceived, list.Count);
            _onUrls?.Invoke(list);
        }
        catch (Exception)
        {
            // Never let an exception cross the native boundary; the caller's handler logs its own failures.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDidFinishLaunching(IntPtr self, IntPtr cmd, IntPtr notification)
    {
        // URLs normally arrive before this callback. If none shows up shortly, the app itself was opened.
        new Thread(() =>
        {
            Thread.Sleep(750);
            if (Volatile.Read(ref _urlsReceived) == 0)
            {
                _onLaunchedWithoutUrls?.Invoke();
            }
        })
        { IsBackground = true }.Start();
    }
}
