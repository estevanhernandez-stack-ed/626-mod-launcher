using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The folder lane's twin of <see cref="DirectInjectHeldFilesTests"/>: a turned-off copy of a mod in
/// <c>disabled\</c> must survive turning a fresh copy of the same mod off.
///
/// <para>Found by the reversibility audit on 2026-09-14. The listing shows only the live copy when both
/// exist, so the user cannot see the held one. Turning the live copy off hit the name collision
/// mid-move, and the rollback ran a recursive delete over <c>disabled\&lt;mod&gt;</c>, taking the held
/// copy and its record with it. Reached from the row toggle, Disable all, apply mode and load
/// profile.</para>
/// </summary>
public class ScannerHeldFilesTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-scan-held-");
    private string Scripts => Path.Combine(_root, "game", "scripts");
    private string Disabled => Path.Combine(_root, "data", "disabled");

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Game() => new()
    {
        Id = "scan-held", Engine = "custom", GameRoot = Path.Combine(_root, "game"), DataDir = Path.Combine(_root, "data"),
        FileExtensions = new[] { "lua" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "scripts") },
    };

    private static async Task Toggle(GameEntry game, string name, bool enabled)
        => await ModToggle.SetEnabledAsync(Scanner.GameContext(game),
            Assert.Single(ModListing.Resolve(game), m => m.Name == name), enabled);

    private List<string> AllContents()
        => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();

    [Fact]
    public async Task Turning_a_fresh_file_mod_off_while_an_old_copy_is_held_deletes_nothing()
    {
        Directory.CreateDirectory(Scripts);
        File.WriteAllText(Path.Combine(Scripts, "fastcraft.lua"), "OLD");
        var game = Game();
        var name = Assert.Single(ModListing.Resolve(game)).Name;
        await Toggle(game, name, false);
        File.WriteAllText(Path.Combine(Scripts, "fastcraft.lua"), "NEW"); // fresh install while the old one is held

        var e = await Assert.ThrowsAsync<HeldCopyCollisionException>(() => Toggle(game, name, false));

        Assert.Contains("Nothing was moved", e.Message);
        var everything = AllContents();
        Assert.Contains("OLD", everything);
        Assert.Contains("NEW", everything);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(Scripts, "fastcraft.lua")));
        Assert.True(File.Exists(Path.Combine(Disabled, name, "meta.json")), "the held copy's record is gone");
    }

    [Fact]
    public async Task Turning_a_fresh_folder_mod_off_while_an_old_copy_is_held_neither_deletes_nor_merges()
    {
        // A by_folder game, where each folder IS a mod. (A lone folder on a lua game lists as a
        // read-only library instead, which never moves.)
        var folder = Path.Combine(_root, "game", "mod", "betterui");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "init.lua"), "OLD-INIT");
        File.WriteAllText(Path.Combine(folder, "old-only.lua"), "OLD-ONLY");
        var game = new GameEntry
        {
            Id = "scan-held-folders", GameRoot = Path.Combine(_root, "game"), DataDir = Path.Combine(_root, "data"),
            GroupingRule = "by_folder",
            ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
        };
        var row = Assert.Single(ModListing.Resolve(game));
        Assert.True(row.IsFolder && !row.ReadOnly, "fixture must produce a switchable folder mod");
        var name = row.Name;
        await Toggle(game, name, false);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "init.lua"), "NEW-INIT");
        File.WriteAllText(Path.Combine(folder, "new-only.lua"), "NEW-ONLY");

        await Assert.ThrowsAsync<HeldCopyCollisionException>(() => Toggle(game, name, false));

        var everything = AllContents();
        foreach (var s in new[] { "OLD-INIT", "OLD-ONLY", "NEW-INIT", "NEW-ONLY" }) Assert.Contains(s, everything);
        // Not merged into one held copy either: the fresh files are still live, the old ones still held.
        Assert.Equal("NEW-INIT", File.ReadAllText(Path.Combine(folder, "init.lua")));
        Assert.False(File.Exists(Path.Combine(folder, "old-only.lua")));
    }

    [Fact]
    public async Task A_clean_folder_lane_round_trip_still_works()
    {
        Directory.CreateDirectory(Scripts);
        File.WriteAllText(Path.Combine(Scripts, "fastcraft.lua"), "ONLY");
        var game = Game();
        var name = Assert.Single(ModListing.Resolve(game)).Name;

        await Toggle(game, name, false);
        await Toggle(game, name, true);

        Assert.Equal("ONLY", File.ReadAllText(Path.Combine(Scripts, "fastcraft.lua")));
        Assert.True(Assert.Single(ModListing.Resolve(game)).Enabled);
    }
}
