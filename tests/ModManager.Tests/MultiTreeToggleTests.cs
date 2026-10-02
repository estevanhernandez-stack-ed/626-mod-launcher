using System.Security.Cryptography;
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
    public async Task A_held_file_at_another_tree_path_still_refuses()
    {
        // Not at any destination this turn-off would use (CoolMod has nothing in r6/input), so only the
        // "the holding folder already holds files" rule can refuse it.
        var old = Path.Combine(HeldTrees, "r6", "input", "Leftover.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "OLD-INPUT");
        var before = GameTree();

        await Assert.ThrowsAsync<HeldCopyCollisionException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));

        Assert.Equal(before, GameTree());
        Assert.Equal("OLD-INPUT", File.ReadAllText(old));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled", "CoolMod")));
    }

    [Fact]
    public async Task A_full_rollback_clears_the_empty_folders_even_when_the_mod_folder_was_already_there()
    {
        // An empty disabled-trees/CoolMod holds nothing, so turning off goes ahead; the segment folders
        // the failed attempt makes inside it must not be left behind.
        Directory.CreateDirectory(HeldTrees);
        var before = GameTree();
        var locked = Path.Combine(GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));

        Assert.Equal(before, GameTree());
        Assert.False(Directory.Exists(HeldTrees));
    }

    [Fact]
    public async Task An_extra_that_cannot_go_back_keeps_the_mod_off()
    {
        // The CET entry fails going out; on the way back r6/tweaks returns, then r6/scripts refuses to.
        // Rolling the main file back after that would list the mod as on with its scripts held, a state
        // no toggle could see. Stopping leaves it off, with a record, so turning it on restores it all.
        var locked = Path.Combine(GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua");
        var heldScripts = Path.Combine(HeldTrees, "r6", "scripts", "CoolMod");
        Scanner.BeforeRollbackMoveForTests = from =>
        {
            if (string.Equals(from, heldScripts, StringComparison.OrdinalIgnoreCase))
                throw new IOException("injected: cannot move back");
        };
        InvalidOperationException e;
        try
        {
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));
        }
        finally { Scanner.BeforeRollbackMoveForTests = null; }

        Assert.Contains("\"r6/scripts/CoolMod\"", e.Message);
        Assert.Contains(HeldTrees, e.Message);
        Assert.Contains("\"CoolMod.archive\"", e.Message);
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(heldScripts, "main.reds")));
        Assert.Equal("TWEAK", File.ReadAllText(Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml")));

        var held = Path.Combine(DataDir, "disabled", "CoolMod");
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(held, "CoolMod.archive")));
        Assert.True(File.Exists(Path.Combine(held, "meta.json")));
        Assert.False(File.Exists(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));

        var row = Assert.Single(await Scanner.BuildModListAsync(Ctx()), m => m.Name == "CoolMod");
        Assert.False(row.Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_owned_or_redeployed_tree_keeps_its_entry_and_the_others_still_move(bool redeployed)
    {
        // Vortex's flag in r6/tweaks: another manager deployed there. Taken over and then re-deployed
        // reads ReDeployed, and the files there are that manager's again, not 626's to move.
        var tweaks = Path.GetFullPath(Path.Combine(GameRoot, "r6", "tweaks"));
        File.WriteAllText(Path.Combine(tweaks, "__folder_managed_by_vortex"), "");
        if (redeployed) TakenOverStore.Add(DataDir, tweaks);

        await Scanner.DisableModAsync("CoolMod", Ctx());

        Assert.Equal("TWEAK", File.ReadAllText(Path.Combine(tweaks, "CoolMod.yaml")));
        Assert.False(Directory.Exists(Path.Combine(HeldTrees, "r6", "tweaks")));
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(HeldTrees, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.False(Directory.Exists(Path.Combine(GameRoot, "r6", "scripts", "CoolMod")));

        // Back on: the owned entry was never touched, and the main file and the held scripts return.
        await Scanner.EnableModAsync("CoolMod", Ctx());
        Assert.Equal("TWEAK", File.ReadAllText(Path.Combine(tweaks, "CoolMod.yaml")));
        Assert.True(File.Exists(Path.Combine(tweaks, "__folder_managed_by_vortex")));
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
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

    // ---- Turning back on (Task 4) ----

    private string HeldMain => Path.Combine(DataDir, "disabled", "CoolMod");
    private string LiveCet => Path.Combine(GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod");

    /// <summary>Every file under <paramref name="dir"/>, by relative path, with a hash of its bytes, so a
    /// round trip is checked byte for byte and not just as text.</summary>
    private static Dictionary<string, string> Hashes(string dir)
        => !Directory.Exists(dir)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
                p => Path.GetRelativePath(dir, p).Replace('\\', '/'),
                p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
                StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Off_then_on_leaves_every_tree_byte_identical_with_both_holding_folders_gone()
    {
        var before = Hashes(GameRoot);

        await Scanner.DisableModAsync("CoolMod", Ctx());
        Assert.True(Directory.Exists(HeldTrees));
        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());

        Assert.True(outcome.Enabled);
        Assert.Null(outcome.Reason);
        Assert.Equal(before, Hashes(GameRoot));
        Assert.False(Directory.Exists(HeldTrees));
        Assert.False(Directory.Exists(HeldMain));
        var row = Assert.Single(await Scanner.BuildModListAsync(Ctx()), m => m.Name == "CoolMod");
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task A_collision_in_one_extra_tree_refuses_with_nothing_written_and_everything_still_held()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        // Something new took the tweak's place while the mod was off.
        Put("r6/tweaks/CoolMod.yaml", "SOMEONE-ELSES");
        var game = Hashes(GameRoot);
        var heldTrees = Hashes(HeldTrees);
        var heldMain = Hashes(HeldMain);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.EnableModAsync("CoolMod", Ctx()));

        Assert.Contains("Couldn't enable \"CoolMod\"", e.Message);
        Assert.Contains("conflict", e.Message);
        Assert.Contains("r6/tweaks/CoolMod.yaml", e.Message);
        // Not even the main file, which has no collision of its own, was written.
        Assert.Equal(game, Hashes(GameRoot));
        Assert.False(File.Exists(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.Equal(heldTrees, Hashes(HeldTrees));
        Assert.Equal(heldMain, Hashes(HeldMain));
    }

    [Fact]
    public async Task A_mod_turned_off_before_stage_two_turns_on_unchanged()
    {
        // Turned off by a build with no extra trees: only the main file was held, no disabled-trees.
        await Scanner.DisableModAsync("CoolMod", Scanner.GameContext(Game()));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees")));
        var before = Hashes(GameRoot);

        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());

        Assert.True(outcome.Enabled);
        Assert.Null(outcome.Reason);
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        before["archive/pc/mod/CoolMod.archive"] = Hashes(GameRoot)["archive/pc/mod/CoolMod.archive"];
        Assert.Equal(before, Hashes(GameRoot));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees")));
        Assert.False(Directory.Exists(HeldMain));
    }

    [Fact]
    public async Task A_held_entry_whose_tree_folder_was_deleted_is_restored_with_the_folder_recreated()
    {
        var before = Hashes(GameRoot);
        await Scanner.DisableModAsync("CoolMod", Ctx());
        // r6/tweaks held only CoolMod's file, so the user tidied the empty folder away; and the whole CET
        // mods path goes too, so more than one missing parent has to come back.
        Directory.Delete(Path.Combine(GameRoot, "r6", "tweaks"), recursive: true);
        Directory.Delete(Path.Combine(GameRoot, "bin"), recursive: true);

        await Scanner.EnableModAsync("CoolMod", Ctx());

        Assert.Equal(before, Hashes(GameRoot));
        Assert.False(Directory.Exists(HeldTrees));
    }

    [Fact]
    public async Task A_mod_left_off_with_only_some_extras_held_comes_all_the_way_back()
    {
        // The stranded state Task 3 can leave: main file held with a record, r6/scripts held, r6/tweaks
        // already back in the game, CET back too. Enable restores only what is held, and the live entries
        // are not collisions because nothing held claims their destinations.
        var before = Hashes(GameRoot);
        var heldScripts = Path.Combine(HeldTrees, "r6", "scripts", "CoolMod");
        Scanner.BeforeRollbackMoveForTests = from =>
        {
            if (string.Equals(from, heldScripts, StringComparison.OrdinalIgnoreCase))
                throw new IOException("injected: cannot move back");
        };
        try
        {
            using (new FileStream(Path.Combine(LiveCet, "init.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
                await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.DisableModAsync("CoolMod", Ctx()));
        }
        finally { Scanner.BeforeRollbackMoveForTests = null; }
        Assert.True(File.Exists(Path.Combine(heldScripts, "main.reds")));
        Assert.True(File.Exists(Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml")));

        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());

        Assert.True(outcome.Enabled);
        Assert.Null(outcome.Reason);
        Assert.Equal(before, Hashes(GameRoot));
        Assert.False(Directory.Exists(HeldTrees));
        Assert.False(Directory.Exists(HeldMain));
    }

    [Fact]
    public async Task A_failure_restoring_an_extra_puts_the_restored_ones_back_and_removes_the_main_copy()
    {
        // CET is the LAST tree, so r6/scripts and r6/tweaks are already back in the game when it fails:
        // the undo of a restored extra is really exercised, then the main copy's.
        var original = Hashes(GameRoot);
        await Scanner.DisableModAsync("CoolMod", Ctx());
        var game = Hashes(GameRoot);
        var heldTrees = Hashes(HeldTrees);
        var heldMain = Hashes(HeldMain);
        var heldCet = Path.Combine(HeldTrees, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod");

        using (new FileStream(Path.Combine(heldCet, "init.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.EnableModAsync("CoolMod", Ctx()));
            Assert.Contains("Couldn't enable \"CoolMod\"", e.Message);
            Assert.DoesNotContain("could not be moved back", e.Message);
        }

        Assert.Equal(game, Hashes(GameRoot));
        Assert.False(Directory.Exists(Path.Combine(GameRoot, "r6", "scripts", "CoolMod")));
        Assert.Equal(heldTrees, Hashes(HeldTrees));
        Assert.Equal(heldMain, Hashes(HeldMain));
        Assert.False(Assert.Single(await Scanner.BuildModListAsync(Ctx()), m => m.Name == "CoolMod").Enabled);

        // Nothing was lost on the way: with the lock gone, it turns on whole.
        await Scanner.EnableModAsync("CoolMod", Ctx());
        Assert.Equal(original, Hashes(GameRoot));
    }

    [Fact]
    public async Task A_restored_extra_that_cannot_go_back_is_named_and_the_mod_can_still_be_turned_on()
    {
        var original = Hashes(GameRoot);
        await Scanner.DisableModAsync("CoolMod", Ctx());
        var heldCet = Path.Combine(HeldTrees, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod");
        var liveTweak = Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml");
        Scanner.BeforeEnableRollbackMoveForTests = from =>
        {
            if (string.Equals(from, liveTweak, StringComparison.OrdinalIgnoreCase))
                throw new IOException("injected: cannot go back to holding");
        };
        InvalidOperationException e;
        try
        {
            using (new FileStream(Path.Combine(heldCet, "init.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
                e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.EnableModAsync("CoolMod", Ctx()));
        }
        finally { Scanner.BeforeEnableRollbackMoveForTests = null; }

        // The tweak stays in the game, said so; everything else went back to holding and the main copy went.
        Assert.Contains("\"r6/tweaks/CoolMod.yaml\" could not be moved back", e.Message);
        Assert.Equal("TWEAK", File.ReadAllText(liveTweak));
        Assert.True(File.Exists(Path.Combine(HeldTrees, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.False(File.Exists(Path.Combine(GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.True(File.Exists(Path.Combine(HeldMain, "meta.json")));

        // That is the partial state enable already handles: it restores what is held.
        await Scanner.EnableModAsync("CoolMod", Ctx());
        Assert.Equal(original, Hashes(GameRoot));
        Assert.False(Directory.Exists(HeldTrees));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tree_another_tool_took_while_the_mod_was_off_skips_the_enable_with_nothing_written(bool redeployed)
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        // While the mod was off, Vortex deployed into r6/tweaks (or took it back after a takeover). Moving
        // the held tweak in would write into that manager's deployment, the main lane's own skip rule.
        var tweaks = Path.GetFullPath(Path.Combine(GameRoot, "r6", "tweaks"));
        File.WriteAllText(Path.Combine(tweaks, "__folder_managed_by_vortex"), "");
        if (redeployed) TakenOverStore.Add(DataDir, tweaks);
        var game = Hashes(GameRoot);
        var heldTrees = Hashes(HeldTrees);
        var heldMain = Hashes(HeldMain);

        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());

        Assert.False(outcome.Enabled);
        Assert.True(outcome.Skipped);
        Assert.Equal("target folder now owned by another tool", outcome.Reason);
        Assert.Equal(game, Hashes(GameRoot));
        Assert.Equal(heldTrees, Hashes(HeldTrees));
        Assert.Equal(heldMain, Hashes(HeldMain));
    }

    [Fact]
    public async Task A_file_where_a_tree_folder_belongs_refuses_before_anything_is_written()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        // r6/tweaks is now a FILE, so the tree folder cannot be recreated. Found by the pre-check, not
        // halfway through the restore after the main file was already copied in.
        Directory.Delete(Path.Combine(GameRoot, "r6", "tweaks"), recursive: true);
        File.WriteAllText(Path.Combine(GameRoot, "r6", "tweaks"), "NOT-A-FOLDER");
        var game = Hashes(GameRoot);
        var heldTrees = Hashes(HeldTrees);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.EnableModAsync("CoolMod", Ctx()));

        Assert.Contains("Couldn't enable \"CoolMod\"", e.Message);
        Assert.Contains("\"r6/tweaks\"", e.Message);
        Assert.Contains("conflict", e.Message);
        Assert.Equal(game, Hashes(GameRoot));
        Assert.Equal(heldTrees, Hashes(HeldTrees));
    }

    [Fact]
    public async Task Files_held_under_a_tree_no_longer_declared_turn_on_with_a_warning_naming_the_folder()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        // A tree the manifest has since dropped: Held does not look there, so restoring cannot reach it.
        var stray = Path.Combine(HeldTrees, "r6", "old", "CoolMod.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        File.WriteAllText(stray, "STRAY");

        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());

        Assert.True(outcome.Enabled);
        Assert.False(outcome.Skipped);
        Assert.NotNull(outcome.Reason);
        Assert.Contains(HeldTrees, outcome.Reason);
        Assert.Contains("files remain", outcome.Reason);
        // Everything the manifest still declares came back; the stray stays exactly where it was.
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.Equal("STRAY", File.ReadAllText(stray));
        Assert.False(Directory.Exists(Path.Combine(HeldTrees, "r6", "scripts")));
    }
}
