using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Loadout.Models;

namespace Loadout.Services;

/// <summary>
/// Motor JUGAR/CERRAR. Lanza en orden con delay sin bloquear la UI,
/// guarda PIDs de la sesión y solo mata esa sesión. No duplica procesos ya vivos.
/// </summary>
public sealed class LaunchService
{
    private readonly List<Process> _session = new();
    private readonly HashSet<string> _sessionNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Master switch: si false, nadie recibe prioridad aunque el slot la tenga marcada.</summary>
    public bool HighPriority { get; set; }

    /// <summary>Nombres de procesos hijos que heredan prioridad (cliente -&gt; partida real).</summary>
    private static readonly Dictionary<string, string[]> CompanionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // LoL: lobby -> partida real + launcher
        ["LeagueClientUx"] = new[] { "League of Legends", "RiotClientServices", "RiotClientElectron" },
        ["RiotClientServices"] = new[] { "LeagueClientUx", "LeagueClient", "League of Legends", "VALORANT", "VALORANT-Win64-Shipping" },
        ["RiotClientElectron"] = new[] { "LeagueClientUx", "League of Legends", "VALORANT", "VALORANT-Win64-Shipping" },
        ["League of Legends"] = new[] { "LeagueClientUx", "RiotClientServices" },
        // Valorant: launcher -> juego
        ["VALORANT"] = new[] { "VALORANT-Win64-Shipping", "RiotClientServices", "Vanguard" },
        ["VALORANT-Win64-Shipping"] = new[] { "VALORANT", "RiotClientServices" },
    };

    /// <summary>Nombres extra que el watcher debe mantener en High durante la sesión.</summary>
    private readonly HashSet<string> _priorityNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Process> SessionProcesses
    {
        get { lock (_lock) { return _session.Where(p => !p.HasExited).ToList(); } }
    }

    public void Track(Process p) { lock (_lock) { _session.Add(p); } }

    /// <summary>Rastrea juegos que ya estaban abiertos (no se relanzan, pero sí se cierran con la sesión).</summary>
    public void TrackRunning(LoadoutItem item)
    {
        try
        {
            if (item.Kind == "Url" || item.Kind == "Steam") return;
            string target = item.Target.Trim('"');
            if (!File.Exists(target)) return;
            string name = Path.GetFileNameWithoutExtension(target);
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (_lock) { _sessionNames.Add(name); }
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (!p.HasExited) Track(p); else p.Dispose(); }
                catch { try { p.Dispose(); } catch { } }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"TrackRunning({item.Name ?? item.Kind})", ex);
        }
    }

    public bool AlreadyRunning(LoadoutItem item)
    {
        try
        {
            if (item.Kind == "Url" || item.Kind == "Steam") return false;
            string name = Path.GetFileNameWithoutExtension(item.Target);
            if (string.IsNullOrWhiteSpace(name)) return false;
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// ¿Este item quiere prioridad? Regla: si el master está OFF -&gt; nadie.
    /// Si el master está ON y NINGÚN slot la tiene marcada -&gt; todos (compatibilidad v1.0).
    /// Si al menos uno la tiene marcada -&gt; solo esos.
    /// </summary>
    public static bool WantsPriority(LoadoutItem item, bool masterOn, bool anyMarked)
    {
        if (!masterOn || !item.Enabled) return false;
        if (item.Kind == "Url" || item.Kind == "Steam") return false;
        if (!anyMarked) return true;
        return item.HighPriority;
    }

    private static string ExeNameOf(LoadoutItem item)
    {
        try { return Path.GetFileNameWithoutExtension(item.Target.Trim('"')) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private void RememberPriorityNames(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return;
        lock (_lock) { _priorityNames.Add(exeName); }
        if (CompanionMap.TryGetValue(exeName, out var companions))
            lock (_lock) { foreach (var c in companions) _priorityNames.Add(c); }
    }

    private static void SetHigh(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.High;
        }
        catch { }
    }

    private static void SetHighByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.High; }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
    }

    /// <summary>
    /// Watcher: re-aplica High a la sesión + hijos (ej. League of Legends.exe que
    /// nace después del cliente). Llamar cada 2-3s mientras IsPlaying.
    /// </summary>
    public void MaintainPriorities()
    {
        if (!HighPriority) return;
        List<string> names;
        lock (_lock) { names = _priorityNames.ToList(); }
        if (names.Count == 0) return;
        foreach (string n in names) SetHighByName(n);
        foreach (int pid in GetLiveSessionIds()) SetHigh(pid);
    }

    /// <summary>Sube a prioridad Alta un juego que ya estaba abierto (solo si el slot la pide).</summary>
    public void BoostRunning(LoadoutItem item, bool wantPriority)
    {
        if (!wantPriority) return;
        try
        {
            if (item.Kind == "Url" || item.Kind == "Steam") return;
            string name = Path.GetFileNameWithoutExtension(item.Target);
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.High;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }
    }

    /// <summary>Juegos Riot (LoL/Valorant): su .exe no abre directo, van por Riot Client.</summary>
    private static readonly Dictionary<string, string> RiotProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeagueClientUx"] = "league_of_legends",
        ["VALORANT"] = "valorant"
    };

    private Process? TryRiotLaunch(LoadoutItem item, bool wantPriority)
    {
        try
        {
            string target = item.Target.Trim('"');
            string name = Path.GetFileNameWithoutExtension(target);
            if (!RiotProducts.TryGetValue(name, out string? product)) return null;
            string? riot = FindRiotClient(Path.GetDirectoryName(target));
            if (riot == null) return null;
            string args = $"--launch-product={product} --launch-patchline=live {(item.Arguments ?? string.Empty)}".Trim();
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = riot,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(riot)!
            });
            if (proc == null) return null;
            Track(proc);
            try
            {
                if (!string.IsNullOrWhiteSpace(name))
                    lock (_lock) { _sessionNames.Add(name); }
            }
            catch { }
            if (wantPriority)
            {
                try { proc.PriorityClass = ProcessPriorityClass.High; } catch { }
                RememberPriorityNames(name);
                RememberPriorityNames("RiotClientServices");
            }
            return proc;
        }
        catch { return null; }
    }

    private static string? FindRiotClient(string? startDir)
    {
        try
        {
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? dir = startDir;
            for (int i = 0; i < 3 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                foreach (string c in new[]
                {
                    Path.Combine(dir, "RiotClientServices.exe"),
                    Path.Combine(dir, "Riot Client", "RiotClientServices.exe")
                })
                {
                    if (tried.Add(c) && File.Exists(c)) return c;
                }
                dir = Path.GetDirectoryName(dir);
            }
            foreach (string c in new[]
            {
                @"C:\Riot Games\Riot Client\RiotClientServices.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games", "Riot Client", "RiotClientServices.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Riot Games", "Riot Client", "RiotClientServices.exe")
            })
            {
                if (tried.Add(c) && File.Exists(c)) return c;
            }
        }
        catch { }
        return null;
    }

    public Process? LaunchOne(LoadoutItem item, bool? wantPriorityOverride = null)
    {
        try
        {
            bool want = wantPriorityOverride ?? HighPriority;
            if (item.Kind == "Url")
            {
                var psi = new ProcessStartInfo { FileName = item.Target, UseShellExecute = true };
                var p = Process.Start(psi);
                if (p != null) Track(p);
                return p;
            }
            if (item.Kind == "Steam")
            {
                var psi = new ProcessStartInfo { FileName = $"steam://run/{item.Target}", UseShellExecute = true };
                var p = Process.Start(psi);
                if (p != null) Track(p);
                return p;
            }
            // Exe | File | Folder
            string target = item.Target.Trim('"');
            if (!File.Exists(target) && !Directory.Exists(target)) return null;
            var riotProc = TryRiotLaunch(item, want);
            if (riotProc != null) return riotProc;
            var start = new ProcessStartInfo
            {
                FileName = target,
                Arguments = item.Arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = File.Exists(target) ? Path.GetDirectoryName(target)! : target
            };
            var proc = Process.Start(start);
            if (proc != null)
            {
                Track(proc);
                try
                {
                    string exeName = Path.GetFileNameWithoutExtension(target);
                    if (!string.IsNullOrWhiteSpace(exeName))
                        lock (_lock) { _sessionNames.Add(exeName); }
                }
                catch { }
                if (want)
                {
                    try { proc.PriorityClass = ProcessPriorityClass.High; } catch { }
                    try { RememberPriorityNames(Path.GetFileNameWithoutExtension(target)); } catch { }
                }
            }
            return proc;
        }
        catch (Exception ex)
        {
            Log.Error($"LaunchOne({item.Name ?? item.Kind}|{item.Target})", ex);
            return null;
        }
    }

    public async Task LaunchSequenceAsync(IEnumerable<LoadoutItem> items, Action<LoadoutItem, bool> onItemLaunched, CancellationToken ct)
    {
        var list = items.OrderBy(i => i.OrderIndex).ToList();
        bool anyMarked = list.Any(i => i.Enabled && i.HighPriority);
        lock (_lock) { _priorityNames.Clear(); }
        foreach (var item in list)
        {
            if (ct.IsCancellationRequested) break;
            if (!item.Enabled) continue; // DESARMADO: se salta, LED queda gris
            bool want = WantsPriority(item, HighPriority, anyMarked);
            bool skipped = AlreadyRunning(item);
            if (!skipped) LaunchOne(item, want);
            else
            {
                if (want)
                {
                    BoostRunning(item, true);
                    RememberPriorityNames(ExeNameOf(item));
                }
                TrackRunning(item);
            }
            onItemLaunched(item, skipped);
            int delay = Math.Clamp(item.DelaySeconds, 0, 60);
            if (delay > 0) await Task.Delay(delay * 1000, ct).ContinueWith(_ => { });
        }
        // Primera pasada inmediata: atrapa hijos que ya existan (partida ya abierta)
        try { MaintainPriorities(); } catch { }
    }

    /// <summary>Polling ligero cada 3s: devuelve ids de procesos vivos de la sesión.</summary>
    public HashSet<int> GetLiveSessionIds()
    {
        lock (_lock)
        {
            var live = new HashSet<int>();
            foreach (var p in _session)
            {
                try { if (!p.HasExited) live.Add(p.Id); } catch { }
            }
            return live;
        }
    }

    public void CloseSession(int timeoutMs = 4000, bool killTree = false)
    {
        List<Process> copy;
        List<string> names;
        lock (_lock) { copy = _session.ToList(); _session.Clear(); names = _sessionNames.ToList(); _sessionNames.Clear(); _priorityNames.Clear(); }
        foreach (var p in copy)
        {
            int pid;
            try { if (p.HasExited) continue; pid = p.Id; }
            catch { continue; }
            // AUTO-KILL companions: taskkill /T mata el árbol (hijos incluidos)
            if (killTree)
            {
                try
                {
                    using var tk = Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill", Arguments = $"/PID {pid} /T /F",
                        UseShellExecute = false, CreateNoWindow = true
                    });
                    tk?.WaitForExit(6000);
                    continue;
                }
                catch { }
            }
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(timeoutMs))
                    p.Kill();
            }
            catch { try { if (!p.HasExited) p.Kill(); } catch { } }
        }
        // Barrido por nombre: atrapa hijos de launchers y juegos que ya estaban abiertos
        int self = Environment.ProcessId;
        foreach (string name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.Id == self || p.HasExited) continue;
                    if (killTree)
                    {
                        try
                        {
                            using var tk = Process.Start(new ProcessStartInfo
                            {
                                FileName = "taskkill", Arguments = $"/PID {p.Id} /T /F",
                                UseShellExecute = false, CreateNoWindow = true
                            });
                            tk?.WaitForExit(6000);
                            continue;
                        }
                        catch { }
                    }
                    p.CloseMainWindow();
                    if (!p.WaitForExit(timeoutMs))
                        p.Kill();
                }
                catch { try { if (!p.HasExited) p.Kill(); } catch { } }
                finally { try { p.Dispose(); } catch { } }
            }
        }
    }
}
