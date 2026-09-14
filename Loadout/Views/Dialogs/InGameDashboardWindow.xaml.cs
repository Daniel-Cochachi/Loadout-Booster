using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Loadout.Services;
using Loadout.ViewModels;

namespace Loadout.Views.Dialogs;

public sealed class InGameHogItem
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Mb { get; set; }
    public string CpuText { get; set; } = string.Empty;
}

public partial class InGameDashboardWindow : Window
{
    private readonly BoostService _boost = new();
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ObservableCollection<InGameHogItem> _items = new();
    private readonly Dictionary<int, (DateTime Time, TimeSpan CpuTime)> _cpuCache = new();

    private bool _isCollapsed;
    private bool _refreshing;

    public InGameDashboardWindow(DatabaseService db, MainViewModel vm)
    {
        InitializeComponent();
        _db = db;
        _vm = vm;

        HogList.ItemsSource = _items;

        // Intervalo de 4s para equilibrio óptimo entre datos vivos y 0 impacto en CPU
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();

        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                _refreshTimer.Start();
                await RefreshDataAsync();
            }
            else
            {
                _refreshTimer.Stop(); // Cero consumo de CPU cuando el HUD está oculto
                _cpuCache.Clear();
            }
        };

        // Posicionar en la esquina superior derecha
        Loaded += (_, _) =>
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 24;
                Top = 32;
            }
            catch { }
        };
    }

    public void UpdateHotkeyHint(string hint)
    {
        HotkeyHintText.Text = string.IsNullOrWhiteSpace(hint) ? "Ctrl+F11" : hint;
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private sealed class DashboardSnapshot
    {
        public double UsedGb { get; set; }
        public double TotalGb { get; set; }
        public double AvailMb { get; set; }
        public double Pct { get; set; }
        public List<InGameHogItem> Hogs { get; set; } = new();
    }

    private async Task RefreshDataAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var sessionPids = _vm.GetSessionPids();

            // Ejecución 100% asíncrona en hilo secundario para NO congelar la interfaz ni el juego
            var snap = await Task.Run(() =>
            {
                _boost.GetMemory(out double totalMb, out double availMb);
                double usedMb = totalMb - availMb;
                double pct = totalMb > 0 ? (usedMb / totalMb * 100.0) : 0;

                // 1. Obtener los 5 procesos que más RAM consumen (excluyendo el juego actual y sistema)
                var rawHogs = _boost.GetTopHogs(5, sessionPids);

                // 2. Muestreo de CPU instantáneo (CERO delay / CERO escaneo completo del SO)
                // Se calcula el delta de tiempo de CPU exclusivamente para estos 5 procesos
                var hogs = new List<InGameHogItem>();
                var now = DateTime.UtcNow;

                foreach (var h in rawHogs)
                {
                    string cpuText = string.Empty;
                    try
                    {
                        using var p = Process.GetProcessById(h.Pid);
                        if (!p.HasExited)
                        {
                            var totalCpu = p.TotalProcessorTime;
                            if (_cpuCache.TryGetValue(h.Pid, out var prev))
                            {
                                double elapsed = (now - prev.Time).TotalMilliseconds * Environment.ProcessorCount;
                                if (elapsed > 0)
                                {
                                    double delta = (totalCpu - prev.CpuTime).TotalMilliseconds;
                                    double cpuPct = Math.Clamp((delta / elapsed) * 100.0, 0, 100);
                                    if (cpuPct >= 0.5)
                                    {
                                        cpuText = $"· CPU {cpuPct:0}%";
                                    }
                                }
                            }
                            _cpuCache[h.Pid] = (now, totalCpu);
                        }
                    }
                    catch { }

                    hogs.Add(new InGameHogItem
                    {
                        Pid = h.Pid,
                        Name = h.Name,
                        Mb = h.Mb,
                        CpuText = cpuText
                    });
                }

                return new DashboardSnapshot
                {
                    UsedGb = usedMb / 1024.0,
                    TotalGb = totalMb / 1024.0,
                    AvailMb = availMb,
                    Pct = pct,
                    Hogs = hogs
                };
            });

            // Aplicar cambios en la UI de forma atómica y ultra-rápida (< 1ms)
            RamStatusText.Text = $"RAM: {snap.UsedGb:0.0} / {snap.TotalGb:0.0} GB ({snap.Pct:0}%)";
            RamBar.Value = Math.Clamp(snap.Pct, 0, 100);
            RamFreeText.Text = $"Libres: {snap.AvailMb:0} MB";

            _items.Clear();
            foreach (var h in snap.Hogs)
            {
                _items.Add(h);
            }

            RefreshStatusText.Text = $"En vivo {DateTime.Now:HH:mm:ss}";
        }
        catch { }
        finally
        {
            _refreshing = false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _refreshTimer.Stop(); // Pausar timer para arrastre con 144Hz fluido
            try { DragMove(); } catch { }
            if (IsVisible) _refreshTimer.Start();
        }
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        if (_isCollapsed)
        {
            Height = 110;
            HogList.Visibility = Visibility.Collapsed;
            FeedbackText.Text = "Modo Mini HUD activado";
        }
        else
        {
            Height = 470;
            HogList.Visibility = Visibility.Visible;
            FeedbackText.Text = "Modo gaming activo · 0% CPU al estar oculto";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private async void KillApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is int pid && pid > 0)
        {
            var (killed, _) = _boost.KillPids(new[] { pid });
            if (killed > 0)
            {
                FeedbackText.Text = "✔ Proceso cerrado correctamente.";
                _cpuCache.Remove(pid);
                await RefreshDataAsync();
            }
        }
    }

    private async void QuickBoost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessionPids = _vm.GetSessionPids();
            var sessionNames = _vm.GetSessionNames();
            int n = _boost.TrimWorkingSets(sessionPids, sessionNames);
            FeedbackText.Text = $"✔ {n} procesos optimizados sin tocar tu juego.";
            await RefreshDataAsync();
        }
        catch
        {
            FeedbackText.Text = "No se pudo optimizar.";
        }
    }
}
