using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Loadout.Views.Dialogs;

public partial class DirectivesDialog : FluentWindow
{
    public List<string> Lines { get; private set; } = new();

    public DirectivesDialog(List<string> current)
    {
        InitializeComponent();
        Loaded += (_, _) => UiFx.FadeIn(this);
        LinesBox.Text = string.Join("\n", current ?? new List<string>());
        LinesBox.Focus();
        LinesBox.CaretIndex = LinesBox.Text.Length;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Lines = LinesBox.Text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
