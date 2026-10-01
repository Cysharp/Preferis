using System.Diagnostics;
using Preferis.Core;
using Preferis.Core.Hosting;
using Preferis.Core.Profiles;

namespace Preferis.Platform.Launching;

/// <summary>Starts the process described by a <see cref="LaunchPlan"/> and does not wait for it.</summary>
public sealed class LaunchPlanExecutor : IBrowserLauncher
{
    private readonly HostPlatform _platform;

    public LaunchPlanExecutor(HostPlatform platform)
    {
        _platform = platform;
    }

    public void Launch(LaunchPlan plan)
    {
        using var process = Process.Start(CreateStartInfo(plan, _platform))
            ?? throw new InvalidOperationException($"The process could not be started: {plan.Path}");
    }

    /// <summary>Arguments are passed one by one (never joined into a string) so URLs are not re-quoted.</summary>
    public static ProcessStartInfo CreateStartInfo(LaunchPlan plan, HostPlatform platform)
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        if (plan.Kind == LaunchKind.OpenWithApplication && platform == HostPlatform.MacOS)
        {
            // Safari and friends do not take URLs on the command line; Launch Services delivers them as Apple Events.
            info.FileName = "/usr/bin/open";
            info.ArgumentList.Add("-a");
            info.ArgumentList.Add(plan.Path);
        }
        else
        {
            info.FileName = plan.Path;
        }
        foreach (var argument in plan.Arguments)
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }
}
