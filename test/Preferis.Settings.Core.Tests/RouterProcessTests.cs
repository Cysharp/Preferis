using System.Diagnostics;
using Preferis.Settings.Services;

namespace Preferis.Settings.Core.Tests;

public class RouterProcessTests
{
    [Fact]
    public async Task Output_is_collected_until_the_router_exits_even_when_a_launched_program_keeps_it_open()
    {
        // The Router exits right after starting the browser, which may inherit its stdout and run for hours.
        var (shell, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "echo routed & start /b ping -n 8 127.0.0.1" })
            : ("/bin/sh", new[] { "-c", "echo routed; sleep 8 &" });
        var started = Stopwatch.StartNew();

        var result = await RouterProcess.RunAsync(shell, arguments);

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(4), $"Took {started.Elapsed.TotalMilliseconds} ms");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("routed", result.Output);
    }

    [Fact]
    public async Task A_missing_router_is_reported()
    {
        var result = await RouterProcess.RunAsync(Path.Combine(Path.GetTempPath(), "no-such-router"), "--register");

        Assert.Equal(2, result.ExitCode);
        Assert.Single(result.Output);
    }
}
