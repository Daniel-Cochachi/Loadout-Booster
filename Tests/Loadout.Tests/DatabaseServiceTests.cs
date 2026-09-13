using Loadout.Models;
using Loadout.Services;
using System.IO;

namespace Loadout.Tests;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LoadoutTests_" + Guid.NewGuid().ToString("N"));
        _db = new DatabaseService(_dir, isTest: true);
        _db.InitializeDatabase();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task UpdateItem_PreservesEnabledFlag()
    {
        int lid = await _db.CreateLoadoutAsync("Test", "#C50337");
        int itemId = await _db.CreateItemAsync(new LoadoutItem
        {
            LoadoutId = lid, Name = "A", Kind = "Exe", Target = @"C:\x.exe"
        });
        await _db.SetItemEnabledAsync(itemId, false);

        var items = await _db.GetItemsAsync(lid);
        var it = items.First();
        Assert.False(it.Enabled);

        // Editar SIN tocar Enabled: el slot no debe re-habilitarse al guardar.
        it.Name = "A2";
        await _db.UpdateItemAsync(it);

        items = await _db.GetItemsAsync(lid);
        Assert.Equal("A2", items.Single().Name);
        Assert.False(items.Single().Enabled);
    }

    [Fact]
    public async Task CreateItem_WithEnabledFalse_IsPersisted()
    {
        int lid = await _db.CreateLoadoutAsync("Test2", "#C50337");
        await _db.CreateItemAsync(new LoadoutItem
        {
            LoadoutId = lid, Name = "Off", Kind = "Exe", Target = @"C:\y.exe", Enabled = false
        });
        var items = await _db.GetItemsAsync(lid);
        Assert.False(items.Single().Enabled);
    }

    [Fact]
    public async Task Notes_RoundTrip()
    {
        int lid = await _db.CreateLoadoutAsync("N", "#C50337", notes: "daño 240");
        Assert.Equal("daño 240", await _db.GetLoadoutNotesAsync(lid));

        await _db.SetLoadoutNotesAsync(lid, "nuevo");
        Assert.Equal("nuevo", await _db.GetLoadoutNotesAsync(lid));
    }

    [Fact]
    public async Task GetLoadouts_ReturnsNotes()
    {
        int lid = await _db.CreateLoadoutAsync("N2", "#C50337", notes: "nota");
        var all = await _db.GetLoadoutsAsync();
        Assert.Equal("nota", all.First(x => x.Id == lid).Notes);
    }

    [Fact]
    public async Task CleanupOrphanCovers_DeletesOnlyUnreferenced()
    {
        int lid = await _db.CreateLoadoutAsync("C", "#C50337");
        string coversDir = Path.Combine(_dir, "covers");
        Directory.CreateDirectory(coversDir);
        string kept = Path.Combine(coversDir, "keep.png");
        string orphan = Path.Combine(coversDir, "orphan.png");
        File.WriteAllText(kept, "x");
        File.WriteAllText(orphan, "x");
        await _db.UpdateLoadoutAsync(lid, "C", "#C50337", "", kept);

        int removed = await _db.CleanupOrphanCoversAsync();
        Assert.Equal(1, removed);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public async Task KillList_IsDeduplicated()
    {
        int lid = await _db.CreateLoadoutAsync("K", "#C50337");
        await _db.AddKillPatternAsync(lid, "chrome");
        await _db.AddKillPatternAsync(lid, "chrome");
        var list = await _db.GetKillListAsync(lid);
        Assert.Single(list);
    }

    [Fact]
    public async Task Directives_RoundTrip()
    {
        int lid = await _db.CreateLoadoutAsync("D", "#C50337");
        await _db.SaveDirectivesAsync(lid, new() { "!alto", "▲ meta", "frame" });
        var rows = await _db.GetDirectivesAsync(lid);
        Assert.Equal(3, rows.Count);
        Assert.Equal("!alto", rows[0].Text);
        await _db.SaveDirectivesAsync(lid, new() { "solo" });
        Assert.Single(await _db.GetDirectivesAsync(lid));
    }
}