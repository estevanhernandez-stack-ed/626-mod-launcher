using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointOrchestratorSafeClearTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-orch-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private sealed class FakeNexus : INexusGate
    {
        public bool IsConnected { get; init; } = true;
        public int DeleteCalls { get; private set; }
        public void DeleteStoredKey() => DeleteCalls++;
    }
    private sealed class FakeProbe : IGameRunningProbe
    {
        public bool Running { get; init; }
        public bool AnyRunning(GameEntry g) => Running;
    }
    private sealed class ThrowingProbe : IGameRunningProbe
    {
        public bool AnyRunning(GameEntry g) => throw new InvalidOperationException("probe unavailable");
    }
    private sealed class FakeProvider : IGameProvider
    {
        private readonly List<GameEntry> _games;
        public int ReloadCalls { get; private set; }
        public FakeProvider(IEnumerable<GameEntry> games) => _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() => ReloadCalls++;
    }

    // dataRoot = <_root>\appdata ; a game whose DataDir is under appdata, with one disabled mod.
    private (GameEntry game, GameContext c, string dataRoot, string modsDir) Setup()
    {
        var dataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(dataRoot);
        var gameRoot = Path.Combine(_root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            DataDir = Path.Combine(dataRoot, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        return (game, Scanner.GameContext(game), dataRoot, modsDir);
    }

    private RestorePointOrchestrator Make(string dataRoot, IGameProvider prov, INexusGate nexus, IGameRunningProbe probe)
        => new(dataRoot, Path.Combine(dataRoot, "restore-points"), "0.4.0", prov, nexus, probe);

    [Fact]
    public async Task SafeClear_archives_seals_and_resets_keeping_nexus()
    {
        var (game, c, dataRoot, modsDir) = Setup();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        File.WriteAllText(Path.Combine(dataRoot, "games.json"), "{\"version\":1,\"games\":[]}");  // top-level state
        var nexus = new FakeNexus(); var prov = new FakeProvider(new[] { game });
        var orch = Make(dataRoot, prov, nexus, new FakeProbe());

        var r = await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = true, KeepNexus = true },
            timestamp: "20260528-141233", default);

        Assert.True(r.Ok);
        Assert.Equal("20260528-141233", r.RestorePointTimestamp);
        var rpDir = Path.Combine(dataRoot, "restore-points", "20260528-141233");
        var m = RestorePointManifestStore.Read(rpDir);
        Assert.True(RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion).Ok);   // sealed + current
        Assert.True(m!.TotalBytes > 0);
        Assert.Equal(0, nexus.DeleteCalls);                                                  // KeepNexus
        Assert.False(File.Exists(Path.Combine(dataRoot, "safe-clear.lock")));                // lock cleared
        Assert.False(File.Exists(Path.Combine(dataRoot, "games.json")));                     // reset
        Assert.NotNull(RestoreMarkers.ReadLastClear(dataRoot));                              // last-clear written
        Assert.Equal("20260528-141233", RestoreMarkers.ReadRestoreAvailable(c.DataDir));     // breadcrumb in game data dir
        Assert.True(prov.ReloadCalls > 0);
    }

    [Fact]
    public async Task SafeClear_succeeds_when_a_game_has_no_mod_data_dir()
    {
        // Regression (live-smoke 2026-05-30): a registered game whose mod data dir doesn't exist on
        // disk (captain-of-industry — registered, never had mods) crashed the WHOLE clear. MUTATE's
        // WriteRestoreAvailable wrote RESTORE-AVAILABLE.json into the missing dir and threw
        // DirectoryNotFoundException. The clear must tolerate a game with no data dir. Note: Setup()
        // does NOT create c.DataDir — the happy-path test only has it because DisableModAsync creates
        // it as a side effect. Here we disable nothing, so c.DataDir is absent: the exact repro.
        var (game, c, dataRoot, _) = Setup();
        File.WriteAllText(Path.Combine(dataRoot, "games.json"), "{\"version\":1,\"games\":[]}");
        Assert.False(Directory.Exists(c.DataDir));   // precondition: no data dir on disk
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());

        var r = await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = true, KeepNexus = true },
            "20260530-000000", default);

        Assert.True(r.Ok);
        Assert.Equal("20260530-000000", r.RestorePointTimestamp);
        Assert.Equal("20260530-000000", RestoreMarkers.ReadRestoreAvailable(c.DataDir));  // marker in freshly-created dir
    }

    [Fact]
    public async Task SafeClear_refuses_when_game_running()
    {
        var (game, _, dataRoot, _) = Setup();
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe { Running = true });
        var r = await orch.SafeClearAsync(new SafeClearOptions(), "20260528-141233", default);
        Assert.False(r.Ok);
        Assert.Contains("T", r.RefusedReason!);
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "restore-points", "20260528-141233")));  // nothing archived
    }

    [Fact]
    public async Task SafeClear_refuses_when_running_probe_is_unavailable()
    {
        // Law E fails CLOSED on a destructive op: if the probe can't verify whether the game is
        // running, refuse rather than risk resetting over live files.
        var (game, _, dataRoot, _) = Setup();
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new ThrowingProbe());
        var r = await orch.SafeClearAsync(new SafeClearOptions(), "20260528-141233", default);
        Assert.False(r.Ok);
        Assert.Contains("verify", r.RefusedReason!);
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "restore-points", "20260528-141233")));  // nothing archived
    }

    [Fact]
    public async Task SafeClear_skip_archive_moves_mods_but_writes_no_manifest()
    {
        var (game, c, dataRoot, modsDir) = Setup();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());
        var r = await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = false }, "20260528-141233", default);
        Assert.True(r.Ok);
        Assert.Null(r.RestorePointTimestamp);
        Assert.Null(RestorePointManifestStore.Read(Path.Combine(dataRoot, "restore-points", "20260528-141233")));  // no manifest
        // mods preserved (never File.Delete'd): the disabled holding still on disk
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));
    }

    [Fact]
    public async Task SafeClear_seals_the_offboarding_sheet_path_for_cleanup_on_restore()
    {
        var (game, c, dataRoot, modsDir) = Setup();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());

        await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = true }, "20260528-141233", default);

        var m = RestorePointManifestStore.Read(Path.Combine(dataRoot, "restore-points", "20260528-141233"))!;
        var ga = Assert.Single(m.Games);
        Assert.Equal(Path.Combine(c.GameRoot, "626-launcher-how-to-launch.txt"), ga.OffboardingSheetGameFolderPath);
    }

    [Fact]
    public async Task SafeClear_never_touches_live_saves_and_preserves_save_backups()
    {
        // Saves are the user's irreplaceable data. This pins the invariant: a full Safe Clear leaves
        // the game's LIVE save folder byte-for-byte untouched, AND the launcher's save backups survive
        // into the restore point + are counted on the manifest (which feeds the off-boarding sheet).
        // The live save folder sits OUTSIDE both dataRoot and gameRoot — exactly like a real game
        // (ER saves live under %APPDATA%\EldenRing) — so this proves the clear's deletes never reach it.
        var (game, c, dataRoot, modsDir) = Setup();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);

        // Live save folder, outside everything Safe Clear operates on.
        var liveSaveDir = Path.Combine(_root, "live-saves");
        Directory.CreateDirectory(liveSaveDir);
        var liveSaveFile = Path.Combine(liveSaveDir, "ER0000.sl2");
        File.WriteAllText(liveSaveFile, "PRECIOUS-SAVE-BYTES");
        var liveSaveBytesBefore = File.ReadAllBytes(liveSaveFile);
        game.SaveDir = liveSaveDir;

        // Two launcher-made save backups under the per-game saves dir (each a timestamped subfolder).
        Directory.CreateDirectory(Path.Combine(c.SavesDir, "auto-20260501-100000"));
        File.WriteAllText(Path.Combine(c.SavesDir, "auto-20260501-100000", "ER0000.sl2"), "BACKUP-1");
        Directory.CreateDirectory(Path.Combine(c.SavesDir, "before-launch-20260502-110000"));
        File.WriteAllText(Path.Combine(c.SavesDir, "before-launch-20260502-110000", "ER0000.sl2"), "BACKUP-2");

        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());
        var r = await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = true, KeepNexus = true },
            "20260528-141233", default);
        Assert.True(r.Ok);

        // 1. The LIVE save folder + file are completely untouched (exists, byte-for-byte identical).
        Assert.True(Directory.Exists(liveSaveDir));
        Assert.True(File.Exists(liveSaveFile));
        Assert.Equal(liveSaveBytesBefore, File.ReadAllBytes(liveSaveFile));

        // 2. The launcher save backups were copied into the restore point (preserved, not lost).
        var archivedSaves = Path.Combine(dataRoot, "restore-points", "20260528-141233", "games", "t", "data", "saves");
        Assert.True(File.Exists(Path.Combine(archivedSaves, "auto-20260501-100000", "ER0000.sl2")));
        Assert.True(File.Exists(Path.Combine(archivedSaves, "before-launch-20260502-110000", "ER0000.sl2")));

        // 3. The manifest records the save location + backup count (feeds the off-boarding sheet).
        var ga = Assert.Single(RestorePointManifestStore.Read(Path.Combine(dataRoot, "restore-points", "20260528-141233"))!.Games);
        Assert.Equal(liveSaveDir, ga.SaveLocation);
        Assert.Equal(2, ga.SaveBackupCount);
    }

    [Fact]
    public async Task SafeClear_keepNexus_false_deletes_the_key()
    {
        var (game, c, dataRoot, modsDir) = Setup();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        var nexus = new FakeNexus();
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), nexus, new FakeProbe());
        var r = await orch.SafeClearAsync(new SafeClearOptions { CreateRestorePoint = true, KeepNexus = false },
            "20260528-141233", default);
        Assert.True(r.Ok);
        Assert.Equal(1, nexus.DeleteCalls);
        // nexus.json never entered the archive
        Assert.False(File.Exists(Path.Combine(dataRoot, "restore-points", "20260528-141233", "nexus.json")));
    }
}
