using System.Windows;
using System.Windows.Media;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

/// <summary>Aviso / confirmación con el tema Crimson Noir. Reemplaza a MessageBox.</summary>
public partial class ConfirmDialog : FluentWindow
{
    private ConfirmDialog(string title, string message, string primary, string? secondary, bool danger, string icon)
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        TitleText.Text = title;
        MessageText.Text = message;
        IconGlyph.Text = icon;
        PrimaryBtn.Content = primary;
        if (secondary == null)
        {
            SecondaryBtn.Visibility = Visibility.Collapsed;
        }
        else SecondaryBtn.Content = secondary;
        if (danger)
        {
            IconTile.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C50337")!);
        }
        else
        {
            IconTile.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E2440")!);
            IconTile.Effect = null;
        }
        PrimaryBtn.Focus();
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Pregunta SÍ/NO temática. true = botón primario.</summary>
    public static bool Ask(Window owner, string title, string message,
        string primary = "ACEPTAR", string secondary = "CANCELAR",
        bool danger = true, string icon = "⚠")
    {
        var dlg = new ConfirmDialog(title, message, primary, secondary, danger, icon) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>Aviso de un solo botón.</summary>
    public static void Info(Window owner, string title, string message,
        string button = "ENTENDIDO", string icon = "ℹ")
    {
        var dlg = new ConfirmDialog(title, message, button, null, false, icon) { Owner = owner };
        dlg.ShowDialog();
    }
}
