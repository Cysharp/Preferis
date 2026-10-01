using System.Diagnostics;
using System.Runtime.Versioning;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;

namespace Preferis.Platform.MacOS;

/// <summary>The <c>system</c> action on macOS: hand the URL to Launch Services' default handler, unless that is us.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacSystemOpener : ISystemOpener
{
    public bool IsSelfDefaultHandler(string scheme)
    {
        using var pool = ObjC.AutoreleasePool();
        var handlerPath = ObjC.DefaultApplicationPathForUrl($"{scheme}://example.com/");
        if (handlerPath is null)
        {
            return false;
        }

        var selfPath = ObjC.MainBundlePath;
        var selfId = ObjC.MainBundleIdentifier;
        var handlerId = ObjC.BundleIdentifier(ObjC.BundleWithPath(handlerPath));
        return (selfId is not null && string.Equals(handlerId, selfId, StringComparison.OrdinalIgnoreCase))
            || (selfPath is not null && string.Equals(handlerPath, selfPath, StringComparison.Ordinal));
    }

    public void OpenWithSystemDefault(RoutingUrl url)
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        info.ArgumentList.Add(url.AbsoluteUri);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("open could not be started.");
    }
}
