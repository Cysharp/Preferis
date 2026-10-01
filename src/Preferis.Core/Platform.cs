namespace Preferis.Core;

/// <summary>Operating systems Preferis runs on. Used wherever configuration or resolution is OS specific.</summary>
public enum Platform
{
    Windows,
    MacOS,
}

public static class PlatformInfo
{
    /// <summary>The platform the current process runs on.</summary>
    public static Platform Current => OperatingSystem.IsWindows() ? Platform.Windows : Platform.MacOS;
}
