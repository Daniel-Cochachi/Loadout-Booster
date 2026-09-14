using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using Microsoft.Win32;

namespace Loadout.Services;

/// <summary>
/// Optimiza la latencia de red para juegos competitivos (Valorant, CS2, LoL)
/// desactivando el algoritmo de Nagle (TcpAckFrequency = 1 y TCPNoDelay = 1)
/// en las interfaces de red activas de Windows.
/// </summary>
public static class NetworkOptimizerService
{
    private const string TcpInterfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtiene los GUIDs de los adaptadores de red Ethernet y Wi-Fi activos que tienen conexión.
    /// </summary>
    private static List<string> GetActiveInterfaceGuids()
    {
        var guids = new List<string>();
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                               nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                              nic.OperationalStatus == OperationalStatus.Up)
                .ToList();

            foreach (var nic in active)
            {
                string id = nic.Id.Trim('{', '}');
                guids.Add(id);
            }
        }
        catch { }
        return guids;
    }

    /// <summary>
    /// Comprueba si la optimización TCP NoDelay está activa en los adaptadores principales.
    /// </summary>
    public static bool IsOptimized()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpInterfacesPath, false);
            if (baseKey == null) return false;

            var activeGuids = GetActiveInterfaceGuids();
            if (activeGuids.Count == 0) return false;

            int checkedCount = 0;
            int optimizedCount = 0;

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                string cleanSub = subKeyName.Trim('{', '}');
                if (!activeGuids.Any(g => string.Equals(g, cleanSub, StringComparison.OrdinalIgnoreCase)))
                    continue;

                checkedCount++;
                using var ifKey = baseKey.OpenSubKey(subKeyName, false);
                if (ifKey != null)
                {
                    object? ack = ifKey.GetValue("TcpAckFrequency");
                    object? noDelay = ifKey.GetValue("TCPNoDelay");

                    if (ack is int ackVal && ackVal == 1 &&
                        noDelay is int delayVal && delayVal == 1)
                    {
                        optimizedCount++;
                    }
                }
            }

            return checkedCount > 0 && optimizedCount == checkedCount;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Aplica TcpAckFrequency = 1 y TCPNoDelay = 1 en las interfaces de red activas.
    /// </summary>
    public static (bool Success, string Message) ApplyOptimization()
    {
        if (!IsAdministrator())
        {
            return (false, "Se requieren permisos de Administrador para optimizar la red. Ejecuta Loadout como Administrador.");
        }

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpInterfacesPath, true);
            if (baseKey == null) return (false, "No se encontró la ruta de configuración TCP en el Registro.");

            var activeGuids = GetActiveInterfaceGuids();
            int modified = 0;

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                string cleanSub = subKeyName.Trim('{', '}');
                // Si encontramos coincidencia con los adaptadores activos o tiene IP asignada:
                using var ifKey = baseKey.OpenSubKey(subKeyName, true);
                if (ifKey == null) continue;

                bool shouldModify = activeGuids.Any(g => string.Equals(g, cleanSub, StringComparison.OrdinalIgnoreCase)) ||
                                    ifKey.GetValue("DhcpIPAddress") != null ||
                                    ifKey.GetValue("IPAddress") != null;

                if (shouldModify)
                {
                    ifKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    ifKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                    modified++;
                }
            }

            if (modified > 0)
                return (true, $"Optimización de red aplicada correctamente en {modified} adaptador(es).");
            
            return (false, "No se detectaron adaptadores de red activos para configurar.");
        }
        catch (Exception ex)
        {
            return (false, $"Error al aplicar la optimización: {ex.Message}");
        }
    }

    /// <summary>
    /// Restaura los valores estándar de Windows (elimina los overrides de TcpAckFrequency y TCPNoDelay).
    /// </summary>
    public static (bool Success, string Message) RevertOptimization()
    {
        if (!IsAdministrator())
        {
            return (false, "Se requieren permisos de Administrador para revertir la configuración de red.");
        }

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpInterfacesPath, true);
            if (baseKey == null) return (false, "No se encontró la ruta TCP en el Registro.");

            int reverted = 0;
            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var ifKey = baseKey.OpenSubKey(subKeyName, true);
                if (ifKey == null) continue;

                bool removed = false;
                if (ifKey.GetValue("TcpAckFrequency") != null)
                {
                    try { ifKey.DeleteValue("TcpAckFrequency", false); removed = true; } catch { }
                }
                if (ifKey.GetValue("TCPNoDelay") != null)
                {
                    try { ifKey.DeleteValue("TCPNoDelay", false); removed = true; } catch { }
                }
                if (removed) reverted++;
            }

            return (true, $"Configuración de red restaurada al estándar de Windows en {reverted} adaptador(es).");
        }
        catch (Exception ex)
        {
            return (false, $"Error al revertir: {ex.Message}");
        }
    }
}
