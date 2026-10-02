using Preferis.Core.Picker;
using Preferis.Platform.Launching;

namespace Preferis.Platform.Tests.Launching;

public class ProcessPickerLauncherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("preferis-picker-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PickerRequest Request() => new()
    {
        Url = "https://example.com/",
        Candidates = [new PickerCandidate { ProfileId = "edge", Name = "Edge" }, new PickerCandidate { ProfileId = "chrome", Name = "Chrome" }],
    };

    /// <summary>A stand-in picker: a shell script on macOS, a batch file on Windows (CreateProcess runs it through cmd).</summary>
    private string FakePicker(string sh, string cmd)
    {
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(_dir, "picker.cmd");
            File.WriteAllText(batch, "@echo off\r\n" + cmd + "\r\n");
            return batch;
        }
        var path = Path.Combine(_dir, "picker.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + sh + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void Writes_the_request_to_stdin_and_reads_the_response_from_stdout()
    {
        var echoed = Path.Combine(_dir, "request.json");
        var picker = FakePicker(
            $"cat > '{echoed}'\necho '{{\"result\":\"selected\",\"profileId\":\"chrome\",\"remember\":true}}'",
            // findstr "^" copies every line of its input.
            $"findstr \"^\" > \"{echoed}\"\r\necho {{\"result\":\"selected\",\"profileId\":\"chrome\",\"remember\":true}}");
        var stderr = new List<string>();

        var response = new ProcessPickerLauncher(picker, TimeSpan.FromSeconds(10), stderr.Add).Show(Request());

        Assert.NotNull(response);
        Assert.Equal(PickerResult.Selected, response.Result);
        Assert.Equal("chrome", response.ProfileId);
        Assert.True(response.Remember);
        Assert.Contains("\"url\": \"https://example.com/\"", File.ReadAllText(echoed));
    }

    [Fact]
    public void The_started_callback_gets_the_picker_process()
    {
        var picker = FakePicker("cat > /dev/null; echo '{\"result\":\"cancelled\"}'", "findstr \"^\" > nul\r\necho {\"result\":\"cancelled\"}");
        var started = new List<int>();

        var response = new ProcessPickerLauncher(picker, TimeSpan.FromSeconds(10), null, started.Add).Show(Request());

        Assert.Equal(PickerResult.Cancelled, response?.Result);
        Assert.True(Assert.Single(started) > 0);
    }

    [Fact]
    public void The_answer_counts_as_soon_as_stdout_closes_even_while_the_picker_animates_out()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // cmd cannot close its stdout and go on; the WinUI picker does it with CloseHandle.
        }

        // The picker answers, closes stdout and keeps running for its closing animation.
        var picker = FakePicker("cat > /dev/null; echo '{\"result\":\"selected\",\"profileId\":\"edge\"}'; exec 1>&-; sleep 3", "");
        var started = System.Diagnostics.Stopwatch.StartNew();

        var response = new ProcessPickerLauncher(picker, TimeSpan.FromSeconds(10), null).Show(Request());

        Assert.Equal("edge", response?.ProfileId);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1.5), $"Took {started.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void A_picker_that_never_answers_is_killed_after_the_timeout()
    {
        var picker = FakePicker("cat > /dev/null; sleep 30", "findstr \"^\" > nul\r\nping -n 30 127.0.0.1 > nul");
        var started = System.Diagnostics.Stopwatch.StartNew();

        Assert.Null(new ProcessPickerLauncher(picker, TimeSpan.FromSeconds(1), null).Show(Request()));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"Took {started.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void Garbage_or_a_foreign_profile_yields_null()
    {
        Assert.Null(new ProcessPickerLauncher(FakePicker("cat > /dev/null; echo 'oops'", "findstr \"^\" > nul\r\necho oops"), TimeSpan.FromSeconds(10), null).Show(Request()));
        Assert.Null(new ProcessPickerLauncher(
            FakePicker("cat > /dev/null; echo '{\"result\":\"selected\",\"profileId\":\"firefox\"}'", "findstr \"^\" > nul\r\necho {\"result\":\"selected\",\"profileId\":\"firefox\"}"),
            TimeSpan.FromSeconds(10),
            null).Show(Request()));
    }

    [Fact]
    public void A_missing_picker_executable_yields_null_without_throwing()
    {
        Assert.Null(new ProcessPickerLauncher(Path.Combine(_dir, "nope"), TimeSpan.FromSeconds(1), null).Show(Request()));
    }

    [Fact]
    public void Stderr_of_the_picker_is_forwarded_to_the_log()
    {
        var lines = new List<string>();
        var picker = FakePicker(
            "cat > /dev/null; echo 'picker window-shown +120 ms' >&2; echo '{\"result\":\"cancelled\"}'",
            "findstr \"^\" > nul\r\necho picker window-shown +120 ms 1>&2\r\necho {\"result\":\"cancelled\"}");

        var response = new ProcessPickerLauncher(picker, TimeSpan.FromSeconds(10), lines.Add).Show(Request());

        Assert.Equal(PickerResult.Cancelled, response?.Result);
        Assert.Contains(lines, l => l.Contains("window-shown", StringComparison.Ordinal));
    }
}
