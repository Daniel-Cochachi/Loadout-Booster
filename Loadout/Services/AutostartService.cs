using System;
using Microsoft.Win32;

namespace Loadout.Services;

/// <summary>Autostart con Windows vía HKCU\...\Run. Best-effort, nunca lanza.</summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Loadout";

    private static string ExePath()
    {
        try { return Environment.ProcessPath ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var v = k?.GetValue(ValueName) as string;
            string exe = ExePath();
            return !string.IsNullOrWhiteSpace(v) && !string.IsNullOrWhiteSpace(exe)
                && v.Contains(exe.Trim('"'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on)
            {
                string exe = ExePath();
                if (!string.IsNullOrWhiteSpace(exe)) k.SetValue(ValueName, $"\"{exe.Trim('"')}\"");
            }
            else k.DeleteValue(ValueName, false);
        }
        catch { }
    }
}
