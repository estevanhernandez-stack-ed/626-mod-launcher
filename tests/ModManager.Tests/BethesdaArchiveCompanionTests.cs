using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Fallout 4 and Starfield mods ship their assets as <c>&lt;Plugin&gt; - Main.ba2</c> / <c>&lt;Plugin&gt; - Textures.ba2</c>
/// beside <c>&lt;Plugin&gt;.esp</c>. The Bethesda preset declares esp/esl/esm/bsa, so a <c>.ba2</c> was never listed
/// and never moved with its plugin: turning a mod off left its archives live. A <c>.ba2</c> is now a companion of
/// the plugin it is named for (same shape as the A3 sidecars), and the base game's own archives are game files.
///
/// <para>Skyrim is pinned unchanged: its <c>.bsa</c> is a declared extension, so <c>Foo.bsa</c> already shares
/// <c>Foo.esp</c>'s row and <c>Foo - Textures.bsa</c> is already a row of its own.</para>
///
/// <para>Fixtures only. No real game folder is ever read or written.</para>
/// </summary>
public class BethesdaArchiveCompanionTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-ba2-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static string Bytes(string file) => "bytes-of-" + file;

    private (string data, GameContext c) Game(string folder, string[] exts, params string[] files)
    {
        var gameRoot = Path.Combine(_root, folder);
        var data = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in files) File.WriteAllText(Path.Combine(data, f), Bytes(f));
        var game = new GameEntry
        {
            Id = folder.ToLowerInvariant().Replace(' ', '-'), GameName = folder, Engine = "bethesda", GameRoot = gameRoot,
            FileExtensions = exts, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-" + folder),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        Directory.CreateDirectory(game.DataDir!);
        return (data, Scanner.GameContext(game));
    }

    private static readonly string[] Fo4Exts = { "esp", "esl", "esm", "bsa" };

    private static async Task<Mod> Row(GameContext c, string name)
        => (await Scanner.BuildModListAsync(c)).Single(m => m.Name == name);

    private static void AssertLive(string data, IEnumerable<string> files, bool live)
    {
        foreach (var f in files)
        {
            var p = Path.Combine(data, f);
            Assert.Equal(live, File.Exists(p));
            if (live) Assert.Equal(Bytes(f), File.ReadAllText(p));
        }
    }

    private static List<string> Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToList();

    // ---------- the mod: archives ride with the plugin ----------

    private static readonly string[] MyModFiles = { "MyMod.esp", "MyMod - Main.ba2", "MyMod - Textures.ba2" };

    [Fact]
    public async Task Fo4_archives_are_companions_of_their_plugin_not_rows_of_their_own()
    {
        var (_, c) = Game("Fallout 4", Fo4Exts, MyModFiles);

        var one = Assert.Single(await Scanner.BuildModListAsync(c));

        Assert.Equal("MyMod", one.Name);
        Assert.Equal(Sorted(MyModFiles), Sorted(one.Files));
        Assert.False(one.IsBase);
    }

    [Fact]
    public async Task Fo4_mod_turns_off_and_on_with_its_archives_byte_identical()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, MyModFiles);

        await Scanner.DisableModAsync("MyMod", c);

        AssertLive(data, MyModFiles, live: false);
        foreach (var f in MyModFiles)
            Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(c.DisabledRoot, "MyMod", f)));
        var held = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.False(held.Enabled);
        Assert.Equal(3, held.Files.Count);

        await Scanner.EnableModAsync("MyMod", c);

        AssertLive(data, MyModFiles, live: true);
        Assert.True(Assert.Single(await Scanner.BuildModListAsync(c)).Enabled);
    }

    [Fact]
    public async Task Fo4_uninstall_removes_the_archives_too()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, MyModFiles.Append("Other.esp").ToArray());

        await Scanner.UninstallModAsync("MyMod", c);

        AssertLive(data, MyModFiles, live: false);
        Assert.True(File.Exists(Path.Combine(data, "Other.esp")));
    }

    [Fact]
    public async Task Pairing_is_case_insensitive()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, "MyMod.esp", "MYMOD - main.BA2", "mymod - Textures.ba2");

        var one = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal(3, one.Files.Count);

        await Scanner.DisableModAsync("MyMod", c);
        Assert.Empty(Directory.GetFiles(data));
    }

    [Theory]
    [InlineData("esp")]
    [InlineData("esm")]
    [InlineData("esl")]
    public async Task Every_plugin_extension_pairs(string ext)
    {
        var (_, c) = Game("Fallout 4", Fo4Exts, $"Thing.{ext}", "Thing - Main.ba2");
        var one = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal(2, one.Files.Count);
    }

    [Fact]
    public async Task A_bare_stem_archive_pairs_too()
    {
        var (_, c) = Game("Fallout 4", Fo4Exts, "Thing.esp", "Thing.ba2");
        Assert.Equal(2, Assert.Single(await Scanner.BuildModListAsync(c)).Files.Count);
    }

    [Fact]
    public async Task A_similarly_prefixed_name_does_not_claim_the_archive()
    {
        // "Cool" must not adopt "CoolExtra - Main.ba2": the stem is followed by " - " or the extension, never a bare prefix.
        var (_, c) = Game("Fallout 4", Fo4Exts, "Cool.esp", "CoolExtra.esp", "CoolExtra - Main.ba2");

        var mods = (await Scanner.BuildModListAsync(c)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "Cool.esp" }, mods[0].Files);
        Assert.Equal(new[] { "CoolExtra - Main.ba2", "CoolExtra.esp" }, Sorted(mods[1].Files));
    }

    [Fact]
    public async Task The_longest_matching_plugin_owns_the_archive()
    {
        var (_, c) = Game("Fallout 4", Fo4Exts, "MyMod.esp", "MyMod - Plus.esp", "MyMod - Main.ba2", "MyMod - Plus - Main.ba2");

        var mods = (await Scanner.BuildModListAsync(c)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "MyMod", "MyMod - Plus" }, mods.Select(m => m.Name));
        Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod.esp" }, Sorted(mods[0].Files));
        Assert.Equal(new[] { "MyMod - Plus - Main.ba2", "MyMod - Plus.esp" }, Sorted(mods[1].Files));
    }

    [Fact]
    public async Task A_shorter_live_plugin_does_not_capture_the_archive_of_a_longer_plugin_in_holding()
    {
        // Upgrade path: an older build held "MyMod - Plus.esp" alone and left its archive live. With the
        // longer plugin out of the folder, live MyMod.esp matches "MyMod - Plus - Main.ba2" by prefix.
        var (data, c) = Game("Fallout 4", Fo4Exts, "MyMod.esp", "MyMod - Plus - Main.ba2");
        var held = Path.Combine(c.DisabledRoot, "MyMod - Plus");
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "MyMod - Plus.esp"), Bytes("MyMod - Plus.esp"));
        File.WriteAllText(Path.Combine(held, "meta.json"), "{\"location\":\"mods\",\"hadOnServer\":{},\"isFolder\":false}");

        var mods = await Scanner.BuildModListAsync(c);

        // The archive is nobody's live file: not in MyMod's row, not a row of its own (hidden behind the held row).
        Assert.Equal(new[] { "MyMod.esp" }, mods.Single(m => m.Name == "MyMod").Files);
        Assert.DoesNotContain(mods, m => m.Files.Contains("MyMod - Plus - Main.ba2"));
        var plus = mods.Single(m => m.Name == "MyMod - Plus");
        Assert.False(plus.Enabled);

        await Scanner.DisableModAsync("MyMod", c);
        Assert.True(File.Exists(Path.Combine(data, "MyMod - Plus - Main.ba2")));
        await Scanner.EnableModAsync("MyMod", c);

        await Scanner.UninstallModAsync("MyMod", c);
        Assert.True(File.Exists(Path.Combine(data, "MyMod - Plus - Main.ba2")));
        Assert.True(File.Exists(Path.Combine(held, "MyMod - Plus.esp")));

        await Scanner.EnableModAsync("MyMod - Plus", c);
        Assert.Equal(Bytes("MyMod - Plus.esp"), File.ReadAllText(Path.Combine(data, "MyMod - Plus.esp")));
        var back = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal(new[] { "MyMod - Plus - Main.ba2", "MyMod - Plus.esp" }, Sorted(back.Files));
    }

    [Fact]
    public async Task Plugins_whose_stems_differ_only_in_case_do_not_both_claim_one_archive()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, "Foo.esp", "FOO.esm", "Foo - Main.ba2");

        var mods = await Scanner.BuildModListAsync(c);

        Assert.Single(mods, m => m.Files.Contains("Foo - Main.ba2"));
        Assert.Equal(1, mods.Sum(m => m.Files.Count(f => f.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase))));

        // The ordinal-smallest stem owns it; uninstalling the other row must not take it.
        Assert.Equal("FOO", mods.Single(m => m.Files.Contains("Foo - Main.ba2")).Name);
        await Scanner.UninstallModAsync("Foo", c);
        AssertLive(data, new[] { "Foo - Main.ba2", "FOO.esm" }, live: true);
        Assert.False(File.Exists(Path.Combine(data, "Foo.esp")));
    }

    [Fact]
    public async Task Two_plugins_of_one_stem_list_the_archive_once()
    {
        var (_, c) = Game("Fallout 4", Fo4Exts, "Thing.esp", "Thing.esm", "Thing - Main.ba2");
        var one = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal(1, one.Files.Count(f => f.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task An_archive_with_no_plugin_is_one_togglable_row_named_for_its_owner()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, "Retex - Main.ba2", "Retex - Textures.ba2");

        var one = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal("Retex", one.Name);
        Assert.False(one.IsBase);

        await Scanner.DisableModAsync("Retex", c);
        Assert.Empty(Directory.GetFiles(data));
        await Scanner.EnableModAsync("Retex", c);
        AssertLive(data, new[] { "Retex - Main.ba2", "Retex - Textures.ba2" }, live: true);
    }

    [Fact]
    public async Task A_leftover_archive_does_not_hide_a_held_copy_of_the_same_name()
    {
        // An older build disabled MyMod.esp alone and left its archive live. The held row keeps its name.
        var (data, c) = Game("Fallout 4", Fo4Exts, "MyMod.esp", "Other.esp");
        Directory.CreateDirectory(Path.Combine(c.DisabledRoot, "MyMod"));
        File.Move(Path.Combine(data, "MyMod.esp"), Path.Combine(c.DisabledRoot, "MyMod", "MyMod.esp"));
        File.WriteAllText(Path.Combine(data, "MyMod - Main.ba2"), "x");

        var mods = await Scanner.BuildModListAsync(c);

        var my = Assert.Single(mods, m => m.Name == "MyMod");
        Assert.False(my.Enabled);
        Assert.Equal(new[] { "MyMod.esp" }, my.Files);
    }

    // ---------- the game's own archives ----------

    private static readonly string[] Fo4Base =
    {
        "Fallout4.esm", "Fallout4 - Textures1.ba2", "Fallout4 - Meshes.ba2",
        "DLCRobot.esm", "DLCRobot - Main.ba2", "DLCRobot - Textures.ba2",
        "ccBGSFO4001-PipBoy(Black).esl", "ccBGSFO4001-PipBoy(Black) - Main.ba2",
    };

    private (string data, GameContext c) Fo4WithBase()
    {
        var (data, c) = Game("Fallout 4", Fo4Exts, Fo4Base.Concat(MyModFiles).ToArray());
        File.WriteAllText(Path.Combine(c.GameRoot, "Fallout4.ccc"), "ccBGSFO4001-PipBoy(Black).esl\r\n");
        return (data, c);
    }

    [Fact]
    public async Task Fo4_base_archives_ride_with_their_master_and_the_row_is_a_game_file()
    {
        var (_, c) = Fo4WithBase();

        var mods = await Scanner.BuildModListAsync(c);

        Assert.Equal(Sorted(new[] { "DLCRobot", "Fallout4", "MyMod", "ccBGSFO4001-PipBoy(Black)" }), Sorted(mods.Select(m => m.Name)));
        Assert.All(mods.Where(m => m.Name != "MyMod"), m => Assert.True(m.IsBase, m.Name));
        Assert.False(mods.Single(m => m.Name == "MyMod").IsBase);
        Assert.Contains("Fallout4 - Textures1.ba2", mods.Single(m => m.Name == "Fallout4").Files);
        Assert.Contains("DLCRobot - Main.ba2", mods.Single(m => m.Name == "DLCRobot").Files);
        Assert.Contains("ccBGSFO4001-PipBoy(Black) - Main.ba2", mods.Single(m => m.Name == "ccBGSFO4001-PipBoy(Black)").Files);
    }

    [Fact]
    public async Task Fo4_disable_all_leaves_every_base_archive_in_place_and_moves_the_mod()
    {
        var (data, c) = Fo4WithBase();

        await Scanner.SetAllModsAsync(false, c);

        AssertLive(data, Fo4Base, live: true);
        AssertLive(data, MyModFiles, live: false);

        await Scanner.SetAllModsAsync(true, c);

        AssertLive(data, Fo4Base, live: true);
        AssertLive(data, MyModFiles, live: true);
    }

    [Fact]
    public async Task Fo4_explicit_turn_off_of_a_base_row_refuses_and_moves_nothing()
    {
        var (data, c) = Fo4WithBase();

        var row = await Row(c, "Fallout4");
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ModToggle.SetEnabledWithOutcomeAsync(c, row, false));

        Assert.Equal("Fallout4.esm is part of the game, so 626 won't turn it off.", ex.Message);
        AssertLive(data, Fo4Base, live: true);
    }

    [Fact]
    public async Task Fo4_uninstall_of_a_base_row_refuses_and_deletes_nothing()
    {
        var (data, c) = Fo4WithBase();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Scanner.UninstallModAsync("Fallout4", c));

        AssertLive(data, Fo4Base, live: true);
    }

    [Fact]
    public async Task An_orphaned_base_archive_is_still_the_games()
    {
        // The DLC master is gone but its archive is not: judged by the file, it is the game's.
        var (data, c) = Game("Fallout 4", Fo4Exts, "Fallout4.esm", "DLCCoast - Main.ba2");

        var coast = await Row(c, "DLCCoast");
        Assert.True(coast.IsBase);
        await Scanner.SetAllModsAsync(false, c);
        AssertLive(data, new[] { "Fallout4.esm", "DLCCoast - Main.ba2" }, live: true);
    }

    private static readonly string[] StarfieldBase =
    {
        "Starfield.esm", "Starfield - Textures01.ba2", "Starfield - Meshes01.ba2",
        "Constellation.esm", "Constellation - Main.ba2",
        "SFBGS003.esm", "SFBGS003 - Main.ba2",
        "BlueprintShips-Starfield.esm", "BlueprintShips-Starfield - Main.ba2",
    };
    private static readonly string[] StarfieldMod = { "StarMod.esm", "StarMod - Main.ba2", "StarMod - Textures.ba2" };

    [Fact]
    public async Task Starfield_mod_moves_with_its_archives_and_the_base_game_stays()
    {
        var (data, c) = Game("Starfield", Fo4Exts, StarfieldBase.Concat(StarfieldMod).ToArray());

        var mods = await Scanner.BuildModListAsync(c);
        Assert.Equal(Sorted(new[] { "BlueprintShips-Starfield", "Constellation", "SFBGS003", "Starfield", "StarMod" }), Sorted(mods.Select(m => m.Name)));
        Assert.All(mods.Where(m => m.Name != "StarMod"), m => Assert.True(m.IsBase, m.Name));
        Assert.False(mods.Single(m => m.Name == "StarMod").IsBase);

        await Scanner.SetAllModsAsync(false, c);
        AssertLive(data, StarfieldBase, live: true);
        AssertLive(data, StarfieldMod, live: false);

        await Scanner.SetAllModsAsync(true, c);
        AssertLive(data, StarfieldBase, live: true);
        AssertLive(data, StarfieldMod, live: true);
    }

    // ---------- Skyrim: unchanged ----------

    [Fact]
    public async Task Skyrim_bsa_of_the_same_stem_still_shares_the_plugins_row()
    {
        var (data, c) = Game("Skyrim", Fo4Exts, "Skyrim.esm", "Skyrim - Textures0.bsa", "MyMod.esp", "MyMod.bsa");

        var my = await Row(c, "MyMod");
        Assert.Equal(new[] { "MyMod.bsa", "MyMod.esp" }, Sorted(my.Files));

        await Scanner.DisableModAsync("MyMod", c);
        Assert.False(File.Exists(Path.Combine(data, "MyMod.bsa")));
        Assert.True(File.Exists(Path.Combine(data, "Skyrim - Textures0.bsa")));
        await Scanner.EnableModAsync("MyMod", c);
        AssertLive(data, new[] { "MyMod.esp", "MyMod.bsa" }, live: true);
    }

    [Fact]
    public async Task Skyrim_dash_suffixed_bsa_is_still_its_own_row()
    {
        // Pinned, not endorsed: today "<Plugin> - Textures.bsa" is a row of its own and stays one.
        var (_, c) = Game("Skyrim", Fo4Exts, "Skyrim.esm", "MyMod.esp", "MyMod - Textures.bsa");

        var mods = await Scanner.BuildModListAsync(c);

        Assert.Equal(Sorted(new[] { "MyMod", "MyMod - Textures", "Skyrim" }), Sorted(mods.Select(m => m.Name)));
        Assert.Equal(new[] { "MyMod.esp" }, mods.Single(m => m.Name == "MyMod").Files);
        Assert.Equal(new[] { "MyMod - Textures.bsa" }, mods.Single(m => m.Name == "MyMod - Textures").Files);
    }

    [Fact]
    public async Task Skyrim_base_archive_rows_stay_archive_only_game_files()
    {
        var (_, c) = Game("Skyrim", Fo4Exts, "Skyrim.esm", "Skyrim - Textures0.bsa", "MyMod.esp");

        var mods = await Scanner.BuildModListAsync(c);

        Assert.True(mods.Single(m => m.Name == "Skyrim - Textures0").IsBase);
        Assert.Equal(new[] { "Skyrim - Textures0.bsa" }, mods.Single(m => m.Name == "Skyrim - Textures0").Files);
        Assert.Equal(new[] { "Skyrim.esm" }, mods.Single(m => m.Name == "Skyrim").Files);
    }
}
