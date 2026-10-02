using System.Diagnostics;
using System.Runtime.Versioning;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Platform.MacOS;

/// <summary>
/// Last-resort dialog. Uses <c>osascript</c> so it works from any thread and without an AppKit run loop;
/// an in-process NSAlert can replace it later. Offers to copy the URL to the clipboard.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacUserNotifier : IUserNotifier
{
    public void ShowError(string title, string message, RoutingUrl? url)
    {
        var text = url is null ? message : $"{message}\n\n{url.AbsoluteUri}";
        var (copy, ok) = (L("Copy URL"), L("OK"));
        var buttons = url is null ? $"{{{Quote(ok)}}}" : $"{{{Quote(copy)}, {Quote(ok)}}}";
        var script = $"display dialog {Quote(text)} with title {Quote(title)} buttons {buttons} default button {Quote(ok)} with icon stop";

        var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardOutput = true };
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add(script);
        try
        {
            using var process = Process.Start(info);
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(120_000);
            if (url is not null && output.Contains(copy, StringComparison.Ordinal))
            {
                CopyToClipboard(url.AbsoluteUri);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"{title}: {message} {url?.AbsoluteUri}");
        }
    }

    private static void CopyToClipboard(string text)
    {
        var info = new ProcessStartInfo("/usr/bin/pbcopy") { UseShellExecute = false, RedirectStandardInput = true };
        using var process = Process.Start(info);
        if (process is not null)
        {
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(5_000);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
