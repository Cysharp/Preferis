namespace Preferis.Core;

/// <summary>Operating systems Preferis runs on. Used wherever configuration or resolution is OS specific.</summary>
public enum HostPlatform
{
    Windows,
    MacOS,
}

public static class PlatformInfo
{
    /// <summary>The platform the current process runs on.</summary>
    public static HostPlatform Current => OperatingSystem.IsWindows() ? HostPlatform.Windows : HostPlatform.MacOS;
}
