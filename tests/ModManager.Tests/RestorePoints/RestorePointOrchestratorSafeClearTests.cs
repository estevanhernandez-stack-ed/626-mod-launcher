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
