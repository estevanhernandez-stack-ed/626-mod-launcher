using System.Diagnostics;
using System.Security.Cryptography;
using ModManager.Core;
using Xunit.Abstractions;

namespace ModManager.Tests;

/// <summary>
/// What a bulk toggle costs on a game with extra mod trees (B4 stage two), counted rather than timed. Each
/// turn-off decides which extra-tree entries move from a full mod listing and a read of every tree; done
/// once per mod, a 200-mod Cyberpunk enable-all or disable-all paid about 200 of each. The bulk paths now
/// decide once per operation, so the counts here are constants, not the mod count.
///
/// <para>Counts come from <see cref="ScanCostProbe"/>, a thread-static seam the scanner bumps on every
/// <c>BuildModList</c> and every <see cref="ModTrees.Build"/>. Wall time is written to the test output for
/// the record and never asserted.</para>
/// </summary>
public class BulkToggleTreeGameCostTests : IDisposable
{
    private static readonly string[] Trees =
    {
        "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods",
    };

    private const int ModCount = 200;
    private readonly string _root = TestSupport.TempDir("mmb-bulkcost-");
    private readonly ITestOutputHelper _out;
    private string GameRoot => Path.Combine(_root, "game");
    private string DataDir => Path.Combine(_root, "data");

    public BulkToggleTreeGameCostTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        ScanCostProbe.Current = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Put(string rel, string content)
    {
        var p = Path.Combine(GameRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private static string ModName(int i) => $"Mod{i:000}";

    /// <summary>The extra-tree entries each mod owns, relative to the game root: 90 mods with one, 30 of them
    /// with a second in another tree, folders and files alternating. 120 entries across all five trees.</summary>
    private static List<(string Mod, string Rel)> ExtraEntries()
    {
        var list = new List<(string, string)>();
        for (var i = 0; i < 90; i++) list.Add((ModName(i), Entry(i, Trees[i % Trees.Length], folder: i % 2 == 0)));
        for (var i = 0; i < 30; i++) list.Add((ModName(i), Entry(i, Trees[(i + 2) % Trees.Length], folder: i % 2 == 1)));
        return list;

        static string Entry(int i, string tree, bool folder)
            => folder ? $"{tree}/{ModName(i)}/payload.bin" : $"{tree}/{ModName(i)}.yaml";
    }

    private void BuildCyberpunkShape()
    {
        for (var i = 0; i < ModCount; i++) Put($"archive/pc/mod/{ModName(i)}.archive", $"ARCHIVE-{i}");
        foreach (var (mod, rel) in ExtraEntries()) Put(rel, $"EXTRA-{mod}-{rel}");
        // Frameworks with no row of their own: never anyone's, never moved.
        Put("red4ext/plugins/ArchiveXL/ArchiveXL.dll", "ARCHIVEXL");
        Put("red4ext/plugins/TweakXL/TweakXL.dll", "TWEAKXL");
    }

    private GameEntry Game() => new()
    {
        Id = "bulk-cost", Engine = "custom", GameRoot = GameRoot, DataDir = DataDir,
        FileExtensions = new[] { "archive" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
    };

    private GameContext Ctx() => Scanner.GameContext(Game(), extraModTrees: Trees);

    /// <summary>Every file under <paramref name="dir"/>: relative path to SHA-256.</summary>
    private static Dictionary<string, string> Hashes(string dir)
        => !Directory.Exists(dir) ? new(StringComparer.OrdinalIgnoreCase)
            : Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
                p => Path.GetRelativePath(dir, p).Replace('\\', '/'),
                p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
                StringComparer.OrdinalIgnoreCase);

    private async Task<(ScanCostProbe Cost, long Ms)> Measure(string label, Func<Task> op)
    {
        var probe = new ScanCostProbe();
        ScanCostProbe.Current = probe;
        var sw = Stopwatch.StartNew();
        try { await op(); }
        finally { ScanCostProbe.Current = null; }
        sw.Stop();
        _out.WriteLine($"{label}: BuildModList={probe.ModListBuilds} ModTrees.Build={probe.TreeBuilds} ms={sw.ElapsedMilliseconds}");
        return (probe, sw.ElapsedMilliseconds);
    }

    private void WriteProfile(GameContext c, string name, IEnumerable<(string Name, bool Enabled)> mods)
    {
        Directory.CreateDirectory(c.ProfilesDir);
        var body = "{\n  \"savedAt\": \"2026-10-02T00:00:00Z\",\n  \"game\": \"bulk-cost\",\n  \"mods\": [\n"
            + string.Join(",\n", mods.Select(m => $"    {{ \"name\": \"{m.Name}\", \"enabled\": {(m.Enabled ? "true" : "false")} }}"))
            + "\n  ]\n}\n";
        File.WriteAllText(Path.Combine(c.ProfilesDir, Profile.SafeProfileName(name) + ".json"), body);
    }

    private void AssertEveryExtraHeld(GameContext c, IEnumerable<string> mods)
    {
        var wanted = new HashSet<string>(mods, StringComparer.Ordinal);
        foreach (var (mod, rel) in ExtraEntries().Where(e => wanted.Contains(e.Mod)))
        {
            Assert.False(File.Exists(Path.Combine(GameRoot, rel)), $"{rel} is still live after {mod} was turned off");
            var held = Path.Combine(DataDir, "disabled-trees", mod, rel);
            Assert.True(File.Exists(held), $"{rel} is not held for {mod}");
            Assert.Equal($"EXTRA-{mod}-{rel}", File.ReadAllText(held));
        }
        Assert.Equal("ARCHIVEXL", File.ReadAllText(Path.Combine(GameRoot, "red4ext", "plugins", "ArchiveXL", "ArchiveXL.dll")));
        Assert.Equal("TWEAKXL", File.ReadAllText(Path.Combine(GameRoot, "red4ext", "plugins", "TweakXL", "TweakXL.dll")));
    }

    [Fact]
    public async Task Enable_all_and_disable_all_read_the_mod_list_and_trees_a_constant_number_of_times()
    {
        BuildCyberpunkShape();
        var before = Hashes(GameRoot);
        var c = Ctx();

        var (off, _) = await Measure("SetAllMods(false)", () => Scanner.SetAllModsAsync(false, c));
        AssertEveryExtraHeld(c, Enumerable.Range(0, ModCount).Select(ModName));
        Assert.All(await Scanner.BuildModListAsync(c), m => Assert.False(m.Enabled));

        var (on, _) = await Measure("SetAllMods(true)", () => Scanner.SetAllModsAsync(true, c));

        // The round trip is byte-identical, and nothing is left held.
        Assert.Equal(before, Hashes(GameRoot));
        Assert.Empty(Hashes(Path.Combine(DataDir, "disabled-trees")));

        Assert.True(off.ModListBuilds <= 2, $"disable-all built the mod list {off.ModListBuilds} times");
        Assert.True(off.TreeBuilds <= 1, $"disable-all read the trees {off.TreeBuilds} times");
        Assert.True(on.ModListBuilds <= 2, $"enable-all built the mod list {on.ModListBuilds} times");
        Assert.True(on.TreeBuilds <= 1, $"enable-all read the trees {on.TreeBuilds} times");
    }

    [Fact]
    public async Task A_profile_load_flipping_a_hundred_mods_reads_a_constant_number_of_times()
    {
        BuildCyberpunkShape();
        var before = Hashes(GameRoot);
        var c = Ctx();
        WriteProfile(c, "half-off", Enumerable.Range(0, ModCount).Select(i => (ModName(i), i >= 100)));
        WriteProfile(c, "all-on", Enumerable.Range(0, ModCount).Select(i => (ModName(i), true)));

        var (off, _) = await Measure("LoadProfile(half-off)", () => Scanner.LoadProfileAsync("half-off", c));
        AssertEveryExtraHeld(c, Enumerable.Range(0, 100).Select(ModName));
        var rows = await Scanner.BuildModListAsync(c);
        Assert.Equal(100, rows.Count(m => !m.Enabled));
        // A mod the profile keeps on keeps its extras live.
        foreach (var (mod, rel) in ExtraEntries().Where(e => string.CompareOrdinal(e.Mod, ModName(100)) >= 0))
            Assert.True(File.Exists(Path.Combine(GameRoot, rel)), $"{rel} moved though {mod} stayed on");

        var (on, _) = await Measure("LoadProfile(all-on)", () => Scanner.LoadProfileAsync("all-on", c));

        Assert.Equal(before, Hashes(GameRoot));
        Assert.Empty(Hashes(Path.Combine(DataDir, "disabled-trees")));

        // The plan itself lists once (ModListing.Resolve); the apply adds a constant on top.
        Assert.True(off.ModListBuilds <= 3, $"profile load (100 off) built the mod list {off.ModListBuilds} times");
        Assert.True(off.TreeBuilds <= 1, $"profile load (100 off) read the trees {off.TreeBuilds} times");
        Assert.True(on.ModListBuilds <= 3, $"profile load (100 on) built the mod list {on.ModListBuilds} times");
        Assert.True(on.TreeBuilds <= 1, $"profile load (100 on) read the trees {on.TreeBuilds} times");
    }

    [Fact]
    public async Task A_loadout_mode_switch_reads_a_constant_number_of_times()
    {
        BuildCyberpunkShape();
        var before = Hashes(GameRoot);
        var c = Ctx();
        // Half the mods are multiplayer-only, so single-player mode turns them off and "all" brings them back.
        Directory.CreateDirectory(c.DataDir);
        File.WriteAllText(c.ClassificationPath, "{\n"
            + string.Join(",\n", Enumerable.Range(0, ModCount).Select(i => $"  \"{ModName(i)}\": \"{(i < 100 ? "mp" : "both")}\""))
            + "\n}\n");

        var (off, _) = await Measure("ApplyMode(sp)", () => Scanner.ApplyModeAsync("sp", c));
        AssertEveryExtraHeld(c, Enumerable.Range(0, 100).Select(ModName));

        var (on, _) = await Measure("ApplyMode(all)", () => Scanner.ApplyModeAsync("all", c));

        Assert.Equal(before, Hashes(GameRoot));
        Assert.True(off.ModListBuilds <= 2, $"mode switch (100 off) built the mod list {off.ModListBuilds} times");
        Assert.True(off.TreeBuilds <= 1, $"mode switch (100 off) read the trees {off.TreeBuilds} times");
        Assert.True(on.ModListBuilds <= 2, $"mode switch (100 on) built the mod list {on.ModListBuilds} times");
        Assert.True(on.TreeBuilds <= 1, $"mode switch (100 on) read the trees {on.TreeBuilds} times");
    }

    [Fact]
    public async Task A_game_with_no_extra_trees_never_reads_a_tree_in_bulk()
    {
        for (var i = 0; i < 20; i++) Put($"archive/pc/mod/{ModName(i)}.archive", $"ARCHIVE-{i}");
        var c = Scanner.GameContext(Game());

        var (off, _) = await Measure("no-trees SetAllMods(false)", () => Scanner.SetAllModsAsync(false, c));
        var (on, _) = await Measure("no-trees SetAllMods(true)", () => Scanner.SetAllModsAsync(true, c));

        Assert.Equal(0, off.TreeBuilds);
        Assert.Equal(0, on.TreeBuilds);
    }

    /// <summary>
    /// The reuse is safe only if one selection, made before anything moves, still names the right entries
    /// for every mod after earlier mods' entries have left the same trees. Three mods share two trees and
    /// are turned off in one bulk operation: each must take exactly its own entries, the later ones
    /// included. A contested pair (Cool_Mod, CoolMod reduce to one key) must still move nothing, because
    /// both names stay claimants for the whole operation.
    /// </summary>
    [Fact]
    public async Task In_one_bulk_disable_every_mod_takes_exactly_its_own_extras_from_shared_trees()
    {
        foreach (var n in new[] { "Alpha", "Beta", "Gamma", "Cool_Mod", "CoolMod" })
            Put($"archive/pc/mod/{n}.archive", $"ARCHIVE-{n}");
        foreach (var n in new[] { "Alpha", "Beta", "Gamma" })
        {
            Put($"r6/scripts/{n}/main.reds", $"SCRIPTS-{n}");
            Put($"r6/tweaks/{n}.yaml", $"TWEAK-{n}");
        }
        Put("r6/scripts/CoolMod/contested.reds", "CONTESTED");
        Put("red4ext/plugins/ArchiveXL/ArchiveXL.dll", "ARCHIVEXL");
        var before = Hashes(GameRoot);
        var c = Ctx();

        await Scanner.SetAllModsAsync(false, c);

        foreach (var n in new[] { "Alpha", "Beta", "Gamma" })
        {
            var held = Hashes(Path.Combine(DataDir, "disabled-trees", n));
            Assert.Equal(new[] { $"r6/scripts/{n}/main.reds", $"r6/tweaks/{n}.yaml" },
                held.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }
        Assert.Equal("CONTESTED", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "CoolMod", "contested.reds")));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees", "CoolMod")));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees", "Cool_Mod")));
        Assert.Equal(new[] { "CoolMod/contested.reds" }, Hashes(Path.Combine(GameRoot, "r6", "scripts")).Keys.ToArray());
        Assert.Equal(new[] { "ArchiveXL/ArchiveXL.dll" }, Hashes(Path.Combine(GameRoot, "red4ext", "plugins")).Keys.ToArray());
        Assert.Empty(Hashes(Path.Combine(GameRoot, "r6", "tweaks")));

        await Scanner.SetAllModsAsync(true, c);

        Assert.Equal(before, Hashes(GameRoot));
    }

    /// <summary>
    /// One loadout switch that turns some mods on and others off in the same pass: evens are multiplayer,
    /// odds single-player. "sp" turns the evens off; "mp" then brings the evens back and turns the odds off,
    /// in one operation that still reads the list and the trees a constant number of times.
    /// </summary>
    [Fact]
    public async Task A_mixed_direction_mode_switch_moves_and_restores_every_extra_at_constant_cost()
    {
        BuildCyberpunkShape();
        var before = Hashes(GameRoot);
        var c = Ctx();
        Directory.CreateDirectory(c.DataDir);
        File.WriteAllText(c.ClassificationPath, "{\n"
            + string.Join(",\n", Enumerable.Range(0, ModCount).Select(i => $"  \"{ModName(i)}\": \"{(i % 2 == 0 ? "mp" : "sp")}\""))
            + "\n}\n");
        var evens = Enumerable.Range(0, ModCount).Where(i => i % 2 == 0).Select(ModName).ToList();
        var odds = Enumerable.Range(0, ModCount).Where(i => i % 2 == 1).Select(ModName).ToList();

        await Scanner.ApplyModeAsync("sp", c);
        AssertEveryExtraHeld(c, evens);

        var (mixed, _) = await Measure("ApplyMode(mp), 100 on + 100 off", () => Scanner.ApplyModeAsync("mp", c));

        AssertEveryExtraHeld(c, odds);
        foreach (var (mod, rel) in ExtraEntries().Where(e => evens.Contains(e.Mod)))
        {
            Assert.Equal($"EXTRA-{mod}-{rel}", File.ReadAllText(Path.Combine(GameRoot, rel)));
            Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees", mod)), $"{mod} still has a holding folder");
        }
        var rows = await Scanner.BuildModListAsync(c);
        Assert.All(rows, m => Assert.Equal(evens.Contains(m.Name), m.Enabled));

        await Scanner.ApplyModeAsync("all", c);

        Assert.Equal(before, Hashes(GameRoot));
        Assert.Empty(Hashes(Path.Combine(DataDir, "disabled-trees")));
        Assert.True(mixed.ModListBuilds <= 2, $"mixed mode switch built the mod list {mixed.ModListBuilds} times");
        Assert.True(mixed.TreeBuilds <= 1, $"mixed mode switch read the trees {mixed.TreeBuilds} times");
    }

    /// <summary>
    /// The one case where a claimant name appears mid-operation, pinned to what the bulk scope does. Under
    /// strip_underscore_p_suffix, Bar_P.pak lists as row Bar and pairs the folder Bar_P/. Once Bar is off
    /// the folder is unpaired and library inference names it Bar_P, whose key (barp) is BarP's. The bulk
    /// operation decides from the rows as they were when it began, when Bar_P was no row at all: BarP's
    /// entry moves with BarP, in any order, and comes back with it byte for byte.
    /// </summary>
    [Fact]
    public async Task A_library_name_that_appears_mid_operation_does_not_change_the_bulk_selection()
    {
        Put("archive/pc/mod/Bar_P.pak", "BAR");
        Put("archive/pc/mod/Bar_P/data.bin", "BAR-FOLDER");
        Put("archive/pc/mod/BarP.pak", "BARP");
        Put("r6/scripts/BarP/main.reds", "BARP-SCRIPTS");
        var before = Hashes(GameRoot);
        var game = new GameEntry
        {
            Id = "bulk-cost-p", Engine = "custom", GameRoot = GameRoot, DataDir = DataDir,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        var c = Scanner.GameContext(game, extraModTrees: Trees);
        var names = (await Scanner.BuildModListAsync(c)).Select(m => m.Name).ToList();
        Assert.Equal(new[] { "Bar", "BarP" }, names);   // Bar sorts first, so a fresh per-mod read saw Bar_P

        await Scanner.SetAllModsAsync(false, c);

        Assert.Equal("BARP-SCRIPTS", File.ReadAllText(
            Path.Combine(DataDir, "disabled-trees", "BarP", "r6", "scripts", "BarP", "main.reds")));
        Assert.False(Directory.Exists(Path.Combine(GameRoot, "r6", "scripts", "BarP")));
        // The orphaned folder is not Bar's to move and stays where it was.
        Assert.Equal("BAR-FOLDER", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "Bar_P", "data.bin")));

        await Scanner.SetAllModsAsync(true, c);

        Assert.Equal(before, Hashes(GameRoot));
        Assert.Empty(Hashes(Path.Combine(DataDir, "disabled-trees")));

        // The case is real: a single toggle reads fresh, so with Bar already off the orphaned Bar_P/ is a
        // library row that contests BarP, and BarP's entry stays put. The bulk path deliberately does not.
        await Scanner.DisableModAsync("Bar", c);
        await Scanner.DisableModAsync("BarP", c);
        Assert.Equal("BARP-SCRIPTS", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "BarP", "main.reds")));
    }
}
