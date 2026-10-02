using System.Runtime.Versioning;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;

namespace Preferis.Platform.Windows;

/// <summary>
/// Last-resort dialog: <c>MessageBoxW</c>, which needs no UI framework (docs/Design.md §8). Offers to copy the URL to the
/// clipboard so that it is not lost.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUserNotifier : IUserNotifier
{
    public void ShowError(string title, string message, RoutingUrl? url)
    {
        var text = url is null ? message : $"{message}\n\n{url.AbsoluteUri}\n\nCopy the URL to the clipboard?";
        var buttons = url is null ? 0 : Native.MbYesNo;
        var answer = Native.MessageBox(IntPtr.Zero, text, title, buttons | Native.MbIconError | Native.MbSetForeground | Native.MbTopmost);
        if (url is not null && answer == Native.IdYes)
        {
            CopyToClipboard(url.AbsoluteUri);
        }
    }

    /// <summary>Puts text on the clipboard. Needs a window to own the clipboard, so a hidden one is made for the moment.</summary>
    public static unsafe bool CopyToClipboard(string text)
    {
        const int MessageOnlyParent = -3;
        // Without an owner window EmptyClipboard leaves the clipboard ownerless and SetClipboardData fails.
        var owner = Native.CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, MessageOnlyParent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (owner == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            if (!Native.OpenClipboard(owner))
            {
                return false;
            }
            try
            {
                Native.EmptyClipboard();
                var bytes = (nuint)((text.Length + 1) * sizeof(char));
                var memory = Native.GlobalAlloc(Native.GmemMoveable, bytes);
                if (memory == IntPtr.Zero)
                {
                    return false;
                }
                var target = (char*)Native.GlobalLock(memory);
                text.AsSpan().CopyTo(new Span<char>(target, text.Length));
                target[text.Length] = '\0';
                Native.GlobalUnlock(memory);
                if (Native.SetClipboardData(Native.CfUnicodeText, memory) == IntPtr.Zero)
                {
                    // Still ours only when the clipboard did not take it.
                    Native.GlobalFree(memory);
                    return false;
                }
                return true;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }
        finally
        {
            Native.DestroyWindow(owner);
        }
    }
}
