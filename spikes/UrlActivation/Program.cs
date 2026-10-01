using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Preferis.Spikes.UrlActivation;

// Spike 1 (Design.md §12): receive URLs delivered by Launch Services as Apple Events
// (application:openURLs:) from a NativeAOT process that uses no UI framework at all.
// Only the Objective-C runtime is called, directly through P/Invoke.
//
// Measured: process start -> Main entered -> applicationDidFinishLaunching -> URL received.
// The log is appended to ~/Library/Logs/PreferisSpike/urlactivation.log because a process
// launched by Launch Services has no useful stdout.
internal static unsafe class Program
{
    private static readonly DateTime MainEntered = DateTime.UtcNow;
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly StringBuilder Log = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "PreferisSpike", "urlactivation.log");

    private static IntPtr _app;
    private static string _mode = "nsapp";

    private static int Main(string[] args)
    {
        Record("main-entered", MainEntered);
        if (args.Length > 0)
        {
            Log.AppendLine($"  argv: {string.Join(' ', args)}");
        }

        // Mode switch for the spike (environment variables do not survive `open`): a marker file.
        var modePath = Path.Combine(Path.GetDirectoryName(LogPath)!, "mode");
        var mode = _mode = File.Exists(modePath) ? File.ReadAllText(modePath).Trim() : "nsapp";
        Log.AppendLine($"  mode: {mode}");

        // Watchdog: a spike must never linger. If nothing arrives, log and exit.
        new Thread(() =>
        {
            Thread.Sleep(8000);
            Log.AppendLine("  watchdog: not finished within 8 s");
            Flush();
            Environment.Exit(2);
        })
        { IsBackground = true }.Start();

        if (mode == "ae")
        {
            RunWithAppleEventManagerOnly();
        }
        else
        {
            RunWithNSApplication();
        }
        return 0;
    }

    // Variant A: full NSApplication with an application delegate (what a normal app does).
    private static void RunWithNSApplication()
    {
        ObjC.LoadAppKit();
        Record("appkit-loaded", DateTime.UtcNow);

        // Build the delegate class at runtime: NSObject subclass with the two methods we care about.
        var delegateClass = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "PreferisSpikeAppDelegate");
        ObjC.AddMethod(delegateClass, "applicationDidFinishLaunching:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnDidFinishLaunching, "v@:@");
        ObjC.AddMethod(delegateClass, "application:openURLs:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnOpenUrls, "v@:@@");
        ObjC.RegisterClassPair(delegateClass);
        var appDelegate = ObjC.Send(ObjC.Send(delegateClass, "alloc"), "init");
        Record("delegate-created", DateTime.UtcNow);

        _app = ObjC.Send(ObjC.GetClass("NSApplication"), "sharedApplication");
        Record("shared-application", DateTime.UtcNow);
        ObjC.SendVoid(_app, "setDelegate:", appDelegate);
        Record("before-run", DateTime.UtcNow);

        ObjC.SendVoid(_app, "run"); // Does not return; terminate: exits the process.
    }

    // Variant B: no NSApplication at all. Foundation's NSAppleEventManager + the main CFRunLoop.
    // Tests whether Launch Services delivers the GURL Apple Event to a process that never
    // connects to the window server.
    private static void RunWithAppleEventManagerOnly()
    {
        ObjC.LoadFoundation();
        Record("foundation-loaded", DateTime.UtcNow);

        var handlerClass = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "PreferisSpikeUrlHandler");
        ObjC.AddMethod(handlerClass, "handleGetURLEvent:withReplyEvent:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnGetUrlEvent, "v@:@@");
        ObjC.RegisterClassPair(handlerClass);
        var handler = ObjC.Send(ObjC.Send(handlerClass, "alloc"), "init");

        var manager = ObjC.Send(ObjC.GetClass("NSAppleEventManager"), "sharedAppleEventManager");
        const uint GURL = 0x4755524C; // 'GURL' — kInternetEventClass == kAEGetURL
        ObjC.SendVoid(manager, "setEventHandler:andSelector:forEventClass:andEventID:", handler, ObjC.Sel("handleGetURLEvent:withReplyEvent:"), GURL, GURL);
        Record("before-runloop", DateTime.UtcNow);

        ObjC.CFRunLoopRun(); // Returns only if stopped; we exit from the handler.
    }

    [UnmanagedCallersOnly]
    private static void OnGetUrlEvent(IntPtr self, IntPtr cmd, IntPtr evt, IntPtr reply)
    {
        try
        {
            var received = DateTime.UtcNow;
            const uint keyDirectObject = 0x2D2D2D2D; // '----'
            var param = ObjC.Send(evt, "paramDescriptorForKeyword:", keyDirectObject);
            var str = ObjC.Send(param, "stringValue");
            var utf8 = ObjC.Send(str, "UTF8String");
            Record("url-received", received);
            Log.AppendLine($"  urls: {Marshal.PtrToStringUTF8(utf8) ?? "<null>"}");
            Flush();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.AppendLine($"  error: {ex}");
            Flush();
            Environment.Exit(1);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDidFinishLaunching(IntPtr self, IntPtr cmd, IntPtr notification)
    {
        Record("did-finish-launching", DateTime.UtcNow);
    }

    [UnmanagedCallersOnly]
    private static void OnOpenUrls(IntPtr self, IntPtr cmd, IntPtr application, IntPtr urls)
    {
        try
        {
            var received = DateTime.UtcNow;
            var count = ObjC.SendNUInt(urls, "count");
            var list = new List<string>();
            for (nuint i = 0; i < count; i++)
            {
                var url = ObjC.Send(urls, "objectAtIndex:", i);
                var str = ObjC.Send(url, "absoluteString");
                var utf8 = ObjC.Send(str, "UTF8String");
                list.Add(Marshal.PtrToStringUTF8(utf8) ?? "<null>");
            }
            Record("url-received", received);
            Log.AppendLine($"  urls: {string.Join(' ', list)}");
            if (_mode.StartsWith("pick", StringComparison.Ordinal))
            {
                // Spike 2: hand the URL to the picker helper in a child process and wait for its answer
                // off the main thread, so the run loop keeps serving events meanwhile.
                var url = list.FirstOrDefault() ?? "";
                new Thread(() => RunPicker(url)) { IsBackground = true }.Start();
                return;
            }
            Flush();
            ObjC.SendVoid(_app, "terminate:", IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.AppendLine($"  error: {ex}");
            Flush();
            Environment.Exit(1);
        }
    }

    // Spawns artifacts/spikes/PickerHelper.app (sibling of our own bundle), writes a request to its
    // stdin, reads the response from its stdout and the helper's own timing log from stderr.
    // Mode "pick-assist" additionally activates the helper from here via NSRunningApplication,
    // to see whether the parent must help the child come to the foreground.
    private static void RunPicker(string url)
    {
        try
        {
            var helper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "PickerHelper.app", "Contents", "MacOS", "PickerHelper"));
            var request = "{\"url\":\"" + url + "\",\"candidates\":[{\"profileId\":\"edge\",\"name\":\"Microsoft Edge\"},{\"profileId\":\"chrome-work\",\"name\":\"Chrome (Work)\"}],\"autoSelectSeconds\":1.5}";
            var psi = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var spawnedAt = DateTime.UtcNow;
            using var process = Process.Start(psi)!;
            Record("picker-spawned", spawnedAt);
            process.StandardInput.Write(request);
            process.StandardInput.Close();

            if (_mode == "pick-assist")
            {
                Thread.Sleep(400); // Give the helper time to create its window first.
                var running = ObjC.Send(ObjC.GetClass("NSRunningApplication"), "runningApplicationWithProcessIdentifier:", (int)process.Id);
                const nuint NSApplicationActivateIgnoringOtherApps = 1 << 1;
                var ok = ObjC.SendBool(running, "activateWithOptions:", NSApplicationActivateIgnoringOtherApps);
                Log.AppendLine($"  router activateWithOptions: => {ok}");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            var response = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Record("picker-responded", DateTime.UtcNow);
            Log.AppendLine($"  picker exit code: {process.ExitCode}");
            Log.AppendLine($"  picker response: {response.Trim()}");
            foreach (var line in stderrTask.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Log.AppendLine($"  {line}");
            }
            Flush();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.AppendLine($"  picker error: {ex}");
            Flush();
            Environment.Exit(1);
        }
    }

    private static void Record(string phase, DateTime at)
    {
        Log.AppendLine($"{phase,-22} {at:HH:mm:ss.fff}  +{(at - ProcessStart).TotalMilliseconds,7:F1} ms from process start");
    }

    private static void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.AppendAllText(LogPath, Log.ToString() + "----\n");
        Log.Clear();
    }
}

// Minimal Objective-C runtime binding. Only what this spike needs.
internal static unsafe partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    public static void LoadAppKit()
    {
        // Loading the framework registers its classes (NSApplication, ...) with the runtime.
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    }

    public static void LoadFoundation()
    {
        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
    }

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRunLoopRun();

    public static IntPtr GetClass(string name)
    {
        var cls = objc_getClass(name);
        return cls != IntPtr.Zero ? cls : throw new InvalidOperationException($"Objective-C class not found: {name}");
    }

    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr AllocateClassPair(IntPtr superclass, string name) => objc_allocateClassPair(superclass, name, 0);

    public static void AddMethod(IntPtr cls, string selector, IntPtr imp, string typeEncoding)
    {
        if (!class_addMethod(cls, Sel(selector), imp, typeEncoding))
        {
            throw new InvalidOperationException($"class_addMethod failed: {selector}");
        }
    }

    public static void RegisterClassPair(IntPtr cls) => objc_registerClassPair(cls);

    public static IntPtr Send(IntPtr receiver, string selector) => objc_msgSend(receiver, Sel(selector));
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg) => objc_msgSend(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, nuint arg) => objc_msgSend_nuint_arg(receiver, Sel(selector), arg);
    public static nuint SendNUInt(IntPtr receiver, string selector) => objc_msgSend_ret_nuint(receiver, Sel(selector));
    public static void SendVoid(IntPtr receiver, string selector) => objc_msgSend_void(receiver, Sel(selector));
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg) => objc_msgSend_void(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, uint arg) => objc_msgSend_uint_arg(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, int arg) => objc_msgSend_int_arg(receiver, Sel(selector), arg);
    public static bool SendBool(IntPtr receiver, string selector, nuint arg) => objc_msgSend_ret_bool_nuint_arg(receiver, Sel(selector), arg);
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2, uint arg3, uint arg4)
        => objc_msgSend_void(receiver, Sel(selector), arg1, arg2, arg3, arg4);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);

    [LibraryImport(LibObjC)]
    private static partial void objc_registerClassPair(IntPtr cls);

    // objc_msgSend is a trampoline; it must be called through a prototype matching the actual method.
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_nuint_arg(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nuint objc_msgSend_ret_nuint(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_uint_arg(IntPtr receiver, IntPtr selector, uint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_int_arg(IntPtr receiver, IntPtr selector, int arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool objc_msgSend_ret_bool_nuint_arg(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, uint arg3, uint arg4);
}
