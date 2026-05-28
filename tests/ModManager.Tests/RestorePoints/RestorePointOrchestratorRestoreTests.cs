using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Tasks 4 + 5 — RestoreAsync, ListRestorePoints, DeleteRestorePoint, DetectInterruptedClear,
/// DiscardPartial. Fakes are duplicated from RestorePointOrchestratorSafeClearTests by design
/// (shared test fixture would couple the files; these tests are a standalone contract surface).
///
/// NOTE on FakeProvider.Reload(): the fake does NOT clear or reload its in-memory list.  This
/// models a provider where the game registration persists across the SafeClear/Restore boundary —
/// accurate enough for headless testing because a real LauncherService would re-read the games.json
/// that SafeClear deleted then RestoreAsync copied back, ending up with the same game registered.
/// The fake skips that disk round-trip and keeps the game in memory; the round-trip test relies on
/// this simplification and documents it here so readers don't mistake it for a bug.
/// </summary>
public class RestorePointOrchestratorRestoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-restore-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private sealed class FakeNexus : INexusGate
    {
        public bool IsConnected { get; init; } = true;
        public void DeleteStoredKey() { }
    }

    private sealed class FakeProbe : IGameRunningProbe
    {
        public bool AnyRunning(GameEntry g) => false;
    }

    private sealed class FakeProvider : IGameProvider
    {
        private readonly List<GameEntry> _games;
        public FakeProvider(IEnumerable<GameEntry> games) => _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        // Intentionally a no-op: the fake keeps its in-memory list intact so Restore can find the
        // game after SafeClear deletes games.json from disk. See class-level NOTE above.
        public void Reload() { }
    }

    // dataRoot = <_root>\appdata ; a game registered with a real DirectInject signature (regulation.bin).
    // DataDir is created eagerly so SafeClear's WriteRestoreAvailable can write its temp file without
    // a DirectoryNotFoundException — AtomicJson expects the parent directory to already exist.
    private (GameEntry game, GameContext c, string dataRoot, string modsDir) Setup()
    {
        var dataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(dataRoot);
        var gameRoot = Path.Combine(_root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var dataDir = Path.Combine(dataRoot, "_626mods", "t");
        Directory.CreateDirectory(dataDir);   // AtomicJson needs the parent to exist before writing
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            DataDir = dataDir,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        return (game, Scanner.GameContext(game), dataRoot, modsDir);
    }

    private RestorePointOrchestrator Make(string dataRoot, IGameProvider prov, INexusGate nexus, IGameRunningProbe probe)
        => new(dataRoot, Path.Combine(dataRoot, "restore-points"), "0.4.0", prov, nexus, probe);

    [Fact]
    public async Task SafeClear_vanilla_then_Restore_brings_back_the_moved_game_file()
    {
        var (game, c, dataRoot, _) = Setup();
        var reg = Path.Combine(c.GameRoot, "regulation.bin");    // real DirectInject signature
        File.WriteAllBytes(reg, new byte[] { 9, 9, 9 });
        var prov = new FakeProvider(new[] { game });
        var orch = Make(dataRoot, prov, new FakeNexus(), new FakeProbe());

        var clear = await orch.SafeClearAsync(
            new SafeClearOptions { CreateRestorePoint = true, DefaultEndState = "vanilla" },
            "20260528-141233", default);
        Assert.True(clear.Ok);
        Assert.False(File.Exists(reg));                          // moved out by vanilla end-state

        // Law A: the SEALED manifest already carries the planned MovedFiles (recorded during capture,
        // before any move). Restore reads them from the seal — they aren't a post-mutate patch.
        var sealedM = RestorePointManifestStore.Read(Path.Combine(dataRoot, "restore-points", "20260528-141233"))!;
        Assert.True(RestorePointManifestStore.Validate(sealedM, RestorePoint.SchemaVersion).Ok);
        Assert.Contains(sealedM.Games.SelectMany(g => g.MovedFiles), mf => mf.Rel.Contains("regulation.bin"));

        var restore = await orch.RestoreAsync("20260528-141233", default);
        Assert.True(restore.Ok);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(reg));   // moved file restored byte-for-byte
    }

    [Fact]
    public async Task Restore_refuses_an_unsealed_point()
    {
        var (_, _, dataRoot, _) = Setup();
        var rpDir = Path.Combine(dataRoot, "restore-points", "20260528-000000");
        Directory.CreateDirectory(rpDir);                        // exists but no manifest.json — unsealed
        var orch = Make(dataRoot, new FakeProvider(Array.Empty<GameEntry>()), new FakeNexus(), new FakeProbe());

        var r = await orch.RestoreAsync("20260528-000000", default);

        Assert.False(r.Ok);
        Assert.NotNull(r.RefusedReason);
    }

    [Fact]
    public async Task ListRestorePoints_returns_sealed_skips_unsealed_and_Delete_removes()
    {
        var (game, c, dataRoot, _) = Setup();
        File.WriteAllBytes(Path.Combine(c.GameRoot, "regulation.bin"), new byte[] { 1 });
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());

        await orch.SafeClearAsync(new SafeClearOptions(), "20260528-141233", default);
        // Drop an unsealed directory — ListRestorePoints must skip it.
        Directory.CreateDirectory(Path.Combine(dataRoot, "restore-points", "20260101-000000"));

        var list = orch.ListRestorePoints();
        Assert.Single(list);
        Assert.Equal("20260528-141233", list[0].Timestamp);
        Assert.Contains("T", list[0].GameNames);

        orch.DeleteRestorePoint("20260528-141233");
        Assert.Empty(orch.ListRestorePoints());
    }

    [Fact]
    public async Task DetectInterruptedClear_reads_lock_and_seal_state_and_DiscardPartial_clears()
    {
        var (game, c, dataRoot, _) = Setup();
        File.WriteAllBytes(Path.Combine(c.GameRoot, "regulation.bin"), new byte[] { 1 });
        var orch = Make(dataRoot, new FakeProvider(new[] { game }), new FakeNexus(), new FakeProbe());

        Assert.Null(orch.DetectInterruptedClear());              // no lock → nothing interrupted

        // Simulate an interrupted clear: lock file points at an UNSEALED rpDir (no manifest).
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "safe-clear.lock"), "20260528-999999");
        Directory.CreateDirectory(Path.Combine(dataRoot, "restore-points", "20260528-999999"));

        var det = orch.DetectInterruptedClear();
        Assert.NotNull(det);
        Assert.Equal("20260528-999999", det!.Timestamp);
        Assert.False(det.Sealed);                                // no manifest → not sealed

        orch.DiscardPartial("20260528-999999");
        Assert.Null(orch.DetectInterruptedClear());              // lock + partial dir gone
    }
}
