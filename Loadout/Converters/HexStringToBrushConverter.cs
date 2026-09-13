using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Loadout.Converters;

/// <summary>Hex string (#RRGGBB) → SolidColorBrush congelado. Para tintes por cartucho/directiva.</summary>
public sealed class HexStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
                b.Freeze();
                return b;
            }
        }
        catch { }
        var fallback = new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x6B));
        fallback.Freeze();
        return fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
