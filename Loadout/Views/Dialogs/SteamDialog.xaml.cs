using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public sealed class SteamRow
{
    public string Name { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}

public partial class SteamDialog : FluentWindow
{
    public List<(string Name, string AppId)> Selected { get; } = new();

    public SteamDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        Refresh();
    }

    private static string? FindSteamDir()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false);
            var v = k?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(v)) return v;
        }
        catch { }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam", false);
            var v = k?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(v)) return v;
        }
        catch { }
        const string def = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(def) ? def : null;
    }

    private static List<string> FindLibraries(string steamDir)
    {
        var libs = new List<string> { steamDir };
        try
        {
            string vdf = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                string text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, @"""path""\s+""([^""]+)"""))
                {
                    string p = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase))
                        libs.Add(p);
                }
            }
        }
        catch { }
        return libs;
    }

    private void Refresh()
    {
        var rows = new List<SteamRow>();
        try
        {
            string? steam = FindSteamDir();
            if (steam == null)
            {
                StatusText.Text = "STEAM NO ENCONTRADO EN ESTE EQUIPO";
                GameList.ItemsSource = rows;
                CountText.Text = "0 JUEGOS";
                return;
            }
            var seen = new HashSet<string>();
            foreach (string lib in FindLibraries(steam))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        string text = File.ReadAllText(acf);
                        string id = Regex.Match(text, @"""appid""\s+""(\d+)""").Groups[1].Value;
                        string name = Regex.Match(text, @"""name""\s+""([^""]+)""").Groups[1].Value;
                        string dir = Regex.Match(text, @"""installdir""\s+""([^""]+)""").Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(Path.Combine(apps, "common", dir))) continue;
                        if (!seen.Add(id)) continue;
                        rows.Add(new SteamRow { Name = name, AppId = id, Detail = $"AppID {id}" });
                    }
                    catch { }
                }
            }
            StatusText.Text = rows.Count > 0 ? $"{rows.Count} JUEGOS INSTALADOS" : "SIN JUEGOS INSTALADOS";
        }
        catch { StatusText.Text = "NO SE PUDO LEER LA LIBRERÍA"; }
        GameList.ItemsSource = rows.OrderBy(r => r.Name).ToList();
        CountText.Text = $"{rows.Count} JUEGOS";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in (GameList.ItemsSource as IEnumerable<SteamRow> ?? Enumerable.Empty<SteamRow>()))
            r.IsChecked = true;
        GameList.Items.Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in (GameList.ItemsSource as IEnumerable<SteamRow> ?? Enumerable.Empty<SteamRow>()))
            if (r.IsChecked) Selected.Add((r.Name, r.AppId));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
