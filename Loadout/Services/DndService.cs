using System;
using Microsoft.Win32;

namespace Loadout.Services;

/// <summary>
/// Modo No Molestar reversible para juegos (Focus Assist / Bloqueo de notificaciones).
/// Guarda los valores previos del sistema y los restaura religiosamente al cerrar la sesión.
/// </summary>
public sealed class DndService
{
    private const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string NocKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\NOC_GLOBAL_SETTING";
    private const string QuietHoursKey = @"Software\Microsoft\Windows\CurrentVersion\QuietHours";

    private int? _prevToast;
    private int? _prevNoc;
    private int? _prevQuiet;
    public bool IsActive { get; private set; }

    public void Enable()
    {
        if (IsActive) return;
        try
        {
            _prevToast = ReadDword(ToastKey, "ToastEnabled");
            _prevNoc = ReadDword(NocKey, "Enabled");
            _prevQuiet = ReadDword(QuietHoursKey, "UserSetProfile");

            WriteDword(ToastKey, "ToastEnabled", 0);
            WriteDword(NocKey, "Enabled", 0);
            WriteDword(QuietHoursKey, "UserSetProfile", 2); // 2 = Priority / Alarms Only (Focus Assist)
            IsActive = true;
        }
        catch (Exception ex)
        {
            Log.Error("Dnd.Enable", ex);
            IsActive = false;
        }
    }

    public void Restore()
    {
        if (!IsActive) return;
        try
        {
            if (_prevToast.HasValue) WriteDword(ToastKey, "ToastEnabled", _prevToast.Value);
            else DeleteValue(ToastKey, "ToastEnabled");

            if (_prevNoc.HasValue) WriteDword(NocKey, "Enabled", _prevNoc.Value);
            else DeleteValue(NocKey, "Enabled");

            if (_prevQuiet.HasValue) WriteDword(QuietHoursKey, "UserSetProfile", _prevQuiet.Value);
            else DeleteValue(QuietHoursKey, "UserSetProfile");
        }
        catch (Exception ex)
        {
            Log.Error("Dnd.Restore", ex);
        }
        finally { IsActive = false; }
    }

    private static int? ReadDword(string path, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path, false);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch { return null; }
    }

    private static void WriteDword(string path, string name, int value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(path);
            k?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void DeleteValue(string path, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path, true);
            k?.DeleteValue(name, false);
        }
        catch { }
    }
}
