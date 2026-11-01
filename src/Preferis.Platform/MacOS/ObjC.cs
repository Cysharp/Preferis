using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Preferis.Platform.MacOS;

/// <summary>
/// The small slice of the Objective-C runtime, Foundation and AppKit that Preferis needs, called directly
/// through P/Invoke so the Router carries no UI framework. Verified in spikes/UrlActivation.
/// objc_msgSend is a trampoline and must be called through a prototype matching the real method signature,
/// hence one import per shape.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

    private static readonly Lazy<bool> AppKitLoaded = new(() =>
    {
        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        return true;
    });

    /// <summary>Loads AppKit (and Foundation). Idempotent; costs about 15 ms the first time.</summary>
    public static void EnsureAppKit() => _ = AppKitLoaded.Value;

    // ---- runtime -------------------------------------------------------------------------------

    public static IntPtr GetClass(string name)
    {
        EnsureAppKit();
        var cls = objc_getClass(name);
        return cls != IntPtr.Zero ? cls : throw new InvalidOperationException($"Objective-C class not found: {name}");
    }

    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr AllocateClassPair(IntPtr superclass, string name) => objc_allocateClassPair(superclass, name, 0);

    public static void AddMethod(IntPtr cls, string selector, IntPtr implementation, string typeEncoding)
    {
        if (!class_addMethod(cls, Sel(selector), implementation, typeEncoding))
        {
            throw new InvalidOperationException($"class_addMethod failed for {selector}");
        }
    }

    public static void RegisterClassPair(IntPtr cls) => objc_registerClassPair(cls);

    public static IntPtr New(IntPtr cls) => Send(Send(cls, "alloc"), "init");

    /// <summary>Scope with its own autorelease pool, for code that runs without an AppKit run loop.</summary>
    public static AutoreleasePoolScope AutoreleasePool() => new(objc_autoreleasePoolPush());

    public readonly struct AutoreleasePoolScope(IntPtr pool) : IDisposable
    {
        public void Dispose() => objc_autoreleasePoolPop(pool);
    }

    // ---- messaging ------------------------------------------------------------------------------

    public static IntPtr Send(IntPtr receiver, string selector) => objc_msgSend(receiver, Sel(selector));
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg) => objc_msgSend(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2) => objc_msgSend(receiver, Sel(selector), arg1, arg2);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2, IntPtr arg3) => objc_msgSend(receiver, Sel(selector), arg1, arg2, arg3);
    public static IntPtr Send(IntPtr receiver, string selector, nuint arg) => objc_msgSend_nuint(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, nuint arg1, IntPtr arg2) => objc_msgSend_nuint(receiver, Sel(selector), arg1, arg2);
    public static IntPtr Send(IntPtr receiver, string selector, int arg) => objc_msgSend_int(receiver, Sel(selector), arg);
    public static nuint SendNUInt(IntPtr receiver, string selector) => objc_msgSend_ret_nuint(receiver, Sel(selector));
    public static nint SendNInt(IntPtr receiver, string selector) => objc_msgSend_ret_nint(receiver, Sel(selector));
    public static int SendInt(IntPtr receiver, string selector) => objc_msgSend_ret_int(receiver, Sel(selector));
    public static bool SendBool(IntPtr receiver, string selector, IntPtr arg) => objc_msgSend_ret_bool(receiver, Sel(selector), arg);
    public static bool SendBool(IntPtr receiver, string selector, nuint arg) => objc_msgSend_ret_bool_nuint(receiver, Sel(selector), arg);
    public static void SendVoid(IntPtr receiver, string selector) => objc_msgSend_void(receiver, Sel(selector));
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg) => objc_msgSend_void(receiver, Sel(selector), arg);
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2) => objc_msgSend_void(receiver, Sel(selector), arg1, arg2);
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2, IntPtr arg3) => objc_msgSend_void(receiver, Sel(selector), arg1, arg2, arg3);
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg1, IntPtr arg2, bool arg3) => objc_msgSend_void_bool(receiver, Sel(selector), arg1, arg2, arg3);

    // ---- strings, URLs, arrays -----------------------------------------------------------------

    public static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return objc_msgSend(GetClass("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static string? FromNSString(IntPtr nsString)
        => nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

    public static bool IsNSString(IntPtr obj) => obj != IntPtr.Zero && SendBool(obj, "isKindOfClass:", GetClass("NSString"));

    public static IntPtr NSUrlFromString(string url) => Send(GetClass("NSURL"), "URLWithString:", NSString(url));

    public static IntPtr NSUrlFromPath(string path) => Send(GetClass("NSURL"), "fileURLWithPath:", NSString(path));

    public static string? UrlPath(IntPtr nsUrl) => nsUrl == IntPtr.Zero ? null : FromNSString(Send(nsUrl, "path"));

    public static string? UrlAbsoluteString(IntPtr nsUrl) => nsUrl == IntPtr.Zero ? null : FromNSString(Send(nsUrl, "absoluteString"));

    public static IEnumerable<IntPtr> Enumerate(IntPtr nsArray)
    {
        if (nsArray == IntPtr.Zero)
        {
            yield break;
        }
        var count = SendNUInt(nsArray, "count");
        for (nuint i = 0; i < count; i++)
        {
            yield return Send(nsArray, "objectAtIndex:", i);
        }
    }

    // ---- NSWorkspace / NSBundle ----------------------------------------------------------------

    public static IntPtr SharedWorkspace => Send(GetClass("NSWorkspace"), "sharedWorkspace");

    public static string? ApplicationPathForBundleId(string bundleId)
        => UrlPath(Send(SharedWorkspace, "URLForApplicationWithBundleIdentifier:", NSString(bundleId)));

    public static string? DefaultApplicationPathForUrl(string url)
        => UrlPath(Send(SharedWorkspace, "URLForApplicationToOpenURL:", NSUrlFromString(url)));

    /// <summary>All applications claiming the URL's scheme (macOS 12+).</summary>
    public static List<string> ApplicationPathsForUrl(string url)
        => Enumerate(Send(SharedWorkspace, "URLsForApplicationsToOpenURL:", NSUrlFromString(url))).Select(UrlPath).OfType<string>().ToList();

    public static IntPtr BundleWithPath(string path) => Send(GetClass("NSBundle"), "bundleWithPath:", NSString(path));

    public static string? BundleIdentifier(IntPtr bundle) => bundle == IntPtr.Zero ? null : FromNSString(Send(bundle, "bundleIdentifier"));

    public static string? InfoString(IntPtr bundle, string key)
    {
        if (bundle == IntPtr.Zero)
        {
            return null;
        }
        var value = Send(bundle, "objectForInfoDictionaryKey:", NSString(key));
        return IsNSString(value) ? FromNSString(value) : null;
    }

    public static IntPtr MainBundle => Send(GetClass("NSBundle"), "mainBundle");

    public static string? MainBundlePath => FromNSString(Send(MainBundle, "bundlePath"));

    public static string? MainBundleIdentifier => BundleIdentifier(MainBundle);

    // ---- Apple Events ---------------------------------------------------------------------------

    // keySenderPIDAttr ('spid')
    private const int SenderProcessIdAttribute = 0x73706964;

    /// <summary>The process that sent the Apple Event being handled on this thread; null outside a handler, or when the event does not say.</summary>
    public static int? CurrentAppleEventSender()
    {
        var current = Send(Send(GetClass("NSAppleEventManager"), "sharedAppleEventManager"), "currentAppleEvent");
        var sender = current == IntPtr.Zero ? IntPtr.Zero : Send(current, "attributeDescriptorForKeyword:", SenderProcessIdAttribute);
        return sender == IntPtr.Zero ? null : SendInt(sender, "int32Value");
    }

    // ---- blocks ---------------------------------------------------------------------------------

    /// <summary>Builds a global (capture-less) Objective-C block around an <c>[UnmanagedCallersOnly]</c> function. Never freed.</summary>
    public static IntPtr CreateGlobalBlock(IntPtr invoke)
    {
        const int BlockIsGlobal = 1 << 28;
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteGlobalBlock");
        block->Flags = BlockIsGlobal;
        block->Invoke = invoke;
        block->Descriptor = (IntPtr)descriptor;
        return (IntPtr)block;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    // ---- Launch Services (C API) ---------------------------------------------------------------

    /// <summary>Tells Launch Services about a bundle (what `lsregister -f` does). <paramref name="nsUrl"/> is toll-free bridged to CFURLRef.</summary>
    public static int RegisterBundle(IntPtr nsUrl) => LSRegisterURL(nsUrl, true);

    /// <summary>Deprecated but functional fallback for setting the default handler of a scheme.</summary>
    public static int SetDefaultHandlerForScheme(string scheme, string bundleId)
        => LSSetDefaultHandlerForURLScheme(NSString(scheme), NSString(bundleId));

    [LibraryImport(CoreServices)]
    private static partial int LSRegisterURL(IntPtr url, [MarshalAs(UnmanagedType.I1)] bool update);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultHandlerForURLScheme(IntPtr scheme, IntPtr bundleId);

    // ---- imports --------------------------------------------------------------------------------

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [LibraryImport(LibObjC)]
    private static partial void objc_registerClassPair(IntPtr cls);

    [LibraryImport(LibObjC)]
    private static partial IntPtr objc_autoreleasePoolPush();

    [LibraryImport(LibObjC)]
    private static partial void objc_autoreleasePoolPop(IntPtr pool);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg1, IntPtr arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_int(IntPtr receiver, IntPtr selector, int arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nuint objc_msgSend_ret_nuint(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_ret_nint(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial int objc_msgSend_ret_int(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool objc_msgSend_ret_bool(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool objc_msgSend_ret_bool_nuint(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, [MarshalAs(UnmanagedType.I1)] bool arg3);
}
