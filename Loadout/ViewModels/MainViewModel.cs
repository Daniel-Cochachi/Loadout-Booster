using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadout.Models;
using Loadout.Services;
using Loadout.Views.Dialogs;

namespace Loadout.ViewModels;

public enum SlotState { Idle, Launching, Running }

public sealed partial class SlotViewModel : ObservableObject
{
    [ObservableProperty] private int order;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string kind = "Exe";
    [ObservableProperty] private string target = string.Empty;
    [ObservableProperty] private int delaySeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArmedLabel))]
    private bool enabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorityLabel))]
    private bool highPriority;
    [ObservableProperty] private SlotState state = SlotState.Idle;
    public int ItemId { get; set; }
    public ImageSource? Icon { get; set; }
    public bool HasIcon => Icon != null;
    public bool HasNoIcon => Icon == null;
    public string SlotNumber => (Order + 1).ToString("00");
    public string DelayLabel => DelaySeconds <= 0 ? "0ms (IMMEDIATE)" : $"+{DelaySeconds * 1000}ms DELAY";
    public string ArmedLabel => Enabled ? "● ARMADO" : "○ APAGADO";
    public string PriorityLabel => HighPriority ? "⚡ HIGH" : "HIGH OFF";
}

public sealed partial class LoadoutEntryViewModel : ObservableObject
{
    [ObservableProperty] private int id;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverInitials))]
    private string name = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBrush))]
    private string colorHex = "#C50337";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBrush))]
    private string colorHex2 = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string notes = string.Empty;
    [ObservableProperty] private string tag = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    [NotifyPropertyChangedFor(nameof(HasNoCover))]
    [NotifyPropertyChangedFor(nameof(CoverInitials))]
    private string coverPath = string.Empty;
    [ObservableProperty] private int itemCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool isActive;
    public string BayNumber { get; set; } = "BAY 00";
    public string BayShort => BayNumber.Replace("BAY ", "");
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);
    public bool HasNoCover => !HasCover;
    public string CoverInitials
    {
        get
        {
            string t = (Name ?? string.Empty).Trim();
            if (t.Length == 0) return "·";
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }
    }
    public string StatusLabel => IsActive ? "● ACTIVE READY" : "STANDBY";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes?.Trim());
    public string NotesPreview
    {
        get
        {
            string t = (Notes ?? string.Empty).Trim();
            if (t.Length <= 160) return t;
            return t.Substring(0, 160).TrimEnd() + "…";
        }
    }

    /// <summary>Fondo de tarjeta/héroe: degradado ColorHex → ColorHex2, o sólido si no hay combina.</summary>
    public Brush CardBrush
    {
        get
        {
            try
            {
                var c1 = (Color)ColorConverter.ConvertFromString(
                    string.IsNullOrWhiteSpace(ColorHex) ? "#C50337" : ColorHex)!;
                if (string.IsNullOrWhiteSpace(ColorHex2)
                    || string.Equals(ColorHex2.Trim(), ColorHex.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var solid = new SolidColorBrush(c1);
                    solid.Freeze();
                    return solid;
                }
                var c2 = (Color)ColorConverter.ConvertFromString(ColorHex2)!;
                var g = new LinearGradientBrush(c1, c2, 135);
                g.Freeze();
                return g;
            }
            catch
            {
                var fallback = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C50337")!);
                fallback.Freeze();
                return fallback;
            }
        }
    }
}

public sealed class DirViewModel
{
    public string Raw { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string Icon { get; set; } = "·";
    public string AccentHex { get; set; } = "#8E2440";
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly LaunchService _launcher = new();
    private readonly DndService _dnd = new();
    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly List<double> _sparkHistory = new();
    private DateTime _sessionStart;
    private TimeSpan _frozenElapsed;
    private int _sessionId;
    private CancellationTokenSource? _launchCts;
    private string? _prevPowerScheme;

    [ObservableProperty] private ObservableCollection<LoadoutEntryViewModel> loadouts = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private LoadoutEntryViewModel? selectedLoadout;
    [ObservableProperty] private ObservableCollection<SlotViewModel> slots = new();
    [ObservableProperty] private ObservableCollection<DirViewModel> directives = new();
    [ObservableProperty] private SlotViewModel? selectedSlot;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartTimerCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isPlaying;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    private bool paused;
    [ObservableProperty] private bool rankedMode;
    [ObservableProperty] private bool minimizeOnLaunch = true;
    [ObservableProperty] private bool highPriority;
    [ObservableProperty] private bool autoKillTree;
    [ObservableProperty] private bool boostOnLaunch;
    [ObservableProperty] private bool perfPower;
    [ObservableProperty] private string sessionElapsed = "00:00:00";
    [ObservableProperty] private string statusLine = "Listo. Crea tu primer perfil.";
    [ObservableProperty] private bool isBusy;
    // Stats hero + barra
    [ObservableProperty] private string statSlots = "00 EJECUTABLES";
    [ObservableProperty] private string statDelay = "0s CADENCIA";
    [ObservableProperty] private string statRam = "—";
    [ObservableProperty] private string statStagger = "AUTO-STAGGER: OFF";
    [ObservableProperty] private string appRamText = "RAM: —";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayLabel))]
    private bool hotkeyArmed = true;
    [ObservableProperty] private string sysMemText = "SYS.MEM: —";
    [ObservableProperty] private string watchText = "DAEMON.WATCH: 0 PROCESSES";
    public PointCollection SparkPoints { get; } = new();

    public MainViewModel(DatabaseService db)
    {
        _db = db;
        for (int i = 0; i < 24; i++) { _sparkHistory.Add(0); }
        RebuildSpark();
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var el = DateTime.Now - _sessionStart;
            SessionElapsed = el.ToString(@"hh\:mm\:ss");
            if (el.TotalHours >= 1 && (int)el.TotalSeconds % 1800 == 0)
                StatusLine = $"Llevas {el:hh\\:mm} jugando. Toma un descanso.";
        };
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += (_, _) => { RefreshSlotStates(); UpdateSysInfo(); MaintainSessionPriorities(); };
    }

    partial void OnSelectedLoadoutChanged(LoadoutEntryViewModel? value)
    {
        _ = LoadSlotsAsync();
        OnPropertyChanged(nameof(SelectedLoadoutName));
        OnPropertyChanged(nameof(SelectedLoadoutTag));
        OnPropertyChanged(nameof(BayLabel));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(HasArmedSlots));
    }

    partial void OnRankedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(RankedBadgeText));
        OnPropertyChanged(nameof(RankedBadgeVisible));
        _ = _db.SetSettingAsync("RankedMode", value ? "true" : "false");
    }

    partial void OnMinimizeOnLaunchChanged(bool value)
        => _ = _db.SetSettingAsync("MinimizeOnLaunch", value ? "true" : "false");

    partial void OnHighPriorityChanged(bool value)
    {
        _launcher.HighPriority = value;
        _ = _db.SetSettingAsync("HighPriority", value ? "true" : "false");
    }

    partial void OnAutoKillTreeChanged(bool value)
        => _ = _db.SetSettingAsync("AutoKillTree", value ? "true" : "false");

    partial void OnBoostOnLaunchChanged(bool value)
        => _ = _db.SetSettingAsync("BoostOnLaunch", value ? "true" : "false");

    partial void OnPerfPowerChanged(bool value)
        => _ = _db.SetSettingAsync("PerfPower", value ? "true" : "false");

    public HashSet<int> GetSessionPids() => _launcher.GetLiveSessionIds();

    /// <summary>Watcher vivo: mientras juegas re-aplica High a la sesión + hijos (LoL/Valo).</summary>
    private void MaintainSessionPriorities()
    {
        try { if (IsPlaying && HighPriority) _launcher.MaintainPriorities(); } catch { }
    }

    public string TrayLabel => HotkeyArmed ? "((•)) TRAY: ARMED" : "((•)) TRAY: SIN HOTKEY";

    /// <summary>Monitor vivo siempre (RAM + sesión cada 3s), no solo jugando.</summary>
    public void StartMonitor()
    {
        try { if (!_pollTimer.IsEnabled) _pollTimer.Start(); } catch { }
    }

    public string SelectedLoadoutName => SelectedLoadout?.Name.ToUpperInvariant() ?? "SIN PERFIL";
    public string SelectedLoadoutTag => string.IsNullOrWhiteSpace(SelectedLoadout?.Tag)
        ? "SIN GATE" : SelectedLoadout.Tag.ToUpperInvariant();
    public string BayLabel => SelectedLoadout == null ? "CARTRIDGE --"
        : $"CARTRIDGE {Loadouts.IndexOf(SelectedLoadout) + 1:00}";
    public bool CanPlay => SelectedLoadout != null && Slots.Count > 0 && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanRestartTimer => IsPlaying;
    public bool CanStop => IsPlaying;
    public bool RankedBadgeVisible => RankedMode;
    public string RankedBadgeText => RankedMode ? "MODO RANKED [DND FORZADO]" : "MODO CASUAL";
    public string PauseLabel => Paused ? "▶ REANUDAR" : "❚❚ PAUSAR";
    public DatabaseService Db => _db;
    public bool DndActive => _dnd.IsActive;
    public string DndLabel => DndActive ? "● DND ACTIVADO" : "○ DND APAGADO";
    public bool HasArmedSlots => Slots.Any(s => s.Enabled);
    public string SessionStateLabel => IsPlaying ? "● EN JUEGO" : "○ EN ESPERA";
    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public void RestoreDndIfActive()
    {
        if (!_dnd.IsActive) return;
        _dnd.Restore();
        OnPropertyChanged(nameof(DndActive));
        OnPropertyChanged(nameof(DndLabel));
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            RankedMode = await _db.GetSettingAsync("RankedMode", "false") == "true";
            MinimizeOnLaunch = await _db.GetSettingAsync("MinimizeOnLaunch", "true") == "true";
            HighPriority = await _db.GetSettingAsync("HighPriority", "false") == "true";
            AutoKillTree = await _db.GetSettingAsync("AutoKillTree", "false") == "true";
            BoostOnLaunch = await _db.GetSettingAsync("BoostOnLaunch", "false") == "true";
            PerfPower = await _db.GetSettingAsync("PerfPower", "false") == "true";
            _launcher.HighPriority = HighPriority;

            var list = await _db.GetLoadoutsAsync();
            Loadouts.Clear();
            int bay = 1;
            foreach (var l in list)
                Loadouts.Add(new LoadoutEntryViewModel
                {
                    Id = l.Id, Name = l.Name, ColorHex = l.ColorHex,
                    Tag = l.Tag ?? string.Empty, ItemCount = l.ItemCount,
                    Notes = l.Notes ?? string.Empty,
                    CoverPath = l.CoverPath ?? string.Empty,
                    ColorHex2 = l.ColorHex2 ?? string.Empty,
                    BayNumber = $"BAY {bay++:00}"
                });
            // Reengancha la selección por Id: si no, la vista queda con el objeto viejo
            int? keepId = SelectedLoadout?.Id;
            SelectedLoadout = keepId.HasValue
                ? Loadouts.FirstOrDefault(l => l.Id == keepId.Value) ?? Loadouts.FirstOrDefault()
                : Loadouts.FirstOrDefault();
            if (SelectedLoadout == null)
                StatusLine = "Sin perfiles. Crea el primero con + NUEVO.";
            await LoadSlotsAsync();
            UpdateSysInfo();
        }
        finally { IsBusy = false; }
    }

    public async Task LoadSlotsAsync()
    {
        if (SelectedLoadout == null)
        {
            Slots.Clear(); Directives.Clear();
            ComputeStats(Array.Empty<LoadoutItem>());
            OnPropertyChanged(nameof(HasArmedSlots));
            OnPropertyChanged(nameof(SelectedLoadoutName));
            OnPropertyChanged(nameof(SelectedLoadoutTag));
            OnPropertyChanged(nameof(BayLabel));
            PlayCommand.NotifyCanExecuteChanged();
            return;
        }
        var items = await _db.GetItemsAsync(SelectedLoadout.Id);
        Slots.Clear();
        foreach (var it in items.OrderBy(i => i.OrderIndex))
            Slots.Add(new SlotViewModel
            {
                ItemId = it.Id, Order = it.OrderIndex, Name = it.Name,
                Kind = it.Kind, Target = it.Target, DelaySeconds = it.DelaySeconds,
                Enabled = it.Enabled, HighPriority = it.HighPriority,
                Icon = GameIconCache.Get(it.Target)
            });
        ComputeStats(items);
        await LoadDirectivesAsync();
        PlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasArmedSlots));
        OnPropertyChanged(nameof(SelectedLoadoutName));
        OnPropertyChanged(nameof(SelectedLoadoutTag));
        OnPropertyChanged(nameof(BayLabel));
    }

    private void ComputeStats(IEnumerable<LoadoutItem> source)
    {
        var items = source.ToList();
        var armed = items.Where(i => i.Enabled).ToList();
        StatSlots = $"{armed.Count:00} EJECUTABLES";
        StatDelay = $"{armed.Sum(i => i.DelaySeconds)}s CADENCIA";
        StatStagger = armed.Any(i => i.DelaySeconds > 0) ? "AUTO-STAGGER: ENABLED" : "AUTO-STAGGER: OFF";
        long bytes = 0;
        foreach (var it in armed)
        {
            try
            {
                if ((it.Kind == "Exe" || it.Kind == "File") && File.Exists(it.Target))
                    bytes += new FileInfo(it.Target).Length;
            }
            catch { }
        }
        StatRam = bytes <= 0 ? "—" : "~" + FormatBytes(bytes) + " ESTIMADO";
    }

    private static string FormatBytes(long b)
        => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : $"{b / (double)(1L << 20):0.0} MB";

    public async Task LoadDirectivesAsync()
    {
        Directives.Clear();
        if (SelectedLoadout == null) return;
        var rows = await _db.GetDirectivesAsync(SelectedLoadout.Id);
        foreach (var d in rows)
        {
            var (icon, hex) = ParseDirective(d.Text);
            string display = d.Text.Length > 1 ? d.Text.Substring(1).TrimStart() : d.Text;
            Directives.Add(new DirViewModel
            {
                Raw = d.Text, DisplayText = display, Icon = icon, AccentHex = hex
            });
        }
    }

    internal static (string Icon, string Hex) ParseDirective(string text)
    {
        if (text.StartsWith("!")) return ("!", "#FF4D6E");
        if (text.StartsWith("#")) return ("#", "#E8A2B2");
        if (text.StartsWith("▲") || text.StartsWith(">")) return ("▲", "#E7547A");
        if (text.StartsWith("/")) return ("/", "#9A8B90");
        return ("·", "#CDBFA9");
    }

    public async Task RefreshCountsAsync()
    {
        var fresh = await _db.GetLoadoutsAsync();
        foreach (var l in Loadouts)
        {
            var f = fresh.FirstOrDefault(x => x.Id == l.Id);
            if (f != null) { l.ItemCount = f.ItemCount; l.Tag = f.Tag ?? string.Empty; }
        }
    }

    public async Task MoveSelectedSlotAsync(int dir)
    {
        if (SelectedLoadout == null || SelectedSlot == null || IsPlaying) return;
        var ordered = Slots.OrderBy(s => s.Order).ToList();
        int idx = ordered.IndexOf(SelectedSlot);
        int swap = idx + dir;
        if (idx < 0 || swap < 0 || swap >= ordered.Count) return;
        (ordered[idx], ordered[swap]) = (ordered[swap], ordered[idx]);
        await _db.ReorderItemsAsync(SelectedLoadout.Id, ordered.Select(s => s.ItemId).ToList());
        await LoadSlotsAsync();
        SelectedSlot = Slots.FirstOrDefault(s => s.ItemId == ordered[swap].ItemId);
    }

    [RelayCommand]
    private async Task ToggleSlotEnabledAsync(SlotViewModel? slot)
    {
        if (slot == null || IsPlaying || SelectedLoadout == null) return;
        slot.Enabled = !slot.Enabled;
        await _db.SetItemEnabledAsync(slot.ItemId, slot.Enabled);
        var items = await _db.GetItemsAsync(SelectedLoadout.Id);
        ComputeStats(items);
        OnPropertyChanged(nameof(HasArmedSlots));
    }

    [RelayCommand]
    private async Task ToggleSlotPriorityAsync(SlotViewModel? slot)
    {
        if (slot == null || IsPlaying || SelectedLoadout == null) return;
        slot.HighPriority = !slot.HighPriority;
        await _db.SetItemPriorityAsync(slot.ItemId, slot.HighPriority);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (SelectedLoadout == null || IsPlaying || Slots.Count == 0) return;
        IsPlaying = true;
        Paused = false;
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(SessionStateLabel));
        foreach (var l in Loadouts) l.IsActive = l.Id == SelectedLoadout.Id;
        SelectedLoadout.IsActive = true;
        _sessionStart = DateTime.Now;
        SessionElapsed = "00:00:00";
        _sessionTimer.Start();
        _pollTimer.Start();
        _launchCts = new CancellationTokenSource();
        try { _sessionId = await _db.StartSessionAsync(SelectedLoadout.Id); } catch { }

        bool dndOn = false;
        if (RankedMode)
        {
            try
            {
                if (await _db.GetSettingAsync("RankedDnd", "true") == "true")
                {
                    _dnd.Enable();
                    dndOn = _dnd.IsActive;
                }
            }
            catch { }
        }
        OnPropertyChanged(nameof(DndActive));
        OnPropertyChanged(nameof(DndLabel));

        if (MinimizeOnLaunch)
        {
            try
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (App.Current.MainWindow is System.Windows.Window w)
                        w.WindowState = System.Windows.WindowState.Minimized;
                });
            }
            catch { }
        }

        var items = await _db.GetItemsAsync(SelectedLoadout.Id);
        string boostNote = string.Empty;
        if (BoostOnLaunch)
        {
            try
            {
                int n = new BoostService().TrimWorkingSets();
                boostNote = $" ⚡Boost:{n}.";
                StatusLine = $"⚡ Boost previo: {n} procesos optimizados. Lanzando {SelectedLoadout.Name}…";
            }
            catch { StatusLine = $"Lanzando {SelectedLoadout.Name}…"; }
        }
        else StatusLine = $"Lanzando {SelectedLoadout.Name}…";

        // Kill-list del perfil: cierra apps configuradas antes de jugar
        try
        {
            var patterns = await _db.GetKillListAsync(SelectedLoadout.Id);
            if (patterns.Count > 0)
            {
                var (k, _) = new BoostService().KillByNames(patterns, GetSessionPids());
                if (k > 0) StatusLine = $"⚡ Kill-list: {k} app(s) cerradas. Lanzando {SelectedLoadout.Name}…";
            }
        }
        catch { }

        // Plan de energía: máximo rendimiento durante la sesión
        _prevPowerScheme = null;
        if (PerfPower)
        {
            try
            {
                var bs = new BoostService();
                string? cur = bs.GetActivePowerScheme();
                if (cur != null && cur != BoostService.HighPerfGuid && bs.SetPowerScheme(BoostService.HighPerfGuid))
                    _prevPowerScheme = cur;
            }
            catch { _prevPowerScheme = null; }
        }
        foreach (var s in Slots) s.State = SlotState.Idle;

        await _launcher.LaunchSequenceAsync(items, (item, skipped) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var slot = Slots.FirstOrDefault(s => s.ItemId == item.Id);
                if (slot != null) slot.State = skipped || _launcher.AlreadyRunning(item) ? SlotState.Running : SlotState.Launching;
            });
        }, _launchCts.Token);

        RefreshSlotStates();
        UpdateSysInfo();
        StatusLine = (dndOn ? $"EN RANKED: {SelectedLoadout.Name} · DND ACTIVADO." : $"En juego: {SelectedLoadout.Name}.") + boostNote;
        PlayCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        if (!IsPlaying) return;
        Paused = !Paused;
        if (Paused)
        {
            _frozenElapsed = DateTime.Now - _sessionStart;
            _sessionTimer.Stop();
            StatusLine = "Cronómetro en pausa. Los procesos siguen activos.";
        }
        else
        {
            _sessionStart = DateTime.Now - _frozenElapsed;
            _sessionTimer.Start();
            StatusLine = $"En juego: {SelectedLoadout?.Name}.";
        }
        OnPropertyChanged(nameof(PauseLabel));
    }

    [RelayCommand(CanExecute = nameof(CanRestartTimer))]
    private async Task RestartTimerAsync()
    {
        if (!IsPlaying || SelectedLoadout == null) return;
        try
        {
            if (_sessionId != 0) await _db.EndSessionAsync(_sessionId);
            _sessionId = await _db.StartSessionAsync(SelectedLoadout.Id);
        }
        catch { }
        _sessionStart = DateTime.Now;
        Paused = false;
        SessionElapsed = "00:00:00";
        if (!_sessionTimer.IsEnabled) _sessionTimer.Start();
        OnPropertyChanged(nameof(PauseLabel));
        StatusLine = "Cronómetro reiniciado. Nueva sesión registrada.";
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (!IsPlaying)
        {
            StatusLine = "Nada en juego.";
            return;
        }
        int live = _launcher.GetLiveSessionIds().Count;
        bool confirmed = await Application.Current.Dispatcher.InvokeAsync(() =>
            ConfirmDialog.Ask(
                (Window)Application.Current.MainWindow!,
                "CERRAR SESIÓN",
                live > 0
                    ? $"¿Cerrar todo y detener {live} proceso(s) en juego? Los accesos de la sesión también se cierran."
                    : "¿Finalizar la sesión? No hay procesos en juego en este momento.",
                "CERRAR TODO", "SEGUIR"));
        if (!confirmed) return;
        try { _launchCts?.Cancel(); } catch { }
        _launcher.CloseSession(4000, AutoKillTree);
        _sessionTimer.Stop();
        RestoreDndIfActive();
        if (_prevPowerScheme != null)
        {
            try { new BoostService().SetPowerScheme(_prevPowerScheme); } catch { }
            _prevPowerScheme = null;
        }
        IsPlaying = false;
        Paused = false;
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(SessionStateLabel));
        foreach (var s in Slots) s.State = SlotState.Idle;
        foreach (var l in Loadouts) l.IsActive = false;
        try
        {
            if (_sessionId != 0) await _db.EndSessionAsync(_sessionId);
        }
        catch { }
        var dur = DateTime.Now - _sessionStart;
        StatusLine = $"Sesión finalizada · {dur:hh\\:mm\\:ss}.";
        try
        {
            var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddText($"Sesión: {SelectedLoadout?.Name}")
                .AddText($"Duración {dur:hh\\:mm\\:ss}. Todos los procesos cerrados.")
                .GetToastContent();
            var notif = new Windows.UI.Notifications.ToastNotification(toast.GetXml());
            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier("Loadout").Show(notif);
        }
        catch { }
        PlayCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSlotStates()
    {
        var live = _launcher.GetLiveSessionIds();
        bool anyLive = live.Count > 0;
        foreach (var s in Slots)
        {
            if (s.State == SlotState.Idle) continue;
            s.State = anyLive ? SlotState.Running : SlotState.Launching;
        }
    }

    private void UpdateSysInfo()
    {
        try
        {
            double appMb = Environment.WorkingSet / (1024.0 * 1024.0);
            AppRamText = $"RAM: {appMb:0.0} MB";
            _sparkHistory.Add(appMb);
            if (_sparkHistory.Count > 24) _sparkHistory.RemoveAt(0);
            RebuildSpark();

            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            double totalGb = ci.TotalPhysicalMemory / (1024.0 * 1024 * 1024.0);
            double usedGb = (ci.TotalPhysicalMemory - ci.AvailablePhysicalMemory) / (1024.0 * 1024 * 1024.0);
            SysMemText = $"SYS.MEM: {usedGb:0.0} / {totalGb:0.0} GB";
        }
        catch { }
        try
        {
            int n = _launcher.GetLiveSessionIds().Count;
            WatchText = $"DAEMON.WATCH: {n} PROCESSES";
        }
        catch { }
    }

    private void RebuildSpark()
    {
        SparkPoints.Clear();
        double max = _sparkHistory.Count > 0 ? _sparkHistory.Max() : 1;
        if (max <= 0) max = 1;
        for (int i = 0; i < _sparkHistory.Count; i++)
        {
            double x = i * (220.0 / 23);
            double y = 30 - (_sparkHistory[i] / max) * 26;
            SparkPoints.Add(new System.Windows.Point(x, y));
        }
    }

    [RelayCommand]
    private async Task AddDemoDataAsync()
    {
        if (SelectedLoadout == null)
        {
            int id = await _db.CreateLoadoutAsync("Noche de prueba", "#C50337");
            await LoadAsync();
            SelectedLoadout = Loadouts.FirstOrDefault(l => l.Id == id);
        }
        var lid = SelectedLoadout!.Id;
        var existing = await _db.GetItemsAsync(lid);
        int order = existing.Count;
        await _db.CreateItemAsync(new LoadoutItem
        {
            LoadoutId = lid, Name = "Notepad", Kind = "Exe",
            Target = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe"),
            OrderIndex = order, DelaySeconds = 1
        });
        await _db.CreateItemAsync(new LoadoutItem
        {
            LoadoutId = lid, Name = "Guía de skins", Kind = "Url",
            Target = "https://example.com", OrderIndex = order + 1, DelaySeconds = 1
        });
        await LoadSlotsAsync();
        await RefreshCountsAsync();
        StatusLine = "Datos de prueba agregados: Notepad + URL.";
    }
}
