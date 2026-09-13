using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public sealed class RunningRow
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}

public partial class RunningDialog : FluentWindow
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "sihost", "textinputhost", "applicationframehost",
        "systemsettings", "searchhost", "startmenuexperiencehost", "taskmgr",
        "loadout", "idle"
    };

    public List<(string Name, string Path)> Selected { get; } = new();

    public RunningDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        Refresh();
    }

    private void Refresh()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<RunningRow>();
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self) continue;
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                string name;
                try { name = p.ProcessName; } catch { continue; }
                if (Skip.Contains(name)) continue;
                string? path;
                try { path = p.MainModule?.FileName; } catch { continue; }
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;
                string title;
                try { title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? name : p.MainWindowTitle; }
                catch { title = name; }
                rows.Add(new RunningRow { Name = title.Length > 48 ? title.Substring(0, 48) + "…" : title, Path = path });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        ProcList.ItemsSource = rows.OrderBy(r => r.Name).ToList();
        CountText.Text = $"{rows.Count} ABIERTOS";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in (ProcList.ItemsSource as IEnumerable<RunningRow> ?? Enumerable.Empty<RunningRow>()))
            r.IsChecked = true;
        ProcList.Items.Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in (ProcList.ItemsSource as IEnumerable<RunningRow> ?? Enumerable.Empty<RunningRow>()))
            if (r.IsChecked) Selected.Add((r.Name, r.Path));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
