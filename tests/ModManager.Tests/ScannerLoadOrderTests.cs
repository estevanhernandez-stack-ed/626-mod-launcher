using ModManager.Core;

namespace ModManager.Tests;

// The load-order feature prefixes pak files with NNNN__ to drive Unreal's alphabetical load.
// That prefix must be invisible to identity: a prefixed file still groups to the same mod key
// and still disables/enables. Guards the working core against the load-order rename.
public class ScannerLoadOrderTests
{
    private static (string modsDir, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("loadorder-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (modsDir, c);
    }

    // Server-build mirror fixture: the SP client folder (mods) and the MP server-build mirror
    // (server) both hold the same pak. The Windrose desync bug — load-order rename touched the
    // primary only, leaving the mirror under a different filename — lived here.
    private static (string modsDir, string mirrorDir, GameContext c) MirrorFixture()
    {
        var root = TestSupport.TempDir("loadorder-mirror-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        var mirrorDir = Path.Combine(gameRoot, "server");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(mirrorDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") { Mirrors = new[] { "server" } } },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (modsDir, mirrorDir, c);
    }

    [Fact]
    public async Task A_launcher_prefixed_file_groups_to_its_base_key()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "0010__cool.pak"), "x");
        var mods = await Scanner.BuildModListAsync(c);
        Assert.Contains(mods, m => m.Name == "cool");
        Assert.DoesNotContain(mods, m => m.Name.StartsWith("0010"));
    }

    [Fact]
    public async Task Disable_enable_works_on_a_prefixed_file()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "0020__cool.pak"), "DATA");

        await Scanner.DisableModAsync("cool", c);
        Assert.False(File.Exists(Path.Combine(modsDir, "0020__cool.pak")));
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(c.DisabledRoot, "cool", "0020__cool.pak")));

        await Scanner.EnableModAsync("cool", c);
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "0020__cool.pak")));
    }

    [Fact]
    public async Task ApplyLoadOrder_prefixes_files_in_order_and_reset_restores()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "C");
        File.WriteAllText(Path.Combine(modsDir, "audio.pak"), "A");

        // Want audio to load before cool.
        await Scanner.ApplyLoadOrderAsync(c, new[] { "audio", "cool" });

        var files = Directory.GetFiles(modsDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "0010__audio.pak", "0020__cool.pak" }, files);

        // Identity survives the prefix.
        var mods = await Scanner.BuildModListAsync(c);
        Assert.Contains(mods, m => m.Name == "audio");
        Assert.Contains(mods, m => m.Name == "cool");

        await Scanner.ResetLoadOrderAsync(c);
        var restored = Directory.GetFiles(modsDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "audio.pak", "cool.pak" }, restored);
    }

    [Fact]
    public async Task ApplyLoadOrder_prefixes_the_mirror_in_lockstep_with_the_primary()
    {
        var (modsDir, mirrorDir, c) = MirrorFixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "C");
        File.WriteAllText(Path.Combine(mirrorDir, "cool.pak"), "C");

        await Scanner.ApplyLoadOrderAsync(c, new[] { "cool" });

        // Both copies carry the same prefix — no SP/MP filename desync.
        Assert.True(File.Exists(Path.Combine(modsDir, "0010__cool.pak")));
        Assert.True(File.Exists(Path.Combine(mirrorDir, "0010__cool.pak")));
        Assert.False(File.Exists(Path.Combine(mirrorDir, "cool.pak")));
    }

    [Fact]
    public async Task ResetLoadOrder_strips_the_mirror_in_lockstep_with_the_primary()
    {
        var (modsDir, mirrorDir, c) = MirrorFixture();
        File.WriteAllText(Path.Combine(modsDir, "0010__cool.pak"), "C");
        File.WriteAllText(Path.Combine(mirrorDir, "0010__cool.pak"), "C");

        await Scanner.ResetLoadOrderAsync(c);

        Assert.True(File.Exists(Path.Combine(modsDir, "cool.pak")));
        Assert.True(File.Exists(Path.Combine(mirrorDir, "cool.pak")));
        Assert.False(File.Exists(Path.Combine(mirrorDir, "0010__cool.pak")));
    }

    [Fact]
    public async Task GetLoadOrder_reconciles_saved_with_current_enabled()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "a.pak"), "x");
        File.WriteAllText(Path.Combine(modsDir, "b.pak"), "x");
        await Scanner.ApplyLoadOrderAsync(c, new[] { "b", "a" }); // persists order b,a

        File.WriteAllText(Path.Combine(modsDir, "c.pak"), "x");   // new mod appears
        var order = await Scanner.GetLoadOrderAsync(c);
        Assert.Equal(new[] { "b", "a", "c" }, order.ToArray());
    }

    /// <summary>
    /// ResetLoadOrder must NOT rename files inside a Vortex-owned folder even when those files
    /// carry a launcher load-order prefix. Files in non-owned locations ARE stripped as usual.
    /// </summary>
    [Fact]
    public async Task ResetLoadOrder_skips_owned_locations_leaves_prefixed_files_intact()
    {
        var root = TestSupport.TempDir("loadorder-owned-");
        var gameRoot = Path.Combine(root, "game");

        // Owned location: has a Vortex deployment marker. The prefix was written externally —
        // our tool must never rename inside here.
        var ownedDir = Path.Combine(gameRoot, "ownedMods");
        Directory.CreateDirectory(ownedDir);
        File.WriteAllText(Path.Combine(ownedDir, "vortex.deployment.x.json"), "{}"); // ownership marker
        File.WriteAllText(Path.Combine(ownedDir, "0010__OwnedThing.pak"), "OWNED");

        // Normal location: a launcher-prefixed file that SHOULD be stripped.
        var normalDir = Path.Combine(gameRoot, "normalMods");
        Directory.CreateDirectory(normalDir);
        File.WriteAllText(Path.Combine(normalDir, "0020__MyMod.pak"), "MINE");

        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[]
            {
                new ModLocation("owned",  "Owned",  "ownedMods"),
                new ModLocation("normal", "Normal", "normalMods"),
            },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });

        await Scanner.ResetLoadOrderAsync(c);

        // Owned folder: prefix must NOT have been stripped — file name unchanged.
        Assert.True(File.Exists(Path.Combine(ownedDir, "0010__OwnedThing.pak")),
            "ResetLoadOrder must not rename a file inside an owned (Vortex-managed) folder.");
        Assert.False(File.Exists(Path.Combine(ownedDir, "OwnedThing.pak")),
            "ResetLoadOrder must not create the stripped filename inside an owned folder.");

        // Normal folder: prefix SHOULD have been stripped.
        Assert.True(File.Exists(Path.Combine(normalDir, "MyMod.pak")),
            "ResetLoadOrder must strip the prefix from a non-owned location's files.");
        Assert.False(File.Exists(Path.Combine(normalDir, "0020__MyMod.pak")),
            "ResetLoadOrder must remove the prefixed filename from a non-owned location.");
    }
}
