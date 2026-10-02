using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two, the row: what a row says about its extra trees comes from the SAME selection the toggle
/// makes, so the row never promises a move the toggle won't make. Cyberpunk-shaped custom game, driven
/// through the public entry points the App calls.
/// </summary>
public class MultiTreeToggleRowTests : IDisposable
{
    private static readonly string[] Trees =
    {
        "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods",
    };
    private const string Cet = "bin/x64/plugins/cyber_engine_tweaks/mods";

    private readonly string _root = TestSupport.TempDir("mmb-multitree-row-");
    private string GameRoot => Path.Combine(_root, "game");
    private string DataDir => Path.Combine(_root, "data");
    private string HeldTrees => Path.Combine(DataDir, "disabled-trees", "CoolMod");

    public MultiTreeToggleRowTests()
    {
        Put("archive/pc/mod/CoolMod.archive", "MAIN");
        Put("r6/scripts/CoolMod/main.reds", "SCRIPTS");
        Put("r6/tweaks/CoolMod.yaml", "TWEAK");
        Put("bin/x64/plugins/cyber_engine_tweaks/mods/CoolMod/init.lua", "CET");
        Put("red4ext/plugins/ArchiveXL/ArchiveXL.dll", "FRAMEWORK");
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
        Id = "multi-tree-row", Engine = "custom", GameRoot = GameRoot, DataDir = DataDir,
        FileExtensions = new[] { "archive" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
    };

    private GameContext Ctx(IReadOnlyList<string>? trees = null) => Scanner.GameContext(Game(), extraModTrees: trees ?? Trees);

    private static async Task<Mod> Row(GameContext ctx, string name)
        => Assert.Single(await Scanner.BuildModListAsync(ctx), m => m.Name == name);

    [Fact]
    public async Task A_live_row_lists_every_tree_that_will_move_and_says_626_moves_them()
    {
        var ctx = Ctx();
        var rows = Scanner.ExtraTreeRowsFor(ctx);

        var text = rows.TextFor(await Row(ctx, "CoolMod"));

        Assert.Equal($"Also has files in r6/scripts, r6/tweaks, {Cet}", text.Line);
        Assert.Equal("626 turns these on and off with the mod.", text.Tooltip);
    }

    [Fact]
    public async Task The_row_and_the_toggle_pick_the_same_entries()
    {
        var ctx = Ctx();
        var promised = Scanner.ExtraTreeRowsFor(ctx).MovesFor(await Row(ctx, "CoolMod")).Movable
            .Select(e => e.Tree + "/" + e.EntryName).ToList();

        await Scanner.DisableModAsync("CoolMod", ctx);

        var held = Scanner.ExtraTreeRowsFor(Ctx()).HeldWhileOffEntries("CoolMod");
        Assert.Equal(promised, held);
        Assert.Equal(3, held.Count);
    }

    [Fact]
    public async Task A_contested_name_is_held_back_on_the_row_and_nothing_moves_on_toggle()
    {
        Put("archive/pc/mod/Cool_Mod.archive", "OTHER MAIN");
        var ctx = Ctx();

        var moves = Scanner.ExtraTreeRowsFor(ctx).MovesFor(await Row(ctx, "CoolMod"));
        var text = Scanner.ExtraTreeRowsFor(ctx).TextFor(await Row(ctx, "CoolMod"));

        Assert.Empty(moves.Movable);
        Assert.Equal(new[] { "r6/scripts", "r6/tweaks", Cet }, moves.HeldBack);
        Assert.DoesNotContain("turns these on and off", text.Tooltip);
        Assert.Contains("stay where they are", text.Tooltip);

        await Scanner.DisableModAsync("CoolMod", ctx);
        Assert.True(File.Exists(Path.Combine(GameRoot, "r6", "tweaks", "CoolMod.yaml")));
    }

    [Fact]
    public async Task A_read_only_row_holds_every_tree_back()
    {
        var ctx = Ctx();
        var row = await Row(ctx, "CoolMod");
        row.ReadOnly = true;

        var moves = Scanner.ExtraTreeRowsFor(ctx).MovesFor(row);

        Assert.Empty(moves.Movable);
        Assert.Equal(new[] { "r6/scripts", "r6/tweaks", Cet }, moves.HeldBack);
    }

    [Fact]
    public async Task A_turned_off_row_names_the_trees_held_for_it()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        var ctx = Ctx();
        var row = await Row(ctx, "CoolMod");
        Assert.False(row.Enabled);

        var text = Scanner.ExtraTreeRowsFor(ctx).TextFor(row);

        Assert.Equal($"Also turned off in r6/scripts, r6/tweaks, {Cet}", text.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back.", text.Tooltip);
    }

    [Fact]
    public async Task A_game_with_no_extra_trees_says_nothing()
    {
        var ctx = Ctx(Array.Empty<string>());

        var text = Scanner.ExtraTreeRowsFor(ctx).TextFor(await Row(ctx, "CoolMod"));

        Assert.False(text.Visible);
    }

    [Fact]
    public async Task No_leftover_after_a_clean_round_trip()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        await Scanner.EnableModAsync("CoolMod", Ctx());

        Assert.Null(Scanner.ExtraTreeLeftover(Ctx(), "CoolMod"));
    }

    [Fact]
    public async Task A_file_left_under_an_undeclared_tree_is_reported_with_its_folder()
    {
        await Scanner.DisableModAsync("CoolMod", Ctx());
        var stray = Path.Combine(HeldTrees, "old", "tree", "CoolMod.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        File.WriteAllText(stray, "STRAY");

        var outcome = await Scanner.EnableModWithOutcomeAsync("CoolMod", Ctx());
        Assert.True(outcome.Enabled);

        Assert.Equal(Path.GetFullPath(HeldTrees), Path.GetFullPath(Scanner.ExtraTreeLeftover(Ctx(), "CoolMod")!));
        Assert.True(File.Exists(stray));
    }
}
