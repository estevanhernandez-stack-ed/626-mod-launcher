using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// The automatic cleanup of <c>beforeRestore</c> undo snapshots, as the orchestrator runs it: after a Kind
/// restore that SUCCEEDED, never on a refusal or a failure, never on Safe Clear, and only ever against the
/// restoring game's own sealed undo snapshots. Everything else in the restore-points folder is byte-identical
/// afterwards.
/// </summary>
public class RestorePointOrchestratorPruneTests : IDisposable
{
    private const string Cap = "20261016-120000";
    private const string Snap1 = "20261017-110000";                       // an hour before Now: young
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Installed = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-prune-" + Guid.NewGuid().ToString("n"));
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

    private sealed record Fx(GameEntry Game, RestorePointOrchestrator Orch, FakeProbe Probe, string Win64, string Data);

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private static Dictionary<string, string> Snapshot(string dir)
        => !Directory.Exists(dir)
            ? new Dictionary<string, string>()
            : Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(dir, p),
                    p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.OrdinalIgnoreCase);

    private Fx Ue4ss()
    {
        var root = Path.Combine(_root, "Palworld");
        var win64 = Path.Combine(root, "Pal", "Binaries", "Win64");
        Put(Path.Combine(win64, "Palworld-Win64-Shipping.exe"), "EXE");
        Put(Path.Combine(win64, "dwmapi.dll"), "PROXY-V1");
        Put(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "UE4SS-V1");
        var data = Path.Combine(DataRoot, "_626mods", "palworld");
        Directory.CreateDirectory(data);
        var g = new GameEntry
        {
            Id = "palworld", GameName = "Palworld", Engine = "ue-pak", GameRoot = root, DataDir = data,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "Pal/Content/Paks/~mods") },
        };
        Directory.CreateDirectory(Path.Combine(root, "Pal", "Content", "Paks", "~mods"));
        var fwDir = Path.Combine(data, "frameworks", "ue4ss");
        Directory.CreateDirectory(fwDir);
        File.WriteAllText(Path.Combine(fwDir, "install.json"), System.Text.Json.JsonSerializer.Serialize(
            new FrameworkInstallManifest("ue4ss", "UE4SS", "team", win64, new[] { "dwmapi.dll", "ue4ss/UE4SS.dll" }, Installed, null),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        Put(Path.Combine(data, "metadata.json"), "{\"v\":1}");
        return Wire(g, win64, data);
    }

    private Fx Wire(GameEntry g, string win64, string data)
    {
        var probe = new FakeProbe();
        var orch = new RestorePointOrchestrator(DataRoot, Points, "0.25.0", new FakeProvider(new[] { g }), new FakeNexus(), probe)
        { UtcNowForTests = () => Now };
        return new Fx(g, orch, probe, win64, data);
    }

    private static string Old(int days, string game = "palworld") => $"{Now.AddDays(-days):yyyyMMdd-HHmmss}-{game}";

    // A real undo snapshot of the game, named for `days` ago.
    private async Task<string> Seed(Fx fx, int days, string kind = RestorePointKinds.BeforeRestore)
    {
        var r = await fx.Orch.CaptureGameOnlyAsync(fx.Game.Id, kind, Now.AddDays(-days).ToString("yyyyMMdd-HHmmss"), default);
        Assert.True(r.Ok, r.RefusedReason);
        return r.RestorePointTimestamp!;
    }

    // A hand-made point: for the shapes the capture path would never produce for this game.
    private void Hand(string name, string? kind, string gameId, bool complete = true, int schema = 1, bool manifest = true)
    {
        var dir = Path.Combine(Points, name);
        Put(Path.Combine(dir, "games", gameId, "payload.txt"), "payload of " + name);
        if (!manifest) return;
        var ga = new GameArchive(gameId, gameId, "C:\\x", "modsActive", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null);
        RestorePointManifestStore.WriteSealed(dir,
            new RestorePointManifest(schema, "0.25.0", name, complete, true, 1, 1, new[] { ga }, kind));
    }

    private async Task<string> CapturePatchDay(Fx fx)
    {
        var cap = await fx.Orch.CaptureGameOnlyAsync(fx.Game.Id, RestorePointKinds.PatchDay, Cap, default);
        Assert.True(cap.Ok, cap.RefusedReason);
        Put(Path.Combine(fx.Win64, "dwmapi.dll"), "PROXY-V2");   // the player carries on, so the restore has something to do
        return cap.RestorePointTimestamp!;
    }

    private static string Undo(string snapshotTimestamp) => $"{snapshotTimestamp}-palworld";

    // ---- the headline ----

    [Fact]
    public async Task A_successful_restore_keeps_the_newest_three_undo_snapshots_and_says_what_it_removed()
    {
        var fx = Ue4ss();
        var old = new[] { await Seed(fx, 2), await Seed(fx, 3), await Seed(fx, 4), await Seed(fx, 5) };
        var ts = await CapturePatchDay(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        // The new undo snapshot counts as one of the three: with it, that is the newest two old ones.
        Assert.True(Directory.Exists(Path.Combine(Points, Undo(Snap1))));
        Assert.True(Directory.Exists(Path.Combine(Points, old[0])));
        Assert.True(Directory.Exists(Path.Combine(Points, old[1])));
        Assert.False(Directory.Exists(Path.Combine(Points, old[2])));
        Assert.False(Directory.Exists(Path.Combine(Points, old[3])));
        Assert.True(Directory.Exists(Path.Combine(Points, ts)));                       // the Patch Day point is not an undo snapshot
        Assert.Contains("Removed 2 older undo snapshots for Palworld (kept the newest 3).", r.Warnings);
        Assert.Contains(r.Warnings, w => w.Contains(Undo(Snap1)));                     // and the way back is still named
        Assert.Equal(4, fx.Orch.ListRestorePoints().Count);
    }

    [Fact]
    public async Task One_removal_reads_as_the_singular()
    {
        var fx = Ue4ss();
        await Seed(fx, 2); await Seed(fx, 3); var oldest = await Seed(fx, 4);
        var ts = await CapturePatchDay(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.False(Directory.Exists(Path.Combine(Points, oldest)));
        Assert.Contains("Removed 1 older undo snapshot for Palworld (kept the newest 3).", r.Warnings);
    }

    [Fact]
    public async Task Nothing_to_remove_adds_no_note()
    {
        var fx = Ue4ss();
        await Seed(fx, 2);
        var ts = await CapturePatchDay(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("Removed"));
        Assert.Equal(3, fx.Orch.ListRestorePoints().Count);
    }

    [Fact]
    public async Task A_young_undo_snapshot_is_never_removed_even_when_it_ranks_fourth()
    {
        var fx = Ue4ss();
        // Ranked newest first: the new undo snapshot (1h), then 2h, 3h, 4h and 20h, then one three days old. The
        // 4h and 20h ones sit outside the three kept but are under a day old, so only the old one goes.
        var h = new[] { 2, 3, 4 }.Select(hours => $"{Now.AddHours(-hours):yyyyMMdd-HHmmss}").ToArray();
        foreach (var stamp in h)
            Assert.True((await fx.Orch.CaptureGameOnlyAsync("palworld", RestorePointKinds.BeforeRestore, stamp, default)).Ok);
        var young = $"{Now.AddHours(-20):yyyyMMdd-HHmmss}-palworld";
        Assert.True((await fx.Orch.CaptureGameOnlyAsync("palworld", RestorePointKinds.BeforeRestore, young[..15], default)).Ok);
        var oldName = await Seed(fx, 3);
        var ts = await CapturePatchDay(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);   

        Assert.True(r.Ok, r.RefusedReason);
        Assert.True(Directory.Exists(Path.Combine(Points, young)));        // 5th newest, 20h old
        Assert.True(Directory.Exists(Path.Combine(Points, $"{h[2]}-palworld")));   // 4th newest, 4h old
        Assert.False(Directory.Exists(Path.Combine(Points, oldName)));     // the only one old enough
        Assert.Contains("Removed 1 older undo snapshot for Palworld (kept the newest 3).", r.Warnings);
    }

    // ---- what is never deleted ----

    [Fact]
    public async Task Patch_Day_Safe_Clear_other_game_unknown_kind_and_unsealed_points_are_left_byte_identical()
    {
        var fx = Ue4ss();
        var thirty = 30;
        await Seed(fx, thirty, RestorePointKinds.PatchDay);                             // a Patch Day point for this game
        Hand(Old(thirty + 1), null, "palworld");                                        // a Safe Clear point (no kind)
        Hand("20250101-000000", null, "palworld");                                      // ... and its bare-timestamp shape
        Hand(Old(thirty + 2, "valheim"), RestorePointKinds.BeforeRestore, "valheim");   // another game's undo snapshot
        Hand(Old(thirty + 3), "somethingNewer", "palworld");                            // a kind from a newer build
        Hand(Old(thirty + 4), RestorePointKinds.BeforeRestore, "palworld", manifest: false);               // no manifest
        Put(Path.Combine(Points, Old(thirty + 5), RestorePointOrchestrator.CaptureClaimFile), "claimed");   // claimed, unsealed
        Hand(Old(thirty + 6), RestorePointKinds.BeforeRestore, "palworld", complete: false);               // manifest, not sealed
        Hand(Old(thirty + 7), RestorePointKinds.BeforeRestore, "palworld", schema: 3);                     // sealed by a newer build
        var old = new[] { await Seed(fx, 2), await Seed(fx, 3), await Seed(fx, 4), await Seed(fx, 5), await Seed(fx, 6) };
        var ts = await CapturePatchDay(fx);
        var untouchable = Directory.GetDirectories(Points).Select(Path.GetFileName).Select(n => n!)
            .Except(old).ToDictionary(n => n, n => Snapshot(Path.Combine(Points, n)));
        Assert.Equal(10, untouchable.Count);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        foreach (var (name, hashes) in untouchable)
        {
            Assert.True(Directory.Exists(Path.Combine(Points, name)), name + " vanished");
            Assert.Equal(hashes, Snapshot(Path.Combine(Points, name)));
        }
        // And the rule did its job on the ones it owns: undo + the two newest old ones stay.
        Assert.Equal(new[] { old[0], old[1] }.OrderBy(n => n),
            old.Where(n => Directory.Exists(Path.Combine(Points, n))).OrderBy(n => n));
        Assert.Contains("Removed 3 older undo snapshots for Palworld (kept the newest 3).", r.Warnings);
    }

    [Fact]
    public async Task Restoring_an_old_undo_snapshot_never_removes_the_one_you_restored_from()
    {
        var fx = Ue4ss();
        await Seed(fx, 2); await Seed(fx, 3); var src = await Seed(fx, 5); var other = await Seed(fx, 4);
        Put(Path.Combine(fx.Win64, "dwmapi.dll"), "PROXY-V2");

        var r = await fx.Orch.RestoreAsync(src, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.True(Directory.Exists(Path.Combine(Points, src)));                      // the oldest, yet kept: it is the source
        Assert.False(Directory.Exists(Path.Combine(Points, other)));                   // the next oldest goes
        Assert.Contains("Removed 1 older undo snapshot for Palworld (kept the newest 3).", r.Warnings);
    }

    [Fact]
    public async Task A_journal_that_is_still_there_and_cannot_be_read_stops_the_cleanup_and_the_restore_still_succeeds()
    {
        var fx = Ue4ss();
        var old = new[] { await Seed(fx, 2), await Seed(fx, 3), await Seed(fx, 4), await Seed(fx, 5) };
        var ts = await CapturePatchDay(fx);
        FileStream? held = null;
        fx.Orch.BeforeOverlayForTests = () =>
        {
            File.WriteAllText(RestoreJournal.PathFor(DataRoot), "garbage");
            held = new FileStream(RestoreJournal.PathFor(DataRoot), FileMode.Open, FileAccess.Read, FileShare.None);   // Clear() can't remove it
        };
        try
        {
            var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

            Assert.True(r.Ok, r.RefusedReason);
            Assert.All(old, n => Assert.True(Directory.Exists(Path.Combine(Points, n))));
            Assert.DoesNotContain(r.Warnings, w => w.Contains("Removed"));
        }
        finally { held?.Dispose(); }
    }

    // ---- never on a refusal or a failure ----

    [Fact]
    public async Task A_refused_restore_prunes_nothing()
    {
        var fx = Ue4ss();
        for (var d = 2; d <= 6; d++) await Seed(fx, d);
        var ts = await CapturePatchDay(fx);
        var before = Snapshot(Points);

        fx.Probe.Running = true;                                                        // refused: the game is running
        var running = await fx.Orch.RestoreAsync(ts, default, Snap1);
        Assert.False(running.Ok);
        Assert.Equal(before, Snapshot(Points));

        fx.Probe.Running = false;                                                       // refused: no room for the undo snapshot
        fx.Orch.SpaceCheckForTests = (path, payload) =>
            SpaceCheck.Evaluate(Path.GetPathRoot(Path.GetFullPath(path))!, payload, availableBytes: 0);
        var noRoom = await fx.Orch.RestoreAsync(ts, default, Snap1);
        Assert.False(noRoom.Ok);
        Assert.Equal(before, Snapshot(Points));

        fx.Orch.SpaceCheckForTests = null;                                              // refused: a reset is unfinished
        File.WriteAllText(Path.Combine(DataRoot, SafeClearLock.FileName), "20261001-000000");
        var locked = await fx.Orch.RestoreAsync(ts, default, Snap1);
        Assert.False(locked.Ok);
        Assert.Equal(before, Snapshot(Points));
    }

    [Fact]
    public async Task A_restore_that_fails_partway_through_the_overlay_prunes_nothing()
    {
        var fx = Ue4ss();
        var old = new List<string>();
        for (var d = 2; d <= 6; d++) old.Add(await Seed(fx, d));
        var ts = await CapturePatchDay(fx);
        FileStream? held = null;
        fx.Orch.BeforeOverlayForTests = () =>
            held = new FileStream(Path.Combine(fx.Win64, "dwmapi.dll"), FileMode.Open, FileAccess.Read, FileShare.None);   // the swap can't land
        try
        {
            var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

            Assert.False(r.Ok);
            Assert.Contains("couldn't finish", r.RefusedReason!);
            Assert.All(old, n => Assert.True(Directory.Exists(Path.Combine(Points, n)), n + " vanished"));
            Assert.True(File.Exists(RestoreJournal.PathFor(DataRoot)));                  // the breadcrumb stays, as before
        }
        finally { held?.Dispose(); }
    }

    // ---- a delete that fails ----

    [Fact]
    public async Task A_delete_that_fails_is_a_warning_and_the_restore_still_succeeds()
    {
        var fx = Ue4ss();
        await Seed(fx, 2); await Seed(fx, 3);
        var stuck = await Seed(fx, 4);
        var gone = await Seed(fx, 5);
        var ts = await CapturePatchDay(fx);
        using var held = new FileStream(Path.Combine(Points, stuck, RestorePointOrchestrator.CaptureClaimFile),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.True(Directory.Exists(Path.Combine(Points, stuck)));
        Assert.False(Directory.Exists(Path.Combine(Points, gone)));
        Assert.Contains(r.Warnings, w => w.StartsWith("Couldn't remove 1 older undo snapshot for Palworld") && w.Contains(stuck));
        Assert.Contains("Removed 1 older undo snapshot for Palworld (kept the newest 3).", r.Warnings);
        Assert.Equal("PROXY-V1", File.ReadAllText(Path.Combine(fx.Win64, "dwmapi.dll")));   // and the restore itself landed
    }

    // ---- Safe Clear is not this ----

    [Fact]
    public async Task A_Safe_Clear_restore_prunes_nothing()
    {
        var root = Path.Combine(_root, "pak");
        Put(Path.Combine(root, "mods", "live.pak"), "LIVE");
        var data = Path.Combine(DataRoot, "_626mods", "pak");
        Directory.CreateDirectory(data);
        var g = new GameEntry
        {
            Id = "pak", GameName = "Pak", GameRoot = root, DataDir = data,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        var fx = Wire(g, root, data);
        var seeded = new List<string>();
        for (var d = 2; d <= 7; d++)
            seeded.Add((await fx.Orch.CaptureGameOnlyAsync("pak", RestorePointKinds.BeforeRestore, Now.AddDays(-d).ToString("yyyyMMdd-HHmmss"), default)).RestorePointTimestamp!);
        Assert.True((await fx.Orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, Cap, default)).Ok);
        var before = Snapshot(Points);

        var r = await fx.Orch.RestoreAsync(Cap, default);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.All(seeded, n => Assert.True(Directory.Exists(Path.Combine(Points, n))));
        Assert.Equal(before, Snapshot(Points));
        Assert.DoesNotContain(r.Warnings, w => w.Contains("undo snapshot"));
    }
}
