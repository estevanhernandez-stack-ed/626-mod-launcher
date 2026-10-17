using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// When a Kind restore leaves its breadcrumb and when it takes it back. The record goes down after the plan
/// and the beforeRestore snapshot succeed and before the first overlay write; it comes off on success and on
/// every refusal that changed nothing; it STAYS when the overlay fails partway, because some files are then
/// from the snapshot and some from before.
/// </summary>
public class RestorePointOrchestratorRestoreJournalTests : IDisposable
{
    private const string Cap = "20261016-120000";
    private const string Snap1 = "20261016-130000";
    private static readonly DateTime Installed = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-rjournal-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string DataRoot => Path.Combine(_root, "appdata");
    private string Points => Path.Combine(DataRoot, "restore-points");

    private sealed class FakeNexus : INexusGate { public bool IsConnected => true; public void DeleteStoredKey() { } }
    private sealed class FakeProbe : IGameRunningProbe
    {
        public bool Running { get; set; }
        public bool AnyRunning(GameEntry g) => Running;
    }
    private sealed class FakeProvider(IEnumerable<GameEntry> games) : IGameProvider
    {
        private readonly List<GameEntry> _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() { }
    }

    private sealed record Fx(GameEntry Game, RestorePointOrchestrator Orch, FakeProbe Probe, string Root, string Data);

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    // A BepInEx game with one registered framework and a metadata.json (v1 at capture). Restoring puts
    // metadata.json back FIRST (launcher data), then the framework files, so a held framework file is a real
    // failure after the first write.
    private Fx Game()
    {
        var root = Path.Combine(_root, "Valheim");
        Put(Path.Combine(root, "BepInEx", "plugins", "Early.dll"), "EARLY");
        Put(Path.Combine(root, "winhttp.dll"), "WINHTTP-V1");
        Put(Path.Combine(root, "BepInEx", "core", "BepInEx.dll"), "CORE-V1");
        Put(Path.Combine(root, "doorstop_config.ini"), "enabled=true");
        var data = Path.Combine(DataRoot, "_626mods", "valheim");
        Directory.CreateDirectory(data);
        Put(Path.Combine(data, "metadata.json"), "{\"v\":1}");
        var g = new GameEntry
        {
            Id = "valheim", GameName = "Valheim", Engine = "bepinex", GameRoot = root, DataDir = data,
            FileExtensions = new[] { "dll" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "plugins", "BepInEx/plugins") },
        };
        var fwDir = Path.Combine(data, "frameworks", "bepinex");
        Directory.CreateDirectory(fwDir);
        File.WriteAllText(Path.Combine(fwDir, "install.json"), JsonSerializer.Serialize(
            new FrameworkInstallManifest("bepinex", "BEPINEX", "team", root,
                new[] { "winhttp.dll", "BepInEx/core/BepInEx.dll", "doorstop_config.ini" }, Installed, null),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var probe = new FakeProbe();
        var orch = new RestorePointOrchestrator(DataRoot, Points, "0.25.0", new FakeProvider(new[] { g }), new FakeNexus(), probe);
        return new Fx(g, orch, probe, root, data);
    }

    private static async Task<string> Capture(Fx fx)
    {
        var cap = await fx.Orch.CaptureGameOnlyAsync(fx.Game.Id, RestorePointKinds.PatchDay, Cap, default);
        Assert.True(cap.Ok, cap.RefusedReason);
        return cap.RestorePointTimestamp!;
    }

    private static void CarryOn(Fx fx)
    {
        Put(Path.Combine(fx.Root, "winhttp.dll"), "WINHTTP-V2");
        Put(Path.Combine(fx.Data, "metadata.json"), "{\"v\":2}");
    }

    // The overlay re-serializes merged JSON, so compare the value, not the bytes.
    private static int Meta(Fx fx)
        => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(fx.Data, "metadata.json")))!["v"]!.GetValue<int>();

    private static string Journal(string dataRoot) => RestoreJournal.PathFor(dataRoot);

    [Fact]
    public async Task The_record_is_on_disk_before_the_first_overlay_write_and_names_the_undo_snapshot()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        InterruptedRestore? seen = null;
        int metaThen = 0;
        fx.Orch.BeforeOverlayForTests = () => { seen = RestoreJournal.Read(DataRoot); metaThen = Meta(fx); };

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.NotNull(seen);                                   // there before the write...
        Assert.Equal(2, metaThen);                    // ...while nothing had been written yet
        Assert.Equal(ts, seen!.RestorePoint);
        Assert.Equal($"{Snap1}-valheim", seen.UndoSnapshot);
        Assert.Equal("valheim", seen.GameId);
        Assert.Equal("Valheim", seen.GameName);
        Assert.Equal(DateTimeKind.Utc, seen.StartedUtc.Kind);
        Assert.True(Math.Abs((DateTime.UtcNow - seen.StartedUtc).TotalMinutes) < 5);
        Assert.Equal(1, Meta(fx));                    // and the restore then went ahead
    }

    [Fact]
    public async Task A_successful_restore_clears_the_record()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.False(File.Exists(Journal(DataRoot)));
        Assert.Null(fx.Orch.DetectInterruptedRestore());
    }

    [Fact]
    public async Task A_failure_after_the_first_write_leaves_the_record_naming_the_undo_snapshot()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        FileStream? held = null;
        // Hold the proxy open once the record is down: metadata.json (launcher data) lands, then the swap of
        // winhttp.dll cannot, i.e. the overlay dies with some files from the snapshot and some from before.
        fx.Orch.BeforeOverlayForTests = () =>
            held = new FileStream(Path.Combine(fx.Root, "winhttp.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

            Assert.False(r.Ok);
            Assert.Contains($"{Snap1}-valheim", r.RefusedReason!);
            Assert.Equal(1, Meta(fx));                // the first write DID land
        }
        finally { held?.Dispose(); }
        Assert.Equal("WINHTTP-V2", File.ReadAllText(Path.Combine(fx.Root, "winhttp.dll")));   // the next write did not land

        var left = fx.Orch.DetectInterruptedRestore();
        Assert.NotNull(left);
        Assert.Equal($"{Snap1}-valheim", left!.UndoSnapshot);
        Assert.Equal(ts, left.RestorePoint);
        // The way back is a real, listed restore point.
        Assert.Contains(fx.Orch.ListRestorePoints(), p => p.Timestamp == left.UndoSnapshot);
    }

    [Fact]
    public async Task A_refusal_for_a_safe_clear_lock_writes_no_record()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        File.WriteAllText(Path.Combine(DataRoot, SafeClearLock.FileName), Cap);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.False(File.Exists(Journal(DataRoot)));
    }

    [Fact]
    public async Task A_refusal_because_the_game_is_running_writes_no_record()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        fx.Probe.Running = true;

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.False(File.Exists(Journal(DataRoot)));
    }

    [Fact]
    public async Task A_snapshot_whose_framework_file_escapes_the_install_root_writes_no_record()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        var rp = Path.Combine(Points, ts);
        var m = RestorePointManifestStore.Read(rp)!;
        var ga = m.Games[0];
        var fw = ga.Frameworks[0] with { InstalledFiles = ga.Frameworks[0].InstalledFiles.Append("../evil.dll").ToList() };
        RestorePointManifestStore.WriteSealed(rp, m with { Games = new[] { ga with { Frameworks = new[] { fw } } } });

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.Contains("Nothing was changed", r.RefusedReason!);
        Assert.False(File.Exists(Journal(DataRoot)));
        Assert.Null(fx.Orch.DetectInterruptedRestore());
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_refuses_the_restore_before_any_overlay_write()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        // A directory squatting on the record's path: the atomic move cannot replace it.
        Directory.CreateDirectory(Journal(DataRoot));
        File.WriteAllText(Path.Combine(Journal(DataRoot), "x"), "x");

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.Equal(2, Meta(fx));                    // nothing overlaid
        Assert.Equal("WINHTTP-V2", File.ReadAllText(Path.Combine(fx.Root, "winhttp.dll")));
    }

    [Fact]
    public async Task A_new_restore_replaces_a_stale_record_from_an_earlier_interrupted_one()
    {
        var fx = Game();
        var ts = await Capture(fx);
        CarryOn(fx);
        RestoreJournal.Write(DataRoot, new InterruptedRestore("old", "old-undo", "valheim", "Valheim", DateTime.UtcNow.AddDays(-1)));

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Null(RestoreJournal.Read(DataRoot));
    }
}
