using System.Diagnostics;
using System.Runtime.InteropServices;
using AppKit;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Platform;

namespace Preferis.Picker.MacOS;

/// <summary>
/// <c>Preferis Picker</c>: reads one <see cref="PickerRequest"/> (stdin, or <c>--request &lt;path&gt;</c>), shows the
/// dialog, writes one <see cref="PickerResponse"/> to stdout and exits with 0. Anything else (bad request, crash)
/// exits without an answer and the Router falls back (docs/Design.md §8, §9.3). Log lines go to stderr, which the
/// Router forwards to its trace log.
/// </summary>
internal static class Program
{
    private static readonly bool Trace = Environment.GetEnvironmentVariable("PREFERIS_TRACE") == "1";

    private static int Main(string[] args)
    {
        Mark("main");
        string json;
        try
        {
            json = args switch
            {
                ["--request", var path] => File.ReadAllText(path),
                [] => Console.In.ReadToEnd(),
                _ => throw new ArgumentException("Usage: \"Preferis Picker\" [--request <path>]   (the request JSON is read from stdin by default)"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var request = PickerMessages.DeserializeRequest(json);
        if (request is null)
        {
            Console.Error.WriteLine("The request is not a valid picker request.");
            return 1;
        }

        UiLanguages.Current = PickerMessages.LanguageOf(request, SystemLanguages.Preferred);

        NSApplication.Init();
        Mark("appkit");
        var app = NSApplication.SharedApplication;
        app.Delegate = new AppDelegate(request);
        app.Run();
        return 0;
    }

    /// <summary>Timestamps relative to process start for the startup measurements of docs/Platform.md §4.</summary>
    public static void Mark(string phase)
    {
        if (Trace)
        {
            var sinceStart = DateTime.Now - Process.GetCurrentProcess().StartTime;
            Console.Error.WriteLine($"picker {phase,-8} +{sinceStart.TotalMilliseconds:F1} ms");
        }
    }
}

internal sealed partial class AppDelegate : NSApplicationDelegate
{
    private readonly PickerRequest _request;
    private PickerWindow? _window;

    public AppDelegate(PickerRequest request)
    {
        _request = request;
    }

    public override void DidFinishLaunching(Foundation.NSNotification notification)
    {
        try
        {
            _window = new PickerWindow(_request, Answer, Quit);
            _window.Show();
            Program.Mark("shown");
        }
        catch (Exception ex)
        {
            // AppKit would log and swallow the exception, leaving an invisible process the Router waits on.
            // Exiting without an answer lets the Router fall back right away (docs/Design.md §8).
            Console.Error.WriteLine($"The picker could not be shown: {ex}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Writes the answer and closes stdout: the Router reads it at end of file and opens the browser while the
    /// window is still animating out, instead of waiting for this process to exit.
    /// </summary>
    private static void Answer(PickerResponse response)
    {
        Console.Out.WriteLine(PickerMessages.Serialize(response));
        Console.Out.Flush();
        close(1);
    }

    private static void Quit() => NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);

    // Disposing Console.Out does not close the descriptor; the Router needs the end of file.
    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int close(int fd);
}
