using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Loadout.Models;

namespace Loadout.Services;

/// <summary>DTO de un acceso dentro del backup.</summary>
public sealed class ItemExportDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Exe";
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int DelaySeconds { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public bool HighPriority { get; set; } = false;
}

/// <summary>DTO de un perfil completo dentro del backup.</summary>
public sealed class LoadoutExportDto
{
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#C50337";
    public string ColorHex2 { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? CoverFile { get; set; }
    public List<ItemExportDto> Items { get; set; } = new();
    public List<string> Directives { get; set; } = new();
    public List<string> KillList { get; set; } = new();
}

/// <summary>Estructura raíz del archivo .json de backup.</summary>
public sealed class BackupFile
{
    public string App { get; set; } = "Loadout";
    public int FormatVersion { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public List<LoadoutExportDto> Loadouts { get; set; } = new();
}

/// <summary>
/// Export/Import de perfiles completos (accesos, directivas, kill-list, notas, portada)
/// a un único archivo JSON. Las portadas viajan en una subcarpeta "covers" junto al .json.
/// </summary>
public static class BackupService
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    private static string CoversDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Loadout", "covers");

    /// <summary>Exporta los perfiles indicados a jsonPath (con sus portadas en covers/ al lado).</summary>
    public static async Task ExportAsync(DatabaseService db, IEnumerable<Models.Loadout> loadouts, string jsonPath)
    {
        var file = new BackupFile { ExportedAt = DateTime.Now };
        string exportDir = Path.GetDirectoryName(jsonPath) ?? ".";
        string coverDir = Path.Combine(exportDir, "covers");

        foreach (var l in loadouts)
        {
            var dto = new LoadoutExportDto
            {
                Name = l.Name,
                ColorHex = l.ColorHex,
                ColorHex2 = l.ColorHex2,
                Tag = l.Tag,
                Notes = await db.GetLoadoutNotesAsync(l.Id)
            };
            dto.Items = (await db.GetItemsAsync(l.Id))
                .Select(i => new ItemExportDto
                {
                    Name = i.Name, Kind = i.Kind, Target = i.Target,
                    Arguments = i.Arguments, OrderIndex = i.OrderIndex,
                    DelaySeconds = i.DelaySeconds, Enabled = i.Enabled,
                    HighPriority = i.HighPriority
                })
                .OrderBy(i => i.OrderIndex)
                .ToList();
            dto.Directives = (await db.GetDirectivesAsync(l.Id)).Select(d => d.Text).ToList();
            dto.KillList = await db.GetKillListAsync(l.Id);

            if (!string.IsNullOrWhiteSpace(l.CoverPath) && File.Exists(l.CoverPath))
            {
                try
                {
                    Directory.CreateDirectory(coverDir);
                    string name = Guid.NewGuid().ToString("N") + Path.GetExtension(l.CoverPath).ToLowerInvariant();
                    File.Copy(l.CoverPath, Path.Combine(coverDir, name), true);
                    dto.CoverFile = name;
                }
                catch (Exception ex)
                {
                    Log.Error($"Export cover de '{l.Name}'", ex);
                }
            }
            file.Loadouts.Add(dto);
        }

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(file, Pretty));
    }

    /// <summary>Importa un archivo de backup. Devuelve (creados, saltados por nombre repetido u error).</summary>
    public static async Task<(int Created, int Skipped)> ImportAsync(DatabaseService db, string jsonPath)
    {
        BackupFile? file;
        try
        {
            string raw = await File.ReadAllTextAsync(jsonPath);
            file = JsonSerializer.Deserialize<BackupFile>(raw, ReadOpts);
        }
        catch (Exception ex)
        {
            Log.Error($"ImportAsync({jsonPath})", ex);
            throw new InvalidDataException("El archivo seleccionado no es un backup válido de LOADOUT.", ex);
        }

        if (file?.Loadouts == null || file.Loadouts.Count == 0)
            throw new InvalidDataException("El archivo no contiene perfiles para importar.");

        int created = 0, skipped = 0;
        string importDir = Path.GetDirectoryName(jsonPath) ?? ".";
        string coversDir = CoversDir();

        foreach (var dto in file.Loadouts)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                skipped++;
                continue;
            }
            var existing = await db.GetLoadoutsAsync();
            if (existing.Any(x => string.Equals(x.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            string coverPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(dto.CoverFile))
            {
                string src = Path.Combine(importDir, "covers", Path.GetFileName(dto.CoverFile));
                if (File.Exists(src))
                {
                    try
                    {
                        Directory.CreateDirectory(coversDir);
                        coverPath = Path.Combine(coversDir, Guid.NewGuid().ToString("N") + Path.GetExtension(dto.CoverFile));
                        File.Copy(src, coverPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Import cover '{dto.CoverFile}'", ex);
                    }
                }
            }

            int id = await db.CreateLoadoutAsync(
                dto.Name,
                string.IsNullOrWhiteSpace(dto.ColorHex) ? "#C50337" : dto.ColorHex,
                coverPath,
                dto.ColorHex2,
                dto.Notes ?? string.Empty);

            foreach (var it in dto.Items.OrderBy(i => i.OrderIndex))
            {
                try
                {
                    await db.CreateItemAsync(new LoadoutItem
                    {
                        LoadoutId = id, Name = it.Name, Kind = it.Kind,
                        Target = it.Target, Arguments = it.Arguments,
                        OrderIndex = it.OrderIndex, DelaySeconds = it.DelaySeconds,
                        Enabled = it.Enabled, HighPriority = it.HighPriority
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"Import item '{it.Name}' de '{dto.Name}'", ex);
                }
            }

            await db.SaveDirectivesAsync(id, dto.Directives ?? new());
            foreach (var k in dto.KillList ?? new())
                await db.AddKillPatternAsync(id, k);

            created++;
        }

        return (created, skipped);
    }
}