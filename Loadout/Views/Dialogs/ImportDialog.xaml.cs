using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Loadout.Services;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public sealed class ImportRow : INotifyPropertyChanged
{
    public ShortcutEntry Entry { get; }
    private bool _checked;
    public bool IsChecked
    {
        get => _checked;
        set { _checked = value; OnPropertyChanged(); }
    }
    public ImportRow(ShortcutEntry e) => Entry = e;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public partial class ImportDialog : FluentWindow
{
    private readonly ObservableCollection<ImportRow> _all = new();
    private bool _bulk; // TODOS/NINGUNO: no refrescar por cada fila
    public System.Collections.Generic.List<ShortcutEntry> Selected { get; } = new();

    public ImportDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        ResultList.ItemsSource = _all;
        Loaded += async (_, _) => await ScanAsync();
    }

    private async System.Threading.Tasks.Task ScanAsync()
    {
        try
        {
            var list = await ShortcutScannerService.ScanAsync();
            _all.Clear();
            foreach (var e in list)
            {
                var row = new ImportRow(e);
                row.PropertyChanged += (_, _) => { if (!_bulk) UpdateCount(); };
                _all.Add(row);
            }
            StatusText.Text = $"{_all.Count} ACCESOS ENCONTRADOS";
            UpdateCount();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error escaneando: " + ex.Message;
        }
    }

    private void UpdateCount()
    {
        int n = _all.Count(r => r.IsChecked);
        CountText.Text = $"{n} seleccionados";
        // NOTA: no tocar el filtro aquí. ApplyFilter solo va en Filter_Changed:
        // reasignar view.Filter mientras se itera la vista lanza InvalidOperationException.
    }

    private void Filter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter();

    private void ApplyFilter()
    {
        string f = FilterBox.Text?.Trim() ?? string.Empty;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ResultList.ItemsSource);
        if (view == null) return;
        if (string.IsNullOrWhiteSpace(f)) view.Filter = null;
        else view.Filter = o => o is ImportRow r &&
            (r.Entry.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
             r.Entry.Target.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _bulk = true;
        try { foreach (var r in _all) r.IsChecked = true; }
        finally { _bulk = false; }
        UpdateCount();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _bulk = true;
        try { foreach (var r in _all) r.IsChecked = false; }
        finally { _bulk = false; }
        UpdateCount();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        Selected.AddRange(_all.Where(r => r.IsChecked).Select(r => r.Entry));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
