using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Loadout.Services;

/// <summary>
/// Genera un icono nítido y pixel-perfect en la resolución nativa de la bandeja de Windows (16x16 / 20x20 / 24x24 / 32x32)
/// mostrando el porcentaje de RAM actual con micro-tipografía de alto contraste, barra de estado y cero distorsión.
/// </summary>
public static class TrayIconRamService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSMICON = 49;

    private static IntPtr _prevHIcon = IntPtr.Zero;

    public static (Icon? Icon, string Tooltip) GenerateRamIcon(double usedGb, double totalGb)
    {
        try
        {
            double pct = totalGb > 0 ? (usedGb / totalGb * 100.0) : 0;
            pct = Math.Clamp(pct, 0, 100);
            double freeMb = Math.Max(0, (totalGb - usedGb) * 1024.0);

            // Tamaño nativo exacto de la bandeja según DPI (16, 20, 24, 32)
            int size = GetSystemMetrics(SM_CXSMICON);
            if (size < 16 || size > 64) size = 16;

            // Paleta temática de Loadout según saturación de memoria
            Color accentColor = pct switch
            {
                >= 90 => Color.FromArgb(255, 59, 48),    // Rojo carmesí alerta
                >= 75 => Color.FromArgb(255, 179, 0),   // Ámbar media
                _ => Color.FromArgb(0, 229, 255)        // Cyan cyber óptimo
            };

            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                
                // En 16x16, SingleBitPerPixelGridFit garantiza trazos 100% nítidos sin blur
                // En resoluciones superiores con escala DPI, AntiAliasGridFit suaviza con precisión
                g.TextRenderingHint = size <= 16 
                    ? TextRenderingHint.SingleBitPerPixelGridFit 
                    : TextRenderingHint.AntiAliasGridFit;

                // 1. Fondo dark slate de alto contraste que combina con Windows 10/11
                using var bgBrush = new SolidBrush(Color.FromArgb(255, 14, 15, 22));
                g.FillRectangle(bgBrush, 0, 0, size, size);

                // 2. Micro-barra de nivel de RAM en la base del icono
                int barH = Math.Max(2, (int)(size * 0.12));
                int barW = (int)Math.Round((size - 2) * (pct / 100.0));
                barW = Math.Clamp(barW, 2, size - 2);
                using var barBrush = new SolidBrush(accentColor);
                g.FillRectangle(barBrush, 1, size - barH - 1, barW, barH);

                // 3. Dígitos en negrita centrados (Tahoma fue diseñada por Microsoft específicamente para micro-pantalla)
                string text = pct >= 100 ? "99" : $"{pct:0}";
                float fontSize = size switch
                {
                    <= 16 => 9.0f,
                    <= 20 => 11.0f,
                    <= 24 => 13.0f,
                    _ => size * 0.52f
                };

                using var font = new Font("Tahoma", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.White);

                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Centrar en el área superior para dar protagonismo a los números
                var textRect = new RectangleF(0, -1, size, size - barH);
                g.DrawString(text, font, textBrush, textRect, sf);
            }

            IntPtr hIcon = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();

            // Liberar handle anterior de GDI para no consumir memoria del sistema
            if (_prevHIcon != IntPtr.Zero)
            {
                try { DestroyIcon(_prevHIcon); } catch { }
            }
            _prevHIcon = hIcon;

            string tooltip = $"Loadout Booster · RAM: {usedGb:0.0} / {totalGb:0.0} GB ({pct:0}%)\nLibres: {freeMb:0} MB";
            return (icon, tooltip);
        }
        catch
        {
            return (null, "Loadout");
        }
    }
}
