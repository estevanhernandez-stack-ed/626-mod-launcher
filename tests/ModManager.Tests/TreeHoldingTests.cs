using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two: the layout where a mod's extra-tree entries wait while it is off. Pure path logic plus
/// one directory read; nothing here moves a file.
/// </summary>
public class TreeHoldingTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-treeholding-");
    private static readonly string[] Trees = { "r6/scripts", "r6/input", "bin/x64/plugins/cyber_engine_tweaks/mods" };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private GameContext Ctx() => new()
    {
        Game = new GameEntry { Id = "test", GameName = "Test Game", GameRoot = _root },
        GameRoot = _root,
        DataDir = Path.Combine(_root, "_data"),
        DisabledRoot = Path.Combine(_root, "_data", "disabled"),
        ProfilesDir = Path.Combine(_root, "_data", "profiles"),
        SavesDir = Path.Combine(_root, "_data", "saves"),
        ClassificationPath = Path.Combine(_root, "_data", "classification.json"),
        MetadataPath = Path.Combine(_root, "_data", "metadata.json"),
        LoadOrderPath = Path.Combine(_root, "_data", "loadorder.json"),
        DeclaredExts = new[] { "archive" },
        Exts = new[] { "archive" },
        FileRe = new System.Text.RegularExpressions.Regex(".*"),
        Locations = Array.Empty<ModLocationCtx>(),
        GroupingRule = "",
        ScanSubfolders = "",
    };

    private static void Put(string path, bool folder = false)
    {
        if (folder) { Directory.CreateDirectory(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Root_is_a_sibling_of_the_primary_disabled_root()
    {
        var ctx = Ctx();
        Assert.Equal(Path.Combine(ctx.DataDir, "disabled-trees"), TreeHolding.Root(ctx));
        Assert.Equal(Path.GetDirectoryName(ctx.DisabledRoot), Path.GetDirectoryName(TreeHolding.Root(ctx)));
        Assert.NotEqual(ctx.DisabledRoot, TreeHolding.Root(ctx));
        Assert.Equal(Path.Combine(TreeHolding.Root(ctx), "CoolMod"), TreeHolding.ModDir(ctx, "CoolMod"));
    }

    [Fact]
    public void PathFor_makes_each_tree_segment_its_own_folder()
    {
        var ctx = Ctx();
        Assert.Equal(
            Path.Combine(ctx.DataDir, "disabled-trees", "CoolMod", "r6", "scripts", "CoolMod"),
            TreeHolding.PathFor(ctx, "CoolMod", "r6/scripts", "CoolMod"));
        Assert.Equal(
            TreeHolding.PathFor(ctx, "CoolMod", "r6/scripts", "CoolMod"),
            TreeHolding.PathFor(ctx, "CoolMod", "r6\\scripts\\", "CoolMod"));
    }

    [Theory]
    [InlineData("r6/scripts", "CoolMod")]
    [InlineData("bin/x64/plugins/cyber_engine_tweaks/mods", "CoolMod")]
    public void Held_round_trips_what_PathFor_placed(string tree, string entry)
    {
        var ctx = Ctx();
        Put(TreeHolding.PathFor(ctx, "CoolMod", tree, entry), folder: true);
        Put(Path.Combine(TreeHolding.PathFor(ctx, "CoolMod", tree, entry), "init.lua"));

        var held = TreeHolding.Held(ctx, "CoolMod", Trees);

        var one = Assert.Single(held);
        Assert.Equal(tree, one.Tree);
        Assert.Equal(entry, one.EntryName);
        Assert.Equal(TreeHolding.PathFor(ctx, "CoolMod", tree, entry), one.AbsPath);
    }

    [Fact]
    public void Held_recognises_a_file_entry_not_only_folders()
    {
        var ctx = Ctx();
        Put(TreeHolding.PathFor(ctx, "CoolMod", "r6/input", "CoolMod.xml"));

        var one = Assert.Single(TreeHolding.Held(ctx, "CoolMod", Trees));

        Assert.Equal("r6/input", one.Tree);
        Assert.Equal("CoolMod.xml", one.EntryName);
    }

    [Fact]
    public void Held_does_not_read_one_mods_entries_as_anothers()
    {
        var ctx = Ctx();
        Put(TreeHolding.PathFor(ctx, "Other", "r6/scripts", "Other.reds"));
        Assert.Empty(TreeHolding.Held(ctx, "CoolMod", Trees));
    }

    [Fact]
    public void Held_on_a_missing_root_or_mod_yields_nothing()
    {
        var ctx = Ctx();
        Assert.False(Directory.Exists(TreeHolding.Root(ctx)));
        Assert.Empty(TreeHolding.Held(ctx, "CoolMod", Trees));
        Assert.Empty(TreeHolding.Held(ctx, "CoolMod", null));
        Directory.CreateDirectory(TreeHolding.ModDir(ctx, "CoolMod"));
        Assert.Empty(TreeHolding.Held(ctx, "CoolMod", Trees));
    }

    [Fact]
    public void Held_lists_every_declared_tree_in_declared_order()
    {
        var ctx = Ctx();
        Put(TreeHolding.PathFor(ctx, "CoolMod", "r6/input", "CoolMod.xml"));
        Put(TreeHolding.PathFor(ctx, "CoolMod", "r6/scripts", "CoolMod.reds"));

        var held = TreeHolding.Held(ctx, "CoolMod", Trees);

        Assert.Equal(new[] { "r6/scripts", "r6/input" }, held.Select(h => h.Tree));
    }

    [Fact]
    public void HoldsFiles_is_true_only_when_a_file_is_held()
    {
        var ctx = Ctx();
        Assert.False(TreeHolding.HoldsFiles(ctx, "CoolMod"));

        Directory.CreateDirectory(TreeHolding.PathFor(ctx, "CoolMod", "r6/scripts", "CoolMod"));
        Assert.False(TreeHolding.HoldsFiles(ctx, "CoolMod"));   // empty folders hold nothing

        Put(Path.Combine(TreeHolding.PathFor(ctx, "CoolMod", "r6/scripts", "CoolMod"), "a.reds"));
        Assert.True(TreeHolding.HoldsFiles(ctx, "CoolMod"));
    }

    [Fact]
    public void RemoveIfEmpty_never_deletes_a_held_file()
    {
        var ctx = Ctx();
        var file = TreeHolding.PathFor(ctx, "CoolMod", "r6/input", "CoolMod.xml");
        Put(file);

        TreeHolding.RemoveIfEmpty(ctx, "CoolMod");
        Assert.True(File.Exists(file));

        File.Delete(file);
        TreeHolding.RemoveIfEmpty(ctx, "CoolMod");
        Assert.False(Directory.Exists(TreeHolding.ModDir(ctx, "CoolMod")));
    }
}
