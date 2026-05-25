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

    // ---------- ReadOnly / owned-folder guard tests ----------

    // Two-location fixture: one Vortex-owned location (ReadOnly mods) + one plain toggleable location.
    private static (string ownedDir, string normalDir, GameContext c) TwoLocationFixture(
        Dictionary<string, string> ownedFiles,
        Dictionary<string, string> normalFiles,
        Dictionary<string, string>? classification = null)
    {
        var root = TestSupport.TempDir("bulk-ro-");
        var gameRoot = Path.Combine(root, "game");

        // Owned location: mods dir with a Vortex deployment marker
        var ownedDir = Path.Combine(gameRoot, "ownedMods");
        Directory.CreateDirectory(ownedDir);
        File.WriteAllText(Path.Combine(ownedDir, "vortex.deployment.x.json"), "{}");
        foreach (var (name, data) in ownedFiles) File.WriteAllText(Path.Combine(ownedDir, name), data);

        // Normal location: plain mods dir
        var normalDir = Path.Combine(gameRoot, "normalMods");
        Directory.CreateDirectory(normalDir);
        foreach (var (name, data) in normalFiles) File.WriteAllText(Path.Combine(normalDir, name), data);

        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t2", GameName = "T2", GameRoot = gameRoot,
            ModLocations = new[]
            {
                new ModLocation("owned", "Owned", "ownedMods"),
                new ModLocation("normal", "Normal", "normalMods"),
            },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });

        if (classification is not null)
        {
            Directory.CreateDirectory(c.DataDir);
            File.WriteAllText(c.ClassificationPath, JsonSerializer.Serialize(classification));
        }

        return (ownedDir, normalDir, c);
    }

    [Fact]
    public async Task SetAllMods_skips_readonly_mods_never_moves_owned_files()
    {
        // Owned location has vortex.deployment marker → its mods are ReadOnly.
        // Normal location has a plain mod that should be toggled.
        var (ownedDir, normalDir, c) = TwoLocationFixture(
            ownedFiles:  new() { ["vortex_mod.pak"] = "OWNED" },
            normalFiles: new() { ["my_mod.pak"] = "MINE" });

        // Both start enabled. Disable all — the owned mod must NOT be moved.
        await Scanner.SetAllModsAsync(false, c);

        // Owned mod file must still exist in its original location.
        Assert.True(File.Exists(Path.Combine(ownedDir, "vortex_mod.pak")),
            "SetAllMods(false) must not move a ReadOnly (Vortex-owned) mod out of its folder.");

        // Normal mod must be disabled (moved to the holding folder).
        Assert.False(File.Exists(Path.Combine(normalDir, "my_mod.pak")),
            "SetAllMods(false) must disable the non-owned mod.");

        var map = await EnabledMap(c);
        Assert.True(map["vortex_mod"],  "Owned mod should still appear enabled (was never moved).");
        Assert.False(map["my_mod"],     "Normal mod should be disabled.");

        // Re-enable all — owned folder still untouched.
        await Scanner.SetAllModsAsync(true, c);

        Assert.True(File.Exists(Path.Combine(ownedDir, "vortex_mod.pak")),
            "SetAllMods(true) must not touch a ReadOnly mod's folder.");

        var map2 = await EnabledMap(c);
        Assert.True(map2["vortex_mod"], "Owned mod should still appear enabled after re-enable.");
        Assert.True(map2["my_mod"],     "Normal mod should be re-enabled.");
    }

    [Fact]
    public async Task ApplyMode_skips_readonly_mods_never_moves_owned_files()
    {
        // Classify the normal mod as "sp" so ApplyMode("mp") would want to disable it,
        // and classify the owned mod as "sp" too — ApplyMode("mp") must skip the owned one entirely.
        var (ownedDir, normalDir, c) = TwoLocationFixture(
            ownedFiles:  new() { ["vortex_sp.pak"] = "OWNED" },
            normalFiles: new() { ["plain_sp.pak"] = "MINE" },
            classification: new() { ["vortex_sp"] = "sp", ["plain_sp"] = "sp" });

        // ApplyMode("mp") wants to disable all "sp" mods. Owned one must be skipped.
        await Scanner.ApplyModeAsync("mp", c);

        // Owned mod must still be in its folder.
        Assert.True(File.Exists(Path.Combine(ownedDir, "vortex_sp.pak")),
            "ApplyMode must not move a ReadOnly (Vortex-owned) mod out of its folder.");

        // Normal sp mod should have been disabled.
        Assert.False(File.Exists(Path.Combine(normalDir, "plain_sp.pak")),
            "ApplyMode must disable the non-owned sp mod.");

        var map = await EnabledMap(c);
        Assert.True(map["vortex_sp"],  "Owned mod should still appear enabled (was never moved).");
        Assert.False(map["plain_sp"],  "Normal sp mod should be disabled.");
    }
}
