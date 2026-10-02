using System.Diagnostics;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;

namespace Preferis.Platform.Windows;

/// <summary>The <c>system</c> action on Windows: hand the URL to the shell's default handler, unless that is us.</summary>
public sealed class WindowsSystemOpener : ISystemOpener
{
    private readonly IUrlAssociations _associations;

    public WindowsSystemOpener(IUrlAssociations associations)
    {
        _associations = associations;
    }

    public bool IsSelfDefaultHandler(string scheme)
        => string.Equals(_associations.GetProgId(scheme), WindowsDefaultBrowserRegistrar.ProgId, StringComparison.OrdinalIgnoreCase);

    public void OpenWithSystemDefault(RoutingUrl url)
    {
        using var process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}
