using System.Runtime.Versioning;

namespace Preferis.Platform.Windows;

/// <summary>Which program Windows opens URLs of a scheme with. Faked in the tests.</summary>
public interface IUrlAssociations
{
    /// <summary>The ProgId the user chose for <paramref name="scheme"/> (e.g. <c>MSEdgeHTM</c>), or null.</summary>
    string? GetProgId(string scheme);

    /// <summary>The handler's display name (e.g. "Microsoft Edge"), or null.</summary>
    string? GetHandlerName(string scheme);
}

/// <summary>
/// Asks the shell (<c>AssocQueryString</c>) rather than reading <c>UserChoice</c> from the registry: the shell knows the
/// effective choice, including where newer Windows versions keep it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsUrlAssociations : IUrlAssociations
{
    public static WindowsUrlAssociations Instance { get; } = new();

    public string? GetProgId(string scheme) => Query(scheme, Native.AssocstrProgId);

    public string? GetHandlerName(string scheme) => Query(scheme, Native.AssocstrFriendlyAppName);

    private static string? Query(string scheme, int what)
    {
        const int Capacity = 1024;
        var buffer = stackalloc char[Capacity];
        uint length = Capacity;
        var result = Native.AssocQueryString(Native.AssocfIsProtocol, what, scheme, null, buffer, ref length);
        if (result != 0 || length <= 1)
        {
            return null;
        }
        // The length includes the terminating null.
        return new string(buffer, 0, (int)length - 1);
    }
}
