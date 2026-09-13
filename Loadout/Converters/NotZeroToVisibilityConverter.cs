using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Loadout.Converters;

/// <summary>&gt;0 → Visible, 0 → Collapsed. Inverso de ZeroToVisConv.</summary>
public sealed class NotZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            int n = value is int i ? i : 0;
            return n != 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { return Visibility.Collapsed; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}