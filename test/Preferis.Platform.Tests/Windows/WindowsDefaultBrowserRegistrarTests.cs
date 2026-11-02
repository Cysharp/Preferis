using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

public class WindowsDefaultBrowserRegistrarTests
{
    private const string Router = @"C:\Tools\Preferis\Preferis.exe";

    private readonly FakeRegistry _registry = new();
    private readonly FakeAssociations _associations = new();
    private readonly List<string> _opened = [];
    private int _notified;

    private WindowsDefaultBrowserRegistrar Registrar(string router = Router)
        => new(_registry, _associations, router, _opened.Add, () => _notified++);

    [Fact]
    public void Register_writes_the_browser_registration_under_the_current_user()
    {
        var result = Registrar().Register();

        Assert.True(result.Succeeded);
        var client = @"HKCU\Software\Clients\StartMenuInternet\Preferis";
        Assert.Equal("Preferis", _registry.GetString(client, null));
        Assert.Equal($"\"{Router}\"", _registry.GetString($@"{client}\shell\open\command", null));
        Assert.Equal($"{Router},0", _registry.GetString($@"{client}\DefaultIcon", null));
        Assert.Equal("Preferis", _registry.GetString($@"{client}\Capabilities", "ApplicationName"));
        Assert.Equal("Preferis", _registry.GetString($@"{client}\Capabilities\StartMenu", "StartMenuInternet"));
        Assert.Equal("PreferisURL", _registry.GetString($@"{client}\Capabilities\URLAssociations", "http"));
        Assert.Equal("PreferisURL", _registry.GetString($@"{client}\Capabilities\URLAssociations", "https"));
        Assert.Equal(@"Software\Clients\StartMenuInternet\Preferis\Capabilities", _registry.GetString(@"HKCU\Software\RegisteredApplications", "Preferis"));

        var progId = @"HKCU\Software\Classes\PreferisURL";
        Assert.Equal("", _registry.GetString(progId, "URL Protocol"));
        // The URL is the rest of the command line, so that a crafted URL cannot add options of its own (as Edge and Chrome do).
        Assert.Equal($"\"{Router}\" --single-argument %1", _registry.GetString($@"{progId}\shell\open\command", null));
        Assert.Equal(1, _notified);
    }

    [Fact]
    public void Register_registers_the_preferis_scheme_too()
    {
        Registrar().Register();

        // Only Preferis handles preferis: URLs, so the shell starts the command directly and nothing is chosen in Settings.
        var scheme = @"HKCU\Software\Classes\preferis";
        Assert.Equal("", _registry.GetString(scheme, "URL Protocol"));
        Assert.Equal("URL:Preferis", _registry.GetString(scheme, null));
        Assert.Equal($"{Router},0", _registry.GetString($@"{scheme}\DefaultIcon", null));
        Assert.Equal($"\"{Router}\" --single-argument %1", _registry.GetString($@"{scheme}\shell\open\command", null));
        Assert.Null(_registry.GetString(@"HKCU\Software\Clients\StartMenuInternet\Preferis\Capabilities\URLAssociations", "preferis"));
    }

    [Fact]
    public void A_registration_without_the_preferis_scheme_needs_registering_again()
    {
        // As an older Preferis registered: the preferis: URLs would not reach it.
        _associations.ProgIds["http"] = _associations.ProgIds["https"] = "PreferisURL";
        var registrar = Registrar();
        registrar.Register();
        _registry.DeleteTree(@"HKCU\Software\Classes\preferis");

        var before = registrar.GetStatus();
        registrar.Register();
        var after = registrar.GetStatus();

        Assert.Equal((false, false), (before.IsRegistered, before.IsDefault));
        Assert.Equal((true, true), (after.IsRegistered, after.IsDefault));
        Assert.Empty(_opened);
    }

    [Fact]
    public void Register_opens_the_default_apps_page_until_preferis_is_chosen_there()
    {
        var result = Registrar().Register();

        // Windows does not let programs pick the default browser; the user does it in Settings.
        Assert.Equal(["ms-settings:defaultapps?registeredAppUser=Preferis"], _opened);
        Assert.Contains("Settings", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_does_not_open_settings_when_preferis_already_is_the_default()
    {
        _associations.ProgIds["http"] = _associations.ProgIds["https"] = "PreferisURL";

        var result = Registrar().Register();

        Assert.True(result.Succeeded);
        Assert.Empty(_opened);
    }

    [Fact]
    public void Status_reports_registration_and_the_current_default()
    {
        _associations.ProgIds["http"] = _associations.ProgIds["https"] = "MSEdgeHTM";
        _associations.Names["https"] = "Microsoft Edge";
        var registrar = Registrar();

        var before = registrar.GetStatus();
        registrar.Register();
        var registered = registrar.GetStatus();
        _associations.ProgIds["http"] = _associations.ProgIds["https"] = "PreferisURL";
        var chosen = registrar.GetStatus();

        Assert.Equal((false, false, "Microsoft Edge"), (before.IsRegistered, before.IsDefault, before.CurrentDefault));
        Assert.Equal((true, false), (registered.IsRegistered, registered.IsDefault));
        Assert.Equal((true, true), (chosen.IsRegistered, chosen.IsDefault));
    }

    [Fact]
    public void A_registration_of_another_copy_needs_registering_again()
    {
        // The registry points at the folder the exe was in when it registered; links would go there.
        Registrar(@"D:\Old\Preferis\Preferis.exe").Register();
        _associations.ProgIds["http"] = _associations.ProgIds["https"] = "PreferisURL";

        var status = Registrar().GetStatus();

        Assert.False(status.IsRegistered);
        Assert.False(status.IsDefault);
    }

    [Fact]
    public void Unregister_removes_everything_register_wrote()
    {
        var registrar = Registrar();
        _registry.Set(@"HKCU\Software\RegisteredApplications", "Other", "Software\\Other\\Capabilities");
        registrar.Register();

        var result = registrar.Unregister();

        Assert.True(result.Succeeded);
        Assert.False(_registry.KeyExists(@"HKCU\Software\Clients\StartMenuInternet\Preferis"));
        Assert.False(_registry.KeyExists(@"HKCU\Software\Classes\PreferisURL"));
        Assert.False(_registry.KeyExists(@"HKCU\Software\Classes\preferis"));
        Assert.Null(_registry.GetString(@"HKCU\Software\RegisteredApplications", "Preferis"));
        Assert.Equal("Software\\Other\\Capabilities", _registry.GetString(@"HKCU\Software\RegisteredApplications", "Other"));
        Assert.Equal(2, _notified);
    }

    [Fact]
    public void Open_system_settings_opens_the_default_apps_page_for_preferis()
    {
        Registrar().OpenSystemSettings();

        Assert.Equal(["ms-settings:defaultapps?registeredAppUser=Preferis"], _opened);
    }

    [Theory]
    [InlineData("PreferisURL", true)]
    [InlineData("preferisurl", true)]
    [InlineData("MSEdgeHTM", false)]
    [InlineData(null, false)]
    public void The_system_opener_recognizes_preferis_as_the_default_handler(string? progId, bool expected)
    {
        if (progId is not null)
        {
            _associations.ProgIds["https"] = progId;
        }

        Assert.Equal(expected, new WindowsSystemOpener(_associations).IsSelfDefaultHandler("https"));
    }

    private sealed class FakeAssociations : IUrlAssociations
    {
        public Dictionary<string, string> ProgIds { get; } = [];

        public Dictionary<string, string> Names { get; } = [];

        public string? GetProgId(string scheme) => ProgIds.GetValueOrDefault(scheme);

        public string? GetHandlerName(string scheme) => Names.GetValueOrDefault(scheme);
    }
}
