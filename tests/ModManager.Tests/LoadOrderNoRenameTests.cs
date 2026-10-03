using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Load order on a Bethesda game must never rename a plugin: Plugins.txt and every plugin's master list
/// name plugins by filename, so a prefixed <c>0010__MyMod.esp</c> is a missing <c>MyMod.esp</c> to the
/// game. And the renames 626 already made (anywhere) have to be undoable from the app, without ever
/// overwriting a file or touching a name 626 did not write.
/// </summary>
public class LoadOrderNoRenameTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-lo-norename-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---------- fixtures ----------

    private (string data, GameContext c) Skyrim(params string[] files)
    {
        var root = Path.Combine(_root, "Skyrim");
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in files) File.WriteAllText(Path.Combine(data, f), "bytes of " + f);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "sky-lo", GameName = "Sky", Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-sky"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        });
        return (data, c);
    }

    private (string mods, GameContext c) Unreal(params string[] files)
    {
        var root = Path.Combine(_root, "Ue");
        var mods = Path.Combine(root, "Game", "Content", "Paks", "~mods");
        Directory.CreateDirectory(mods);
        foreach (var f in files) File.WriteAllText(Path.Combine(mods, f), "bytes of " + f);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "ue-lo", GameName = "Ue", Engine = "ue-pak", GameRoot = root,
            FileExtensions = new[] { "pak", "ucas", "utoc" }, GroupingRule = "strip_underscore_p_suffix",
            DataDir = Path.Combine(_root, "data-ue"),
            ModLocations = new[] { new ModLocation("mods", "mods", "Game/Content/Paks/~mods") },
        });
        return (mods, c);
    }

    private static SortedDictionary<string, string> Snapshot(string dir)
        => new(Directory.GetFiles(dir).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllText), StringComparer.Ordinal);

    // ---------- Bethesda: apply renames nothing ----------

    [Fact]
    public async Task Bethesda_apply_renames_nothing_and_writes_no_loadorder_json()
    {
        var (data, c) = Skyrim("Skyrim.esm", "MyMod.esp", "Other.esp");
        var before = Snapshot(data);

        var result = await Scanner.ApplyLoadOrderAsync(c, new[] { "Other", "MyMod" });

        Assert.Equal(LoadOrderMechanism.NotSupported, result.Mechanism);
        Assert.Equal(LoadOrderSupport.BethesdaReason, result.Reason);
        Assert.Equal(before, Snapshot(data));
        Assert.False(File.Exists(c.LoadOrderPath), "loadorder.json must not be written for a game whose order 626 can't apply");
    }

    // ---------- undo ----------

    [Fact]
    public async Task Undo_round_trips_an_unreal_mods_folder()
    {
        var (mods, c) = Unreal("Cool_P.pak", "Cool_P.ucas", "Cool_P.utoc", "Audio.pak");
        var before = Snapshot(mods);

        await Scanner.ApplyLoadOrderAsync(c, new[] { "Audio", "Cool" });
        Assert.Contains("0010__Audio.pak", Snapshot(mods).Keys);
        Assert.Contains("0020__Cool_P.utoc", Snapshot(mods).Keys);
        Assert.True(File.Exists(c.LoadOrderPath));

        var result = await Scanner.ResetLoadOrderAsync(c);

        Assert.Equal(before, Snapshot(mods));
        Assert.False(File.Exists(c.LoadOrderPath));
        Assert.Equal(4, result.Renamed);
        Assert.True(result.LoadOrderCleared);
        Assert.Equal("Removed 626's prefix from 4 files.", result.Describe());
        Assert.True(Scanner.PlanUndoLoadOrder(c).IsEmpty);
    }

    [Fact]
    public async Task Undo_restores_a_bethesda_plugin_626_already_prefixed()
    {
        var (data, c) = Skyrim("Skyrim.esm", "Other.esp");
        File.WriteAllText(Path.Combine(data, "0010__MyMod.esp"), "bytes of MyMod.esp");

        Assert.Equal("1 plugin carries a load-order prefix from 626.", Scanner.PlanUndoLoadOrder(c).Describe(pluginGame: true));
        var result = await Scanner.ResetLoadOrderAsync(c);

        Assert.Equal("Removed 626's prefix from 1 file.", result.Describe());

        Assert.Equal("bytes of MyMod.esp", File.ReadAllText(Path.Combine(data, "MyMod.esp")));
        Assert.False(File.Exists(Path.Combine(data, "0010__MyMod.esp")));
    }

    [Fact]
    public async Task Undo_never_overwrites_and_keeps_loadorder_json_on_a_collision()
    {
        var (data, c) = Skyrim("Skyrim.esm");
        File.WriteAllText(Path.Combine(data, "0010__MyMod.esp"), "PREFIXED");
        File.WriteAllText(Path.Combine(data, "MyMod.esp"), "PLAIN");
        Directory.CreateDirectory(c.DataDir);
        File.WriteAllText(c.LoadOrderPath, "[\"MyMod\"]");

        var plan = Scanner.PlanUndoLoadOrder(c);
        var result = await Scanner.ResetLoadOrderAsync(c);

        Assert.True(Assert.Single(plan.Items).Collision);
        Assert.Equal(0, result.Renamed);
        Assert.False(result.LoadOrderCleared);
        Assert.Equal("Removed 626's prefix from 0 files. 0010__MyMod.esp was left as is: MyMod.esp already exists.", result.Describe());
        Assert.Equal("PREFIXED", File.ReadAllText(Path.Combine(data, "0010__MyMod.esp")));
        Assert.Equal("PLAIN", File.ReadAllText(Path.Combine(data, "MyMod.esp")));
        Assert.True(File.Exists(c.LoadOrderPath), "a rename that didn't happen means the order isn't undone; keep the record");
    }

    [Fact]
    public async Task Undo_leaves_a_name_626_did_not_write()
    {
        var (mods, c) = Unreal("10__thing.pak", "0010__Mine.pak");

        var plan = Scanner.PlanUndoLoadOrder(c);
        Assert.Equal("0010__Mine.pak", Assert.Single(plan.Items).From);
        Assert.Equal("1 mod file carries a load-order prefix from 626.", plan.Describe(pluginGame: false));
        await Scanner.ResetLoadOrderAsync(c);

        Assert.True(File.Exists(Path.Combine(mods, "10__thing.pak")), "a mod author's 10__ is not 626's 4-digit prefix");
        Assert.False(File.Exists(Path.Combine(mods, "thing.pak")));
        Assert.True(File.Exists(Path.Combine(mods, "Mine.pak")));
    }

    [Fact]
    public async Task Undo_skips_a_vortex_owned_folder()
    {
        var (data, c) = Skyrim("Skyrim.esm");
        File.WriteAllText(Path.Combine(data, "vortex.deployment.x.json"), "{}");
        File.WriteAllText(Path.Combine(data, "0010__MyMod.esp"), "OWNED");

        Assert.True(Scanner.PlanUndoLoadOrder(c).IsEmpty);
        await Scanner.ResetLoadOrderAsync(c);

        Assert.True(File.Exists(Path.Combine(data, "0010__MyMod.esp")));
        Assert.False(File.Exists(Path.Combine(data, "MyMod.esp")));
    }

    // ---------- the rule ----------

    [Fact]
    public void The_rule_refuses_bethesda_and_any_plugin_game()
    {
        var (_, sky) = Skyrim("Skyrim.esm");
        Assert.Equal(LoadOrderMechanism.NotSupported, LoadOrderSupport.For(sky).Mechanism);

        // A Morrowind added as a custom game: no "bethesda" engine id, same Plugins-by-name problem.
        var custom = Scanner.GameContext(new GameEntry
        {
            Id = "mw", GameName = "Mw", Engine = "custom", GameRoot = _root,
            FileExtensions = new[] { "esp", "esm" }, ModLocations = new[] { new ModLocation("mods", "Data Files", "Data Files") },
        });
        Assert.Equal(LoadOrderSupport.BethesdaReason, LoadOrderSupport.For(custom).Reason);
    }

    [Fact]
    public void The_rule_keeps_prefix_rename_for_unreal_and_config_for_me2()
    {
        var (_, ue) = Unreal();
        Assert.Equal(LoadOrderMechanism.PrefixRename, LoadOrderSupport.For(ue).Mechanism);
        Assert.Equal(LoadOrderMechanism.Config, LoadOrderSupport.For(ue, configBacked: true).Mechanism);
        var independent = LoadOrderSupport.For(ue, loadsIndependently: true);
        Assert.Equal(LoadOrderMechanism.NotSupported, independent.Mechanism);
        Assert.Equal(LoadOrderSupport.IndependentReason, independent.Reason);
    }

    [Theory]
    [InlineData("0010__a.pak", true)]
    [InlineData("0990__a.pak", true)]
    [InlineData("10000__a.pak", true)]   // what Prefix writes past the 999th mod
    [InlineData("10__a.pak", false)]
    [InlineData("100__a.pak", false)]
    [InlineData("0011__a.pak", false)]   // Prefix only writes multiples of ten
    [InlineData("0010_a.pak", false)]
    public void Own_prefix_is_exactly_what_Prefix_writes(string name, bool own)
        => Assert.Equal(own, LoadOrderApply.HasOwnPrefix(name));

    [Fact]
    public void Every_prefix_Prefix_writes_is_recognised_as_its_own()
    {
        foreach (var i in new[] { 0, 1, 9, 98, 99, 998, 999, 1500 })
            Assert.True(LoadOrderApply.HasOwnPrefix(LoadOrderApply.Prefix(i) + "x.pak"), LoadOrderApply.Prefix(i));
    }

    [Fact]
    public async Task Undo_renames_a_sidecar_with_its_file()
    {
        var root = Path.Combine(_root, "Cp");
        var mods = Path.Combine(root, "archive", "pc", "mod");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "0010__Foo.archive"), "A");
        File.WriteAllText(Path.Combine(mods, "0010__Foo.archive.xl"), "X");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "cp", GameName = "Cp", Engine = "custom", GameRoot = root, DataDir = Path.Combine(_root, "data-cp"),
            FileExtensions = new[] { "archive" }, ModLocations = new[] { new ModLocation("mods", "mods", "archive/pc/mod") },
        });

        var result = await Scanner.ResetLoadOrderAsync(c);

        Assert.Equal(2, result.Renamed);
        Assert.Equal("A", File.ReadAllText(Path.Combine(mods, "Foo.archive")));
        Assert.Equal("X", File.ReadAllText(Path.Combine(mods, "Foo.archive.xl")));
    }
}
