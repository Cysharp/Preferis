using Preferis.Core.Routing;
using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

public class WindowsSourceAppDetectorTests
{
    private const int RouterId = 1000;
    private const string System32 = @"C:\Windows\System32\";
    private const string VsCode = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe";
    private const string Terminal = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.12741.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";
    private const string TerminalId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string Outlook = @"C:\Program Files\WindowsApps\Microsoft.OutlookForWindows_1.2025.1.0_x64__8wekyb3d8bbwe\olk.exe";
    private const string OutlookId = "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows";
    private const string Store = @"C:\Program Files\WindowsApps\Microsoft.WindowsStore_22410.1401.5.0_x64__8wekyb3d8bbwe\WinStore.App.exe";
    private const string StoreId = "Microsoft.WindowsStore_8wekyb3d8bbwe!App";

    private static readonly DateTime RouterStarted = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeProcesses _processes = new();

    private SourceApp? Detect() => WindowsSourceAppDetector.Detect(_processes);

    private void Router(int parent)
        => _processes.Add(new(RouterId, parent, "Preferis.exe", @"C:\Users\me\AppData\Local\Preferis\current\Preferis.exe", RouterStarted, null));

    // Backslashes are not separators on macOS, where these tests run too: no Path.GetFileName.
    private void Running(int id, int parent, string path, string? appId = null, DateTime? started = null)
        => _processes.Add(new(id, parent, path[(path.LastIndexOf('\\') + 1)..], path, started ?? RouterStarted.AddMinutes(-1), appId));

    [Fact]
    public void A_program_that_opened_the_url_itself_is_the_parent()
    {
        Running(200, parent: 300, VsCode);
        Running(300, parent: 4, @"C:\Windows\explorer.exe");
        Router(parent: 200);

        Assert.Equal(new SourceApp(null, VsCode), Detect());
    }

    [Fact]
    public void A_packaged_program_is_also_known_by_its_application_user_model_id()
    {
        Running(200, parent: 4, Terminal, TerminalId);
        Router(parent: 200);

        Assert.Equal(new SourceApp(TerminalId, Terminal), Detect());
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("conhost.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("OpenWith.exe")]
    [InlineData("CMD.EXE")]
    public void Programs_that_only_pass_the_url_on_are_skipped(string intermediary)
    {
        Running(200, parent: 4, VsCode);
        Running(210, parent: 200, System32 + intermediary);
        Running(220, parent: 210, System32 + intermediary);
        Router(parent: 220);

        Assert.Equal(new SourceApp(null, VsCode), Detect());
    }

    [Fact]
    public void A_broker_further_up_is_not_looked_at()
    {
        // The new Outlook is started by sihost.exe, and opens links itself.
        Running(150, parent: 4, System32 + "sihost.exe");
        Running(200, parent: 150, Outlook, OutlookId);
        Router(parent: 200);
        Running(400, parent: 4, VsCode);
        _processes.Foreground = 400;

        Assert.Equal(new SourceApp(OutlookId, Outlook), Detect());
    }

    [Theory]
    [InlineData("sihost.exe")]
    [InlineData("explorer.exe")]
    [InlineData("svchost.exe")]
    [InlineData("RuntimeBroker.exe")]
    [InlineData("services.exe")]
    [InlineData("SiHost.exe")]
    public void When_a_broker_started_the_router_the_app_in_front_opened_the_url(string broker)
    {
        Running(150, parent: 4, System32 + broker);
        Running(400, parent: 4, VsCode);
        Router(parent: 150);
        _processes.Foreground = 400;

        Assert.Equal(new SourceApp(null, VsCode), Detect());
    }

    [Fact]
    public void A_uwp_app_in_front_is_found_inside_its_frame()
    {
        Running(150, parent: 4, System32 + "sihost.exe");
        Running(500, parent: 4, System32 + "ApplicationFrameHost.exe");
        Running(510, parent: 4, Store, StoreId);
        Router(parent: 150);
        _processes.Foreground = 500;
        _processes.ForegroundChildren = [500, 510, 500];

        Assert.Equal(new SourceApp(StoreId, Store), Detect());
    }

    [Fact]
    public void A_frame_without_an_app_in_it_leaves_the_app_unknown()
    {
        Running(150, parent: 4, System32 + "sihost.exe");
        Running(500, parent: 4, System32 + "ApplicationFrameHost.exe");
        Router(parent: 150);
        _processes.Foreground = 500;
        _processes.ForegroundChildren = [500];

        Assert.Null(Detect());
    }

    [Fact]
    public void Without_a_window_in_front_a_broker_leaves_the_app_unknown()
    {
        Running(150, parent: 4, System32 + "sihost.exe");
        Router(parent: 150);

        Assert.Null(Detect());
    }

    [Fact]
    public void A_parent_that_has_exited_or_cannot_be_opened_leaves_the_app_unknown()
    {
        // cmd /c start: the intermediary may be gone before the Router looks. The window in front may have nothing to do
        // with the URL (a script in the background), so it is not asked. A service cannot be opened, and it tells no more.
        Running(400, parent: 4, VsCode);
        Router(parent: 210);
        _processes.Foreground = 400;

        Assert.Null(Detect());
    }

    [Fact]
    public void A_process_that_took_over_the_id_of_the_parent_is_not_the_parent()
    {
        Running(200, parent: 4, VsCode, started: RouterStarted.AddMilliseconds(5));
        Router(parent: 200);

        Assert.Null(Detect());
    }

    [Fact]
    public void A_process_that_took_over_the_id_of_an_intermediarys_parent_is_not_its_parent()
    {
        Running(200, parent: 4, VsCode, started: RouterStarted.AddSeconds(-1));
        Running(210, parent: 200, System32 + "cmd.exe", started: RouterStarted.AddSeconds(-2));
        Router(parent: 210);

        Assert.Null(Detect());
    }

    [Fact]
    public void A_chain_of_intermediaries_without_an_end_leaves_the_app_unknown()
    {
        // Process ids are reused; an intermediary may end up as its own parent.
        Running(210, parent: 210, System32 + "cmd.exe");
        Router(parent: 210);

        Assert.Null(Detect());
    }

    [Fact]
    public void The_processes_of_this_computer_are_read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var processes = new WindowsProcesses(foregroundWindow: 0);

        var self = processes.Find(processes.CurrentProcessId);

        Assert.NotNull(self);
        Assert.Equal(Environment.ProcessPath, self.Path, ignoreCase: true);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), self.Name, ignoreCase: true);
        Assert.Null(self.AppUserModelId);
        using (var current = System.Diagnostics.Process.GetCurrentProcess())
        {
            Assert.Equal(current.StartTime.ToUniversalTime(), self.StartTime!.Value, TimeSpan.FromMilliseconds(1));
        }
        var parent = processes.Find(self.ParentId);
        Assert.NotNull(parent);
        Assert.Equal(Path.GetFileName(parent.Path), parent.Name);
        Assert.True(parent.StartTime <= self.StartTime);
        // Windows process ids are multiples of 4.
        Assert.Null(processes.Find(3));
        Assert.Null(processes.ForegroundProcessId);
        Assert.Empty(processes.ForegroundChildProcessIds());
    }

    [Fact]
    public void The_app_that_started_the_tests_is_found()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var processes = new WindowsProcesses(foregroundWindow: 0);
        var parent = processes.Find(processes.Find(processes.CurrentProcessId)!.ParentId)!;
        if (WindowsSourceAppDetector.IsIntermediary(parent.Name) || WindowsSourceAppDetector.IsBroker(parent.Name))
        {
            // Run by something other than dotnet test or an IDE: the rest is covered with made-up processes.
            return;
        }

        var source = WindowsSourceAppDetector.CaptureForeground().Detect();

        Assert.Equal(new SourceApp(parent.AppUserModelId, parent.Path), source);
    }

    private sealed class FakeProcesses : IProcesses
    {
        private readonly Dictionary<int, ProcessFacts> _processes = [];

        public int CurrentProcessId => RouterId;

        public int? Foreground { get; set; }

        public int[] ForegroundChildren { get; set; } = [];

        public int? ForegroundProcessId => Foreground;

        public void Add(ProcessFacts process) => _processes.Add(process.Id, process);

        public ProcessFacts? Find(int processId) => _processes.GetValueOrDefault(processId);

        public IEnumerable<int> ForegroundChildProcessIds() => ForegroundChildren;
    }
}
