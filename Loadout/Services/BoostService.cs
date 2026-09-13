using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Loadout.Services;

/// <summary>Tragón de RAM candidato a cerrar (nunca crítico del sistema).</summary>
public sealed class HogInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Mb { get; set; }
    public string Display => $"{Name} · {Mb:0} MB";
}

/// <summary>
/// Game Boost: mide RAM, recorta working sets (libera RAM sin matar nada)
/// y cierra apps pesadas elegidas por el usuario. Nunca toca procesos críticos.
/// </summary>
public sealed class BoostService
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>Recorta la RAM del propio proceso (ideal al ir a bandeja).</summary>
    public static void TrimSelf()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            EmptyWorkingSet(p.Handle);
        }
        catch (Exception ex) { Log.Error("TrimSelf", ex); }
        try { GC.Collect(2, GCCollectionMode.Optimized, false); } catch { }
    }

    // Procesos que jamás se tocan (sistema, shell, host críticos)
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss", "csrss", "wininit", "services", "lsass", "lsaiso",
        "dwm", "explorer", "taskhostw", "shellexperiencehost", "searchhost",
        "startmenuexperiencehost", "sihost", "ctfmon", "fontdrvhost", "conhost",
        "winlogon", "spoolsv", "svchost", "runtimebroker", "applicationframehost",
        "idle", "secure system", "memory compression"
    };

    public void GetMemory(out double totalMb, out double availMb)
    {
        var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
        totalMb = ci.TotalPhysicalMemory / (1024.0 * 1024.0);
        availMb = ci.AvailablePhysicalMemory / (1024.0 * 1024.0);
    }

    /// <summary>Recorta la RAM en uso de procesos accesibles. Devuelve cuántos se optimizaron.</summary>
    public int TrimWorkingSets()
    {
        int done = 0;
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || p.Id == 4 || p.Id == self) continue;
                if (p.HasExited) continue;
                string name;
                try { name = p.ProcessName; } catch { continue; }
                if (Protected.Contains(name)) continue;
                if (EmptyWorkingSet(p.Handle)) done++;
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return done;
    }

    /// <summary>Top N procesos por RAM, excluyendo sistema, la app y la sesión en juego.</summary>
    public List<HogInfo> GetTopHogs(int top = 8, HashSet<int>? sessionPids = null)
    {
        int self = Environment.ProcessId;
        var hogs = new List<HogInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || p.Id == 4 || p.Id == self) continue;
                if (sessionPids != null && sessionPids.Contains(p.Id)) continue;
                string name;
                try { name = p.ProcessName; } catch { continue; }
                if (Protected.Contains(name)) continue;
                if (p.HasExited) continue;
                double mb;
                try { mb = p.WorkingSet64 / (1024.0 * 1024.0); } catch { continue; }
                if (mb < 80) continue; // solo tragalonas reales
                hogs.Add(new HogInfo { Pid = p.Id, Name = name, Mb = mb });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return hogs.OrderByDescending(h => h.Mb).Take(Math.Max(1, top)).ToList();
    }

    /// <summary>Mata PIDs (ya filtrados). Devuelve (cerrados, fallidos).</summary>
    public (int Killed, int Failed) KillPids(IEnumerable<int> pids)
    {
        int killed = 0, failed = 0;
        foreach (int pid in pids.Distinct())
        {
            try
            {
                if (pid == 0 || pid == 4 || pid == Environment.ProcessId) continue;
                using var p = Process.GetProcessById(pid);
                string name;
                try { name = p.ProcessName; } catch { failed++; continue; }
                if (Protected.Contains(name)) continue;
                if (p.HasExited) continue;
                p.Kill();
                if (p.WaitForExit(4000)) killed++; else failed++;
            }
            catch { failed++; }
        }
        return (killed, failed);
    }

    /// <summary>Mata por nombre de proceso (kill-list del perfil). Seguro: respeta protegidos y sesión.</summary>
    public (int Killed, int Failed) KillByNames(IEnumerable<string> patterns, HashSet<int>? sessionPids = null)
    {
        var set = new HashSet<string>(
            patterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()));
        if (set.Count == 0) return (0, 0);
        int killed = 0, failed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || p.Id == 4 || p.Id == Environment.ProcessId) continue;
                if (sessionPids != null && sessionPids.Contains(p.Id)) continue;
                string name;
                try { name = p.ProcessName; } catch { continue; }
                if (Protected.Contains(name)) continue;
                if (!set.Contains(name.ToLowerInvariant())) continue;
                if (p.HasExited) continue;
                try
                {
                    p.Kill();
                    if (p.WaitForExit(4000)) killed++; else failed++;
                }
                catch { failed++; }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return (killed, failed);
    }

    /// <summary>%CPU por PID con doble muestreo. Claves = pid, valor = % de CPU total.</summary>
    public async Task<Dictionary<int, double>> SampleCpuAsync(int sampleMs = 800)
    {
        var result = new Dictionary<int, double>();
        try
        {
            Dictionary<int, TimeSpan> Take()
            {
                var d = new Dictionary<int, TimeSpan>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (!p.HasExited) d[p.Id] = p.TotalProcessorTime;
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
                return d;
            }
            var t0 = Take();
            var sw = Stopwatch.StartNew();
            await Task.Delay(Math.Max(300, sampleMs));
            sw.Stop();
            var t1 = Take();
            double wallMs = Math.Max(1, sw.Elapsed.TotalMilliseconds) * Environment.ProcessorCount;
            foreach (var kv in t1)
            {
                if (!t0.TryGetValue(kv.Key, out var before)) continue;
                double pct = (kv.Value - before).TotalMilliseconds / wallMs * 100.0;
                if (pct >= 0.5) result[kv.Key] = pct;
            }
        }
        catch { }
        return result;
    }

    // ---------- Plan de energía ----------
    public const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public string? GetActivePowerScheme()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg", Arguments = "/getactivescheme",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
            var m = Regex.Match(output, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }
        catch (Exception ex)
        {
            Log.Error("GetActivePowerScheme", ex);
            return null;
        }
    }

    public bool SetPowerScheme(string guid)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg", Arguments = $"/setactive {guid}",
                UseShellExecute = false, CreateNoWindow = true
            });
            if (p == null) return false;
            return p.WaitForExit(10000) && p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error($"SetPowerScheme({guid})", ex);
            return false;
        }
    }

    // ---------- Game DVR (grabación en segundo plano) ----------
    private const string GameDvrKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameConfigKey = @"System\GameConfigStore";

    public (int? GameDvr, int? AppCapture) ReadGameDvr()
    {
        int? Read(string path, string name)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(path, false);
                return k?.GetValue(name) is int v ? v : null;
            }
            catch { return null; }
        }
        return (Read(GameDvrKey, "AppCaptureEnabled"), Read(GameConfigKey, "GameDVR_Enabled"));
    }

    public void WriteGameDvr(int gameDvr, int appCapture)
    {
        void Write(string path, string name, int value)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(path);
                k?.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }
        Write(GameDvrKey, "AppCaptureEnabled", appCapture);
        Write(GameConfigKey, "GameDVR_Enabled", gameDvr);
    }

    // ---------- Temporales ----------
    /// <summary>Borra %TEMP% liberable (salta lo bloqueado, sin diálogos). Devuelve (MB, archivos).</summary>
    public (double Mb, int Files) CleanTemp()
    {
        long bytes = 0;
        int files = 0;
        try
        {
            string temp = Path.GetTempPath();
            if (Directory.Exists(temp))
            {
                foreach (string f in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        long len = fi.Exists ? fi.Length : 0;
                        fi.Delete();
                        bytes += len;
                        files++;
                    }
                    catch { }
                }
                foreach (string d in Directory.EnumerateDirectories(temp))
                {
                    try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); }
                    catch { }
                }
            }
        }
        catch (Exception ex) { Log.Error("CleanTemp", ex); }
        return (bytes / (1024.0 * 1024.0), files);
    }
}
