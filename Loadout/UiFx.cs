using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Loadout;

internal static class UiFx
{
    // Apertura suave de ventanas/diálogos con la "caja flotante"
    public static void FadeIn(Window w)
    {
        try
        {
            w.Opacity = 0;
            var a = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            w.BeginAnimation(UIElement.OpacityProperty, a);
        }
        catch { }
    }
}