using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Preferis.Core.Routing;

namespace Preferis.Platform.Windows;

/// <summary>
/// Windows: which application opened the URL, for the app conditions (docs/Design.md §6.2, docs/Platform.md §2.9).
/// The shell runs the registered command inside the program that asked it to open the URL, so that program is the
/// Router's parent; programs that only pass the URL on (cmd, rundll32) are looked past. When a broker started the Router
/// (sihost.exe for UWP apps, explorer.exe), the parent says nothing about the caller, and the window that was in front
/// when the Router started is asked instead: the link was clicked in it.
/// </summary>
public sealed class WindowsSourceAppDetector
{
    // Started the Router for someone else; the chain is followed past them to the program the link was clicked in.
    private static readonly string[] Intermediaries = ["cmd.exe", "conhost.exe", "rundll32.exe", "OpenWith.exe"];

    // Long-running processes that start programs on behalf of others: as the parent they say nothing about the caller.
    private static readonly string[] Brokers =
        ["explorer.exe", "svchost.exe", "sihost.exe", "RuntimeBroker.exe", "ApplicationFrameHost.exe", "services.exe"];

    // Owns the frame of every UWP app's window.
    private const string FrameHost = "ApplicationFrameHost.exe";

    // Nothing real passes a URL on through more programs than this; process ids are reused, and a chain may loop.
    private const int MaxIntermediaries = 8;

    private readonly nint _foregroundWindow;

    private WindowsSourceAppDetector(nint foregroundWindow)
    {
        _foregroundWindow = foregroundWindow;
    }

    /// <summary>Notes the window in front. Call it first thing: once the Router runs, another window may come to the front.</summary>
    [SupportedOSPlatform("windows")]
    public static WindowsSourceAppDetector CaptureForeground() => new(Native.GetForegroundWindow());

    /// <summary>The app that opened the URL, or null when it cannot be told.</summary>
    [SupportedOSPlatform("windows")]
    public SourceApp? Detect() => Detect(new WindowsProcesses(_foregroundWindow));

    internal static SourceApp? Detect(IProcesses processes)
    {
        var child = processes.Find(processes.CurrentProcessId);
        for (var hops = 0; child is not null && hops <= MaxIntermediaries; hops++)
        {
            // Started after its child, it is not the parent: the parent exited and another process got its id. Without the
            // parent (exited, or a service that cannot be opened) the window in front is no clue either, as whatever
            // started the Router may have run in the background.
            if (processes.Find(child.ParentId) is not { } parent || parent.StartTime > child.StartTime)
            {
                return null;
            }
            if (!IsIntermediary(parent.Name))
            {
                return IsBroker(parent.Name) ? InFront(processes) : From(parent);
            }
            child = parent;
        }
        return null;
    }

    internal static bool IsIntermediary(string name) => Intermediaries.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static bool IsBroker(string name) => Brokers.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static SourceApp? InFront(IProcesses processes)
    {
        if (processes.ForegroundProcessId is not { } id || processes.Find(id) is not { } owner)
        {
            return null;
        }
        if (!owner.Name.Equals(FrameHost, StringComparison.OrdinalIgnoreCase))
        {
            return From(owner);
        }
        // The app's own window is a child of the frame.
        var hosted = processes.ForegroundChildProcessIds().Where(child => child != owner.Id).Select(processes.Find).OfType<ProcessFacts>().FirstOrDefault();
        return hosted is null ? null : From(hosted);
    }

    private static SourceApp From(ProcessFacts process) => new(process.AppUserModelId, process.Path);
}

/// <summary>A process as the detection of the source app sees it.</summary>
/// <param name="Name">The executable's file name.</param>
/// <param name="StartTime">In UTC; null when Windows does not tell.</param>
/// <param name="AppUserModelId">The application user model id of a packaged app; null otherwise.</param>
internal sealed record ProcessFacts(int Id, int ParentId, string Name, string Path, DateTime? StartTime, string? AppUserModelId);

/// <summary>What the detection reads from Windows, so that the tests can make up processes and windows.</summary>
internal interface IProcesses
{
    int CurrentProcessId { get; }

    /// <summary>Null when no process has that id (any more), or when it cannot be opened (another user's, a service).</summary>
    ProcessFacts? Find(int processId);

    /// <summary>The process of the window that was in front when the Router started; null when there was none.</summary>
    int? ForegroundProcessId { get; }

    /// <summary>The processes of the child windows of that window, in window order.</summary>
    IEnumerable<int> ForegroundChildProcessIds();
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsProcesses(nint foregroundWindow) : IProcesses
{
    // MAX_PATH; longer paths are possible (longPathAware) and get a second try.
    private const int UsualPathLength = 260;
    private const int LongestPathLength = 32767;

    // APPLICATION_USER_MODEL_ID_MAX_LENGTH, with the terminating null.
    private const int LongestAppUserModelIdLength = 130;

    public int CurrentProcessId => Environment.ProcessId;

    public int? ForegroundProcessId
    {
        get
        {
            if (foregroundWindow == 0)
            {
                return null;
            }
            Native.GetWindowThreadProcessId(foregroundWindow, out var id);
            return id == 0 ? null : (int)id;
        }
    }

    public ProcessFacts? Find(int processId)
    {
        // An exited process can still be opened while someone holds a handle to it (its parent, until it reaps it).
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            return null;
        }
        try
        {
            if (ImagePath(handle) is not { } path)
            {
                return null;
            }
            DateTime? started = Native.GetProcessTimes(handle, out var creation, out _, out _, out _) ? DateTime.FromFileTimeUtc(creation) : null;
            return new ProcessFacts(processId, ParentId(handle), Path.GetFileName(path), path, started, AppUserModelId(handle));
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    public IEnumerable<int> ForegroundChildProcessIds()
    {
        if (foregroundWindow == 0)
        {
            return [];
        }
        var ids = new List<int>();
        var handle = GCHandle.Alloc(ids);
        try
        {
            Native.EnumChildWindows(foregroundWindow, &AddWindowProcess, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return ids;
    }

    [UnmanagedCallersOnly]
    private static int AddWindowProcess(nint window, nint ids)
    {
        Native.GetWindowThreadProcessId(window, out var id);
        ((List<int>)GCHandle.FromIntPtr(ids).Target!).Add((int)id);
        return 1;
    }

    // 0 when Windows does not tell: no process has that id.
    private static int ParentId(nint process)
    {
        Native.ProcessBasicInformation information;
        return Native.NtQueryInformationProcess(process, Native.ProcessBasicInformationClass, &information, (uint)sizeof(Native.ProcessBasicInformation), out _) == 0
            ? (int)information.InheritedFromUniqueProcessId
            : 0;
    }

    private static string? ImagePath(nint process)
    {
        foreach (var capacity in (ReadOnlySpan<int>)[UsualPathLength, LongestPathLength])
        {
            var buffer = new char[capacity];
            var size = (uint)capacity;
            fixed (char* text = buffer)
            {
                if (Native.QueryFullProcessImageName(process, 0, text, ref size))
                {
                    return new string(text, 0, (int)size);
                }
            }
            if (Marshal.GetLastPInvokeError() != Native.ErrorInsufficientBuffer)
            {
                return null;
            }
        }
        return null;
    }

    private static string? AppUserModelId(nint process)
    {
        var buffer = stackalloc char[LongestAppUserModelIdLength];
        var length = (uint)LongestAppUserModelIdLength;
        // APPMODEL_ERROR_NO_APPLICATION for a process outside a package.
        return Native.GetApplicationUserModelId(process, ref length, buffer) == 0 ? new string(buffer) : null;
    }
}
