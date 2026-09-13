using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Loadout.ViewModels;

namespace Loadout.Converters;

public sealed class SlotStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Off = Create("#2E1D2032");
    private static readonly SolidColorBrush Launching = Create("#E7547A");
    private static readonly SolidColorBrush Running = Create("#C50337");

    private static SolidColorBrush Create(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is SlotState s
            ? s switch { SlotState.Launching => Launching, SlotState.Running => Running, _ => Off }
            : Off;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
