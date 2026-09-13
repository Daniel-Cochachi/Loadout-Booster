using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WpfUiControls = Wpf.Ui.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public partial class LoadoutDialog : FluentWindow
{
    // Paleta Crimson Noir de 12 — del noir profundo al hueso, pasando por carmesí
    public static readonly List<string> Palette = new()
    {
        "#3A0A16", "#5E021E", "#7A0226", "#9E0330",
        "#C50337", "#E01B4B", "#FF4D6E", "#FF7A95",
        "#F2A9BC", "#E9C9D1", "#8E2440", "#FBF1EB"
    };

    public string LoadoutName { get; private set; } = string.Empty;
    public string ColorHex { get; private set; } = "#C50337";
    public string ColorHex2 { get; private set; } = string.Empty;
    public string LoadoutTag { get; private set; } = string.Empty;
    public string CoverPath { get; private set; } = string.Empty;
    public string Notes { get; private set; } = string.Empty;

    private readonly string _originalCover = string.Empty;
    private string _coverCurrent = string.Empty;
    private bool _syncingHex;
    private string _color1 = "#C50337";
    private string _color2 = "#080B13";
    private bool _editingSecond;

    public LoadoutDialog(string name = "", string colorHex = "#C50337", string tag = "", string coverPath = "", string colorHex2 = "", string notes = "")
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        HeaderTitle.Text = string.IsNullOrWhiteSpace(name) ? "NUEVO PERFIL" : "EDITAR PERFIL";
        NameBox.Text = name;
        TagBox.Text = tag;
        NotesBox.Text = notes ?? string.Empty;
        _originalCover = coverPath ?? string.Empty;
        _coverCurrent = _originalCover;
        UpdateCoverPreview();
        NameBox.Focus();
        var brushes = new List<SolidColorBrush>();
        foreach (var hex in Palette)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            b.Freeze();
            brushes.Add(b);
        }
        ColorList.ItemsSource = brushes;
        _color1 = TryNormalizeHex(colorHex) ?? "#C50337";
        _color2 = TryNormalizeHex(colorHex2) ?? "#080B13";
        _editingSecond = false;
        ApplyColorToEditors(_color1);
        UpdateSwatches();
    }

    private static string? TryNormalizeHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string t = raw.Trim().TrimStart('#');
        if (t.Length != 6) return null;
        foreach (char c in t)
            if (!Uri.IsHexDigit(c)) return null;
        return "#" + t.ToUpperInvariant();
    }

    private string ActiveColor => _editingSecond ? _color2 : _color1;

    private static SolidColorBrush BrushOf(string hex)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            b.Freeze();
            return b;
        }
        catch
        {
            var f = new SolidColorBrush(Colors.Transparent);
            f.Freeze();
            return f;
        }
    }

    private static bool IsLight(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.55;
        }
        catch { return false; }
    }

    private void UpdateSwatches()
    {
        Swatch1.Background = BrushOf(_color1);
        Swatch1.Foreground = BrushOf(IsLight(_color1) ? "#1A1200" : "#FBF1EB");
        Swatch1.BorderBrush = BrushOf(_editingSecond ? "#847B70" : "#FBF1EB");
        Swatch1.BorderThickness = new Thickness(_editingSecond ? 1 : 2.5);
        Swatch2.Background = BrushOf(_color2);
        Swatch2.Foreground = BrushOf(IsLight(_color2) ? "#1A1200" : "#FBF1EB");
        Swatch2.BorderBrush = BrushOf(_editingSecond ? "#FBF1EB" : "#847B70");
        Swatch2.BorderThickness = new Thickness(_editingSecond ? 2.5 : 1);
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        _editingSecond = (string?)((System.Windows.Controls.Button)sender).Tag == "1";
        _syncingHex = true;
        try { ApplyColorToEditors(ActiveColor); }
        finally { _syncingHex = false; }
        UpdateSwatches();
    }

    private void SetActiveColor(string hex)
    {
        if (_editingSecond) _color2 = hex; else _color1 = hex;
        UpdateSwatches();
    }

    /// <summary>Pinta paleta + HEX + R/G/B + muestra con el color dado (sin tocar el otro).</summary>
    private void ApplyColorToEditors(string hex)
    {
        _syncingHex = true;
        try
        {
            HexBox.Text = hex;
            HexPreview.Opacity = 1;
            HexPreview.Background = BrushOf(hex);
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            RBox.Text = c.R.ToString();
            GBox.Text = c.G.ToString();
            BBox.Text = c.B.ToString();
            ColorList.SelectedIndex = Palette.IndexOf(hex);
        }
        catch { }
        finally { _syncingHex = false; }
    }

    private void ColorList_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingHex) return;
        int i = ColorList.SelectedIndex;
        if (i < 0 || i >= Palette.Count) return;
        SetActiveColor(Palette[i]);
        _syncingHex = true;
        try { ApplyColorToEditors(Palette[i]); }
        finally { _syncingHex = false; }
    }

    private void HexBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string? hex = TryNormalizeHex(HexBox.Text);
        if (hex == null)
        {
            HexPreview.Opacity = 0.35;
            return;
        }
        if (_syncingHex)
        {
            HexPreview.Opacity = 1;
            HexPreview.Background = BrushOf(hex);
            return;
        }
        _syncingHex = true;
        try
        {
            SetActiveColor(hex);
            ApplyColorToEditors(hex);
        }
        finally { _syncingHex = false; }
    }

    private static bool TryByte(string? raw, out byte value)
    {
        value = 0;
        if (!int.TryParse((raw ?? string.Empty).Trim(), out int n)) return false;
        if (n < 0 || n > 255) return false;
        value = (byte)n;
        return true;
    }

    private void RgbBox_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingHex) return;
        if (RBox == null || GBox == null || BBox == null) return;
        if (!TryByte(RBox.Text, out byte r) || !TryByte(GBox.Text, out byte g) || !TryByte(BBox.Text, out byte b))
            return;
        string hex = $"#{r:X2}{g:X2}{b:X2}";
        _syncingHex = true;
        try
        {
            SetActiveColor(hex);
            ApplyColorToEditors(hex);
        }
        finally { _syncingHex = false; }
    }

    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Portada del perfil",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Todos|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != true) return;
        _coverCurrent = ofd.FileName;
        UpdateCoverPreview();
    }

    private void ClearCover_Click(object sender, RoutedEventArgs e)
    {
        _coverCurrent = string.Empty;
        UpdateCoverPreview();
    }

    private void UpdateCoverPreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_coverCurrent) && File.Exists(_coverCurrent))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_coverCurrent, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 112;
                bmp.EndInit();
                bmp.Freeze();
                CoverPreview.Source = bmp;
                CoverPlaceholder.Visibility = Visibility.Collapsed;
                return;
            }
        }
        catch { }
        CoverPreview.Source = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            NameBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4D6E")!);
            return;
        }
        LoadoutName = name;
        LoadoutTag = TagBox.Text.Trim();
        Notes = NotesBox.Text.Trim();
        ColorHex = TryNormalizeHex(_color1) ?? "#C50337";
        string? c2 = TryNormalizeHex(_color2);
        ColorHex2 = c2 != null && !string.Equals(c2, ColorHex, StringComparison.OrdinalIgnoreCase) ? c2 : string.Empty;
        CoverPath = PersistCover(_coverCurrent, _originalCover);
        DialogResult = true;
    }

    private static string PersistCover(string current, string original)
    {
        try
        {
            string coversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadout", "covers");
            if (string.IsNullOrWhiteSpace(current))
            {
                DeleteManagedCover(original, coversDir);
                return string.Empty;
            }
            if (string.Equals(current, original, StringComparison.OrdinalIgnoreCase) && File.Exists(current))
                return current;
            if (!File.Exists(current)) return string.Empty;
            Directory.CreateDirectory(coversDir);
            string dest = Path.Combine(coversDir, Guid.NewGuid().ToString("N") + Path.GetExtension(current).ToLowerInvariant());
            File.Copy(current, dest);
            DeleteManagedCover(original, coversDir);
            return dest;
        }
        catch { return original ?? string.Empty; }
    }

    /// <summary>Borra la portada gestionada anterior si vive en la carpeta de covers (evita huérfanos).</summary>
    private static void DeleteManagedCover(string? path, string coversDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full = Path.GetFullPath(path);
            string managed = Path.GetFullPath(coversDir);
            if (!full.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
