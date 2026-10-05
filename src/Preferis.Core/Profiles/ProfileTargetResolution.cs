using Preferis.Core.Browsers;
using Preferis.Core.Configuration;

namespace Preferis.Core.Profiles;

/// <summary>A target that can open a URL on this machine, before URL-specific arguments are built.</summary>
public abstract record ResolvedProfileTarget
{
    public sealed record Browser(DetectedBrowser Installation, string? ProfileDirectory) : ResolvedProfileTarget;

    public sealed record Command(CommandSpec Spec) : ResolvedProfileTarget;
}

/// <summary>Why target resolution failed. Presentation layers can explain these without repeating the resolution rules.</summary>
public enum ProfileTargetFailure
{
    ProfileMissing,
    TargetMissing,
    CommandMissingForPlatform,
    BrowserMissingForPlatform,
    BrowserNotInstalled,
    BrowserProfileNotFound,
    BrowserProfileDataUnavailable,
    BrowserProfileNameMissing,
}

/// <summary>The URL-independent part of profile resolution, shared by routing and the settings availability display.</summary>
public abstract record ProfileTargetResolution
{
    /// <param name="Profile">The profile actually used, including when a fallback was taken.</param>
    public sealed record Resolved(Profile Profile, ResolvedProfileTarget Target, IReadOnlyList<string> Warnings) : ProfileTargetResolution;

    public sealed record Unresolved(ProfileTargetFailure Failure, string Reason) : ProfileTargetResolution;
}
