using System;
using Microsoft.Win32;

namespace Loadout.Services;

/// <summary>
/// Modo Ranked: No Molestar reversible. Guarda los valores previos del
/// sistema y los restaura al cerrar sesión. Todo best-effort: nunca lanza.
/// </summary>
public sealed class DndService
{
    private const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string NocKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\NOC_GLOBAL_SETTING";

    private int? _prevToast;
    private int? _prevNoc;
    public bool IsActive { get; private set; }

    public void Enable()
    {
        if (IsActive) return;
        try
        {
            _prevToast = ReadDword(ToastKey, "ToastEnabled");
            _prevNoc = ReadDword(NocKey, "Enabled");
            WriteDword(ToastKey, "ToastEnabled", 0);
            WriteDword(NocKey, "Enabled", 0);
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
            using var k = Registry.CurrentUser.CreateSubKey(path)
                ?? throw new InvalidOperationException("No se pudo abrir registro.");
            k.SetValue(name, value, RegistryValueKind.DWord);
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
