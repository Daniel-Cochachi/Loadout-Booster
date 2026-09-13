using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Loadout.Converters;

/// <summary>Hex de fondo → color de texto legible (oscuro o claro). Para pósters.</summary>
public sealed class HexToContrastConverter : IValueConverter
{
    private static readonly SolidColorBrush Dark;
    private static readonly SolidColorBrush Light;

    static HexToContrastConverter()
    {
        Dark = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x00));
        Dark.Freeze();
        Light = new SolidColorBrush(Color.FromRgb(0xF5, 0xF1, 0xEA));
        Light.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                var c = (Color)ColorConverter.ConvertFromString(hex)!;
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return lum > 0.55 ? Dark : Light;
            }
        }
        catch { }
        return Light;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
