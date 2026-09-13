using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Loadout.Models;
using Loadout.Services;
using Loadout.ViewModels;
using Loadout.Views.Dialogs;
using WpfUiControls = Wpf.Ui.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout;

public partial class MainWindow : FluentWindow
{
    private const int HOTKEY_ID = 0xB007;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_L = 0x4C;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private MainViewModel _vm = null!;
    private HwndSource? _hwnd;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Ventana primero, datos después: shell visible al instante, carga async.
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var db = (DatabaseService)Application.Current.Properties["Db"]!;
        _vm = new MainViewModel(db);
        DataContext = _vm;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedLoadout))
                _ = RefreshFavStarAsync();
        };
        await _vm.LoadAsync();
        await RefreshFavStarAsync();
        AnimateCarouselEntrance();
        FadeInSparkline();
        try { _minToTray = await Db.GetSettingAsync("MinimizeToTray", "true") == "true"; } catch { }
        _vm.StartMonitor();
        _ = WarmUpBoostAsync();
        _ = CleanupOrphanCoversAsync();
    }

    private async Task CleanupOrphanCoversAsync()
    {
        try
        {
            int n = await Db.CleanupOrphanCoversAsync();
            if (n > 0) Loadout.Services.Log.Info($"Limpieza de covers huérfanos: {n} archivo(s).");
        }
        catch (Exception ex) { Loadout.Services.Log.Error("CleanupOrphanCoversAsync", ex); }
    }

    // Entrada escalonada tipo cascada para el carrusel de perfiles
    private void AnimateCarouselEntrance()
    {
        try
        {
            for (int i = 0; i < PosterList.Items.Count; i++)
            {
                if (PosterList.ItemContainerGenerator.ContainerFromIndex(i) is not UIElement c) continue;
                if (c.Opacity >= 1 && c.RenderTransform is TranslateTransform tt && tt.Y == 0) continue;
                c.Opacity = 0;
                c.RenderTransformOrigin = new Point(0.5, 0.5);
                c.RenderTransform = new TranslateTransform(0, 16);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = easing };
                Storyboard.SetTarget(fade, c);
                Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
                var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = easing };
                Storyboard.SetTarget(slide, c);
                Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(i * 45) };
                sb.Children.Add(fade);
                sb.Children.Add(slide);
                sb.Begin((FrameworkElement)c, true);
            }
        }
        catch { }
    }

    private void FadeInSparkline()
    {
        try
        {
            SparkLine.Opacity = 0;
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(600))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            SparkLine.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwnd?.AddHook(WndProc);
        _ = ApplyHotkeyAsync();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            Dispatcher.Invoke(() => Tray_Open(this, new RoutedEventArgs()));
        }
        return IntPtr.Zero;
    }

    private async Task ApplyHotkeyAsync()
    {
        if (_hwnd == null) return;
        try
        {
            UnregisterHotKey(_hwnd.Handle, HOTKEY_ID);
            string? on = await Db.GetSettingAsync("HotkeyEnabled", "true");
            bool armed = false;
            if (on == "true")
                armed = RegisterHotKey(_hwnd.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_L);
            if (_vm != null) _vm.HotkeyArmed = armed;
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (_hwnd != null) UnregisterHotKey(_hwnd.Handle, HOTKEY_ID);
        }
        catch { }
        try { _vm?.RestoreDndIfActive(); } catch { }
        try { Tray.Dispose(); } catch { }
        base.OnClosed(e);
    }

    private DatabaseService Db => (DatabaseService)Application.Current.Properties["Db"]!;

    private async Task<bool> CheckFreeLimitAsync()
    {
        string? max = await Db.GetSettingAsync("MaxFreeLoadouts", "3");
        if (int.TryParse(max, out int lim) && _vm.Loadouts.Count >= lim)
        {
            ConfirmDialog.Info(this, "LÍMITE GRATIS",
                $"Alcanzaste el límite gratis ({lim} perfiles). Desbloquea PRO para crear más.");
            return false;
        }
        return true;
    }

    // ---------- Favorito (bandeja + hotkey juegan este) ----------
    private async Task<int?> GetFavoriteIdAsync()
    {
        string? v = await Db.GetSettingAsync("FavoriteLoadoutId", "");
        return int.TryParse(v, out int id) ? id : null;
    }

    private async Task RefreshFavStarAsync()
    {
        try
        {
            int? fav = await GetFavoriteIdAsync();
            int? sel = _vm.SelectedLoadout?.Id;
            FavBtn.Content = (fav.HasValue && sel.HasValue && fav.Value == sel.Value) ? "★" : "☆";
        }
        catch { }
    }

    private async void Fav_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null) return;
        int sel = _vm.SelectedLoadout.Id;
        int? fav = await GetFavoriteIdAsync();
        await Db.SetSettingAsync("FavoriteLoadoutId",
            (fav.HasValue && fav.Value == sel) ? "" : sel.ToString());
        await RefreshFavStarAsync();
    }

    private async Task TogglePlayFavoriteAsync()
    {
        if (_vm == null) return;
        await Dispatcher.InvokeAsync(async () =>
        {
            if (_vm.IsPlaying)
            {
                if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null);
                return;
            }
            int? fav = await GetFavoriteIdAsync();
            var target = (fav.HasValue ? _vm.Loadouts.FirstOrDefault(l => l.Id == fav.Value) : null)
                ?? _vm.SelectedLoadout ?? _vm.Loadouts.FirstOrDefault();
            if (target == null) return;
            _vm.SelectedLoadout = target;
            // Esperar a que carguen los slots antes de jugar
            await _vm.LoadSlotsAsync();
            if (_vm.PlayCommand.CanExecute(null)) _vm.PlayCommand.Execute(null);
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
        => ScrollPosters(-420);

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
        => ScrollPosters(420);

    private void ScrollPosters(double delta)
    {
        try
        {
            var sv = FindVisualChild<System.Windows.Controls.ScrollViewer>(PosterList);
            sv?.ScrollToVerticalOffset(sv.VerticalOffset + delta);
        }
        catch { }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // ---------- Directivas ranked ----------
    private void Directives_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null) return;
        int lid = _vm.SelectedLoadout.Id;
        _ = Task.Run(async () =>
        {
            var rows = await Db.GetDirectivesAsync(lid);
            var current = rows.Select(d => d.Text).ToList();
            var dlg = await Dispatcher.InvokeAsync(() =>
            {
                var d = new DirectivesDialog(current) { Owner = this };
                return d.ShowDialog() == true ? d : null;
            });
            if (dlg == null) return;
            await Db.SaveDirectivesAsync(lid, dlg.Lines);
            await Dispatcher.InvokeAsync(async () => await _vm.LoadDirectivesAsync());
        });
    }

    // ---------- Diagnostics ----------
    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DiagnosticsDialog(Db, _vm) { Owner = this };
        dlg.ShowDialog();
    }

    // ---------- Game Boost ----------
    private BoostDialog? _boostDlg;

    private void Boost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_boostDlg == null)
            {
                _boostDlg = new BoostDialog(Db, _vm);
                _boostDlg.Owner = this;
            }
            _boostDlg.Owner = this;
            if (!_boostDlg.IsVisible) _boostDlg.Show();
            _boostDlg.WindowState = WindowState.Normal;
            _boostDlg.Activate();
        }
        catch
        {
            var dlg = new BoostDialog(Db, _vm) { Owner = this };
            dlg.Show();
        }
    }

    private async Task WarmUpBoostAsync()
    {
        try
        {
            await Task.Delay(8000);
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _boostDlg ??= new BoostDialog(Db, _vm);
                    _boostDlg.Owner = this;
                }
                catch { }
            });
        }
        catch { }
    }

    // ---------- Ajustes ----------
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(Db) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try { _minToTray = await Db.GetSettingAsync("MinimizeToTray", "true") == "true"; } catch { }
        _ = ApplyHotkeyAsync(); // el hotkey pudo cambiar
    }

    private void NewLoadout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LoadoutDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _ = Task.Run(async () =>
        {
            if (!await CheckFreeLimitAsync()) return;
            await Db.CreateLoadoutAsync(dlg.LoadoutName, dlg.ColorHex, dlg.CoverPath, dlg.ColorHex2, dlg.Notes);
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadAsync();
                AnimateCarouselEntrance();
            });
        });
    }

    private void EditLoadout_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null) return;
        var sel = _vm.SelectedLoadout;
        var dlg = new LoadoutDialog(sel.Name, sel.ColorHex, sel.Tag, sel.CoverPath, sel.ColorHex2, sel.Notes) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _ = Task.Run(async () =>
        {
            await Db.UpdateLoadoutAsync(sel.Id, dlg.LoadoutName, dlg.ColorHex, dlg.LoadoutTag, dlg.CoverPath, dlg.ColorHex2, dlg.Notes);
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadAsync();
                AnimateCarouselEntrance();
            });
        });
    }

    private void DeleteLoadout_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null || _vm.IsPlaying) return;
        var sel = _vm.SelectedLoadout;
        if (!ConfirmDialog.Ask(this, "BORRAR PERFIL",
                $"¿Eliminar '{sel.Name}' y todos sus accesos? Esta acción no se puede deshacer.",
                "ELIMINAR", "CANCELAR", danger: true)) return;
        _ = Task.Run(async () =>
        {
            await Db.DeleteLoadoutAsync(sel.Id);
            await Db.CleanupOrphanCoversAsync();
            await Dispatcher.InvokeAsync(async () =>
            {
                _vm.SelectedLoadout = null;
                await _vm.LoadAsync();
                AnimateCarouselEntrance();
            });
        });
    }

    private static string SanitizeFileName(string name)
    {
        var invalids = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalids.Contains(c)).ToArray());
    }

    private async void ExportLoadout_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null)
        {
            ConfirmDialog.Info(this, "SIN PERFIL", "Selecciona un perfil para exportar.");
            return;
        }
        var sel = _vm.SelectedLoadout;
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar loadout",
            Filter = "Loadout backup (*.loadout.json)|*.loadout.json|JSON (*.json)|*.json",
            DefaultExt = ".loadout.json",
            FileName = $"loadout-{SanitizeFileName(sel.Name)}.loadout.json"
        };
        if (sfd.ShowDialog(this) != true) return;
        try
        {
            var model = new Loadout.Models.Loadout
            {
                Id = sel.Id, Name = sel.Name, ColorHex = sel.ColorHex,
                ColorHex2 = sel.ColorHex2, Tag = sel.Tag, Notes = sel.Notes,
                CoverPath = sel.CoverPath
            };
            await BackupService.ExportAsync(Db, new[] { model }, sfd.FileName);
            _vm.StatusLine = $"⮬ Perfil '{sel.Name}' exportado.";
        }
        catch (Exception ex)
        {
            Loadout.Services.Log.Error("ExportLoadout_Click", ex);
            ConfirmDialog.Info(this, "ERROR AL EXPORTAR", ex.Message);
        }
    }

    private async void ImportLoadout_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar loadout(s)",
            Filter = "Loadout backup (*.loadout.json;*.json)|*.loadout.json;*.json|Todos|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != true) return;
        try
        {
            var (created, skipped) = await BackupService.ImportAsync(Db, ofd.FileName);
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadAsync();
                AnimateCarouselEntrance();
                _vm.StatusLine = created > 0
                    ? $"⮮ Importados {created} perfil(es)." + (skipped > 0 ? $" Saltados: {skipped} (ya existen)." : string.Empty)
                    : $"Nada que importar.{ (skipped > 0 ? $" Saltados: {skipped} (ya existen)." : string.Empty) }";
            });
        }
        catch (Exception ex)
        {
            Loadout.Services.Log.Error("ImportLoadout_Click", ex);
            ConfirmDialog.Info(this, "ERROR AL IMPORTAR", ex.Message);
        }
    }

    private void Slots_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Slots_Drop(object sender, DragEventArgs e)
    {
        if (_vm.SelectedLoadout == null || _vm.IsPlaying) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var exes = files.Where(f => string.Equals(Path.GetExtension(f), ".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (exes.Count == 0) return;
        _ = Task.Run(async () =>
        {
            int lid = _vm.SelectedLoadout!.Id;
            var existing = await Db.GetItemsAsync(lid);
            int order = existing.Count;
            foreach (string exe in exes)
            {
                await Db.CreateItemAsync(new LoadoutItem
                {
                    LoadoutId = lid, Name = Path.GetFileNameWithoutExtension(exe),
                    Kind = "Exe", Target = exe, Arguments = string.Empty,
                    OrderIndex = order++, DelaySeconds = 2
                });
            }
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
                _vm.StatusLine = $"＋ {exes.Count} acceso(s) por arrastre.";
            });
        });
    }

    private void Running_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null)
        {
            ConfirmDialog.Info(this, "SIN PERFIL", "Crea un perfil primero.");
            return;
        }
        var dlg = new RunningDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected.Count == 0) return;
        _ = Task.Run(async () =>
        {
            int lid = _vm.SelectedLoadout!.Id;
            var existing = await Db.GetItemsAsync(lid);
            int order = existing.Count;
            foreach (var (name, path) in dlg.Selected)
            {
                await Db.CreateItemAsync(new LoadoutItem
                {
                    LoadoutId = lid, Name = name, Kind = "Exe", Target = path,
                    Arguments = string.Empty, OrderIndex = order++, DelaySeconds = 2
                });
            }
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
                _vm.StatusLine = $"＋ {dlg.Selected.Count} juego(s) en ejecución agregados.";
            });
        });
    }

    private void Steam_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null)
        {
            ConfirmDialog.Info(this, "SIN PERFIL", "Crea un perfil primero.");
            return;
        }
        var dlg = new SteamDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected.Count == 0) return;
        _ = Task.Run(async () =>
        {
            int lid = _vm.SelectedLoadout!.Id;
            var existing = await Db.GetItemsAsync(lid);
            int order = existing.Count;
            foreach (var (name, appId) in dlg.Selected)
            {
                await Db.CreateItemAsync(new LoadoutItem
                {
                    LoadoutId = lid, Name = name, Kind = "Steam", Target = appId,
                    Arguments = string.Empty, OrderIndex = order++, DelaySeconds = 2
                });
            }
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
                _vm.StatusLine = $"＋ {dlg.Selected.Count} juego(s) de Steam agregados.";
            });
        });
    }

    private void FooterTag_OpenProfile(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm?.SelectedLoadout == null) return;
        EditLoadout_Click(this, new RoutedEventArgs());
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null)
        {
            ConfirmDialog.Info(this, "SIN PERFIL", "Crea un perfil primero.");
            return;
        }
        var dlg = new ItemDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _ = Task.Run(async () =>
        {
            int lid = _vm.SelectedLoadout!.Id;
            var existing = await Db.GetItemsAsync(lid);
            await Db.CreateItemAsync(new LoadoutItem
            {
                LoadoutId = lid, Name = dlg.ItemName, Kind = dlg.Kind,
                Target = dlg.Target, Arguments = dlg.Arguments,
                OrderIndex = existing.Count, DelaySeconds = dlg.DelaySeconds,
                HighPriority = dlg.HighPriority
            });
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
            });
        });
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null || _vm.SelectedSlot == null || _vm.IsPlaying) return;
        var slot = _vm.SelectedSlot;
        _ = Task.Run(async () =>
        {
            var items = await Db.GetItemsAsync(_vm.SelectedLoadout!.Id);
            var cur = items.FirstOrDefault(i => i.Id == slot.ItemId);
            if (cur == null) return;
            var result = await Dispatcher.InvokeAsync(() =>
            {
                var dlg = new ItemDialog(cur.Name, cur.Kind, cur.Target, cur.Arguments, cur.DelaySeconds, cur.HighPriority) { Owner = this };
                return dlg.ShowDialog() == true ? dlg : null;
            });
            if (result == null) return;
            cur.Name = result.ItemName;
            cur.Kind = result.Kind;
            cur.Target = result.Target;
            cur.Arguments = result.Arguments;
            cur.DelaySeconds = result.DelaySeconds;
            cur.HighPriority = result.HighPriority;
            await Db.UpdateItemAsync(cur);
            await Dispatcher.InvokeAsync(async () => await _vm.LoadSlotsAsync());
        });
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSlot == null || _vm.IsPlaying) return;
        int id = _vm.SelectedSlot.ItemId;
        _ = Task.Run(async () =>
        {
            await Db.DeleteItemAsync(id);
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
            });
        });
    }

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
        => await _vm.MoveSelectedSlotAsync(-1);

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
        => await _vm.MoveSelectedSlotAsync(1);

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoadout == null)
        {
            ConfirmDialog.Info(this, "SIN PERFIL", "Crea un perfil primero.");
            return;
        }
        var dlg = new ImportDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected.Count == 0) return;
        _ = Task.Run(async () =>
        {
            int lid = _vm.SelectedLoadout!.Id;
            var existing = await Db.GetItemsAsync(lid);
            var known = new System.Collections.Generic.HashSet<string>(
                existing.Select(i => i.Kind + "|" + i.Target),
                System.StringComparer.OrdinalIgnoreCase);
            int order = existing.Count;
            int added = 0;
            foreach (var s in dlg.Selected)
            {
                string key = s.Kind + "|" + s.Target;
                if (!known.Add(key)) continue; // no duplicar lo ya importado
                await Db.CreateItemAsync(new LoadoutItem
                {
                    LoadoutId = lid, Name = s.Name, Kind = s.Kind,
                    Target = s.Target, Arguments = s.Arguments,
                    OrderIndex = order++, DelaySeconds = 2
                });
                added++;
            }
            await Dispatcher.InvokeAsync(async () =>
            {
                await _vm.LoadSlotsAsync();
                await _vm.RefreshCountsAsync();
                _vm.StatusLine = $"{added} accesos importados.";
            });
        });
    }

    private void Tray_Open(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void Tray_PlayFav(object sender, RoutedEventArgs e)
        => await TogglePlayFavoriteAsync();

    private void Tray_Boost(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(() =>
        {
            int n = 0;
            double freed = 0;
            try
            {
                var bs = new BoostService();
                bs.GetMemory(out _, out double before);
                n = bs.TrimWorkingSets();
                bs.GetMemory(out _, out double after);
                freed = after - before;
                BoostService.TrimSelf();
            }
            catch { }
            Dispatcher.Invoke(() =>
            {
                try { Tray.ShowBalloonTip("⚡ GAME BOOST", $"＋{freed:0} MB · {n} procesos.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info); }
                catch { }
            });
        });
    }

    private bool _reallyExit;
    private bool _trayHintShown;
    private bool _minToTray = true;

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            string? v = "true";
            try { v = await Db.GetSettingAsync("MinimizeToTray", "true"); } catch { }
            if (v == "true")
            {
                e.Cancel = true;
                Hide();
                try { BoostService.TrimSelf(); } catch { }
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    try { Tray.ShowBalloonTip("LOADOUT", "Sigo en segundo plano. Doble clic para abrir.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info); }
                    catch { }
                }
                return;
            }
        }
        base.OnClosing(e);
    }

    private void Tray_Exit(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        Application.Current.Shutdown();
    }

    private void Tray_Toggle(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
                Tray_Open(sender, e);
            else
                WindowState = WindowState.Minimized;
        }
        catch { }
    }
}
