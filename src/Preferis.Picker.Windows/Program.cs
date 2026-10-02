using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Preferis.Core.Picker;

namespace Preferis.Picker.Windows;

/// <summary>
/// <c>Preferis.Picker.exe</c>: reads one <see cref="PickerRequest"/> (stdin, or <c>--request &lt;path&gt;</c>), shows the
/// window, writes one <see cref="PickerResponse"/> to stdout and exits with 0. Anything else (bad request, crash)
/// exits without an answer and the Router falls back (docs/Design.md §8, §9.3). Log lines go to stderr, which the
/// Router forwards to its trace log.
/// </summary>
internal static partial class Program
{
    private const int StdOutputHandle = -11;
    private static readonly bool Trace = Environment.GetEnvironmentVariable("PREFERIS_TRACE") == "1";

    [STAThread]
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
                _ => throw new ArgumentException("Usage: Preferis.Picker.exe [--request <path>]   (the request JSON is read from stdin by default)"),
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

        // Meanwhile WinUI starts, which takes as long.
        var images = CandidateImages.StartLoading(request);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(request, images);
        });
        return 0;
    }

    /// <summary>
    /// Writes the answer and closes stdout: the Router reads it at end of file and opens the browser while this window
    /// is still closing, instead of waiting for the process to exit.
    /// </summary>
    public static void Answer(PickerResponse response)
    {
        Console.Out.WriteLine(PickerMessages.Serialize(response));
        Console.Out.Flush();
        // Disposing Console.Out does not close the handle; the Router needs the end of file.
        var handle = GetStdHandle(StdOutputHandle);
        if (handle != IntPtr.Zero && handle != -1)
        {
            SetStdHandle(StdOutputHandle, IntPtr.Zero);
            CloseHandle(handle);
        }
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

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int stdHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int stdHandle, IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
