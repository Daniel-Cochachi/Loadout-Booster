using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Loadout.Services;
using Loadout.ViewModels;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public partial class DiagnosticsDialog : FluentWindow
{
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;

    public DiagnosticsDialog(DatabaseService db, MainViewModel vm)
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        _db = db;
        _vm = vm;
        Loaded += async (_, _) =>
        {
            try
            {
                var loads = await _db.GetLoadoutsAsync();
                LoadoutsCount.Text = loads.Count.ToString();
                SlotsCount.Text = loads.Sum(l => l.ItemCount).ToString();
                SessionsCount.Text = (await _db.GetSessionCountAsync()).ToString();
                RamText.Text = $"{Environment.WorkingSet / (1024.0 * 1024.0):0.0} MB";
                DbPathText.Text = _db.GetDatabasePath();
            }
            catch { }
        };
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(_db.GetDatabasePath())!;
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
        }
        catch { }
    }

    private void Seed_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.AddDemoDataCommand.CanExecute(null)) _vm.AddDemoDataCommand.Execute(null);
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
