using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Loadout.Services;
using Loadout.ViewModels;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public sealed class HogRow
{
    public int Pid { get; set; }
    public double Mb { get; set; }
    public string Display { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}

public partial class BoostDialog : FluentWindow
{
    private readonly BoostService _boost = new();
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private bool _loadingChecks = true;

    public BoostDialog(DatabaseService db, MainViewModel vm)
    {
        InitializeComponent();
        IsVisibleChanged += async (_, e) =>
        {
            if (!(e.NewValue is true)) return;
            UiFx.FadeIn(this);
            RefreshContext();
            await RefreshAllAsync();
        };
        _db = db;
        _vm = vm;
    }

    /// <summary>Re-sincroniza perfil, checks y kill-list en cada apertura (la ventana se reutiliza).</summary>
    private void RefreshContext()
    {
        _loadingChecks = true;
        try
        {
            string pname = _vm.SelectedLoadout?.Name ?? "SIN PERFIL";
            KillListLabel.Text = $"CIERRE AUTO · {pname.ToUpperInvariant()} (ej. chrome)";
            AutoBoostBox.IsChecked = _vm.BoostOnLaunch;
            PerfPowerBox.IsChecked = _vm.PerfPower;
        }
        finally { _loadingChecks = false; }
        _ = LoadGameDvrAsync();
        _ = LoadKillListAsync();
    }

    private async Task LoadGameDvrAsync()
    {
        try { GameDvrBox.IsChecked = await _db.GetSettingAsync("GameDvrOff", "false") == "true"; }
        catch { }
        finally { _loadingChecks = false; }
    }

    private async Task LoadKillListAsync()
    {
        try
        {
            int? lid = _vm.SelectedLoadout?.Id;
            KillListBox.ItemsSource = lid == null
                ? new List<string> { "(elige un perfil para configurar)" }
                : await _db.GetKillListAsync(lid.Value);
        }
        catch { KillListBox.ItemsSource = null; }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            _boost.GetMemory(out double total, out double avail);
            double used = total - avail;
            RamText.Text = $"RAM: {used:0} / {total:0} MB LIBRES · {avail:0} MB";
            RamBar.Value = total > 0 ? used / total * 100.0 : 0;
        }
        catch { RamText.Text = "RAM: —"; }
        // Pase 1 (instantáneo): lista sin CPU para que la ventana pinte ya
        List<HogRow> rows = new();
        try
        {
            rows = _boost.GetTopHogs(8, _vm.GetSessionPids())
                .Select(h => new HogRow { Pid = h.Pid, Mb = h.Mb, Display = $"{h.Name} · {h.Mb:0} MB · CPU …" })
                .ToList();
            if (rows.Count == 0)
                rows.Add(new HogRow { Pid = -1, Mb = 0, Display = "SIN TRAGALONAS · TODO LIMPIO ✔", IsChecked = false });
            HogList.ItemsSource = rows;
        }
        catch { HogList.ItemsSource = null; return; }
        // Pase 2 (diferido): %CPU real sin bloquear la apertura
        try
        {
            var cpu = await _boost.SampleCpuAsync(500);
            foreach (var r in rows)
            {
                if (r.Pid <= 0) continue;
                string c = cpu.TryGetValue(r.Pid, out double v) ? $" · CPU {v:0}%" : string.Empty;
                r.Display = $"{r.Display.Split('·')[0].Trim()} · {r.Mb:0} MB{c}";
            }
            HogList.ItemsSource = null;
            HogList.ItemsSource = rows;
        }
        catch { }
        finally { LoadingOverlay.Visibility = Visibility.Collapsed; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ResultText.Text = string.Empty;
        await RefreshAllAsync();
    }

    private async void Trim_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _boost.GetMemory(out _, out double before);
            int n = _boost.TrimWorkingSets();
            _boost.GetMemory(out _, out double after);
            ResultText.Text = $"✔ +{after - before:0} MB · {n} PROCESOS";
        }
        catch { ResultText.Text = "NO SE PUDO OPTIMIZAR"; }
        await RefreshAllAsync();
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultText.Text = "LIMPIANDO…";
            var (mb, files) = await Task.Run(() => _boost.CleanTemp());
            ResultText.Text = $"✔ +{mb:0} MB · {files} ARCHIVOS TEMP";
        }
        catch { ResultText.Text = "NO SE PUDO LIMPIAR"; }
        await RefreshAllAsync();
    }

    private async void Kill_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pids = (HogList.ItemsSource as IEnumerable<HogRow> ?? new List<HogRow>())
                .Where(r => r.Pid > 0 && r.IsChecked).Select(r => r.Pid).ToList();
            if (pids.Count == 0)
            {
                ResultText.Text = "MARCA AL MENOS UNO";
                return;
            }
            if (!ConfirmDialog.Ask(this, "CERRAR PROCESOS",
                    $"¿Cerrar {pids.Count} proceso(s)? Pueden perder trabajo no guardado.",
                    "CERRAR", "CANCELAR", danger: true)) return;
            var (killed, failed) = _boost.KillPids(pids);
            ResultText.Text = $"✔ {killed} CERRADOS" + (failed > 0 ? $" · {failed} FALLARON" : string.Empty);
        }
        catch { ResultText.Text = "NO SE PUDO CERRAR"; }
        await RefreshAllAsync();
    }

    private async void KillAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int? lid = _vm.SelectedLoadout?.Id;
            if (lid == null)
            {
                ResultText.Text = "ELIGE UN PERFIL PRIMERO";
                return;
            }
            string p = (KillBox.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(p)) return;
            await _db.AddKillPatternAsync(lid.Value, p);
            KillBox.Text = string.Empty;
            await LoadKillListAsync();
        }
        catch { }
    }

    private async void KillRemove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int? lid = _vm.SelectedLoadout?.Id;
            if (lid == null || KillListBox.SelectedItem is not string sel) return;
            await _db.DeleteKillPatternAsync(lid.Value, sel);
            await LoadKillListAsync();
        }
        catch { }
    }

    private void AutoBoost_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingChecks) return;
        try { _vm.BoostOnLaunch = AutoBoostBox.IsChecked == true; } catch { }
    }

    private void PerfPower_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingChecks) return;
        try { _vm.PerfPower = PerfPowerBox.IsChecked == true; } catch { }
    }

    private async void GameDvr_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingChecks) return;
        try
        {
            bool off = GameDvrBox.IsChecked == true;
            if (off)
            {
                var (g, a) = _boost.ReadGameDvr();
                await _db.SetSettingAsync("GameDvrPrevG", g?.ToString() ?? "-1");
                await _db.SetSettingAsync("GameDvrPrevA", a?.ToString() ?? "-1");
                _boost.WriteGameDvr(0, 0);
            }
            else
            {
                string? g = await _db.GetSettingAsync("GameDvrPrevG", "1");
                string? a = await _db.GetSettingAsync("GameDvrPrevA", "1");
                _boost.WriteGameDvr(ParsePrev(g), ParsePrev(a));
            }
            await _db.SetSettingAsync("GameDvrOff", off ? "true" : "false");
        }
        catch { }
    }

    private static int ParsePrev(string? v) => int.TryParse(v, out int n) && n >= 0 ? n : 1;

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        try { Hide(); } catch { }
    }
}
