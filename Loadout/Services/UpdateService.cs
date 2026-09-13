using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Loadout.Services;

/// <summary>
/// Auto-update vía Velopack + GitHub Releases.
///
/// Canal GitHub (Setup.exe). El canal Store/MSIX se actualiza por Store,
/// este servicio solo actúa cuando la app fue instalada con Velopack
/// (es no-op en debug o en el .exe suelto / MSIX).
/// </summary>
public static class UpdateService
{
    public const string RepoUrl = "https://github.com/Daniel-Cochachi/Loadout-Booster";

    private static UpdateManager CreateManager()
        => new(new GithubSource(RepoUrl, null, false));

    /// <summary>¿Esta instalación es Velopack (puede auto-actualizarse)?</summary>
    public static bool IsVelopackInstall
    {
        get
        {
            try { return CreateManager().IsInstalled; }
            catch { return false; }
        }
    }

    public static string CurrentVersion
    {
        get
        {
            try
            {
                return CreateManager().CurrentVersion?.ToString()
                    ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                    ?? "1.0.0";
            }
            catch
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            }
        }
    }

    /// <summary>
    /// Busca, descarga y aplica update. Devuelve mensaje para StatusLine.
    /// Si nothingToDo devuelve null.
    /// </summary>
    /// <param name="askRestart">Pregunta al usuario antes de reiniciar para aplicar.</param>
    public static async Task<string?> CheckAndApplyAsync(Func<string, Task<bool>>? askRestart = null)
    {
        if (!IsVelopackInstall) return null;
        try
        {
            var mgr = CreateManager();
            var update = await mgr.CheckForUpdatesAsync();
            if (update == null) return null;

            await mgr.DownloadUpdatesAsync(update);

            bool restart = true;
            if (askRestart != null)
            {
                try { restart = await askRestart("Hay una actualización disponible. ¿Reiniciar para aplicarla?"); }
                catch { restart = true; }
            }
            if (restart) mgr.ApplyUpdatesAndRestart(update);
            return "⮮ Actualización lista. Reinicia para aplicarla.";
        }
        catch (Exception ex)
        {
            Log.Error("CheckAndApplyAsync", ex);
            return null;
        }
    }
}
