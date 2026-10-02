using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two, selection only: which extra-tree entries may move with a mod. Nothing here moves
/// anything; the toggle consumes <see cref="ModTrees.MovableFor"/>.
/// </summary>
public class ModTreesToggleTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-modtrees-toggle-");
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks", "red4ext/plugins" };
    private static readonly string[] None = Array.Empty<string>();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Dir(string rel) => Directory.CreateDirectory(Path.Combine(_root, rel));
    private void File_(string rel) { Dir(Path.GetDirectoryName(rel)!); File.WriteAllText(Path.Combine(_root, rel), "x"); }
    private static bool NotOwned(string _) => false;

    [Fact]
    public void Movable_entries_come_back_in_manifest_order()
    {
        Dir("red4ext/plugins/CoolMod");
        Dir("r6/scripts/CoolMod");
        File_("r6/tweaks/CoolMod.yaml");

        var moves = ModTrees.Build(_root, Trees).MovableFor("CoolMod", None, NotOwned);

        Assert.Equal(new[] { "r6/scripts", "r6/tweaks", "red4ext/plugins" }, moves.Movable.Select(m => m.Tree));
        Assert.Empty(moves.HeldBack);
        Assert.All(moves.Movable, m => Assert.True(Path.IsPathRooted(m.AbsPath)));
    }

    [Fact]
    public void Folder_and_file_entries_with_the_same_key_are_both_returned()
    {
        Dir("r6/scripts/CoolMod");
        File_("r6/tweaks/CoolMod.yaml");

        var moves = ModTrees.Build(_root, Trees).MovableFor("CoolMod", None, NotOwned);

        Assert.Equal(new[] { "CoolMod", "CoolMod.yaml" }, moves.Movable.Select(m => m.EntryName));
        Assert.True(Directory.Exists(moves.Movable[0].AbsPath));
        Assert.True(File.Exists(moves.Movable[1].AbsPath));
    }

    [Fact]
    public void Two_claimants_of_one_key_move_nothing_and_hold_every_tree_back()
    {
        Dir("r6/scripts/CoolMod");
        File_("r6/tweaks/CoolMod.yaml");

        var moves = ModTrees.Build(_root, Trees).MovableFor("Cool_Mod", new[] { "CoolMod" }, NotOwned);

        Assert.Empty(moves.Movable);
        Assert.Equal(new[] { "r6/scripts", "r6/tweaks" }, moves.HeldBack);
    }

    [Fact]
    public void An_entry_holding_another_declared_tree_is_held_back()
    {
        // "red4ext" is a mod's name here, and it also holds the declared tree red4ext/plugins.
        Dir("red4ext/plugins/Other");
        Dir("r6/scripts/red4ext/plugins");
        Dir("r6/scripts/Real");
        var trees = new[] { "r6/scripts", "red4ext/plugins", "r6" };

        var moves = ModTrees.Build(_root, trees).MovableFor("scripts", None, NotOwned);

        Assert.Empty(moves.Movable);
        Assert.Equal(new[] { "r6" }, moves.HeldBack);
    }

    [Fact]
    public void An_entry_holding_one_of_the_games_own_mod_folders_never_moves()
    {
        // Build already skips a tree that holds an own folder, so the entry is not even recorded; the
        // entry-level rule is the second line of defence. Either way, nothing moves.
        Dir("r6/scripts/CoolMod/inner");
        var own = new[] { Path.Combine(_root, "r6", "scripts", "CoolMod", "inner") };

        var moves = ModTrees.Build(_root, Trees, own).MovableFor("CoolMod", None, NotOwned);

        Assert.Empty(moves.Movable);
    }

    [Fact]
    public void A_tool_owned_tree_is_held_back_and_the_others_still_move()
    {
        Dir("r6/scripts/CoolMod");
        Dir("red4ext/plugins/CoolMod");
        var ownedDir = Path.GetFullPath(Path.Combine(_root, "r6", "scripts"));

        var moves = ModTrees.Build(_root, Trees).MovableFor("CoolMod", None,
            dir => string.Equals(dir, ownedDir, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { "red4ext/plugins" }, moves.Movable.Select(m => m.Tree));
        Assert.Equal(new[] { "r6/scripts" }, moves.HeldBack);
    }

    [Fact]
    public void A_framework_folder_with_no_row_is_never_returned()
    {
        Dir("red4ext/plugins/ArchiveXL");
        Dir("r6/scripts/CoolMod");
        var trees = ModTrees.Build(_root, Trees);

        // The mod rows are CoolMod and others; none is named ArchiveXL, so nothing asks for the folder.
        var moves = trees.MovableFor("CoolMod", new[] { "OtherMod" }, NotOwned);

        Assert.DoesNotContain(moves.Movable, m => m.EntryName == "ArchiveXL");
        Assert.Equal(new[] { "CoolMod" }, moves.Movable.Select(m => m.EntryName));
        Assert.Empty(trees.MovableFor("Unrelated", None, NotOwned).Movable);
    }

    [Fact]
    public void Unknown_blank_and_empty_inputs_return_nothing()
    {
        Dir("r6/scripts/CoolMod");
        var trees = ModTrees.Build(_root, Trees);

        Assert.Empty(trees.MovableFor("Nope", None, NotOwned).Movable);
        Assert.Empty(trees.MovableFor(null, None, NotOwned).Movable);
        Assert.Empty(ModTrees.Empty.MovableFor("CoolMod", None, NotOwned).Movable);
    }

    [Fact]
    public void A_context_carries_the_trees_it_was_given_and_none_by_default()
    {
        var game = new GameEntry { Id = "no-such-game-in-any-feed", GameRoot = _root };

        Assert.Null(Scanner.GameContext(game).ExtraModTrees);
        Assert.Equal(Trees, Scanner.GameContext(game, extraModTrees: Trees).ExtraModTrees);
    }
}
