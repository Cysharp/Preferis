using Preferis.Core.Routing;
using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

public class MacSourceAppDetectorTests
{
    private const int RouterId = 1000;
    private const int Launchd = 1;
    private const string Slack = "/Applications/Slack.app";
    private const string SlackId = "com.tinyspeck.slackmacgap";
    private const string Terminal = "/System/Applications/Utilities/Terminal.app";
    private const string TerminalId = "com.apple.Terminal";
    private const string VsCode = "/Applications/Visual Studio Code.app";
    private const string VsCodeId = "com.microsoft.VSCode";
    private const string Chrome = "/Applications/Google Chrome.app";
    private const string ChromeId = "com.google.Chrome";
    private const string Settings = "/Applications/Preferis.app/Contents/Helpers/Preferis Settings.app";
    private const string SettingsId = "net.mayuki.Preferis.Settings";

    private readonly FakeProcesses _processes = new();

    public MacSourceAppDetectorTests()
    {
        _processes.Bundle(Slack, SlackId);
        _processes.Bundle(Terminal, TerminalId);
        _processes.Bundle(VsCode, VsCodeId);
        _processes.Bundle(Chrome, ChromeId);
        _processes.Bundle(Settings, SettingsId);
    }

    private SourceApp? Detect(int sender) => MacSourceAppDetector.Detect(sender, _processes);

    private void Running(int id, int parent, string? path) => _processes.Add(new(id, parent, path));

    private void Router(int parent) => Running(RouterId, parent, "/Applications/Preferis.app/Contents/MacOS/Preferis");

    // An app launched by Launch Services, and the shell in its window: zsh under login, as Terminal starts them.
    private void TerminalWithShell()
    {
        Running(200, Launchd, Terminal + "/Contents/MacOS/Terminal");
        Running(210, parent: 200, "/usr/bin/login");
        Running(220, parent: 210, "/bin/zsh");
    }

    [Fact]
    public void An_app_that_sent_the_url_is_the_app()
    {
        Running(200, Launchd, Slack + "/Contents/MacOS/Slack");
        Router(Launchd);

        Assert.Equal(new SourceApp(SlackId, Slack), Detect(200));
    }

    [Fact]
    public void An_app_without_a_bundle_id_is_known_by_its_path()
    {
        Running(200, Launchd, "/Applications/Unnamed.app/Contents/MacOS/Unnamed");
        Router(Launchd);

        Assert.Equal(new SourceApp(null, "/Applications/Unnamed.app"), Detect(200));
    }

    [Theory]
    // Electron apps run their terminals and extensions in helpers.
    [InlineData(VsCode + "/Contents/Frameworks/Code Helper.app/Contents/MacOS/Code Helper", VsCode)]
    [InlineData(VsCode + "/Contents/Frameworks/Code Helper (Plugin).app/Contents/MacOS/Code Helper (Plugin)", VsCode)]
    [InlineData(Chrome + "/Contents/Frameworks/Google Chrome Framework.framework/Versions/131.0.6778.86/Helpers/Google Chrome Helper.app/Contents/MacOS/Google Chrome Helper", Chrome)]
    public void A_helper_in_the_frameworks_of_an_app_counts_as_the_app(string helper, string app)
    {
        Running(200, Launchd, helper);
        Router(Launchd);

        Assert.Equal(app, Detect(200)?.Path);
    }

    [Fact]
    public void An_app_inside_another_app_is_an_app_of_its_own()
    {
        Running(200, Launchd, Settings + "/Contents/MacOS/Preferis Settings");
        Router(Launchd);

        Assert.Equal(new SourceApp(SettingsId, Settings), Detect(200));
    }

    [Theory]
    [InlineData("/Applications/slack.APP/contents/macos/Slack", "/Applications/slack.APP")]
    // Programs inside an app bundle that are not its app: a command line tool, an XPC service, an iOS app's wrapper.
    [InlineData("/Applications/Xcode.app/Contents/Developer/usr/bin/xcodebuild", null)]
    [InlineData(Slack + "/Contents/XPCServices/Updater.xpc/Contents/MacOS/Updater", null)]
    [InlineData("/Applications/Game.app/Wrapper/Game.app/Game", null)]
    [InlineData("/usr/local/bin/gh", null)]
    [InlineData("/Contents/MacOS/Preferis", null)]
    public void An_app_is_a_bundle_whose_own_program_runs(string executable, string? app)
    {
        Running(200, Launchd, executable);
        Router(Launchd);

        Assert.Equal(app, Detect(200)?.Path);
    }

    [Fact]
    public void Programs_that_are_no_app_are_looked_past_to_the_app_they_run_in()
    {
        // `open https://...` in Terminal.
        TerminalWithShell();
        Running(230, parent: 220, "/usr/bin/open");
        Router(Launchd);

        Assert.Equal(new SourceApp(TerminalId, Terminal), Detect(230));
    }

    [Fact]
    public void A_program_that_has_exited_but_not_yet_been_reaped_is_looked_past()
    {
        // Its path is gone, its parent is still known.
        TerminalWithShell();
        Running(230, parent: 220, path: null);
        Router(Launchd);

        Assert.Equal(new SourceApp(TerminalId, Terminal), Detect(230));
    }

    [Fact]
    public void A_program_run_by_no_app_leaves_the_app_unknown()
    {
        // A launchd job, or a program whose parent has exited: launchd takes the orphans.
        Running(300, Launchd, "/bin/sh");
        Running(310, parent: 300, "/usr/bin/open");
        Router(Launchd);

        Assert.Null(Detect(310));
    }

    [Fact]
    public void A_sender_that_has_exited_leaves_the_app_unknown()
    {
        Router(Launchd);

        Assert.Null(Detect(230));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Launchd)]
    [InlineData(RouterId)]
    public void A_sender_that_tells_nothing_leaves_the_app_unknown(int sender)
    {
        Running(Launchd, 0, "/sbin/launchd");
        Router(Launchd);

        Assert.Null(Detect(sender));
    }

    [Fact]
    public void A_chain_without_an_end_leaves_the_app_unknown()
    {
        // The processes are read one at a time and may change in between; the walk ends anyway.
        Running(300, parent: 310, "/bin/zsh");
        Running(310, parent: 300, "/bin/zsh");
        Router(Launchd);

        Assert.Null(Detect(300));
    }

    [Fact]
    public void For_a_url_on_the_command_line_the_app_is_the_one_the_router_was_started_from()
    {
        TerminalWithShell();
        Router(parent: 220);

        Assert.Equal(new SourceApp(TerminalId, Terminal), MacSourceAppDetector.DetectParent(_processes));
    }

    [Fact]
    public void The_settings_open_urls_as_themselves()
    {
        // Open on the test page runs the Router with the URL.
        Running(200, Launchd, Settings + "/Contents/MacOS/Preferis Settings");
        Router(parent: 200);

        Assert.Equal(new SourceApp(SettingsId, Settings), MacSourceAppDetector.DetectParent(_processes));
    }

    [Fact]
    public void The_processes_of_this_computer_are_read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        var processes = new MacProcesses();

        var self = processes.Find(processes.CurrentProcessId);

        Assert.NotNull(self);
        Assert.EndsWith("/" + Path.GetFileName(Environment.ProcessPath), self.Path);
        Assert.True(self.ParentId > 0);
        Assert.NotNull(processes.Find(self.ParentId));
        Assert.Null(processes.Find(-1));
        Assert.Equal("com.apple.finder", processes.BundleIdentifier("/System/Library/CoreServices/Finder.app"));
        Assert.Null(processes.BundleIdentifier("/usr/bin"));
    }

    private sealed class FakeProcesses : IMacProcesses
    {
        private readonly Dictionary<int, MacProcessFacts> _processes = [];
        private readonly Dictionary<string, string> _bundleIds = [];

        public int CurrentProcessId => RouterId;

        public void Add(MacProcessFacts process) => _processes.Add(process.Id, process);

        public void Bundle(string path, string id) => _bundleIds.Add(path, id);

        public MacProcessFacts? Find(int processId) => _processes.GetValueOrDefault(processId);

        public string? BundleIdentifier(string bundlePath) => _bundleIds.GetValueOrDefault(bundlePath);
    }
}
