using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;
using ModManager.Core.RestorePoints;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests;

/// <summary>
/// The game's own files can never be turned off. On a Bethesda <c>Data</c> game the base masters
/// (Skyrim.esm, Update.esm, ...), the Creation Club content the game's own <c>.ccc</c> list names, and
/// the archives that belong to them all listed as ordinary rows, so Disable All, a loadout, a profile,
/// a single toggle and uninstall moved them to holding and the game would not start. The same held for
/// the base paks of a files-form UE <c>Content/Paks</c> location.
///
/// <para>Every case asserts the base files are byte-identical IN PLACE afterwards, and that the real
/// mod beside them still moves and comes back byte-identical, so the guard can't pass by refusing
/// everything.</para>
/// </summary>
[Collection("McpDataRoot")]
public class BaseGameFilesToggleTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-basefiles-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---------- fixtures ----------

    private static readonly string[] SkyrimBase =
        { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "Skyrim - Textures0.bsa", "ccBGSSSE001-Fish.esm", "ccBGSSSE001-Fish.bsa" };
    private static readonly string[] SkyrimMod = { "MyMod.esp", "MyMod.bsa" };

    private string SkyrimRoot => Path.Combine(_root, "Skyrim Special Edition");
    private string SkyrimData => Path.Combine(SkyrimRoot, "Data");

    private static string Bytes(string file) => "bytes-of-" + file;

    private GameEntry SkyrimGame(bool writeCcc = true)
    {
        Directory.CreateDirectory(SkyrimData);
        foreach (var f in SkyrimBase.Concat(SkyrimMod)) File.WriteAllText(Path.Combine(SkyrimData, f), Bytes(f));
        if (writeCcc) File.WriteAllText(Path.Combine(SkyrimRoot, "Skyrim.ccc"), "ccBGSSSE001-Fish.esm\r\n");
        var game = new GameEntry
        {
            Id = "skyrim-test", GameName = "Skyrim Test", Engine = "bethesda", GameRoot = SkyrimRoot,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-skyrim"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        Directory.CreateDirectory(game.DataDir!);
        return game;
    }

    private void AssertSkyrimBaseInPlace()
    {
        foreach (var f in SkyrimBase)
        {
            var p = Path.Combine(SkyrimData, f);
            Assert.True(File.Exists(p), $"{f} left the Data folder");
            Assert.Equal(Bytes(f), File.ReadAllText(p));
        }
    }

    private void AssertMyMod(bool live)
    {
        foreach (var f in SkyrimMod)
        {
            var p = Path.Combine(SkyrimData, f);
            Assert.Equal(live, File.Exists(p));
            if (live) Assert.Equal(Bytes(f), File.ReadAllText(p));
        }
    }

    private static Mod Row(GameContext c, string name)
        => ModListing.Resolve(c.Game).Single(m => m.Name == name);

    // ---------- bulk paths: skip silently ----------

    [Fact]
    public async Task Disable_all_leaves_the_game_files_and_moves_the_mod()
    {
        var c = Scanner.GameContext(SkyrimGame());
        await Scanner.SetAllModsAsync(false, c);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: false);

        await Scanner.SetAllModsAsync(true, c);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: true);
    }

    [Fact]
    public async Task A_loadout_mode_leaves_the_game_files_and_moves_the_mod()
    {
        var c = Scanner.GameContext(SkyrimGame());
        // Everything single-player, so the multiplayer loadout turns every row off.
        Scanner.SaveClassification(c, (await Scanner.BuildModListAsync(c)).ToDictionary(m => m.Name, _ => "sp"));
        await Scanner.ApplyModeAsync("mp", c);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: false);

        await Scanner.ApplyModeAsync("sp", c);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: true);
    }

    [Fact]
    public async Task A_profile_that_has_everything_off_leaves_the_game_files()
    {
        var c = Scanner.GameContext(SkyrimGame());
        Directory.CreateDirectory(c.ProfilesDir);
        var mods = ModListing.Resolve(c.Game).Select(m => new { name = m.Name, enabled = false }).ToArray();
        File.WriteAllText(Path.Combine(c.ProfilesDir, "off.json"),
            JsonSerializer.Serialize(new { savedAt = "2026-10-02T00:00:00Z", game = "Skyrim Test", mods }));

        var plan = Scanner.ProfilePlan("off", c);
        Assert.DoesNotContain(plan, p => p.Mod.Name is "Skyrim" or "Update" or "Dawnguard" or "Skyrim - Textures0" or "ccBGSSSE001-Fish");
        var failed = await Scanner.ApplyProfilePlanAsync(plan, c);
        Assert.Empty(failed);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: false);
    }

    [Fact]
    public async Task A_hand_built_profile_plan_that_names_a_game_file_skips_it_silently()
    {
        var c = Scanner.GameContext(SkyrimGame());
        var plan = new List<(Mod, bool)> { (Row(c, "Skyrim"), false), (Row(c, "MyMod"), false) };
        var failed = await Scanner.ApplyProfilePlanAsync(plan, c);
        Assert.Empty(failed);
        AssertSkyrimBaseInPlace();
        AssertMyMod(live: false);
    }

    // ---------- explicit paths: refuse in words ----------

    [Fact]
    public async Task A_single_toggle_off_on_a_game_file_refuses_and_moves_nothing()
    {
        var c = Scanner.GameContext(SkyrimGame());
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "Skyrim"), false));
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't turn it off.", ex.Message);
        AssertSkyrimBaseInPlace();

        // The archive and the Creation Club rows are the game's too.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "Skyrim - Textures0"), false));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "ccBGSSSE001-Fish"), false));
        AssertSkyrimBaseInPlace();
    }

    [Fact]
    public async Task A_single_toggle_on_the_real_mod_still_round_trips()
    {
        var c = Scanner.GameContext(SkyrimGame());
        var off = await ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "MyMod"), false);
        Assert.True(off.Applied);
        AssertMyMod(live: false);
        var on = await ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "MyMod"), true);
        Assert.True(on.Applied);
        AssertMyMod(live: true);
        AssertSkyrimBaseInPlace();
    }

    [Fact]
    public async Task Uninstall_refuses_a_game_file_and_deletes_nothing()
    {
        var c = Scanner.GameContext(SkyrimGame());
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => ModUninstall.Run(c, Row(c, "Update")));
        Assert.Equal("Update.esm is part of the game, so 626 won't remove it.", ex.Message);
        var ex2 = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Scanner.UninstallModAsync("Update", c));
        Assert.Equal("Update.esm is part of the game, so 626 won't remove it.", ex2.Message);
        AssertSkyrimBaseInPlace();
    }

    [Fact]
    public void Safe_clear_vanilla_never_plans_a_game_file_even_one_626_recorded()
    {
        var game = SkyrimGame();
        var c = Scanner.GameContext(game);
        // Creation Club content dropped in through 626 has an install record, which is what lets vanilla
        // turn a row off in a Data folder. The record must not outrank the game's own list.
        ModInstallRegistry.Save(c.DataDir, new ModInstallManifest("cc-fish", "ccBGSSSE001-Fish.zip", "mods",
            new[] { "ccBGSSSE001-Fish.esm", "ccBGSSSE001-Fish.bsa" }, DateTime.UtcNow.AddMinutes(1)));
        ModInstallRegistry.Save(c.DataDir, new ModInstallManifest("mymod", "MyMod.zip", "mods",
            SkyrimMod, DateTime.UtcNow.AddMinutes(1)));

        var plan = RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c));
        Assert.Contains(plan, p => p.Name == "MyMod");
        Assert.DoesNotContain(plan, p => p.Name is "ccBGSSSE001-Fish" or "Skyrim" or "Update" or "Dawnguard" or "Skyrim - Textures0");
    }

    // ---------- Fallout 4 ----------

    [Fact]
    public async Task Fallout_4_masters_archives_and_creation_club_stay_in_place()
    {
        var root = Path.Combine(_root, "Fallout 4");
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        string[] baseFiles =
        {
            "Fallout4.esm", "DLCRobot.esm", "Fallout4 - Meshes.ba2", "DLCRobot - Main.ba2",
            "ccBGSFO4044-HellfirePowerArmor.esl", "ccBGSFO4044-HellfirePowerArmor - Main.ba2",
        };
        foreach (var f in baseFiles.Append("MyFo4Mod.esp")) File.WriteAllText(Path.Combine(data, f), Bytes(f));
        File.WriteAllText(Path.Combine(root, "Fallout4.ccc"), "ccBGSFO4044-HellfirePowerArmor.esl\n");
        var game = new GameEntry
        {
            Id = "fo4-test", GameName = "Fallout 4 Test", Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa", "ba2" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-fo4"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        var c = Scanner.GameContext(game);

        await Scanner.SetAllModsAsync(false, c);
        foreach (var f in baseFiles) Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(data, f)));
        Assert.False(File.Exists(Path.Combine(data, "MyFo4Mod.esp")));

        await Scanner.SetAllModsAsync(true, c);
        Assert.Equal(Bytes("MyFo4Mod.esp"), File.ReadAllText(Path.Combine(data, "MyFo4Mod.esp")));
    }

    // ---------- users already hit ----------

    [Fact]
    public async Task A_game_file_already_held_lists_turns_back_on_and_then_refuses_off()
    {
        var game = SkyrimGame();
        var c = Scanner.GameContext(game);
        // What an older build left behind: Skyrim.esm moved into holding by Disable All.
        var held = Path.Combine(c.DisabledRoot, "Skyrim");
        Directory.CreateDirectory(held);
        File.Move(Path.Combine(SkyrimData, "Skyrim.esm"), Path.Combine(held, "Skyrim.esm"));
        File.WriteAllText(Path.Combine(held, "meta.json"),
            "{\"location\":\"mods\",\"hadOnServer\":{},\"disabledAt\":\"2026-10-01T00:00:00Z\",\"isFolder\":false}");

        var row = Row(c, "Skyrim");
        Assert.False(row.Enabled);

        var on = await ModToggle.SetEnabledWithOutcomeAsync(c, row, true);
        Assert.True(on.Applied);
        AssertSkyrimBaseInPlace();

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "Skyrim"), false));
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't turn it off.", ex.Message);
        AssertSkyrimBaseInPlace();
    }

    // ---------- ccc missing / unreadable ----------

    [Fact]
    public async Task Without_a_ccc_list_the_static_masters_still_hold_and_nothing_throws()
    {
        var c = Scanner.GameContext(SkyrimGame(writeCcc: false));
        await Scanner.SetAllModsAsync(false, c);
        foreach (var f in new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "Skyrim - Textures0.bsa" })
            Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(SkyrimData, f)));
        // Unlisted Creation Club content is an ordinary row when the game's list is gone.
        Assert.False(File.Exists(Path.Combine(SkyrimData, "ccBGSSSE001-Fish.esm")));
        AssertMyMod(live: false);
    }

    [Fact]
    public async Task An_unreadable_ccc_list_is_treated_as_empty()
    {
        var game = SkyrimGame(writeCcc: false);
        // A folder where the list should be: reading it throws.
        Directory.CreateDirectory(Path.Combine(SkyrimRoot, "Skyrim.ccc"));
        var c = Scanner.GameContext(game);
        Assert.NotEmpty(ModListing.Resolve(game));
        await Scanner.SetAllModsAsync(false, c);
        Assert.Equal(Bytes("Skyrim.esm"), File.ReadAllText(Path.Combine(SkyrimData, "Skyrim.esm")));
        AssertMyMod(live: false);
    }

    // ---------- UE files-form Content/Paks ----------

    [Fact]
    public async Task Files_form_content_paks_keeps_base_paks_and_still_toggles_a_windows_named_mod()
    {
        var root = Path.Combine(_root, "UeGame");
        var paks = Path.Combine(root, "UeGame", "Content", "Paks");
        Directory.CreateDirectory(paks);
        string[] baseFiles = { "pakchunk0-Windows.pak", "pakchunk0-Windows.utoc", "pakchunk0-Windows.ucas", "global.utoc", "global.ucas" };
        foreach (var f in baseFiles.Append("BetterHUD-WindowsNoEditor.pak")) File.WriteAllText(Path.Combine(paks, f), Bytes(f));
        var game = new GameEntry
        {
            Id = "ue-test", GameName = "UE Test", Engine = "ue-pak", GameRoot = root,
            FileExtensions = new[] { "pak", "ucas", "utoc" }, GroupingRule = "strip_underscore_p_suffix",
            DataDir = Path.Combine(_root, "data-ue"),
            ModLocations = new[] { new ModLocation("mods", "Paks", "UeGame/Content/Paks") },
        };
        var c = Scanner.GameContext(game);

        await Scanner.SetAllModsAsync(false, c);
        foreach (var f in baseFiles) Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(paks, f)));
        Assert.False(File.Exists(Path.Combine(paks, "BetterHUD-WindowsNoEditor.pak")));

        // BetterHUD is a normal, toggleable row: a broad "-Windows" rule would have hidden it.
        var hud = Row(c, "BetterHUD-WindowsNoEditor");
        Assert.False(hud.Enabled);
        var on = await ModToggle.SetEnabledWithOutcomeAsync(c, hud, true);
        Assert.True(on.Applied);
        Assert.Equal(Bytes("BetterHUD-WindowsNoEditor.pak"), File.ReadAllText(Path.Combine(paks, "BetterHUD-WindowsNoEditor.pak")));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "pakchunk0-Windows"), false));
        foreach (var f in baseFiles) Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(paks, f)));
    }

    // ---------- MCP ----------

    [Fact]
    public async Task Mcp_set_mod_enabled_off_on_a_game_file_returns_the_refusal()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        var game = SkyrimGame();
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), game));

        var result = JsonSerializer.SerializeToElement(await WriteTools.SetModEnabled(game.Id!, "Skyrim", enabled: false));
        Assert.False(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Equal("Skyrim.esm is part of the game, so 626 won't turn it off.", result.GetProperty("detail").GetString());
        AssertSkyrimBaseInPlace();
    }

    // ---------- fix round: real mods whose names start with a game file's stem ----------

    private static async Task AssertRoundTrips(GameContext c, string row, string dir, params string[] files)
    {
        var m = ModListing.Resolve(c.Game).Single(x => x.Name == row);
        Assert.False(m.IsBase, $"{row} was marked as the game's");
        Assert.True((await ModToggle.SetEnabledWithOutcomeAsync(c, m, false)).Applied);
        foreach (var f in files) Assert.False(File.Exists(Path.Combine(dir, f)), $"{f} stayed");
        Assert.True((await ModToggle.SetEnabledWithOutcomeAsync(c, ModListing.Resolve(c.Game).Single(x => x.Name == row), true)).Applied);
        foreach (var f in files) Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(dir, f)));
    }

    [Fact]
    public async Task Mods_named_after_a_game_stem_still_toggle_and_archive_only_game_rows_stay_base()
    {
        var game = SkyrimGame();
        string[] extra = { "Dawnguard - Fixes.esp", "Dawnguard - Fixes.bsa", "Update - Patch.esp", "Update - Patch.bsa",
                           "Fallout - Armor.esp", "Fallout - Armor.bsa" };
        foreach (var f in extra) File.WriteAllText(Path.Combine(SkyrimData, f), Bytes(f));
        var c = Scanner.GameContext(game);

        await AssertRoundTrips(c, "Dawnguard - Fixes", SkyrimData, "Dawnguard - Fixes.esp", "Dawnguard - Fixes.bsa");
        await AssertRoundTrips(c, "Update - Patch", SkyrimData, "Update - Patch.esp", "Update - Patch.bsa");
        await AssertRoundTrips(c, "Fallout - Armor", SkyrimData, "Fallout - Armor.esp", "Fallout - Armor.bsa");
        Assert.True(Row(c, "Skyrim - Textures0").IsBase);
        AssertSkyrimBaseInPlace();
    }

    [Fact]
    public async Task A_bare_stem_archive_beside_a_mod_plugin_toggles_with_it()
    {
        var root = Path.Combine(_root, "NoMaster");
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in new[] { "Skyrim.esp", "Skyrim.bsa", "Skyrim - Textures0.bsa" }) File.WriteAllText(Path.Combine(data, f), Bytes(f));
        var game = new GameEntry
        {
            Id = "nomaster", GameName = "No Master", Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-nomaster"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        var c = Scanner.GameContext(game);
        await AssertRoundTrips(c, "Skyrim", data, "Skyrim.esp", "Skyrim.bsa");
        Assert.True(Row(c, "Skyrim - Textures0").IsBase);
    }

    [Fact]
    public async Task New_vegas_base_archives_stay_and_a_fallout_named_mod_toggles()
    {
        var root = Path.Combine(_root, "FalloutNV");
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in new[] { "FalloutNV.esm", "Fallout - Meshes.bsa", "Fallout - Armor.esp", "Fallout - Armor.bsa" })
            File.WriteAllText(Path.Combine(data, f), Bytes(f));
        var game = new GameEntry
        {
            Id = "fnv", GameName = "FNV", Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-fnv"),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        var c = Scanner.GameContext(game);
        Assert.True(Row(c, "Fallout - Meshes").IsBase);
        await AssertRoundTrips(c, "Fallout - Armor", data, "Fallout - Armor.esp", "Fallout - Armor.bsa");
        await Scanner.SetAllModsAsync(false, c);
        Assert.Equal(Bytes("Fallout - Meshes.bsa"), File.ReadAllText(Path.Combine(data, "Fallout - Meshes.bsa")));
        Assert.Equal(Bytes("FalloutNV.esm"), File.ReadAllText(Path.Combine(data, "FalloutNV.esm")));
    }

    [Fact]
    public async Task The_refusal_names_the_rows_game_plugin_not_its_archive()
    {
        var c = Scanner.GameContext(SkyrimGame());
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(c, "ccBGSSSE001-Fish"), false));
        Assert.Equal("ccBGSSSE001-Fish.esm is part of the game, so 626 won't turn it off.", ex.Message);
        var why = ModUninstall.Refusal(c, Row(c, "ccBGSSSE001-Fish"));
        Assert.Equal("ccBGSSSE001-Fish.esm is part of the game, so 626 won't remove it.", why!.Message);
        var ex2 = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Scanner.UninstallModAsync("ccBGSSSE001-Fish", c));
        Assert.Equal("ccBGSSSE001-Fish.esm is part of the game, so 626 won't remove it.", ex2.Message);
    }

    // ---------- review round: Skyrim VR's archive, Oblivion's DLC plugins ----------

    private (GameContext C, string Data) Bethesda(string id, params string[] files)
    {
        var root = Path.Combine(_root, id);
        var data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        foreach (var f in files) File.WriteAllText(Path.Combine(data, f), Bytes(f));
        var game = new GameEntry
        {
            Id = id, GameName = id, Engine = "bethesda", GameRoot = root,
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" }, GroupingRule = "filename_no_ext",
            DataDir = Path.Combine(_root, "data-" + id),
            ModLocations = new[] { new ModLocation("mods", "Data", "Data") },
        };
        return (Scanner.GameContext(game), data);
    }

    [Fact]
    public void Skyrim_vr_main_archive_alone_is_the_games()
    {
        var (c, _) = Bethesda("skyrimvr", "SkyrimVR.esm", "Skyrim_VR - Main.bsa", "MyMod.esp");
        Assert.True(Row(c, "Skyrim_VR - Main").IsBase);
        Assert.True(Row(c, "SkyrimVR").IsBase);
        Assert.False(Row(c, "MyMod").IsBase);
    }

    [Fact]
    public async Task Oblivion_dlc_plugins_and_their_archives_are_the_games_beside_oblivion_esm()
    {
        var (c, data) = Bethesda("oblivion", "Oblivion.esm", "Knights.esp", "Knights.bsa", "DLCShiveringIsles.esp",
            "DLCShiveringIsles - Meshes.bsa", "DLCHorseArmor.esp", "DLCHorseArmor.bsa", "MyMod.esp");
        foreach (var r in new[] { "Knights", "DLCShiveringIsles", "DLCShiveringIsles - Meshes", "DLCHorseArmor" })
            Assert.True(Row(c, r).IsBase, r);
        Assert.False(Row(c, "MyMod").IsBase);

        await Scanner.SetAllModsAsync(false, c);
        foreach (var f in new[] { "Knights.esp", "Knights.bsa", "DLCShiveringIsles.esp", "DLCShiveringIsles - Meshes.bsa" })
            Assert.Equal(Bytes(f), File.ReadAllText(Path.Combine(data, f)));
        Assert.False(File.Exists(Path.Combine(data, "MyMod.esp")));
    }

    [Fact]
    public async Task A_skyrim_mod_named_like_an_oblivion_dlc_is_a_normal_row()
    {
        var (c, data) = Bethesda("skyrim-knights", "Skyrim.esm", "Knights.esp", "Knights.bsa", "DLCShiveringIsles - Meshes.bsa");
        Assert.False(Row(c, "DLCShiveringIsles - Meshes").IsBase);
        await AssertRoundTrips(c, "Knights", data, "Knights.esp", "Knights.bsa");
    }
}
