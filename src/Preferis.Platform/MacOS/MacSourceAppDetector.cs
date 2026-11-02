using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Preferis.Core.Routing;

namespace Preferis.Platform.MacOS;

/// <summary>
/// macOS: which application opened the URL, for the app conditions (docs/Design.md §6.2, docs/Platform.md §3.9).
/// The Apple Event that delivers the URL names the process that sent it. A program that is no app (open, a shell, a
/// command line tool) is looked past, through its parents, to the app it runs in. launchd starts apps and background jobs
/// alike, so the walk ends there.
/// </summary>
public static class MacSourceAppDetector
{
    // Nothing real runs a program deeper inside an app; the processes are read one by one and may change in between.
    private const int MaxHops = 8;

    // The parent of every app, and of the orphans of exited processes: nothing above it opened the URL.
    private const int Launchd = 1;

    /// <summary>The app that sent the URL, from the process the Apple Event names; null when it cannot be told.</summary>
    [SupportedOSPlatform("macos")]
    public static SourceApp? Detect(int senderProcessId) => Detect(senderProcessId, new MacProcesses());

    /// <summary>For a URL on the command line: the app the Router was started from; null when it cannot be told.</summary>
    [SupportedOSPlatform("macos")]
    public static SourceApp? DetectParent() => DetectParent(new MacProcesses());

    /// <summary>The program of a process, for the log; null when it has exited.</summary>
    [SupportedOSPlatform("macos")]
    public static string? ExecutablePath(int processId) => new MacProcesses().Find(processId)?.Path;

    // An event the Router sent itself says nothing about who asked for the URL.
    internal static SourceApp? Detect(int senderProcessId, IMacProcesses processes)
        => senderProcessId == processes.CurrentProcessId ? null : Walk(processes, processes.Find(senderProcessId));

    internal static SourceApp? DetectParent(IMacProcesses processes)
        => processes.Find(processes.CurrentProcessId) is { } router ? Walk(processes, ParentOf(processes, router)) : null;

    private static SourceApp? Walk(IMacProcesses processes, MacProcessFacts? process)
    {
        for (var hops = 0; process is not null && hops <= MaxHops; hops++)
        {
            if (process.Path is { } path && AppOf(path) is { } app)
            {
                return new SourceApp(processes.BundleIdentifier(app), app);
            }
            process = ParentOf(processes, process);
        }
        return null;
    }

    // Unlike on Windows, a parent id is never stale: launchd takes the children of a process that exits. So no start times.
    private static MacProcessFacts? ParentOf(IMacProcesses processes, MacProcessFacts child)
        => child.ParentId > Launchd ? processes.Find(child.ParentId) : null;

    /// <summary>
    /// The app whose own program <paramref name="executablePath"/> is (<c>X.app/Contents/MacOS/x</c>); null for other
    /// programs (command line tools, XPC services), even inside an app. A helper in the frameworks of an app (Electron's,
    /// Chromium's) counts as that app; an app elsewhere inside another (Contents/Helpers, Contents/Applications) is an app of its own.
    /// </summary>
    internal static string? AppOf(string executablePath)
    {
        var parts = executablePath.Split('/');
        if (parts.Length < 4 || !IsBundle(parts[^4]) || !IsNamed(parts[^3], "Contents") || !IsNamed(parts[^2], "MacOS"))
        {
            return null;
        }
        for (var i = 0; i < parts.Length - 4; i++)
        {
            if (IsBundle(parts[i]) && IsNamed(parts[i + 1], "Contents") && IsNamed(parts[i + 2], "Frameworks"))
            {
                return string.Join('/', parts[..(i + 1)]);
            }
        }
        return string.Join('/', parts[..^3]);
    }

    // The default file system ignores case.
    private static bool IsBundle(string name) => name.Length > ".app".Length && name.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(string name, string expected) => name.Equals(expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A process as the detection of the source app sees it.</summary>
/// <param name="ParentId">1 (launchd) for an app, and for a process whose parent has exited.</param>
/// <param name="Path">The program; null when it is gone (a process that has exited and is not yet reaped).</param>
internal sealed record MacProcessFacts(int Id, int ParentId, string? Path);

/// <summary>What the detection reads from macOS, so that the tests can make up processes.</summary>
internal interface IMacProcesses
{
    int CurrentProcessId { get; }

    /// <summary>Null when no process has that id (any more).</summary>
    MacProcessFacts? Find(int processId);

    /// <summary>The CFBundleIdentifier of the bundle; null when it has none.</summary>
    string? BundleIdentifier(string bundlePath);
}

[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacProcesses : IMacProcesses
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    // PROC_PIDT_SHORTBSDINFO. PROC_PIDTBSDINFO is refused for processes of other users, such as the login that Terminal
    // runs as root between itself and the shell.
    private const int ShortBsdInfoFlavor = 13;

    // PROC_PIDPATHINFO_MAXSIZE
    private const int LongestPath = 4096;

    public int CurrentProcessId => Environment.ProcessId;

    public MacProcessFacts? Find(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }
        BsdShortInfo info;
        // 1 asks for zombies too: a program that has exited still names its parent until the parent reaps it.
        if (proc_pidinfo(processId, ShortBsdInfoFlavor, 1, &info, sizeof(BsdShortInfo)) != sizeof(BsdShortInfo))
        {
            return null;
        }
        var buffer = stackalloc byte[LongestPath];
        var length = proc_pidpath(processId, buffer, LongestPath);
        return new MacProcessFacts(processId, (int)info.ParentId, length > 0 ? Encoding.UTF8.GetString(buffer, length) : null);
    }

    public string? BundleIdentifier(string bundlePath)
    {
        using var pool = ObjC.AutoreleasePool();
        return ObjC.BundleIdentifier(ObjC.BundleWithPath(bundlePath));
    }

    // struct proc_bsdshortinfo: pid, ppid, pgid, status, comm[16], flags, uid, gid, ruid, rgid, svuid, svgid, rfu.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct BsdShortInfo
    {
        [FieldOffset(4)]
        public uint ParentId;
    }

    [LibraryImport(LibSystem)]
    private static partial int proc_pidinfo(int pid, int flavor, ulong arg, void* buffer, int bufferSize);

    [LibraryImport(LibSystem)]
    private static partial int proc_pidpath(int pid, byte* buffer, uint bufferSize);
}
