using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner-bulk.test.js — setAllMods + applyMode (build the list once, act on each).
public class ScannerBulkTests
{
    private static (string modsDir, GameContext c) Fixture(
        Dictionary<string, string> files, Dictionary<string, string>? classification = null)
    {
        var root = TestSupport.TempDir("bulk-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        foreach (var (name, data) in files) File.WriteAllText(Path.Combine(modsDir, name), data);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        if (classification is not null)
        {
            Directory.CreateDirectory(c.DataDir);
            File.WriteAllText(c.ClassificationPath, JsonSerializer.Serialize(classification));
        }
        return (modsDir, c);
    }

    private static async Task<Dictionary<string, bool>> EnabledMap(GameContext c)
    {
        var outMap = new Dictionary<string, bool>();
        foreach (var m in await Scanner.BuildModListAsync(c)) outMap[m.Name] = m.Enabled;
        return outMap;
    }

    [Fact]
    public async Task SetAllMods_false_disables_every_mod_true_restores()
    {
        var (modsDir, c) = Fixture(new() { ["a.pak"] = "A", ["b.pak"] = "B" });

        await Scanner.SetAllModsAsync(false, c);
        Assert.Equal(new Dictionary<string, bool> { ["a"] = false, ["b"] = false }, await EnabledMap(c));

        await Scanner.SetAllModsAsync(true, c);
        Assert.Equal(new Dictionary<string, bool> { ["a"] = true, ["b"] = true }, await EnabledMap(c));
        Assert.Equal("A", TestSupport.Read(Path.Combine(modsDir, "a.pak")));
    }

    [Fact]
    public async Task ApplyMode_mp_keeps_mp_both_disables_sp()
    {
        var (_, c) = Fixture(
            new() { ["sp.pak"] = "1", ["mp.pak"] = "2", ["both.pak"] = "3" },
            new() { ["sp"] = "sp", ["mp"] = "mp", ["both"] = "both" });

        await Scanner.ApplyModeAsync("mp", c);

        Assert.Equal(new Dictionary<string, bool> { ["sp"] = false, ["mp"] = true, ["both"] = true }, await EnabledMap(c));
    }

    [Fact]
    public async Task ApplyMode_sp_then_all()
    {
        var (_, c) = Fixture(
            new() { ["sp.pak"] = "1", ["mp.pak"] = "2", ["both.pak"] = "3" },
            new() { ["sp"] = "sp", ["mp"] = "mp", ["both"] = "both" });

        await Scanner.ApplyModeAsync("sp", c);
        Assert.Equal(new Dictionary<string, bool> { ["sp"] = true, ["mp"] = false, ["both"] = true }, await EnabledMap(c));

        await Scanner.ApplyModeAsync("all", c);
        Assert.Equal(new Dictionary<string, bool> { ["sp"] = true, ["mp"] = true, ["both"] = true }, await EnabledMap(c));
    }
}
