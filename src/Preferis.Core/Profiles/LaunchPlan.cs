using Preferis.Core.Browsers;

namespace Preferis.Core.Profiles;

public enum LaunchKind
{
    /// <summary>Run <see cref="LaunchPlan.Path"/> with <see cref="LaunchPlan.Arguments"/>; the URL is one of the arguments.</summary>
    Execute,

    /// <summary>Ask the OS to open the URL (the single argument) with the application at <see cref="LaunchPlan.Path"/> (<c>open -a</c> on macOS).</summary>
    OpenWithApplication,
}

/// <summary>What the Platform layer must do to open a URL with a resolved profile.</summary>
public sealed record LaunchPlan(
    LaunchKind Kind,
    string Path,
    IReadOnlyList<string> Arguments,
    DetectedBrowser? Browser,
    string? BrowserProfileDirectory);

public abstract record ResolveResult
{
    /// <param name="ProfileId">The profile actually used; differs from the requested one when a fallback was taken.</param>
    public sealed record Resolved(LaunchPlan Plan, IReadOnlyList<string> Warnings, string ProfileId) : ResolveResult;

    public sealed record Unresolved(string Reason) : ResolveResult;
}
