using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Preferis.Spikes.SourceApp;

// Spike 10 (Design.md §12): can Windows tell which application opened a URL?
//
// macOS names the sender in the Apple Event; Windows hands the handler nothing but a command line. The probe records
// the two clues that are left:
// - the parent process chain: ShellExecuteEx runs a registered "shell\open\command" inside the caller, so the caller is
//   the parent unless something in between (cmd, rundll32, a broker) started the handler;
// - the foreground window at start: the window the user clicked the link in, as long as nothing has replaced it.
//
// Registered as a browser, the probe sends http(s) URLs on to the browser that was the default before, so that it can
// stay the default while links are clicked in real applications. The log is appended to source-app.log next to the
// executable: a program started by the shell has no console. It holds no paths of the URLs, only scheme and host.
internal static unsafe class Program
{
    private const string SingleArgumentMarker = "--single-argument";
    private const string Scheme = "preferis-probe";
    private const string ProgId = "PreferisProbeURL";
    private const string AppName = "PreferisSourceProbe";
    private const string DisplayName = "Preferis Source Probe";
    private const string ClientKey = @"Software\Clients\StartMenuInternet\" + AppName;
    private const string RegisteredApplications = @"Software\RegisteredApplications";

    // Started the probe for someone else; the chain is followed past them to the program the link was clicked in.
    private static readonly string[] Intermediaries = ["cmd.exe", "conhost.exe", "rundll32.exe", "OpenWith.exe"];

    // Long-running processes that start programs on behalf of others: as the parent they say nothing about the caller.
    private static readonly string[] Brokers =
        ["explorer.exe", "svchost.exe", "sihost.exe", "RuntimeBroker.exe", "ApplicationFrameHost.exe", "services.exe"];

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "source-app.log");
    private static readonly string ForwardPath = Path.Combine(AppContext.BaseDirectory, "forward.txt");
    private static readonly string ExePath = Environment.ProcessPath!;
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static int _frameHostId;
    private static int _hostedId;

    private static int Main(string[] args)
    {
        // Before anything else: from now on, any window may come to the front.
        var foregroundWindow = Native.GetForegroundWindow();
        var foregroundReadAt = DateTime.UtcNow;

        switch (args.FirstOrDefault())
        {
            case "--register":
                return Register(browser: false);
            case "--register-browser":
                return Register(browser: true);
            case "--unregister":
                return Unregister();
            case SingleArgumentMarker:
            {
                // The registered command: the rest of the raw command line is the URL, as in the Router.
                var commandLine = Environment.CommandLine;
                var marker = commandLine.IndexOf(" " + SingleArgumentMarker, StringComparison.Ordinal);
                var url = marker < 0 ? "" : commandLine[(marker + 1 + SingleArgumentMarker.Length)..].Trim();
                return Probe(url, foregroundWindow, foregroundReadAt);
            }
            case null or "--help" or "-h":
                return Usage();
            default:
                return Probe(args[0], foregroundWindow, foregroundReadAt);
        }
    }

    private static int Probe(string url, IntPtr foregroundWindow, DateTime foregroundReadAt)
    {
        var processes = Snapshot();
        var self = Describe(Environment.ProcessId, processes);
        var log = new StringBuilder();
        log.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Redact(url)}");
        log.AppendLine($"self: {self?.ToString() ?? "not in the process snapshot"}; {SelfContext()}");
        if (self?.StartTime is { } started)
        {
            log.AppendLine($"foreground read {(foregroundReadAt - started).TotalMilliseconds:F0} ms after the process started");
        }

        var foreground = Foreground(foregroundWindow, processes, log);
        log.AppendLine("parents:");
        List<ProcessInfo> chain = self is null ? [] : ParentChain(self, processes, log);
        log.AppendLine($"guess: {Guess(chain, foreground)}");

        if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            log.AppendLine($"forward: {Forward(url)}");
        }
        Append(log.AppendLine().ToString());
        return 0;
    }

    private static Dictionary<int, (int ParentId, string Name)> Snapshot()
    {
        var processes = new Dictionary<int, (int ParentId, string Name)>();
        var snapshot = Native.CreateToolhelp32Snapshot(Native.Th32csSnapProcess, 0);
        if (snapshot == -1)
        {
            return processes;
        }
        try
        {
            var entry = new Native.ProcessEntry32 { Size = (uint)sizeof(Native.ProcessEntry32) };
            for (var more = Native.Process32First(snapshot, ref entry); more; more = Native.Process32Next(snapshot, ref entry))
            {
                processes[(int)entry.ProcessId] = ((int)entry.ParentProcessId, new string(entry.ExeFile));
            }
        }
        finally
        {
            Native.CloseHandle(snapshot);
        }
        return processes;
    }

    // The snapshot names every process; the rest needs a handle, which processes of other users and services may refuse.
    private static ProcessInfo? Describe(int id, Dictionary<int, (int ParentId, string Name)> processes)
    {
        if (!processes.TryGetValue(id, out var entry))
        {
            return null;
        }
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)id);
        if (handle == 0)
        {
            // ERROR_INVALID_PARAMETER: no such process any more. An intermediary (cmd /c start) exits right after the launch.
            var unavailable = Marshal.GetLastPInvokeError() == Native.ErrorAccessDenied ? "no access" : "exited after the snapshot";
            return new ProcessInfo(id, entry.ParentId, entry.Name, null, null, null, null, unavailable);
        }
        try
        {
            DateTime? start = Native.GetProcessTimes(handle, out var creation, out _, out _, out _) ? DateTime.FromFileTimeUtc(creation) : null;
            return new ProcessInfo(id, entry.ParentId, entry.Name, ImagePath(handle), start, PackageFullName(handle), AppUserModelId(handle), null);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static List<ProcessInfo> ParentChain(ProcessInfo self, Dictionary<int, (int ParentId, string Name)> processes, StringBuilder log)
    {
        var chain = new List<ProcessInfo>();
        var child = self;
        // 0 is the idle process and 4 the kernel: the top of every chain.
        while (chain.Count < 12 && child.ParentId > 4)
        {
            if (Describe(child.ParentId, processes) is not { } parent)
            {
                log.AppendLine($"  pid {child.ParentId}: exited");
                break;
            }
            // Started after its child, it is not the parent: the parent exited and a new process got its id.
            if (parent.StartTime > child.StartTime)
            {
                log.AppendLine($"  pid {parent.Id} {parent.Name}: started after its child (the parent exited, its id was reused)");
                break;
            }
            chain.Add(parent);
            log.AppendLine($"  {chain.Count}. {parent}");
            child = parent;
        }
        return chain;
    }

    private static ProcessInfo? Foreground(IntPtr window, Dictionary<int, (int ParentId, string Name)> processes, StringBuilder log)
    {
        if (window == 0)
        {
            log.AppendLine("foreground: none");
            return null;
        }
        Native.GetWindowThreadProcessId(window, out var id);
        var owner = Describe((int)id, processes);
        log.AppendLine($"foreground: class {ClassName(window)}, {owner?.ToString() ?? $"pid {id} (exited)"}");

        // A UWP app's frame belongs to ApplicationFrameHost.exe; the app's own CoreWindow is a child of it.
        if (owner is not null && owner.Name.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            _frameHostId = owner.Id;
            _hostedId = 0;
            Native.EnumChildWindows(window, &FindHostedWindow, 0);
            if (_hostedId != 0 && Describe(_hostedId, processes) is { } hosted)
            {
                log.AppendLine($"  hosted: {hosted}");
                return hosted;
            }
        }
        return owner;
    }

    [UnmanagedCallersOnly]
    private static int FindHostedWindow(IntPtr window, IntPtr param)
    {
        Native.GetWindowThreadProcessId(window, out var id);
        if ((int)id == _frameHostId)
        {
            return 1;
        }
        _hostedId = (int)id;
        return 0;
    }

    // One way the Router could decide: the first parent that is not an intermediary, unless it is a broker.
    private static string Guess(List<ProcessInfo> chain, ProcessInfo? foreground)
    {
        var caller = chain.FirstOrDefault(p => !Intermediaries.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
        var skipped = chain.TakeWhile(p => p != caller).Select(p => p.Name).ToList();
        var parentGuess = caller is null ? "-"
            : Brokers.Contains(caller.Name, StringComparer.OrdinalIgnoreCase) ? $"{caller.Name} (a broker: no answer)"
            : caller.Name;
        if (skipped.Count > 0)
        {
            parentGuess += $" (past {string.Join(", ", skipped)})";
        }
        var agreement = caller is null || foreground is null ? "cannot compare"
            : caller.Id == foreground.Id ? "same process"
            : caller.ImagePath is not null && string.Equals(caller.ImagePath, foreground.ImagePath, StringComparison.OrdinalIgnoreCase) ? "same program"
            : "differ";
        return $"parents -> {parentGuess}; foreground -> {foreground?.Name ?? "-"}; {agreement}";
    }

    private static string Forward(string url)
    {
        if (!File.Exists(ForwardPath))
        {
            return "no forward.txt, the URL is not opened";
        }
        try
        {
            var (program, arguments) = SplitCommand(File.ReadAllText(ForwardPath).Trim());
            if (string.Equals(Path.GetFullPath(program), ExePath, StringComparison.OrdinalIgnoreCase))
            {
                return "forward.txt names this probe, the URL is not opened";
            }
            arguments = arguments.Contains("%1", StringComparison.Ordinal) ? arguments.Replace("%1", url, StringComparison.Ordinal) : $"{arguments} \"{url}\"";
            // Started by the shell for a click, the probe may bring a window to the front; pass the right on to the browser.
            Native.AllowSetForegroundWindow(Native.AsfwAny);
            using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false });
            return $"{Path.GetFileName(program)} (pid {process?.Id})";
        }
        catch (Exception ex)
        {
            return $"failed: {ex.Message}";
        }
    }

    private static (string Program, string Arguments) SplitCommand(string command)
    {
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end < 0 ? (command.Trim('"'), "") : (command[1..end], command[(end + 1)..].Trim());
        }
        var space = command.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    private static int Register(bool browser)
    {
        AttachToParentConsole();
        var command = $"\"{ExePath}\" {SingleArgumentMarker} %1";
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}"))
        {
            key.SetValue(null, $"URL:{DisplayName}");
            key.SetValue("URL Protocol", "");
            using var open = key.CreateSubKey(@"shell\open\command");
            open.SetValue(null, command);
        }
        Console.WriteLine($"Registered {Scheme}: (try: Start-Process {Scheme}://test)");
        if (!browser)
        {
            return 0;
        }

        // The browser before the probe becomes the default: http(s) URLs are sent on to it.
        if (AssocCommand("https") is { } current && !current.Contains(ExePath, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(ForwardPath, current);
        }
        Console.WriteLine(File.Exists(ForwardPath) ? $"http(s) URLs go on to: {File.ReadAllText(ForwardPath)}" : "Warning: no browser to send http(s) URLs on to");

        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            key.SetValue(null, $"{DisplayName} URL");
            key.SetValue("URL Protocol", "");
            using var open = key.CreateSubKey(@"shell\open\command");
            open.SetValue(null, command);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(ClientKey))
        {
            key.SetValue(null, DisplayName);
            using var open = key.CreateSubKey(@"shell\open\command");
            open.SetValue(null, $"\"{ExePath}\"");
            using var capabilities = key.CreateSubKey("Capabilities");
            capabilities.SetValue("ApplicationName", DisplayName);
            capabilities.SetValue("ApplicationDescription", "Records which application opened a link (Preferis spike).");
            using var startMenu = capabilities.CreateSubKey("StartMenu");
            startMenu.SetValue("StartMenuInternet", AppName);
            using var associations = capabilities.CreateSubKey("URLAssociations");
            associations.SetValue("http", ProgId);
            associations.SetValue("https", ProgId);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(RegisteredApplications))
        {
            key.SetValue(AppName, ClientKey + @"\Capabilities");
        }
        Native.SHChangeNotify(Native.ShcneAssocChanged, 0, 0, 0);
        using (Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={AppName}") { UseShellExecute = true }))
        {
        }
        Console.WriteLine($"Choose {DisplayName} as the default browser in the Settings page that opened.");
        return 0;
    }

    private static int Unregister()
    {
        AttachToParentConsole();
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClientKey, throwOnMissingSubKey: false);
        using (var key = Registry.CurrentUser.OpenSubKey(RegisteredApplications, writable: true))
        {
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        Native.SHChangeNotify(Native.ShcneAssocChanged, 0, 0, 0);
        Console.WriteLine("Unregistered. If the probe was the default browser, choose your browser again in Settings > Default apps.");
        return 0;
    }

    private static int Usage()
    {
        AttachToParentConsole();
        Console.WriteLine($"""
            Preferis spike 10: which application opened the URL?

              SourceApp --register            Register the {Scheme}: scheme only (for scripted launches, measure.ps1).
              SourceApp --register-browser    Also register as a browser and open Default apps to choose it.
                                              http(s) URLs are sent on to the browser that was the default.
              SourceApp --unregister          Remove both registrations.
              SourceApp <url>                 Probe once, as if <url> had been opened.

            Log: {LogPath}
            """);
        return 1;
    }

    private static string? AssocCommand(string scheme)
    {
        var buffer = stackalloc char[2048];
        var length = 2048u;
        return Native.AssocQueryString(Native.AssocfIsProtocol, Native.AssocstrCommand, scheme, null, buffer, ref length) == 0 ? new string(buffer) : null;
    }

    private static string? ImagePath(IntPtr process)
    {
        var buffer = stackalloc char[1024];
        var size = 1024u;
        return Native.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private static string? PackageFullName(IntPtr process)
    {
        var buffer = stackalloc char[256];
        var length = 256u;
        return Native.GetPackageFullName(process, ref length, buffer) == 0 ? new string(buffer) : null;
    }

    private static string? AppUserModelId(IntPtr process)
    {
        var buffer = stackalloc char[256];
        var length = 256u;
        return Native.GetApplicationUserModelId(process, ref length, buffer) == 0 ? new string(buffer) : null;
    }

    private static string ClassName(IntPtr window)
    {
        var buffer = stackalloc char[256];
        var length = Native.GetClassName(window, buffer, 256);
        return new string(buffer, 0, Math.Max(length, 0));
    }

    // Started by a packaged application, does the probe run inside its package (identity, job, virtualized AppData)?
    private static string SelfContext()
    {
        var buffer = stackalloc char[256];
        var length = 256u;
        var package = Native.GetCurrentPackageFullName(ref length, buffer) == 0 ? new string(buffer) : "-";
        var inJob = Native.IsProcessInJob(Native.GetCurrentProcess(), 0, out var result) && result;
        return $"own package {package}, in a job: {(inJob ? "yes" : "no")}";
    }

    private static string Redact(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Host}/... ({url.Length} chars)" : $"<{url.Length} chars>";

    private static void Append(string text)
    {
        // Links clicked in quick succession start probes side by side; each holds the file only while it writes.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.AppendAllText(LogPath, text);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(25);
            }
        }
    }

    // As the Router: a GUI program borrows the terminal's console for its output unless the output is redirected.
    private static void AttachToParentConsole()
    {
        var output = Native.GetStdHandle(Native.StdOutputHandle);
        if (output != 0 && output != -1 && Native.GetFileType(output) != Native.FileTypeUnknown)
        {
            return;
        }
        Native.AttachConsole(Native.AttachParentProcess);
    }

    private sealed record ProcessInfo(
        int Id, int ParentId, string Name, string? ImagePath, DateTime? StartTime, string? Package, string? AppUserModelId, string? Unavailable)
    {
        public override string ToString()
        {
            var text = new StringBuilder($"pid {Id} {Name}");
            text.Append(StartTime is { } start ? $", started {start.ToLocalTime():HH:mm:ss.fff}" : $", {Unavailable ?? "start time unknown"}");
            if (ImagePath is not null)
            {
                text.Append(", ").Append(ImagePath.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase));
            }
            if (Package is not null)
            {
                text.Append(", package ").Append(Package);
            }
            if (AppUserModelId is not null)
            {
                text.Append(", AUMID ").Append(AppUserModelId);
            }
            return text.ToString();
        }
    }
}
