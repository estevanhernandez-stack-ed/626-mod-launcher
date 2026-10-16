using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;
using ModManager.Core.RestorePoints;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests;

/// <summary>
/// Elden Ring ships its own <c>Game/regulation.bin</c>. The direct-inject catalog matches
/// <c>regulation.bin</c> by NAME alone (a "Modded regulation.bin" row), so the game's untouched file
/// listed as an enabled mod, and every route that turns a mod off (a row toggle, Disable all, Safe Clear's
/// return to vanilla, the agent's <c>set_mod_enabled</c>) MOVED it, leaving the game unable to launch
/// until a restore.
///
/// <para>The file is a mod only with provenance: 626 replaced it (the replace snapshots the original into
/// the replaced store first), or an install record of 626's names it in the play folder. Without either it
/// is the game's own, and the existing game-file guard (<see cref="Mod.IsBase"/>) applies.</para>
///
/// <para>Every case that expects the file to stay asserts its bytes are untouched, and the cases that
/// expect a move assert the file comes back byte-identical, so the guard cannot pass by refusing everything.</para>
/// </summary>
[Collection("McpDataRoot")]
public class RegulationBinProvenanceTests : IDisposable
{
    private const string Reg = "regulation.bin";
    private const string RowName = "Modded regulation.bin";
    private static readonly byte[] Vanilla = { 1, 1, 2, 3, 5, 8 };
    private static readonly byte[] Modded = { 9, 9, 9, 9 };

    private readonly string _root = TestSupport.TempDir("mmb-regbin-");
    private readonly string _savedDataRoot = McpConfig.DataRoot;

    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");
    private string DataDir => Path.Combine(_root, "data");
    private string RegPath => Path.Combine(Play, Reg);

    public RegulationBinProvenanceTests()
    {
        McpConfig.DataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(Play);
        File.WriteAllBytes(RegPath, Vanilla);
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
        // A real mod beside it, so a guard that refuses everything cannot pass.
        File.WriteAllText(Path.Combine(Play, "erss-fg.dll"), "fg");
        File.WriteAllText(Path.Combine(Play, "erss-fg.toml"), "toml");
        RegistryStore.Save(McpConfig.DataRoot, Registry.UpsertGame(Registry.EmptyRegistry(), Game()));
    }

    public void Dispose()
    {
        McpConfig.DataRoot = _savedDataRoot;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // Engine fromsoft without a Mod Engine 2 config is the direct-inject lane. No Steam id, so no ban-risk gate.
    private GameEntry Game() => new()
    {
        Id = "er-reg", GameName = "ER", Engine = "fromsoft", GameRoot = GameRoot, DataDir = DataDir,
    };

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);
    private Mod Row(string name) => ModListing.Resolve(Game()).Single(m => m.Name == name);
    private Mod? MaybeRow(string name) => ModListing.Resolve(Game()).FirstOrDefault(m => m.Name == name);

    private void AssertVanillaInPlace()
    {
        Assert.True(File.Exists(RegPath), "regulation.bin left the game folder");
        Assert.Equal(Vanilla, File.ReadAllBytes(RegPath));
    }

    // 626 replaces the game's regulation.bin through the direct-inject drop: the original is snapshotted
    // into <play>/_626/replaced first, and the dropped file takes its place.
    private void InstallModdedRegulationThrough626()
    {
        var staging = Path.Combine(_root, "drop");
        Directory.CreateDirectory(staging);
        var src = Path.Combine(staging, Reg);
        File.WriteAllBytes(src, Modded);
        var plan = DirectInject.Plan(Play, new[] { src });
        Assert.Single(plan.Collisions);
        var result = DirectInject.Execute(Play, DirectInject.ReplacedRoot(Play), plan, new HashSet<string>(new[] { Reg }));
        Assert.Contains(Reg, result.Updated);
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));
    }

    // ---------- the listing: the game's own file is the game's ----------

    [Fact]
    public void An_unclaimed_regulation_bin_lists_as_the_games_own()
    {
        var row = Row(RowName);
        Assert.True(row.Enabled);
        Assert.True(row.IsBase);
    }

    [Fact]
    public void Real_direct_inject_mods_beside_it_are_not_marked_as_the_games()
    {
        Assert.False(Row("ERSS2 Frame Gen").IsBase);
    }

    // ---------- every turn-off route leaves it where it is ----------

    [Fact]
    public async Task A_single_toggle_off_refuses_in_words_and_moves_nothing()
    {
        var c = Scanner.GameContext(Game());
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => ModToggle.SetEnabledWithOutcomeAsync(c, Row(RowName), false));
        Assert.Equal("regulation.bin is part of the game, so 626 won't turn it off.", ex.Message);
        AssertVanillaInPlace();
    }

    [Fact]
    public async Task The_agents_set_mod_enabled_refuses_with_game_file_and_moves_nothing()
    {
        var r = Json(await WriteTools.SetModEnabled("er-reg", RowName, enabled: false));
        Assert.False(r.GetProperty("ok").GetBoolean(), r.ToString());
        Assert.Equal("game_file", r.GetProperty("refusal").GetString());
        AssertVanillaInPlace();
    }

    [Fact]
    public void The_bulk_direct_inject_toggle_skips_it_silently_whatever_the_row_says()
    {
        // Disable all in the app walks the rows and calls this per name. A bulk caller never throws for a
        // game file, and the move itself must not happen even for a row that was not marked.
        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);
        AssertVanillaInPlace();
    }

    [Fact]
    public void Disable_all_turns_the_real_mod_off_and_leaves_regulation_bin()
    {
        foreach (var m in ModListing.Resolve(Game()).Where(m => m.Enabled && !m.IsBase).ToList())
            ModToggle.SetDirectInjectEnabled(Game(), m.Name, enabled: false);

        AssertVanillaInPlace();
        Assert.False(File.Exists(Path.Combine(Play, "erss-fg.dll")));
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));

        foreach (var m in ModListing.Resolve(Game()).Where(m => !m.Enabled).ToList())
            ModToggle.SetDirectInjectEnabled(Game(), m.Name, enabled: true);
        Assert.Equal("fg", File.ReadAllText(Path.Combine(Play, "erss-fg.dll")));
        AssertVanillaInPlace();
    }

    [Fact]
    public void Return_to_vanilla_plans_neither_a_move_nor_a_turn_off_for_it()
    {
        var c = Scanner.GameContext(Game());
        var moves = RestorePointEngine.PlanVanillaMoves(c);
        var turnOffs = RestorePointEngine.PlanVanillaTurnOffs(c, moves);
        Assert.DoesNotContain(moves, mf => mf.Rel.EndsWith(Reg, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(turnOffs, t => t.Name == RowName);
        Assert.Contains(turnOffs, t => t.Name == "ERSS2 Frame Gen");   // the real mod still goes
    }

    [Fact]
    public void Return_to_vanilla_at_a_game_root_that_holds_it_directly_does_not_move_it()
    {
        // A game whose exe folder IS the game root (no Game\ subfolder): the root-level move plan is the
        // route that reaches the file.
        var root = Path.Combine(_root, "flatgame");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, Reg), Vanilla);
        File.WriteAllText(Path.Combine(root, "erss-fg.dll"), "fg");
        var game = new GameEntry { Id = "flat", GameName = "Flat", Engine = "fromsoft", GameRoot = root, DataDir = Path.Combine(_root, "flat-data") };
        var c = Scanner.GameContext(game);

        var moves = RestorePointEngine.PlanVanillaMoves(c);

        Assert.DoesNotContain(moves, mf => mf.Rel == Reg);
        Assert.Contains(moves, mf => mf.Rel == "erss-fg.dll");
    }

    // ---------- a regulation.bin 626 installed is still a mod ----------

    [Fact]
    public async Task A_regulation_bin_626_replaced_still_toggles_and_round_trips_byte_identical()
    {
        InstallModdedRegulationThrough626();
        var c = Scanner.GameContext(Game());

        var row = Row(RowName);
        Assert.False(row.IsBase);
        Assert.True(row.Enabled);

        var off = await ModToggle.SetEnabledWithOutcomeAsync(c, row, false);
        Assert.True(off.Applied);
        Assert.False(File.Exists(RegPath));
        Assert.False(Row(RowName).Enabled);
        Assert.False(Row(RowName).IsBase);   // held, still the mod's

        var on = await ModToggle.SetEnabledWithOutcomeAsync(c, Row(RowName), true);
        Assert.True(on.Applied);
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));
    }

    [Fact]
    public async Task The_agent_can_toggle_a_regulation_bin_626_replaced()
    {
        InstallModdedRegulationThrough626();

        var off = Json(await WriteTools.SetModEnabled("er-reg", RowName, enabled: false));
        Assert.True(off.GetProperty("ok").GetBoolean(), off.ToString());
        Assert.False(File.Exists(RegPath));

        var on = Json(await WriteTools.SetModEnabled("er-reg", RowName, enabled: true));
        Assert.True(on.GetProperty("ok").GetBoolean(), on.ToString());
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));
    }

    [Fact]
    public void The_original_a_replace_displaced_is_still_in_the_replaced_store()
    {
        InstallModdedRegulationThrough626();
        var stored = Directory.GetFiles(DirectInject.ReplacedRoot(Play), Reg, SearchOption.AllDirectories);
        Assert.Single(stored);
        Assert.Equal(Vanilla, File.ReadAllBytes(stored[0]));
    }

    [Fact]
    public void Return_to_vanilla_still_turns_a_626_replaced_regulation_bin_off()
    {
        InstallModdedRegulationThrough626();
        var c = Scanner.GameContext(Game());
        var turnOffs = RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c));
        Assert.Contains(turnOffs, t => t.Name == RowName);
    }

    [Fact]
    public void An_install_record_that_names_it_in_the_play_folder_is_provenance()
    {
        ModInstallRegistry.Save(DataDir, new ModInstallManifest(
            "reg-pack", "RegPack.zip", "direct-inject", new[] { Reg }, DateTime.UtcNow,
            LocationPath: ModInstallRegistry.LocationPathFor(GameRoot, Play)));
        Assert.False(Row(RowName).IsBase);
    }

    [Fact]
    public void An_install_record_for_another_folder_is_not_provenance()
    {
        // Same relative name, different location (a Mod Engine 2 mod folder). It says nothing about Game\.
        ModInstallRegistry.Save(DataDir, new ModInstallManifest(
            "me2-pack", "Me2Pack.zip", "mod", new[] { Reg }, DateTime.UtcNow,
            LocationPath: "mod"));
        Assert.True(Row(RowName).IsBase);
    }

    [Fact]
    public void A_replace_recorded_in_the_data_folder_for_this_exact_file_is_provenance()
    {
        // Scanner.ExecuteIntake keeps its replaced store under the data dir and records the absolute path.
        var batch = ReplacedStore.NewBatch(Path.Combine(DataDir, "replaced"));
        ReplacedStore.WriteManifest(batch, new[] { new ReplacedStore.ReplacedEntry(RegPath, Reg, DateTime.UtcNow) });
        Assert.False(Row(RowName).IsBase);
    }

    [Fact]
    public void A_data_folder_replace_of_a_same_named_file_elsewhere_is_not_provenance()
    {
        var other = Path.Combine(GameRoot, "mod", Reg);
        var batch = ReplacedStore.NewBatch(Path.Combine(DataDir, "replaced"));
        ReplacedStore.WriteManifest(batch, new[] { new ReplacedStore.ReplacedEntry(other, Reg, DateTime.UtcNow) });
        Assert.True(Row(RowName).IsBase);
    }

    // ---------- a copy an older build already moved aside ----------

    [Fact]
    public void A_regulation_bin_an_older_build_held_lists_as_the_games_and_can_come_home()
    {
        // v0.24.0's Return to vanilla moved the game's own file into direct-disabled. It must list as the
        // game's (never to be turned off again), and turning it ON, the repair, must work.
        var folder = DirectInjectListing.PlayFolder(GameRoot)!;
        var holding = DirectInjectListing.Holding(Game());
        var mod = DirectInjectListing.Enabled(folder).Single(m => m.Name == RowName);
        DirectInject.Disable(folder, holding, mod);
        Assert.False(File.Exists(RegPath));

        var held = Row(RowName);
        Assert.False(held.Enabled);
        Assert.True(held.IsBase);

        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: true);

        AssertVanillaInPlace();
        Assert.True(Row(RowName).Enabled);
    }

    // ---------- the repair path when Steam verify put a fresh copy back ----------

    // An older build held regulation.bin in direct-disabled; the play folder now has a fresh one (Steam verify).
    private string HoldOldCopyThenPutFreshOneBack()
    {
        var folder = DirectInjectListing.PlayFolder(GameRoot)!;
        var holding = DirectInjectListing.Holding(Game());
        DirectInject.Disable(folder, holding, DirectInjectListing.Enabled(folder).Single(m => m.Name == RowName));
        File.WriteAllBytes(RegPath, Modded);   // the fresh live copy, distinguishable from the held one
        return Path.Combine(holding, EnginePresets.Slugify(RowName));
    }

    [Fact]
    public void Turning_on_a_held_copy_over_a_live_game_file_refuses_with_a_specific_message_and_moves_nothing()
    {
        var heldDir = HoldOldCopyThenPutFreshOneBack();

        var ex = Assert.Throws<HeldCopyCollisionException>(() => ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: true));

        // Says the live file is the game's (so "turn the live copy off" is not the advice) and where the held copy is.
        Assert.Contains("the game's", ex.Message);
        Assert.DoesNotContain("Turn that copy off", ex.Message);
        Assert.Contains(heldDir, ex.Message);
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));                         // the live file untouched
        Assert.Equal(Vanilla, File.ReadAllBytes(Path.Combine(heldDir, Reg)));      // the held copy stays held
    }

    [Fact]
    public void A_live_copy_626_replaced_keeps_the_ordinary_collision_message()
    {
        // Provenance on the live file: it is a mod, so the usual "turn that copy off or remove it" advice is true.
        var heldDir = HoldOldCopyThenPutFreshOneBack();
        ModInstallRegistry.Save(DataDir, new ModInstallManifest(
            "reg-pack", "RegPack.zip", "direct-inject", new[] { Reg }, DateTime.UtcNow,
            LocationPath: ModInstallRegistry.LocationPathFor(GameRoot, Play)));

        var ex = Assert.Throws<HeldCopyCollisionException>(() => ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: true));

        Assert.Contains("Turn that copy off", ex.Message);
        Assert.Equal(Vanilla, File.ReadAllBytes(Path.Combine(heldDir, Reg)));
    }

    [Fact]
    public void Enable_all_carries_on_past_a_refusal_and_reports_it()
    {
        // The real mod is held too; the regulation.bin collision must not stop it coming back.
        var folder = DirectInjectListing.PlayFolder(GameRoot)!;
        var holding = DirectInjectListing.Holding(Game());
        DirectInject.Disable(folder, holding, DirectInjectListing.Enabled(folder).Single(m => m.Name == "ERSS2 Frame Gen"));
        HoldOldCopyThenPutFreshOneBack();
        Assert.False(File.Exists(Path.Combine(Play, "erss-fg.dll")));

        var refused = ModToggle.SetDirectInjectAll(Game(), ModListing.Resolve(Game()), on: true);

        Assert.Equal("fg", File.ReadAllText(Path.Combine(Play, "erss-fg.dll")));    // the rest went on
        var one = Assert.Single(refused);
        Assert.Equal(RowName, one.Name);
        Assert.Contains("the game's", one.Error);
    }

    [Fact]
    public void Disable_all_through_the_bulk_helper_skips_the_game_file_and_moves_the_mod()
    {
        var refused = ModToggle.SetDirectInjectAll(Game(), ModListing.Resolve(Game()), on: false);

        Assert.Empty(refused);                       // a game file is skipped, not a refusal
        AssertVanillaInPlace();
        Assert.False(File.Exists(Path.Combine(Play, "erss-fg.dll")));
    }
}
