using System.Diagnostics;
using System.Runtime.Versioning;
using Preferis.Core.Hosting;

namespace Preferis.Platform.Windows;

/// <summary>
/// Registers <c>Preferis.exe</c> as a browser under <c>HKEY_CURRENT_USER</c> (no administrator rights). Windows does not
/// let programs choose the default browser, so registering opens the Default apps page where the user picks Preferis.
/// See docs/Platform.md §2.2.
/// </summary>
public sealed class WindowsDefaultBrowserRegistrar : IDefaultBrowserRegistrar
{
    /// <summary>The ProgId URLs are opened with; also how Preferis recognizes itself as the default handler.</summary>
    public const string ProgId = "PreferisURL";

    private const string AppName = "Preferis";
    private const string ClientKey = @"HKCU\Software\Clients\StartMenuInternet\" + AppName;
    private const string CapabilitiesPath = @"Software\Clients\StartMenuInternet\" + AppName + @"\Capabilities";
    private const string ProgIdKey = @"HKCU\Software\Classes\" + ProgId;
    private const string RegisteredApplications = @"HKCU\Software\RegisteredApplications";
    private const string DefaultAppsPage = "ms-settings:defaultapps?registeredAppUser=" + AppName;
    private static readonly string[] Schemes = ["http", "https"];

    private readonly IRegistry _registry;
    private readonly IUrlAssociations _associations;
    private readonly string _routerPath;
    private readonly Action<string> _openUri;
    private readonly Action _notifyAssociationsChanged;

    /// <param name="routerPath">The <c>Preferis.exe</c> that URLs should be sent to.</param>
    [SupportedOSPlatform("windows")]
    public WindowsDefaultBrowserRegistrar(string routerPath)
        : this(WindowsRegistry.Instance, WindowsUrlAssociations.Instance, routerPath, OpenUri, NotifyAssociationsChanged)
    {
    }

    internal WindowsDefaultBrowserRegistrar(IRegistry registry, IUrlAssociations associations, string routerPath, Action<string> openUri, Action notifyAssociationsChanged)
    {
        _registry = registry;
        _associations = associations;
        _routerPath = routerPath;
        _openUri = openUri;
        _notifyAssociationsChanged = notifyAssociationsChanged;
    }

    public RegistrationStatus GetStatus()
    {
        // A registration of a copy elsewhere (an older build folder) does not count: URLs would be sent there.
        var command = _registry.GetString($@"{ProgIdKey}\shell\open\command", null);
        var registeredPath = command is null ? null : WindowsBrowserDetector.ExecutableFromCommand(command, Environment.GetEnvironmentVariable);
        var isRegistered = registeredPath is not null
            && SamePath(registeredPath, _routerPath)
            && _registry.GetString(RegisteredApplications, AppName) is not null;
        var isDefault = isRegistered && Schemes.All(IsChosen);
        return new RegistrationStatus(isRegistered, isDefault, _associations.GetHandlerName("https"));
    }

    public RegistrationResult Register()
    {
        try
        {
            var quoted = $"\"{_routerPath}\"";
            _registry.SetString(ClientKey, null, AppName);
            _registry.SetString($@"{ClientKey}\DefaultIcon", null, $"{_routerPath},0");
            // Started without a URL (from the legacy Start menu entry): the Router opens the settings.
            _registry.SetString($@"{ClientKey}\shell\open\command", null, quoted);
            _registry.SetString($@"{ClientKey}\Capabilities", "ApplicationName", AppName);
            _registry.SetString($@"{ClientKey}\Capabilities", "ApplicationDescription", "Opens every link in the browser and profile your rules choose.");
            _registry.SetString($@"{ClientKey}\Capabilities", "ApplicationIcon", $"{_routerPath},0");
            _registry.SetString($@"{ClientKey}\Capabilities\StartMenu", "StartMenuInternet", AppName);
            foreach (var scheme in Schemes)
            {
                _registry.SetString($@"{ClientKey}\Capabilities\URLAssociations", scheme, ProgId);
            }

            _registry.SetString(ProgIdKey, null, "Preferis URL");
            _registry.SetString(ProgIdKey, "URL Protocol", "");
            _registry.SetString($@"{ProgIdKey}\DefaultIcon", null, $"{_routerPath},0");
            // Everything after --single-argument is the URL, so that a crafted URL cannot pass options (as Edge and Chrome do).
            _registry.SetString($@"{ProgIdKey}\shell\open\command", null, $"{quoted} --single-argument %1");

            _registry.SetString(RegisteredApplications, AppName, CapabilitiesPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new RegistrationResult(false, $"Could not write the registration: {ex.Message}");
        }
        _notifyAssociationsChanged();

        if (Schemes.All(IsChosen))
        {
            return new RegistrationResult(true, "Preferis is the default browser.");
        }
        _openUri(DefaultAppsPage);
        return new RegistrationResult(true, "Preferis is registered. Choose it as the default browser in the Settings page that opened.");
    }

    public RegistrationResult Unregister()
    {
        try
        {
            _registry.DeleteTree(ClientKey);
            _registry.DeleteTree(ProgIdKey);
            _registry.DeleteValue(RegisteredApplications, AppName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new RegistrationResult(false, $"Could not remove the registration: {ex.Message}");
        }
        _notifyAssociationsChanged();
        return new RegistrationResult(true, "Preferis was unregistered. Windows asks which browser to use for the next link.");
    }

    public void OpenSystemSettings() => _openUri(DefaultAppsPage);

    private bool IsChosen(string scheme) => string.Equals(_associations.GetProgId(scheme), ProgId, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void OpenUri(string uri)
    {
        using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    // Explorer and the Settings app cache associations; tell them to read the registry again.
    [SupportedOSPlatform("windows")]
    private static void NotifyAssociationsChanged() => Native.SHChangeNotify(Native.ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
}
