using Preferis.Core;
using Preferis.Core.Profiles;
using Preferis.Platform.Launching;

namespace Preferis.Platform.Tests.Launching;

public class LaunchPlanExecutorTests
{
    [Fact]
    public void Execute_plans_start_the_binary_with_the_arguments_verbatim()
    {
        var plan = new LaunchPlan(LaunchKind.Execute, "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", ["--profile-directory=Profile 1", "https://example.com/?a=1&b=2"], null, "Profile 1");

        var info = LaunchPlanExecutor.CreateStartInfo(plan, HostPlatform.MacOS);

        Assert.Equal(plan.Path, info.FileName);
        Assert.Equal(["--profile-directory=Profile 1", "https://example.com/?a=1&b=2"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.False(info.RedirectStandardOutput);
    }

    [Fact]
    public void Open_with_application_plans_use_open_on_macos()
    {
        var plan = new LaunchPlan(LaunchKind.OpenWithApplication, "/Applications/Safari.app", ["https://example.com/"], null, null);

        var info = LaunchPlanExecutor.CreateStartInfo(plan, HostPlatform.MacOS);

        Assert.Equal("/usr/bin/open", info.FileName);
        Assert.Equal(["-a", "/Applications/Safari.app", "https://example.com/"], info.ArgumentList);
    }

    [Fact]
    public void Open_with_application_plans_execute_the_binary_on_windows()
    {
        var plan = new LaunchPlan(LaunchKind.OpenWithApplication, @"C:\Browsers\contoso.exe", ["https://example.com/"], null, null);

        var info = LaunchPlanExecutor.CreateStartInfo(plan, HostPlatform.Windows);

        Assert.Equal(@"C:\Browsers\contoso.exe", info.FileName);
        Assert.Equal(["https://example.com/"], info.ArgumentList);
    }
}
