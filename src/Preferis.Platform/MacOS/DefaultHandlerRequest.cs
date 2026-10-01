namespace Preferis.Platform.MacOS;

/// <summary>
/// Decides, scheme by scheme, how to become the default URL handler. Kept apart from the Launch Services calls so the
/// rules can be tested: what counts is the state afterwards, not what the calls report. Confirming the browser change
/// for http makes macOS switch https as well, after which the request for https reports an error and the deprecated
/// fallback is refused (-54) although Preferis already handles https.
/// </summary>
internal static class DefaultHandlerRequest
{
    /// <param name="isDefault">Whether Preferis currently handles the scheme.</param>
    /// <param name="request">The macOS 12 request (the system may ask the user); returns its error, or null.</param>
    /// <param name="legacy">The deprecated <c>LSSetDefaultHandlerForURLScheme</c>; returns its status.</param>
    /// <returns>Why a scheme is still not handled by Preferis, or null when all are.</returns>
    public static string? Run(IReadOnlyList<string> schemes, Func<string, bool> isDefault, Func<string, string?> request, Func<string, int> legacy)
    {
        foreach (var scheme in schemes)
        {
            if (isDefault(scheme))
            {
                continue;
            }

            // A request without error is trusted: Launch Services may show the change only a moment later.
            var error = request(scheme);
            if (error is null || isDefault(scheme))
            {
                continue;
            }

            var status = legacy(scheme);
            if (!isDefault(scheme))
            {
                return $"Could not become the default handler for {scheme}: {error} (fallback LSSetDefaultHandlerForURLScheme returned {status})";
            }
        }
        return null;
    }
}
