using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppKit;
using CoreGraphics;
using Foundation;

namespace Preferis.Spikes.PickerHelper;

// Spike 5 + 2 (Design.md §12): an AppKit picker written with Microsoft.macOS, published with
// NativeAOT as a helper bundle. Reads a PickerRequest (JSON) from stdin, shows a panel with one
// button per candidate, writes a PickerResponse (JSON) to stdout and exits.
//
// For unattended runs the request may set autoSelectSeconds > 0: the first candidate is chosen
// automatically after that delay, after logging whether the helper actually became the active
// application (the question spike 2 asks).
internal static class Program
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly DateTime MainEntered = DateTime.UtcNow;

    public static void Main(string[] args)
    {
        Log("main-entered", MainEntered);
        NSApplication.Init();
        Log("nsapplication-init", DateTime.UtcNow);
        var app = NSApplication.SharedApplication;
        app.Delegate = new AppDelegate();
        app.Run();
    }

    public static void Log(string phase, DateTime at)
        => Console.Error.WriteLine($"picker {phase,-22} +{(at - ProcessStart).TotalMilliseconds,7:F1} ms from process start");

    public static void Log(string message) => Console.Error.WriteLine($"picker {message}");
}

internal sealed class AppDelegate : NSApplicationDelegate
{
    private NSWindow? _window;
    private PickerRequest _request = new();

    public override void DidFinishLaunching(NSNotification notification)
    {
        Program.Log("did-finish-launching", DateTime.UtcNow);

        try
        {
            var json = Console.In.ReadToEnd();
            _request = JsonSerializer.Deserialize(json, PickerJsonContext.Default.PickerRequest) ?? new PickerRequest();
        }
        catch (Exception ex)
        {
            Program.Log($"request parse error: {ex.Message}");
        }

        var candidates = _request.Candidates ?? [];
        var stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 8,
            EdgeInsets = new NSEdgeInsets(16, 16, 16, 16),
        };
        stack.AddArrangedSubview(new NSTextField { StringValue = _request.Url ?? "", Editable = false, Bordered = false, DrawsBackground = false });
        foreach (var candidate in candidates)
        {
            var id = candidate.ProfileId ?? "";
            stack.AddArrangedSubview(NSButton.CreateButton(candidate.Name ?? id, () => Select(id)));
        }
        stack.AddArrangedSubview(NSButton.CreateButton("Cancel", Cancel));

        _window = new NSWindow(new CGRect(0, 0, 420, 80 + 36 * (candidates.Count + 1)),
            NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
        {
            Title = "Preferis Picker Spike",
            Level = NSWindowLevel.Floating,
            ContentView = stack,
        };
        _window.Center();
        _window.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        Program.Log("window-shown", DateTime.UtcNow);

        // Report activation state shortly after showing, then auto-select if requested.
        NSTimer.CreateScheduledTimer(0.3, _ =>
        {
            var app = NSApplication.SharedApplication;
            Program.Log($"activation: isActive={app.Active} isKeyWindow={_window.IsKeyWindow} frontmost={NSWorkspace.SharedWorkspace.FrontmostApplication?.BundleIdentifier}");
        });
        if (_request.AutoSelectSeconds > 0)
        {
            NSTimer.CreateScheduledTimer(_request.AutoSelectSeconds, _ =>
            {
                if (candidates.Count > 0) Select(candidates[0].ProfileId ?? ""); else Cancel();
            });
        }
    }

    private void Select(string profileId)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new PickerResponse { Result = "selected", ProfileId = profileId }, PickerJsonContext.Default.PickerResponse));
        Console.Out.Flush();
        NSApplication.SharedApplication.Terminate(this);
    }

    private void Cancel()
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new PickerResponse { Result = "cancelled" }, PickerJsonContext.Default.PickerResponse));
        Console.Out.Flush();
        NSApplication.SharedApplication.Terminate(this);
    }
}

internal sealed class PickerRequest
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("candidates")] public List<PickerCandidate>? Candidates { get; set; }
    [JsonPropertyName("autoSelectSeconds")] public double AutoSelectSeconds { get; set; }
}

internal sealed class PickerCandidate
{
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class PickerResponse
{
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("profileId")] public string? ProfileId { get; set; }
}

[JsonSerializable(typeof(PickerRequest))]
[JsonSerializable(typeof(PickerResponse))]
internal sealed partial class PickerJsonContext : JsonSerializerContext;
