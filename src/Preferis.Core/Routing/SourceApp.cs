namespace Preferis.Core.Routing;

/// <summary>
/// The application that opened the URL, as far as the platform could tell; the app conditions match against it
/// (docs/Design.md §6.2). Either part may be unknown: a Windows program outside a package has no id.
/// </summary>
/// <param name="Id">macOS: the bundle id. Windows: the application user model id of a packaged app.</param>
/// <param name="Path">macOS: the <c>.app</c> bundle. Windows: the executable.</param>
public sealed record SourceApp(string? Id, string? Path);
