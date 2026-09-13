using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Loadout.Services;

/// <summary>Caché de iconos reales de juegos (.exe). Hilo-seguro, con tope.</summary>
public static class GameIconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    public static ImageSource? Get(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string t = target.Trim().Trim('"');
        if (!t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(t)) return null;
        lock (Lock)
        {
            if (Cache.TryGetValue(t, out var cached)) return cached;
        }
        ImageSource? img = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(t);
            if (icon != null)
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                img = bs;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GameIconCache.Get({t}): {ex.Message}");
        }
        lock (Lock)
        {
            if (Cache.Count > 200) Cache.Clear();
            Cache[t] = img;
        }
        return img;
    }
}
