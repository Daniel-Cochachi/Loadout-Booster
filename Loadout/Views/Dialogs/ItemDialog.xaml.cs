using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WpfUiControls = Wpf.Ui.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public partial class ItemDialog : FluentWindow
{
    public string ItemName { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "Exe";
    public string Target { get; private set; } = string.Empty;
    public string Arguments { get; private set; } = string.Empty;
    public int DelaySeconds { get; private set; } = 2;
    public bool HighPriority { get; private set; }

    public ItemDialog(string name = "", string kind = "Exe", string target = "", string args = "", int delay = 2, bool priority = false)
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        NameBox.Text = name;
        TargetBox.Text = target;
        ArgsBox.Text = args;
        DelayBox.Text = delay.ToString();
        PriorityBox.IsChecked = priority;
        foreach (ComboBoxItem ci in KindBox.Items)
            if ((string)ci.Tag == kind) { KindBox.SelectedItem = ci; break; }
        if (KindBox.SelectedIndex < 0) KindBox.SelectedIndex = 0;
        UpdateForKind();
        NameBox.Focus();
    }

    private string CurrentKind() => ((ComboBoxItem)KindBox.SelectedItem)?.Tag as string ?? "Exe";

    private void KindBox_Changed(object sender, SelectionChangedEventArgs e) => UpdateForKind();

    private void UpdateForKind()
    {
        if (TargetLabel == null || BrowseBtn == null) return;
        string k = CurrentKind();
        TargetLabel.Text = k switch
        {
            "Exe" => "RUTA .EXE",
            "Url" => "URL (https://…)",
            "Steam" => "STEAM APP ID (ej. 730)",
            "Folder" => "CARPETA",
            _ => "ARCHIVO"
        };
        BrowseBtn.Visibility = (k == "Url" || k == "Steam") ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        string k = CurrentKind();
        if (k == "Folder")
        {
            var dlg = new OpenFolderDialog { Title = "Elige carpeta" };
            if (dlg.ShowDialog() == true) TargetBox.Text = dlg.FolderName;
            return;
        }
        var f = new OpenFileDialog();
        f.Filter = k == "Exe" ? "Ejecutables (*.exe)|*.exe|Todos (*.*)|*.*"
                              : "Todos (*.*)|*.*";
        if (f.ShowDialog() == true) TargetBox.Text = f.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string target = TargetBox.Text.Trim();
        string k = CurrentKind();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target))
        {
            ConfirmDialog.Info(this, "FALTA INFO", "Nombre y ruta/URL son obligatorios.");
            return;
        }
        if (k == "Exe" && !File.Exists(target))
        {
            ConfirmDialog.Info(this, "SIN ARCHIVO", "No se encontró el ejecutable. Revisa la ruta en el disco.");
            return;
        }
        if (k == "Url" && !target.StartsWith("http://") && !target.StartsWith("https://"))
        {
            ConfirmDialog.Info(this, "URL INVÁLIDA", "La URL debe empezar con http:// o https://");
            return;
        }
        if (!int.TryParse(DelayBox.Text.Trim(), out int d)) d = 2;
        ItemName = name;
        Kind = k;
        Target = target;
        Arguments = ArgsBox.Text.Trim();
        DelaySeconds = System.Math.Clamp(d, 0, 60);
        HighPriority = PriorityBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
