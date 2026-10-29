namespace Preferis.Platform.Windows;

/// <summary>
/// The Velopack installation that <c>Preferis.exe</c>, the picker and the settings may run from on Windows
/// (docs/Platform.md §2.1): <c>%LocalAppData%\Preferis\current\</c>, with Velopack's <c>Update.exe</c> in the folder
/// above. The portable zip and a local build have no <c>Update.exe</c>, and Velopack plays no part in them.
/// </summary>
public static class VelopackInstallation
{
    /// <summary>Whether this executable runs from a Velopack installation.</summary>
    public static bool IsInstalled { get; } = OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));

    /// <summary>
    /// Velopack's start-up code, the first thing every executable runs. Installing, updating and uninstalling start
    /// <c>Preferis.exe</c> with a hook argument (<c>--veloapp-install &lt;version&gt;</c> and the like) and expect it to
    /// exit within seconds: this exits. Outside an installation it does nothing at all, so that a link click costs what
    /// it did: Velopack would otherwise look for an installation and append to a log in <c>%LocalAppData%\velopack</c>
    /// on every start.
    /// </summary>
    /// <param name="applyPendingUpdate">
    /// Whether an update that was downloaded but not installed yet is installed now, which restarts Preferis. The
    /// settings do, so that no update is left behind; the Router and the picker do not, so that no link waits for one.
    /// </param>
    public static void Start(bool applyPendingUpdate)
    {
#if VELOPACK
        if (IsInstalled)
        {
            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(applyPendingUpdate).Run();
        }
#endif
    }
}
