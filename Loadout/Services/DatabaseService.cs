using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Loadout.Models;
using Microsoft.Data.Sqlite;

namespace Loadout.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _dbFolder;

    public DatabaseService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appData, "Loadout");
        _dbFolder = appFolder;
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "loadout.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    /// <summary>Constructor para tests: apunta a una carpeta temporal y no toca la BD real del usuario.</summary>
    internal DatabaseService(string dbFolder, bool isTest)
    {
        _ = isTest;
        _dbFolder = dbFolder;
        Directory.CreateDirectory(dbFolder);
        _dbPath = Path.Combine(dbFolder, "loadout.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void InitializeDatabase()
    {
        try
        {
            InitializeDatabaseCore();
        }
        catch (Exception ex)
        {
            Log.Error("InitializeDatabase", ex);
            throw;
        }
    }

    private void InitializeDatabaseCore()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(@"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -64000;
        ");

        string sqlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "schema.sql");
        if (!File.Exists(sqlPath))
            throw new FileNotFoundException("No se encontró Data/schema.sql.");

        string schemaSql = File.ReadAllText(sqlPath);
        connection.Execute(schemaSql);
        connection.Execute("PRAGMA foreign_keys = ON;");
        // Migración v1.1: notas por loadout (ignora si ya existe)
        try { connection.Execute("ALTER TABLE Loadouts ADD COLUMN Notes TEXT NOT NULL DEFAULT '';"); }
        catch { }
        connection.Execute(@"
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('HotkeyEnabled', 'true');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('FavoriteLoadoutId', '');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('RankedMode', 'false');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('MinimizeOnLaunch', 'true');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('HighPriority', 'false');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('AutoKillTree', 'false');
        ");
        // Migración v1.2 tactical: ARMADO por slot, etiqueta por loadout, directivas ranked
        try { connection.Execute("ALTER TABLE LoadoutItems ADD COLUMN Enabled INTEGER NOT NULL DEFAULT 1;"); }
        catch { }
        try { connection.Execute("ALTER TABLE Loadouts ADD COLUMN Tag TEXT NOT NULL DEFAULT '';"); }
        catch { }
        // Migración v1.3: portada 3D por perfil (ignora si ya existe)
        try { connection.Execute("ALTER TABLE Loadouts ADD COLUMN CoverPath TEXT NOT NULL DEFAULT '';"); }
        catch { }
        // Migración v1.4: segundo color combinable (ignora si ya existe)
        try { connection.Execute("ALTER TABLE Loadouts ADD COLUMN ColorHex2 TEXT NOT NULL DEFAULT '';"); }
        catch { }
        // Migración v1.5: prioridad por acceso (ignora si ya existe)
        try { connection.Execute("ALTER TABLE LoadoutItems ADD COLUMN HighPriority INTEGER NOT NULL DEFAULT 0;"); }
        catch { }
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Directives (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LoadoutId INTEGER NOT NULL REFERENCES Loadouts(Id) ON DELETE CASCADE,
                Text TEXT NOT NULL DEFAULT '',
                OrderIndex INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_directives_loadout ON Directives (LoadoutId, OrderIndex);
            CREATE TABLE IF NOT EXISTS KillList (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LoadoutId INTEGER NOT NULL REFERENCES Loadouts(Id) ON DELETE CASCADE,
                Pattern TEXT NOT NULL DEFAULT ''
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_killlist_unique ON KillList (LoadoutId, Pattern);
        ");
    }

    public SqliteConnection GetConnection() => new(_connectionString);
    public string GetDatabasePath() => _dbPath;

    public async Task<List<Models.Loadout>> GetLoadoutsAsync()
    {
        using var c = GetConnection();
        var rows = await c.QueryAsync<Models.Loadout>(@"
            SELECT l.Id, l.Name, l.ColorHex, l.SortOrder, l.CreatedAt,
                   COALESCE(l.Tag, '') AS Tag,
                   COALESCE(l.CoverPath, '') AS CoverPath,
                   COALESCE(l.ColorHex2, '') AS ColorHex2,
                   COALESCE(l.Notes, '') AS Notes,
                   (SELECT COUNT(*) FROM LoadoutItems i WHERE i.LoadoutId = l.Id) AS ItemCount
            FROM Loadouts l ORDER BY l.SortOrder, l.Id;");
        return rows.ToList();
    }

    public async Task<int> CreateLoadoutAsync(string name, string colorHex, string coverPath = "", string colorHex2 = "", string notes = "")
    {
        using var c = GetConnection();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO Loadouts (Name, ColorHex, CoverPath, ColorHex2, SortOrder, Notes) VALUES (@Name, @ColorHex, @CoverPath, @ColorHex2,
              (SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Loadouts), @Notes);
            SELECT last_insert_rowid();", new { Name = name, ColorHex = colorHex, CoverPath = coverPath ?? string.Empty, ColorHex2 = colorHex2 ?? string.Empty, Notes = notes ?? string.Empty });
    }

    public async Task DeleteLoadoutAsync(int id)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("DELETE FROM LoadoutItems WHERE LoadoutId = @Id; DELETE FROM KillList WHERE LoadoutId = @Id; DELETE FROM Loadouts WHERE Id = @Id;", new { Id = id });
    }

    public async Task RenameLoadoutAsync(int id, string name, string colorHex)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("UPDATE Loadouts SET Name = @Name, ColorHex = @ColorHex WHERE Id = @Id;",
            new { Id = id, Name = name, ColorHex = colorHex });
    }

    public async Task UpdateLoadoutAsync(int id, string name, string colorHex, string tag, string coverPath = "", string colorHex2 = "", string notes = "")
    {
        using var c = GetConnection();
        await c.ExecuteAsync("UPDATE Loadouts SET Name = @Name, ColorHex = @ColorHex, Tag = @Tag, CoverPath = @CoverPath, ColorHex2 = @ColorHex2, Notes = @Notes WHERE Id = @Id;",
            new { Id = id, Name = name, ColorHex = colorHex, Tag = tag ?? string.Empty, CoverPath = coverPath ?? string.Empty, ColorHex2 = colorHex2 ?? string.Empty, Notes = notes ?? string.Empty });
    }

    public async Task SetItemEnabledAsync(int id, bool enabled)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("UPDATE LoadoutItems SET Enabled = @E WHERE Id = @Id;",
            new { E = enabled ? 1 : 0, Id = id });
    }

    public async Task SetItemPriorityAsync(int id, bool priority)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("UPDATE LoadoutItems SET HighPriority = @P WHERE Id = @Id;",
            new { P = priority ? 1 : 0, Id = id });
    }

    public async Task<List<Directive>> GetDirectivesAsync(int loadoutId)
    {
        using var c = GetConnection();
        var rows = await c.QueryAsync<Directive>(
            "SELECT * FROM Directives WHERE LoadoutId = @Id ORDER BY OrderIndex, Id;",
            new { Id = loadoutId });
        return rows.ToList();
    }

    public async Task SaveDirectivesAsync(int loadoutId, List<string> lines)
    {
        using var c = GetConnection();
        c.Open();
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("DELETE FROM Directives WHERE LoadoutId = @Id;",
            new { Id = loadoutId }, tx);
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(t)) continue;
            await c.ExecuteAsync(
                "INSERT INTO Directives (LoadoutId, Text, OrderIndex) VALUES (@L, @T, @O);",
                new { L = loadoutId, T = t, O = i }, tx);
        }
        tx.Commit();
    }

    public async Task<List<string>> GetKillListAsync(int loadoutId)
    {
        try
        {
            using var c = GetConnection();
            var rows = await c.QueryAsync<string>(
                "SELECT Pattern FROM KillList WHERE LoadoutId = @Id ORDER BY Pattern;",
                new { Id = loadoutId });
            return rows.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"GetKillListAsync({loadoutId})", ex);
            return new List<string>();
        }
    }

    public async Task AddKillPatternAsync(int loadoutId, string pattern)
    {
        using var c = GetConnection();
        await c.ExecuteAsync(
            "INSERT OR IGNORE INTO KillList (LoadoutId, Pattern) VALUES (@L, @P);",
            new { L = loadoutId, P = pattern.Trim().ToLowerInvariant() });
    }

    public async Task DeleteKillPatternAsync(int loadoutId, string pattern)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("DELETE FROM KillList WHERE LoadoutId = @L AND Pattern = @P;",
            new { L = loadoutId, P = pattern });
    }

    public async Task<int> GetSessionCountAsync()
    {
        try
        {
            using var c = GetConnection();
            return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SessionLog;");
        }
        catch (Exception ex)
        {
            Log.Error("GetSessionCountAsync", ex);
            return 0;
        }
    }

    public async Task<List<LoadoutItem>> GetItemsAsync(int loadoutId)
    {
        using var c = GetConnection();
        var rows = await c.QueryAsync<LoadoutItem>(
            "SELECT * FROM LoadoutItems WHERE LoadoutId = @Id ORDER BY OrderIndex, Id;", new { Id = loadoutId });
        return rows.ToList();
    }

    public async Task<int> CreateItemAsync(LoadoutItem item)
    {
        using var c = GetConnection();
        return await c.ExecuteScalarAsync<int>(@"
            INSERT INTO LoadoutItems (LoadoutId, Name, Kind, Target, Arguments, OrderIndex, DelaySeconds, Enabled, HighPriority)
            VALUES (@LoadoutId, @Name, @Kind, @Target, @Arguments, @OrderIndex, @DelaySeconds, @Enabled, @HighPriority);
            SELECT last_insert_rowid();", item);
    }

    public async Task DeleteItemAsync(int id)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("DELETE FROM LoadoutItems WHERE Id = @Id;", new { Id = id });
    }

    public async Task UpdateItemAsync(LoadoutItem item)
    {
        using var c = GetConnection();
        await c.ExecuteAsync(@"
            UPDATE LoadoutItems SET Name = @Name, Kind = @Kind, Target = @Target,
              Arguments = @Arguments, OrderIndex = @OrderIndex, DelaySeconds = @DelaySeconds,
              Enabled = @Enabled, HighPriority = @HighPriority
            WHERE Id = @Id;", item);
    }

    public async Task ReorderItemsAsync(int loadoutId, System.Collections.Generic.List<int> orderedIds)
    {
        using var c = GetConnection();
        c.Open();
        using var tx = c.BeginTransaction();
        for (int i = 0; i < orderedIds.Count; i++)
            await c.ExecuteAsync("UPDATE LoadoutItems SET OrderIndex = @O WHERE Id = @Id;",
                new { O = i, Id = orderedIds[i] }, tx);
        tx.Commit();
    }

    public async Task<int> StartSessionAsync(int loadoutId)
    {
        using var c = GetConnection();
        return await c.ExecuteScalarAsync<int>(
            "INSERT INTO SessionLog (LoadoutId, StartedAt) VALUES (@Id, datetime('now','localtime')); SELECT last_insert_rowid();",
            new { Id = loadoutId });
    }

    public async Task EndSessionAsync(int sessionId)
    {
        using var c = GetConnection();
        await c.ExecuteAsync("UPDATE SessionLog SET EndedAt = datetime('now','localtime') WHERE Id = @Id;", new { Id = sessionId });
    }

    public async Task<string?> GetSettingAsync(string key, string? def = null)
    {
        using var c = GetConnection();
        var v = await c.ExecuteScalarAsync<string?>("SELECT Value FROM AppSettings WHERE Key = @Key;", new { Key = key });
        return v ?? def;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        using var c = GetConnection();
        await c.ExecuteAsync(
            "INSERT INTO AppSettings (Key, Value) VALUES (@Key, @Value) ON CONFLICT(Key) DO UPDATE SET Value = @Value;",
            new { Key = key, Value = value });
    }

    public async Task<string> GetLoadoutNotesAsync(int loadoutId)
    {
        try
        {
            using var c = GetConnection();
            var v = await c.ExecuteScalarAsync<string>(
                "SELECT Notes FROM Loadouts WHERE Id = @Id;", new { Id = loadoutId });
            return v ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error($"GetLoadoutNotesAsync({loadoutId})", ex);
            return string.Empty;
        }
    }

    public async Task SetLoadoutNotesAsync(int loadoutId, string notes)
    {
        try
        {
            using var c = GetConnection();
            await c.ExecuteAsync("UPDATE Loadouts SET Notes = @N WHERE Id = @Id;",
                new { N = notes ?? string.Empty, Id = loadoutId });
        }
        catch (Exception ex)
        {
            Log.Error($"SetLoadoutNotesAsync({loadoutId})", ex);
        }
    }

    /// <summary>Borra archivos de portada huérfanos (ya no referenciados por ningún perfil).</summary>
    public async Task<int> CleanupOrphanCoversAsync()
    {
        try
        {
            string coversDir = Path.Combine(_dbFolder, "covers");
            if (!Directory.Exists(coversDir)) return 0;
            using var c = GetConnection();
            var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in await c.QueryAsync<string>("SELECT CoverPath FROM Loadouts WHERE CoverPath <> '';"))
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { inUse.Add(Path.GetFullPath(p)); } catch { }
            }
            int removed = 0;
            foreach (string f in Directory.EnumerateFiles(coversDir))
            {
                try
                {
                    if (inUse.Contains(Path.GetFullPath(f))) continue;
                    File.Delete(f);
                    removed++;
                }
                catch { }
            }
            return removed;
        }
        catch { return 0; }
    }
}
