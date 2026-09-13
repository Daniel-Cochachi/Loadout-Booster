namespace Loadout.Models;

public class Loadout
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#C50337";
    public string ColorHex2 { get; set; } = string.Empty;
    public string CoverPath { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public bool IsActive { get; set; }
}

public class LoadoutItem
{
    public int Id { get; set; }
    public int LoadoutId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Exe"; // Exe|Url|Steam|Folder|File
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int DelaySeconds { get; set; } = 2;
    public bool Enabled { get; set; } = true; // ARMADO: false = se salta al lanzar
    public bool HighPriority { get; set; } = false; // ⚡ PRIORITARIO: solo este acceso recibe CPU High
}

public class Directive
{
    public int Id { get; set; }
    public int LoadoutId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}

public class SessionLog
{
    public int Id { get; set; }
    public int LoadoutId { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string? EndedAt { get; set; }
}
