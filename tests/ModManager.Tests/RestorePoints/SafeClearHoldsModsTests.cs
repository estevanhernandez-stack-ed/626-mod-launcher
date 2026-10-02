using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// "Return to vanilla" holds every active mod (2026-10-02 design note): the vanilla end-state turns off
/// every enabled, switchable row through ModToggle, seals that set in the manifest as turnedOffByClear
/// during CAPTURE, and Restore turns exactly that set back on. Driven end to end through the orchestrator
/// over three real temp games: a pak game (with an owned Vortex folder and a mod that was already off), a
/// Cyberpunk-shaped game whose mod has extra-tree entries, and a FromSoft direct-inject game.
/// </summary>
public class SafeClearHoldsModsTests : IDisposable
{
    private const string Ts = "20261002-120000";
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-holds-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string DataRoot => Path.Combine(_root, "appdata");
    private string RpDir => Path.Combine(DataRoot, "restore-points", Ts);

    private sealed class FakeNexus : INexusGate
    {
        public bool IsConnected => true;
        public void DeleteStoredKey() { }
    }
    private sealed class FakeProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }

    // Keeps its games across the clear (see RestorePointOrchestratorRestoreTests' note) and hands the
    // Cyberpunk-shaped game its extra trees, which a custom engine gets from no manifest.
    private sealed class Provider(IEnumerable<GameEntry> games) : IGameProvider
    {
        private readonly List<GameEntry> _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g)
            => g.Id == "tree" ? Scanner.GameContext(g, extraModTrees: Trees) : Scanner.GameContext(g);
        public void Reload() { }
    }

    private void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private string DataDir(string id)
    {
        var d = Path.Combine(DataRoot, "_626mods", id);
        Directory.CreateDirectory(d);
        return d;
    }

    // A pak game: "alpha" on, "offmod" already off before the clear, "owned" in a Vortex-owned folder.
    private GameEntry PakGame(string steamAppId = "")
    {
        var root = Path.Combine(_root, "pakgame");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        Put(Path.Combine(root, "mods", "offmod.pak"), "OFF");
        Put(Path.Combine(root, "vortex", "owned.pak"), "OWNED");
        Put(Path.Combine(root, "vortex", "vortex.deployment.pak.json"), "{}");
        return new GameEntry
        {
            Id = "pak", GameName = "Pak Game", Engine = "minecraft", GameRoot = root, DataDir = DataDir("pak"), SteamAppId = steamAppId,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[]
            {
                new ModLocation("mods", "Mods", "mods"),
                new ModLocation("vortex", "Vortex", "vortex"),
            },
        };
    }

    private GameEntry TreeGame()
    {
        var root = Path.Combine(_root, "treegame");
        Put(Path.Combine(root, "archive", "pc", "mod", "CoolMod.archive"), "MAIN");
        Put(Path.Combine(root, "r6", "scripts", "CoolMod", "main.reds"), "SCRIPTS");
        Put(Path.Combine(root, "r6", "tweaks", "CoolMod.yaml"), "TWEAK");
        return new GameEntry
        {
            Id = "tree", GameName = "Tree Game", Engine = "custom", SteamAppId = "1091500", GameRoot = root, DataDir = DataDir("tree"),
            FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
    }

    // FromSoft direct-inject: regulation.bin at the game root (the existing vanilla-moved step plans it),
    // plus a regulation.bin-shaped mod under Game\ that the root-only plan never reached.
    private GameEntry DirectInjectGame(bool withGameSubfolder)
    {
        var root = Path.Combine(_root, withGameSubfolder ? "digame-sub" : "digame");
        var play = withGameSubfolder ? Path.Combine(root, "Game") : root;
        Directory.CreateDirectory(play);
        File.WriteAllBytes(Path.Combine(play, "regulation.bin"), new byte[] { 7, 7, 7 });
        return new GameEntry
        {
            Id = withGameSubfolder ? "di-sub" : "di", GameName = "DI Game", Engine = "fromsoft",
            GameRoot = root, DataDir = DataDir(withGameSubfolder ? "di-sub" : "di"),
        };
    }

    private RestorePointOrchestrator Make(Provider p)
        => new(DataRoot, Path.Combine(DataRoot, "restore-points"), "0.5.0", p, new FakeNexus(), new FakeProbe());

    private static Dictionary<string, string> Snapshot(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p),
                p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.OrdinalIgnoreCase);

    private static void AssertSameTree(Dictionary<string, string> expected, string root)
    {
        var actual = Snapshot(root);
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        foreach (var (rel, sha) in expected) Assert.Equal(sha, actual[rel]);
    }

    private async Task<(Provider p, GameEntry pak, GameEntry tree, GameEntry di)> ThreeGamesAsync()
    {
        var pak = PakGame();
        var tree = TreeGame();
        var di = DirectInjectGame(withGameSubfolder: false);
        var p = new Provider(new[] { pak, tree, di });
        await Scanner.DisableModAsync("offmod", p.ContextFor(pak));   // off before the clear
        return (p, pak, tree, di);
    }

    private static GameArchive Archive(RestorePointManifest m, string id) => m.Games.Single(g => g.Id == id);

    [Fact]
    public async Task Vanilla_turns_every_enabled_scanner_mod_off_and_holds_its_extra_trees()
    {
        var (p, pak, tree, di) = await ThreeGamesAsync();

        var r = await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);
        Assert.True(r.Ok, r.RefusedReason);

        // Pak game: alpha is held, the already-off mod is still held, the owned one never moved.
        var pakCtx = p.ContextFor(pak);
        Assert.False(File.Exists(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(pakCtx.DisabledRoot, "alpha", "alpha.pak")));
        Assert.Equal("OFF", File.ReadAllText(Path.Combine(pakCtx.DisabledRoot, "offmod", "offmod.pak")));
        Assert.Equal("OWNED", File.ReadAllText(Path.Combine(pak.GameRoot, "vortex", "owned.pak")));

        // Tree game: the main archive AND its extra-tree entries are held.
        var treeCtx = p.ContextFor(tree);
        var heldTrees = Path.Combine(treeCtx.DataDir, "disabled-trees", "CoolMod");
        Assert.False(File.Exists(Path.Combine(tree.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.False(Directory.Exists(Path.Combine(tree.GameRoot, "r6", "scripts", "CoolMod")));
        Assert.False(File.Exists(Path.Combine(tree.GameRoot, "r6", "tweaks", "CoolMod.yaml")));
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(heldTrees, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.Equal("TWEAK", File.ReadAllText(Path.Combine(heldTrees, "r6", "tweaks", "CoolMod.yaml")));

        // The sealed set: exactly what was on and switchable. The direct-inject file is the vanilla-moved
        // step's, so it is not in the set (never double-moved).
        var m = RestorePointManifestStore.Read(RpDir)!;
        Assert.Equal(new[] { "alpha" }, Archive(m, "pak").TurnedOffByClear!.Select(x => x.Name));
        Assert.Equal(new[] { "CoolMod" }, Archive(m, "tree").TurnedOffByClear!.Select(x => x.Name));
        Assert.Empty(Archive(m, "di").TurnedOffByClear!);
        Assert.Contains(Archive(m, "di").MovedFiles, mf => mf.Rel == "regulation.bin");
        Assert.False(File.Exists(Path.Combine(di.GameRoot, "regulation.bin")));
        Assert.Null(Archive(m, "pak").TurnOffSkipped);
    }

    [Fact]
    public async Task Restore_turns_exactly_the_pre_clear_set_back_on_byte_identical()
    {
        var (p, pak, tree, di) = await ThreeGamesAsync();
        var before = new[] { pak, tree, di }.ToDictionary(g => g.Id, g => Snapshot(g.GameRoot));
        var orch = Make(p);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok, restore.RefusedReason);
        Assert.Empty(restore.Warnings);
        foreach (var g in new[] { pak, tree, di }) AssertSameTree(before[g.Id], g.GameRoot);

        // Nothing left behind in holding for the mods that came back.
        Assert.False(Directory.Exists(Path.Combine(p.ContextFor(pak).DisabledRoot, "alpha")));
        Assert.False(Directory.Exists(Path.Combine(p.ContextFor(tree).DataDir, "disabled-trees", "CoolMod")));
    }

    [Fact]
    public async Task A_mod_that_was_off_before_the_clear_stays_off_after_restore()
    {
        var (p, pak, _, _) = await ThreeGamesAsync();
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.True((await orch.RestoreAsync(Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(pak.GameRoot, "mods", "offmod.pak")));
        Assert.Equal("OFF", File.ReadAllText(Path.Combine(p.ContextFor(pak).DisabledRoot, "offmod", "offmod.pak")));
        Assert.Contains(ModListing.Resolve(pak), x => x.Name == "offmod" && !x.Enabled);
        Assert.Contains(ModListing.Resolve(pak), x => x.Name == "alpha" && x.Enabled);
    }

    [Fact]
    public async Task An_owned_mod_is_untouched_and_flagged()
    {
        var (p, pak, _, _) = await ThreeGamesAsync();
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.Equal("OWNED", File.ReadAllText(Path.Combine(pak.GameRoot, "vortex", "owned.pak")));
        var ga = Archive(RestorePointManifestStore.Read(RpDir)!, "pak");
        Assert.Contains(ga.OwnedMods, o => o.Name == "owned" && o.ManagedBy == "Vortex");
        Assert.DoesNotContain(ga.TurnedOffByClear!, x => x.Name == "owned");
    }

    [Fact]
    public async Task One_refused_turn_off_does_not_abort_the_rest_and_is_recorded()
    {
        var (p, pak, tree, _) = await ThreeGamesAsync();
        var pakCtx = p.ContextFor(pak);
        // "bravo" is turned off, then a fresh copy installed: an earlier copy is held, so its turn-off refuses.
        Put(Path.Combine(pak.GameRoot, "mods", "bravo.pak"), "OLD-BRAVO");
        await Scanner.DisableModAsync("bravo", pakCtx);
        Put(Path.Combine(pak.GameRoot, "mods", "bravo.pak"), "NEW-BRAVO");
        var orch = Make(p);

        var r = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Contains(r.Warnings, w => w.Contains("bravo"));
        // bravo stayed exactly as it was; alpha and the other game were still turned off.
        Assert.Equal("NEW-BRAVO", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "bravo.pak")));
        Assert.Equal("OLD-BRAVO", File.ReadAllText(Path.Combine(pakCtx.DisabledRoot, "bravo", "bravo.pak")));
        Assert.False(File.Exists(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.False(File.Exists(Path.Combine(tree.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));

        var m = RestorePointManifestStore.Read(RpDir)!;
        Assert.True(RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion).Ok);
        var skip = Assert.Single(Archive(m, "pak").TurnOffSkipped!);
        Assert.Equal("bravo", skip.Name);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));

        // Restore brings alpha back and leaves bravo's two copies where they were, without a warning:
        // bravo never went off.
        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.Equal("NEW-BRAVO", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "bravo.pak")));
        Assert.Equal("OLD-BRAVO", File.ReadAllText(Path.Combine(pakCtx.DisabledRoot, "bravo", "bravo.pak")));
    }

    [Fact]
    public async Task An_old_manifest_without_the_field_restores_as_it_did()
    {
        var (p, pak, _, _) = await ThreeGamesAsync();
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        // Rewrite the seal as a pre-change build would have written it: schema 1, no turnedOffByClear.
        var m = RestorePointManifestStore.Read(RpDir)!;
        RestorePointManifestStore.WriteSealed(RpDir, m with
        {
            SchemaVersion = 1,
            Games = m.Games.Select(g => g with { TurnedOffByClear = null, TurnOffSkipped = null, HeldCopies = null }).ToList(),
        });
        Assert.DoesNotContain("turnedOffByClear\": [", File.ReadAllText(Path.Combine(RpDir, RestorePointManifestStore.FileName)));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok, restore.RefusedReason);
        Assert.Empty(restore.Warnings);
        // Today's behaviour: Restore does not turn anything on. alpha stays held, still listed, nothing lost.
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(p.ContextFor(pak).DisabledRoot, "alpha", "alpha.pak")));
        Assert.Contains(ModListing.Resolve(pak), x => x.Name == "alpha" && !x.Enabled);
    }

    [Fact]
    public async Task ModsActive_is_unchanged_and_records_no_turn_off_set()
    {
        var (p, pak, tree, _) = await ThreeGamesAsync();

        var r = await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, Ts, default);

        Assert.True(r.Ok);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.Equal("OFF", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "offmod.pak")));   // modsActive re-enables all, as before
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(tree.GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.All(RestorePointManifestStore.Read(RpDir)!.Games, g => Assert.Null(g.TurnedOffByClear));
    }

    [Fact]
    public async Task A_crash_partway_leaves_a_point_restore_completes_without_false_warnings()
    {
        // The sealed set names a mod that never went off (MUTATE died first). Restore turns the recorded
        // set on and judges by the final listing, so the still-live mod is not reported.
        var (p, pak, _, _) = await ThreeGamesAsync();
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        await Scanner.EnableModAsync("alpha", p.ContextFor(pak));   // as if MUTATE never reached it

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
    }

    [Fact]
    public async Task A_direct_inject_mod_under_Game_is_turned_off_and_back_on()
    {
        // The vanilla-moved step only looks at the game root, so a FromSoft play folder under Game\ was
        // never reached. The toggle path reaches it.
        var di = DirectInjectGame(withGameSubfolder: true);
        var p = new Provider(new[] { di });
        var before = Snapshot(di.GameRoot);
        var orch = Make(p);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.False(File.Exists(Path.Combine(di.GameRoot, "Game", "regulation.bin")));
        Assert.NotEmpty(Archive(RestorePointManifestStore.Read(RpDir)!, di.Id).TurnedOffByClear!);

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        AssertSameTree(before, di.GameRoot);
    }

    [Fact]
    public async Task Restore_leaves_mods_off_on_an_unacknowledged_ban_risk_game_and_says_so()
    {
        var pak = PakGame(steamAppId: "4032350");   // a High ban-risk floor title
        var p = new Provider(new[] { pak });
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("alpha") || w.Contains("ban risk", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(p.ContextFor(pak).DisabledRoot, "alpha", "alpha.pak")));
    }

    [Fact]
    public async Task Restore_turns_mods_on_on_an_acknowledged_ban_risk_game()
    {
        var pak = PakGame(steamAppId: "4032350");
        BanRiskAckStore.Ack(pak.DataDir!, pak.Id);   // acknowledged before the clear; archived with the data dir
        var p = new Provider(new[] { pak });
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
    }

    [Fact]
    public async Task Skip_archive_vanilla_still_holds_mods_and_never_deletes_them()
    {
        var (p, pak, _, _) = await ThreeGamesAsync();

        var r = await Make(p).SafeClearAsync(
            new SafeClearOptions { CreateRestorePoint = false, DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(p.ContextFor(pak).DisabledRoot, "alpha", "alpha.pak")));
        Assert.Contains(ModListing.Resolve(pak), x => x.Name == "alpha" && !x.Enabled);
    }

    // ---- Round 1: the restore point carries a copy of every mod vanilla turned off ----

    private static void DeleteHoldings(GameContext c)
    {
        foreach (var d in new[] { c.DisabledRoot, Path.Combine(c.DataDir, "disabled-trees") })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact]
    public async Task Vanilla_copies_each_turned_off_mods_held_folders_into_the_restore_point_with_checksums()
    {
        var (p, pak, tree, _) = await ThreeGamesAsync();
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var m = RestorePointManifestStore.Read(RpDir)!;
        Assert.True(RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion).Ok);

        var alpha = Assert.Single(Archive(m, "pak").HeldCopies!);
        Assert.Equal("alpha", alpha.Name);
        Assert.Contains(alpha.Files, f => f.Rel.Replace('\\', '/') == "disabled/alpha/alpha.pak");
        // The copy is the held folder as a whole, its record included, so it can go back exactly as it was.
        Assert.Contains(alpha.Files, f => f.Rel.Replace('\\', '/') == "disabled/alpha/meta.json");

        var cool = Assert.Single(Archive(m, "tree").HeldCopies!);
        Assert.Contains(cool.Files, f => f.Rel.Replace('\\', '/') == "disabled-trees/CoolMod/r6/scripts/CoolMod/main.reds");
        Assert.Contains(cool.Files, f => f.Rel.Replace('\\', '/') == "disabled-trees/CoolMod/r6/tweaks/CoolMod.yaml");

        // Every recorded file is in the archive, byte for byte what is held.
        foreach (var (id, ga) in new[] { ("pak", Archive(m, "pak")), ("tree", Archive(m, "tree")) })
            foreach (var f in ga.HeldCopies!.SelectMany(h => h.Files))
            {
                var archived = Path.Combine(RpDir, "games", id, "held", f.Rel);
                Assert.True(File.Exists(archived), archived);
                Assert.Equal(f.Sha256, FileTally.Sha256(archived), ignoreCase: true);
                Assert.Equal(f.Sha256, FileTally.Sha256(Path.Combine(ga.DataDir!, f.Rel)), ignoreCase: true);
            }
        // The pre-clear held mod is the data-dir capture's, not a held copy.
        Assert.DoesNotContain(Archive(m, "pak").HeldCopies!, h => h.Name == "offmod");
    }

    [Fact]
    public async Task Restore_after_the_data_folder_holdings_were_deleted_brings_every_mod_back_byte_identical()
    {
        var (p, pak, tree, di) = await ThreeGamesAsync();
        var before = new[] { pak, tree, di }.ToDictionary(g => g.Id, g => Snapshot(g.GameRoot));
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        DeleteHoldings(p.ContextFor(pak));
        DeleteHoldings(p.ContextFor(tree));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok, restore.RefusedReason);
        Assert.Empty(restore.Warnings);
        foreach (var g in new[] { pak, tree, di }) AssertSameTree(before[g.Id], g.GameRoot);
        // The mod that was off before the clear came back off, from the data-dir capture.
        Assert.Equal("OFF", File.ReadAllText(Path.Combine(p.ContextFor(pak).DisabledRoot, "offmod", "offmod.pak")));
    }

    [Fact]
    public async Task A_damaged_archived_copy_is_refused_for_that_mod_reported_and_the_others_continue()
    {
        var (p, pak, tree, _) = await ThreeGamesAsync();
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        File.WriteAllText(Path.Combine(RpDir, "games", "pak", "held", "disabled", "alpha", "alpha.pak"), "TAMPERED");
        DeleteHoldings(p.ContextFor(pak));
        DeleteHoldings(p.ContextFor(tree));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("alpha") && w.Contains("checksum", StringComparison.OrdinalIgnoreCase));
        // Nothing of alpha was put anywhere: not in the game, not in holding.
        Assert.False(File.Exists(Path.Combine(pak.GameRoot, "mods", "alpha.pak")));
        Assert.False(Directory.Exists(Path.Combine(p.ContextFor(pak).DisabledRoot, "alpha")));
        // The other game's mod came back whole.
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(tree.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(tree.GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
    }

    [Fact]
    public async Task A_crash_before_the_copy_was_recorded_restores_from_the_data_folder()
    {
        // Turn-offs done, copy never recorded (heldCopies null): the mods are still in the data folder.
        var (p, pak, tree, di) = await ThreeGamesAsync();
        var before = new[] { pak, tree, di }.ToDictionary(g => g.Id, g => Snapshot(g.GameRoot));
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var m = RestorePointManifestStore.Read(RpDir)!;
        RestorePointManifestStore.WriteSealed(RpDir, m with { Games = m.Games.Select(g => g with { HeldCopies = null }).ToList() });

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        foreach (var g in new[] { pak, tree, di }) AssertSameTree(before[g.Id], g.GameRoot);
    }

    [Fact]
    public async Task A_crash_before_the_copy_with_the_data_folder_gone_says_so_instead_of_claiming_success()
    {
        var (p, pak, _, _) = await ThreeGamesAsync();
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var m = RestorePointManifestStore.Read(RpDir)!;
        RestorePointManifestStore.WriteSealed(RpDir, m with { Games = m.Games.Select(g => g with { HeldCopies = null }).ToList() });
        DeleteHoldings(p.ContextFor(pak));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("alpha"));
    }
}
