using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Loadout.Models;

namespace Loadout.Services;

/// <summary>
/// Guardián en segundo plano que detecta el inicio y cierre automático de juegos
/// configurados en Loadout (Valorant, LoL, CS2, etc.) sin necesidad de abrir la ventana
/// ni presionar "JUGAR" manualmente. 0% de impacto en CPU (solo busca nombres específicos cada 4s).
/// </summary>
public sealed class GameSentinelService
{
    private readonly DispatcherTimer _timer;
    private readonly DatabaseService _db;
    private readonly Func<bool> _isPlayingCheck;
    private readonly Func<Task<List<Models.Loadout>>> _getLoadouts;
    private readonly Func<int, Task<List<LoadoutItem>>> _getItems;
    private readonly Func<Models.Loadout, Process, Task> _onGameDetected;
    private readonly Func<Task> _onGameExited;

    private bool _checking;
    private int _trackedPid;
    private string _trackedName = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsAutoSessionActive { get; private set; }

    // Motores de juego 3D conocidos para mapeo inteligente
    private static readonly Dictionary<string, string[]> KnownEngineAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VALORANT"] = new[] { "VALORANT-Win64-Shipping" },
        ["VALORANT-Win64-Shipping"] = new[] { "VALORANT-Win64-Shipping" },
        ["League of Legends"] = new[] { "League of Legends" },
        ["LeagueClient"] = new[] { "League of Legends" },
        ["cs2"] = new[] { "cs2" }
    };

    public GameSentinelService(
        DatabaseService db,
        Func<bool> isPlayingCheck,
        Func<Task<List<Models.Loadout>>> getLoadouts,
        Func<int, Task<List<LoadoutItem>>> getItems,
        Func<Models.Loadout, Process, Task> onGameDetected,
        Func<Task> onGameExited)
    {
        _db = db;
        _isPlayingCheck = isPlayingCheck;
        _getLoadouts = getLoadouts;
        _getItems = getItems;
        _onGameDetected = onGameDetected;
        _onGameExited = onGameExited;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += async (_, _) => await CheckTickAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public void MarkManualSessionStarted()
    {
        IsAutoSessionActive = false;
        _trackedPid = 0;
        _trackedName = string.Empty;
    }

    public void ResetSessionState()
    {
        IsAutoSessionActive = false;
        _trackedPid = 0;
        _trackedName = string.Empty;
    }

    private async Task CheckTickAsync()
    {
        if (!Enabled || _checking) return;
        _checking = true;

        try
        {
            bool isPlaying = _isPlayingCheck();

            // 1. Si ya estamos en una sesión auto-iniciada por el guardián:
            if (isPlaying && IsAutoSessionActive)
            {
                bool stillRunning = false;
                if (_trackedPid > 0)
                {
                    try
                    {
                        using var p = Process.GetProcessById(_trackedPid);
                        if (!p.HasExited) stillRunning = true;
                    }
                    catch { }
                }

                if (!stillRunning && !string.IsNullOrWhiteSpace(_trackedName))
                {
                    // Comprobar si aún hay instancias activas con ese nombre
                    var procs = Process.GetProcessesByName(_trackedName);
                    if (procs.Length > 0)
                    {
                        stillRunning = true;
                        _trackedPid = procs[0].Id;
                        foreach (var pr in procs) pr.Dispose();
                    }
                }

                // Si el juego se cerró completamente, terminar la sesión automáticamente
                if (!stillRunning)
                {
                    IsAutoSessionActive = false;
                    _trackedPid = 0;
                    _trackedName = string.Empty;
                    await _onGameExited();
                }
                return;
            }

            // 2. Si NO estamos en juego: vigilar si el usuario abrió algún juego
            if (!isPlaying)
            {
                var loadouts = await _getLoadouts();
                if (loadouts == null || loadouts.Count == 0) return;

                foreach (var loadout in loadouts)
                {
                    var items = await _getItems(loadout.Id);
                    if (items == null) continue;

                    var candidateExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Target)) continue;
                        string rawName = Path.GetFileNameWithoutExtension(item.Target.Trim('"'));
                        if (!string.IsNullOrWhiteSpace(rawName))
                        {
                            candidateExeNames.Add(rawName);
                            if (KnownEngineAliases.TryGetValue(rawName, out var aliases))
                            {
                                foreach (var a in aliases) candidateExeNames.Add(a);
                            }
                        }
                    }

                    // También agregar por nombre de perfil si coincide con juegos comunes
                    if (KnownEngineAliases.TryGetValue(loadout.Name.Trim(), out var nameAliases))
                    {
                        foreach (var a in nameAliases) candidateExeNames.Add(a);
                    }

                    foreach (var exeName in candidateExeNames)
                    {
                        // Consulta ultra-rápida de Win32 (< 0.05ms para un nombre concreto)
                        var procs = Process.GetProcessesByName(exeName);
                        if (procs.Length > 0)
                        {
                            var targetProc = procs.FirstOrDefault(p => !p.HasExited) ?? procs[0];
                            int pid = targetProc.Id;

                            // Disponer el resto
                            foreach (var p in procs)
                            {
                                if (p.Id != pid) p.Dispose();
                            }

                            IsAutoSessionActive = true;
                            _trackedPid = pid;
                            _trackedName = exeName;

                            await _onGameDetected(loadout, targetProc);
                            return; // Sesión iniciada, salir
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            _checking = false;
        }
    }
}
