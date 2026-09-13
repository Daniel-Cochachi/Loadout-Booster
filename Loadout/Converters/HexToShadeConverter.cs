using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Loadout.Converters;

/// <summary>Hex → tono oscuro sólido (factor). Para números pop-out detrás de pósters.</summary>
public sealed class HexToShadeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                var c = (Color)ColorConverter.ConvertFromString(hex)!;
                double f = 0.45;
                if (parameter is string p && double.TryParse(p,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double pf))
                    f = Math.Clamp(pf, 0.1, 1.0);
                var b = new SolidColorBrush(Color.FromRgb(
                    (byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f)));
                b.Freeze();
                return b;
            }
        }
        catch { }
        var fallback = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x10));
        fallback.Freeze();
        return fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
