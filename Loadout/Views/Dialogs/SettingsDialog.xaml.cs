using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Loadout.Services;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public partial class SettingsDialog : FluentWindow
{
    private readonly DatabaseService _db;

    public SettingsDialog(DatabaseService db)
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        _db = db;
        Loaded += async (_, _) =>
        {
            AutoDetectBox.IsChecked = await _db.GetSettingAsync("AutoDetectGames", "true") == "true";
            AlwaysDndBox.IsChecked = await _db.GetSettingAsync("AlwaysDnd", "true") == "true";
            DndBox.IsChecked = await _db.GetSettingAsync("RankedDnd", "true") == "true";
            TrayBox.IsChecked = await _db.GetSettingAsync("MinimizeToTray", "true") == "true";
            HotBox.IsChecked = await _db.GetSettingAsync("HotkeyEnabled", "true") == "true";
            AutoBox.IsChecked = AutostartService.IsEnabled()
                || await _db.GetSettingAsync("StartWithWindows", "false") == "true";
            DashboardEnabledBox.IsChecked = await _db.GetSettingAsync("DashboardEnabled", "true") == "true";

            string currentHotkey = await _db.GetSettingAsync("DashboardHotkey", "Ctrl + F11 (Recomendado)") ?? "Ctrl + F11 (Recomendado)";
            foreach (System.Windows.Controls.ComboBoxItem item in DashboardHotkeyCombo.Items)
            {
                if (item.Content?.ToString() == currentHotkey)
                {
                    DashboardHotkeyCombo.SelectedItem = item;
                    break;
                }
            }

            UpdateTcpStatusUi();
            DbPathText.Text = _db.GetDatabasePath();
            VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        };
    }

    private void UpdateTcpStatusUi()
    {
        bool opt = NetworkOptimizerService.IsOptimized();
        if (opt)
        {
            TcpStatusText.Text = "🟢 Latencia TCP: Optimizada (TCP NoDelay activo · Sin retardo de Nagle)";
            TcpStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255));
        }
        else
        {
            TcpStatusText.Text = "⚪ Latencia TCP: Estándar de Windows (Búfer de Nagle activo)";
            TcpStatusText.Foreground = (Brush)FindResource("CabText");
        }
    }

    private void ApplyTcp_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = NetworkOptimizerService.ApplyOptimization();
        TcpFeedbackText.Text = (success ? "✔ " : "⚠ ") + msg;
        TcpFeedbackText.Foreground = success ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(255, 179, 0));
        TcpFeedbackText.Visibility = Visibility.Visible;
        UpdateTcpStatusUi();
    }

    private void RevertTcp_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = NetworkOptimizerService.RevertOptimization();
        TcpFeedbackText.Text = (success ? "✔ " : "⚠ ") + msg;
        TcpFeedbackText.Foreground = (Brush)FindResource("CabTextMut");
        TcpFeedbackText.Visibility = Visibility.Visible;
        UpdateTcpStatusUi();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        bool auto = AutoBox.IsChecked == true;
        await _db.SetSettingAsync("AutoDetectGames", AutoDetectBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("AlwaysDnd", AlwaysDndBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("RankedDnd", DndBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("MinimizeToTray", TrayBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("HotkeyEnabled", HotBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("DashboardEnabled", DashboardEnabledBox.IsChecked == true ? "true" : "false");
        if (DashboardHotkeyCombo.SelectedItem is System.Windows.Controls.ComboBoxItem selItem)
        {
            await _db.SetSettingAsync("DashboardHotkey", selItem.Content?.ToString() ?? "Ctrl + F11 (Recomendado)");
        }
        await _db.SetSettingAsync("StartWithWindows", auto ? "true" : "false");
        AutostartService.SetEnabled(auto);
        DialogResult = true;
    }

    private void OpenDbFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(_db.GetDatabasePath())!;
            Process.Start(new ProcessStartInfo
            {
                FileName = dir, UseShellExecute = true, Verb = "open"
            });
        }
        catch { }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        UpdateBtn.Content = "⟳ BUSCANDO…";
        UpdateStatusText.Text = "Buscando actualizaciones en GitHub…";
        try
        {
            if (!UpdateService.IsVelopackInstall)
            {
                UpdateStatusText.Text = "⚠ Esta instalación no soporta auto-update. Descarga el Setup.exe desde GitHub.";
                UpdateBtn.Content = "⟳ BUSCAR ACTUALIZACIONES";
                UpdateBtn.IsEnabled = true;
                return;
            }
            string? msg = await UpdateService.CheckAndApplyAsync(async question =>
            {
                return await Dispatcher.InvokeAsync(() =>
                    ConfirmDialog.Ask(this, "ACTUALIZACIÓN", question, "REINICIAR", "DESPUÉS"));
            });
            if (msg != null)
                UpdateStatusText.Text = "✅ " + msg;
            else
                UpdateStatusText.Text = "✅ Ya tienes la última versión.";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"❌ Error: {ex.Message}";
            Log.Error("CheckUpdate_Click", ex);
        }
        finally
        {
            UpdateBtn.Content = "⟳ BUSCAR ACTUALIZACIONES";
            UpdateBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
