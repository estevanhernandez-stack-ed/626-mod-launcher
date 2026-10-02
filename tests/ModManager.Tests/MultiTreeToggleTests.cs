using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two, turning off: a mod's same-named entries in the game's extra trees go to
/// <c>disabled-trees/&lt;Mod&gt;</c> with its main files, as one operation that either completes or puts
/// everything back. Driven through the public toggle entry points on a Cyberpunk-shaped custom game, so
/// the test reaches the same <c>DisableEntry</c> the row toggle, bulk toggles and the MCP reach.
/// </summary>
public class MultiTreeToggleTests : IDisposable
{
    private static readonly string[] Trees =
    {
        "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods",
    };

    private readonly string _root = TestSupport.TempDir("mmb-multitree-");
    private string GameRoot => Path.Combine(_root, "game");
    private string DataDir => Path.Combine(_root, "data");
    private string HeldTrees => Path.Combine(DataDir, "disabled-trees", "CoolMod");

    public MultiTreeToggleTests()
    {
        Put("archive/pc/mod/CoolMod.archive", "MAIN");
        Put("r6/scripts/CoolMod/main.reds", "SCRIPTS");
        Put("r6/tweaks/CoolMod.yaml", "TWEAK");
        Put("bin/x64/plugins/cyber_engine_tweaks/mods/CoolMod/init.lua", "CET");
        // Not CoolMod's: a framework folder with no row of its own, and another mod's scripts.
        Put("red4ext/plugins/ArchiveXL/ArchiveXL.dll", "FRAMEWORK");
        Put("r6/scripts/OtherThing/other.reds", "OTHER");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Put(string rel, string content)
    {
        var p = Path.Combine(GameRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private GameEntry Game() => new()
    {
        Id = "multi-tree", Engine = "custom", GameRoot = GameRoot, DataDir = DataDir,
        FileExtensions = new[] { "archive" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
    };

    private GameContext Ctx() => Scanner.GameContext(Game(), extraModTrees: Trees);

    /// <summary>Every file under the game root, by relative path, with its contents.</summary>
    private Dictionary<string, string> GameTree()
        => Directory.GetFiles(GameRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(GameRoot, p), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Fixture_lists_CoolMod_as_a_switchable_row()
    {
        var rows = await Scanner.BuildModListAsync(Ctx());
        var row = Assert.Single(rows, m => m.Name == "CoolMod");
        Assert.True(row.Enabled && !row.ReadOnly);
    }

    [Fact]
    public async Task Turning_off_holds_every_extra_entry_beside_the_main_files()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());

        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(HeldTrees, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.Equal("TWEAK", File.ReadAllText(Path.Combine(HeldTrees, "r6", "tweaks", "CoolMod.yaml")));
        Assert.Equal("CET", File.ReadAllText(Path.Combine(HeldTrees,
            "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua")));

        Assert.False(Directory.Exists(Path.Combine(GameRoot, "r6", "scripts", "CoolMod")));
        Assert.False(File.Exists(Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml")));
        Assert.False(Directory.Exists(Path.Combine(GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod")));

        // The main files are held exactly as before stage two.
        var held = Path.Combine(DataDir, "disabled", "CoolMod");
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(held, "CoolMod.archive")));
        Assert.True(File.Exists(Path.Combine(held, "meta.json")));
        Assert.False(File.Exists(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));

        // What is not CoolMod's stays live.
        Assert.Equal("FRAMEWORK", File.ReadAllText(Path.Combine(GameRoot, "red4ext", "plugins", "ArchiveXL", "ArchiveXL.dll")));
        Assert.Equal("OTHER", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "OtherThing", "other.reds")));
    }

    [Fact]
    public async Task Turning_off_through_the_row_toggle_reaches_the_same_path()
    {
        var ctx = Ctx();
        var row = Assert.Single(await Scanner.BuildModListAsync(ctx), m => m.Name == "CoolMod");

        await ModToggle.SetEnabledAsync(ctx, row, enabled: false);

        Assert.True(File.Exists(Path.Combine(HeldTrees, "r6", "tweaks", "CoolMod.yaml")));
        Assert.False(File.Exists(Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml")));
    }

    [Fact]
    public async Task A_held_extra_already_there_refuses_with_nothing_moved()
    {
        var old = Path.Combine(HeldTrees, "r6", "tweaks", "CoolMod.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "OLD-TWEAK");
        var before = GameTree();

        var e = await Assert.ThrowsAsync<HeldCopyCollisionException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));

        Assert.Contains("Nothing was moved", e.Message);
        Assert.Contains("disabled-trees", e.Message);
        Assert.Equal(before, GameTree());
        Assert.Equal("OLD-TWEAK", File.ReadAllText(old));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled", "CoolMod")));
    }

    [Fact]
    public async Task An_existing_empty_destination_refuses_with_nothing_moved()
    {
        // No file is held, so HoldsFiles alone would let it through; the destination folder is still
        // taken, and a move onto it would fail halfway through the operation instead of before it.
        Directory.CreateDirectory(Path.Combine(HeldTrees, "r6", "scripts", "CoolMod"));
        var before = GameTree();

        await Assert.ThrowsAsync<HeldCopyCollisionException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));

        Assert.Equal(before, GameTree());
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled", "CoolMod")));
    }

    [Fact]
    public async Task A_failure_midway_through_the_extras_puts_everything_back()
    {
        // The CET entry is the LAST tree, so r6/scripts and r6/tweaks have already moved when it fails:
        // the rollback of a moved extra is really exercised, not just the main file's.
        var before = GameTree();
        var locked = Path.Combine(GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));
            // It failed on the CET entry, after the main file and two extras had already moved.
            Assert.Contains("cyber_engine_tweaks", e.Message);
            Assert.DoesNotContain("could not be moved back", e.Message);
        }

        Assert.Equal(before, GameTree());
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.False(File.Exists(Path.Combine(DataDir, "disabled", "CoolMod", "meta.json")));
        Assert.False(Directory.Exists(HeldTrees));
    }

    [Fact]
    public async Task A_read_only_row_moves_nothing()
    {
        var ctx = Ctx();
        var row = Assert.Single(await Scanner.BuildModListAsync(ctx), m => m.Name == "CoolMod");
        row.ReadOnly = true;
        var before = GameTree();

        await Scanner.SetAppendedRowEnabledAsync(row, enabled: false, ctx);

        Assert.Equal(before, GameTree());
        Assert.False(Directory.Exists(HeldTrees));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled", "CoolMod")));
    }
}
