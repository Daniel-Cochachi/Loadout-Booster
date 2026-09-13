using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
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
            DndBox.IsChecked = await _db.GetSettingAsync("RankedDnd", "true") == "true";
            TrayBox.IsChecked = await _db.GetSettingAsync("MinimizeToTray", "true") == "true";
            HotBox.IsChecked = await _db.GetSettingAsync("HotkeyEnabled", "true") == "true";
            AutoBox.IsChecked = AutostartService.IsEnabled()
                || await _db.GetSettingAsync("StartWithWindows", "false") == "true";
            DbPathText.Text = _db.GetDatabasePath();
            VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        bool auto = AutoBox.IsChecked == true;
        await _db.SetSettingAsync("RankedDnd", DndBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("MinimizeToTray", TrayBox.IsChecked == true ? "true" : "false");
        await _db.SetSettingAsync("HotkeyEnabled", HotBox.IsChecked == true ? "true" : "false");
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
