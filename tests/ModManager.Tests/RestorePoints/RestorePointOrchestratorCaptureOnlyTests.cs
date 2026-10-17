using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Capture-only restore point (Patch Day, ledger D2): snapshot ONE game, write nothing to its folder,
/// never apply an end state. Fakes are duplicated from the sibling orchestrator tests by design.
/// </summary>
public class RestorePointOrchestratorCaptureOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-capture-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private sealed class FakeNexus : INexusGate
    {
        public bool IsConnected => true;
        public int DeleteCalls { get; private set; }
        public void DeleteStoredKey() => DeleteCalls++;
    }
    private sealed class FakeProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }
    private sealed class FakeProvider : IGameProvider
    {
        private readonly List<GameEntry> _games;
        public int ReloadCalls { get; private set; }
        public FakeProvider(IEnumerable<GameEntry> games) => _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() => ReloadCalls++;
    }

    private GameEntry MakeGame(string id, string name, string dataRoot)
    {
        var gameRoot = Path.Combine(_root, "game-" + id);
        Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
        var dataDir = Path.Combine(dataRoot, "_626mods", id);
        Directory.CreateDirectory(dataDir);
        return new GameEntry
        {
            Id = id, GameName = name, GameRoot = gameRoot, DataDir = dataDir,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
    }

    // Two games: "t" (the one captured) and "u" (must stay untouched and absent). "t" has an enabled mod
    // in the game folder, a disabled one held in the data dir, and a launcher-owned file in the data dir.
    private async Task<(GameEntry t, GameEntry u, string dataRoot, RestorePointOrchestrator orch, FakeProvider prov, FakeNexus nexus)> Fixture()
    {
        var dataRoot = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(dataRoot);
        var t = MakeGame("t", "T", dataRoot);
        var u = MakeGame("u", "U", dataRoot);
        File.WriteAllText(Path.Combine(t.GameRoot, "mods", "live.pak"), "LIVE");
        File.WriteAllText(Path.Combine(t.GameRoot, "mods", "held.pak"), "HELD");
        File.WriteAllText(Path.Combine(t.GameRoot, "game.exe"), "EXE");
        File.WriteAllText(Path.Combine(u.GameRoot, "mods", "other.pak"), "OTHER");
        await Scanner.DisableModAsync("held", Scanner.GameContext(t));
        File.WriteAllText(Path.Combine(t.DataDir!, "notes.json"), "{\"v\":1}");
        File.WriteAllText(Path.Combine(dataRoot, "games.json"), "{\"version\":1,\"games\":[]}");
        var prov = new FakeProvider(new[] { t, u });
        var nexus = new FakeNexus();
        var orch = new RestorePointOrchestrator(dataRoot, Path.Combine(dataRoot, "restore-points"), "0.25.0", prov, nexus, new FakeProbe());
        return (t, u, dataRoot, orch, prov, nexus);
    }

    private static Dictionary<string, string> Hash(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return map;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            map[Path.GetRelativePath(dir, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        return map;
    }

    private static Dictionary<string, string> HashTopLevel(string dataRoot)
        => Hash(dataRoot)
            .Where(kv => !kv.Key.StartsWith("restore-points", StringComparison.OrdinalIgnoreCase)
                      && !kv.Key.StartsWith("_626mods", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static string RpDir(string dataRoot, string ts) => Path.Combine(dataRoot, "restore-points", ts);

    [Fact]
    public async Task CaptureGameOnly_leaves_the_game_folder_and_data_dir_byte_identical()
    {
        var (t, u, dataRoot, orch, prov, nexus) = await Fixture();
        var gameBefore = Hash(t.GameRoot); var dataBefore = Hash(t.DataDir!);
        var otherBefore = Hash(u.GameRoot); var otherDataBefore = Hash(u.DataDir!);
        var topBefore = HashTopLevel(dataRoot);
        Assert.NotEmpty(gameBefore); Assert.NotEmpty(dataBefore);

        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal("20261016-120000-t", r.RestorePointTimestamp);   // folder name: {timestamp}-{gameId}
        Assert.Equal(gameBefore, Hash(t.GameRoot));            // game folder + mod dirs
        Assert.Equal(dataBefore, Hash(t.DataDir!));            // no RESTORE-AVAILABLE.json, nothing held or moved
        Assert.Equal(otherBefore, Hash(u.GameRoot));
        Assert.Equal(otherDataBefore, Hash(u.DataDir!));
        Assert.Equal(topBefore, HashTopLevel(dataRoot));       // games.json kept, no lock, no last-clear.json
        Assert.Equal(0, nexus.DeleteCalls);
        Assert.Equal(0, prov.ReloadCalls);                     // no reset, nothing to re-read
        Assert.False(File.Exists(Path.Combine(dataRoot, SafeClearLock.FileName)));
    }

    [Fact]
    public async Task CaptureGameOnly_manifest_holds_only_the_named_game_and_no_end_state_work()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();

        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);
        Assert.True(r.Ok, r.RefusedReason);

        var rp = RpDir(dataRoot, "20261016-120000-t");
        var m = RestorePointManifestStore.Read(rp)!;
        Assert.True(RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion).Ok);
        var only = Assert.Single(m.Games);
        Assert.Equal("t", only.Id);
        Assert.Equal("modsActive", only.EndState);                       // nothing was turned off
        Assert.Null(only.TurnedOffByClear);
        Assert.Empty(only.MovedFiles);
        Assert.Null(only.OffboardingSheetGameFolderPath);                // no sheet is written into the game folder
        Assert.Equal(1, m.SchemaVersion);                                // readable by builds that predate the turn-off record
        Assert.Equal(RestorePointKinds.PatchDay, m.Kind);
        Assert.False(Directory.Exists(Path.Combine(rp, "games", "u")));
        Assert.True(File.Exists(Path.Combine(rp, "games", "t", "data", "notes.json")));   // the captured payload
        Assert.False(File.Exists(Path.Combine(rp, "games.json")));       // launcher registry is not part of a one-game point
        Assert.True(m.TotalBytes > 0 && m.FileCount > 0);
    }

    [Fact]
    public async Task ListRestorePoints_shows_the_capture_with_its_kind()
    {
        var (_, _, _, orch, _, _) = await Fixture();
        Assert.True((await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default)).Ok);

        var info = Assert.Single(orch.ListRestorePoints());
        Assert.Equal("20261016-120000-t", info.Timestamp);
        Assert.Equal(new[] { "T" }, info.GameNames);
        Assert.Equal(RestorePointKinds.PatchDay, info.Kind);
        Assert.True(info.Complete);
    }

    [Fact]
    public async Task A_safe_clear_point_has_no_kind()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, "20261016-130000", default)).Ok);

        Assert.Null(Assert.Single(orch.ListRestorePoints()).Kind);
        var json = File.ReadAllText(Path.Combine(RpDir(dataRoot, "20261016-130000"), RestorePointManifestStore.FileName));
        Assert.DoesNotContain("\"kind\"", json);                         // omitted when null
    }

    [Fact]
    public async Task Free_space_refusal_returns_a_reason_and_leaves_no_restore_point()
    {
        var (t, _, dataRoot, orch, _, _) = await Fixture();
        orch.SpaceCheckForTests = (path, payload) => SpaceCheck.Evaluate(Path.GetPathRoot(Path.GetFullPath(path))!, payload, availableBytes: 0);
        var gameBefore = Hash(t.GameRoot);

        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);

        Assert.False(r.Ok);
        Assert.Null(r.RestorePointTimestamp);
        Assert.Contains("free space", r.RefusedReason!);
        Assert.False(Directory.Exists(RpDir(dataRoot, "20261016-120000-t")));
        Assert.Empty(orch.ListRestorePoints());
        Assert.Equal(gameBefore, Hash(t.GameRoot));
    }

    [Fact]
    public async Task An_unknown_game_is_refused_with_a_reason()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var r = await orch.CaptureGameOnlyAsync("nope", RestorePointKinds.PatchDay, "20261016-120000", default);
        Assert.False(r.Ok);
        Assert.Contains("nope", r.RefusedReason!);
        Assert.False(Directory.Exists(RpDir(dataRoot, "20261016-120000-t")));
    }

    [Fact]
    public async Task A_safe_clear_lock_on_disk_refuses_the_capture_and_is_left_alone()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var lockPath = Path.Combine(dataRoot, SafeClearLock.FileName);
        File.WriteAllText(lockPath, "20261016-110000");

        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);

        Assert.False(r.Ok);
        Assert.Contains("reset", r.RefusedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("20261016-110000", File.ReadAllText(lockPath));     // the clear's breadcrumb is not ours to touch
        Assert.False(Directory.Exists(RpDir(dataRoot, "20261016-120000-t")));
    }

    [Fact]
    public async Task An_existing_restore_point_with_the_same_timestamp_is_refused_and_survives()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        Assert.True((await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default)).Ok);
        var before = Hash(RpDir(dataRoot, "20261016-120000-t"));

        var again = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);

        Assert.False(again.Ok);
        Assert.Equal(before, Hash(RpDir(dataRoot, "20261016-120000-t")));  // never delete a point we did not make
        Assert.Single(orch.ListRestorePoints());
    }

    [Fact]
    public async Task A_capture_that_fails_partway_leaves_no_restore_point_and_no_game_change()
    {
        var (t, _, dataRoot, orch, _, _) = await Fixture();
        var locked = Path.Combine(t.DataDir!, "locked.bin");
        File.WriteAllText(locked, "X");
        var gameBefore = Hash(t.GameRoot);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);
            Assert.False(r.Ok);
            Assert.False(string.IsNullOrWhiteSpace(r.RefusedReason));
        }

        Assert.False(Directory.Exists(RpDir(dataRoot, "20261016-120000-t")));   // partial archive cleaned up
        Assert.Empty(orch.ListRestorePoints());
        Assert.Equal(gameBefore, Hash(t.GameRoot));
        // and a retry after the lock is gone works: the failure left nothing behind to trip over
        Assert.True((await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default)).Ok);
    }

    [Fact]
    public async Task Restore_of_a_kind_point_is_refused_and_touches_nothing()
    {
        var (t, u, dataRoot, orch, prov, _) = await Fixture();
        var ctx = Scanner.GameContext(t);
        var cap = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);
        Assert.True(cap.Ok, cap.RefusedReason);

        // The player carries on after capture: a restore that replayed would have things to undo here.
        File.WriteAllText(Path.Combine(t.DataDir!, "notes.json"), "{\"v\":2}");
        await Scanner.EnableModAsync("held", ctx);
        File.WriteAllText(Path.Combine(t.GameRoot, "mods", "live.pak"), "LIVE-EDITED");
        File.WriteAllText(Path.Combine(dataRoot, "games.json"), "{\"version\":1,\"games\":[{\"edited\":true}]}");
        RestoreMarkers.WriteLastClear(dataRoot, "20261001-000000", "20261001-000000");
        var gameBefore = Hash(t.GameRoot); var dataBefore = Hash(t.DataDir!);
        var otherBefore = Hash(u.GameRoot); var topBefore = HashTopLevel(dataRoot);
        var pointBefore = Hash(RpDir(dataRoot, cap.RestorePointTimestamp!));
        var reloadsBefore = prov.ReloadCalls;

        var restore = await orch.RestoreAsync(cap.RestorePointTimestamp!, default);

        Assert.False(restore.Ok);
        Assert.Equal(RestorePointOrchestrator.KindRestoreUnavailable, restore.RefusedReason);
        Assert.Empty(restore.Conflicts); Assert.Empty(restore.Warnings);
        Assert.Equal(gameBefore, Hash(t.GameRoot));              // game folder and every mod in it
        Assert.Equal(dataBefore, Hash(t.DataDir!));              // data dir: nothing overlaid, nothing resurrected
        Assert.Equal(otherBefore, Hash(u.GameRoot));
        Assert.Equal(topBefore, HashTopLevel(dataRoot));         // registry and the earlier reset's offer
        Assert.NotNull(RestoreMarkers.ReadLastClear(dataRoot));
        Assert.Equal(reloadsBefore, prov.ReloadCalls);
        Assert.Equal(pointBefore, Hash(RpDir(dataRoot, cap.RestorePointTimestamp!)));   // the snapshot is kept
        Assert.Single(orch.ListRestorePoints());
    }

    [Fact]
    public async Task Two_games_captured_with_the_same_timestamp_both_succeed()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();

        var a = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);
        var b = await orch.CaptureGameOnlyAsync("u", RestorePointKinds.PatchDay, "20261016-120000", default);

        Assert.True(a.Ok, a.RefusedReason); Assert.True(b.Ok, b.RefusedReason);
        Assert.NotEqual(a.RestorePointTimestamp, b.RestorePointTimestamp);
        var list = orch.ListRestorePoints();
        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "T" }, list.Single(i => i.Timestamp == a.RestorePointTimestamp).GameNames);
        Assert.Equal(new[] { "U" }, list.Single(i => i.Timestamp == b.RestorePointTimestamp).GameNames);
        Assert.Equal("t", Assert.Single(RestorePointManifestStore.Read(RpDir(dataRoot, a.RestorePointTimestamp!))!.Games).Id);
    }

    [Fact]
    public async Task A_safe_clear_point_keeps_the_bare_timestamp_name()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var r = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, "20261016-130000", default);
        Assert.Equal("20261016-130000", r.RestorePointTimestamp);
        Assert.True(Directory.Exists(RpDir(dataRoot, "20261016-130000")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("2026..1016")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("C:evil")]
    public async Task A_timestamp_that_is_not_a_plain_name_is_refused(string ts)
    {
        var (t, _, dataRoot, orch, _, _) = await Fixture();
        var gameBefore = Hash(t.GameRoot);

        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, ts, default);

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.RefusedReason));
        Assert.Null(r.RestorePointTimestamp);
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "restore-points")) && Directory.GetDirectories(Path.Combine(dataRoot, "restore-points")).Length > 0);
        Assert.False(Directory.Exists(Path.Combine(_root, "evil")));
        Assert.Equal(gameBefore, Hash(t.GameRoot));
    }

    [Fact]
    public async Task A_game_id_that_is_not_a_plain_name_is_refused()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var r = await orch.CaptureGameOnlyAsync("../t", RestorePointKinds.PatchDay, "20261016-120000", default);
        Assert.False(r.Ok);
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "restore-points")) && Directory.GetDirectories(Path.Combine(dataRoot, "restore-points")).Length > 0);
    }

    [Fact]
    public async Task The_claim_marker_is_not_counted_in_the_manifest_totals()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var r = await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default);
        var rp = RpDir(dataRoot, r.RestorePointTimestamp!);
        var m = RestorePointManifestStore.Read(rp)!;
        var files = Directory.GetFiles(rp, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(RestorePointManifestStore.FileName) && !f.EndsWith(RestorePointOrchestrator.CaptureClaimFile)).ToList();
        Assert.Equal(files.Count, m.FileCount);
        Assert.Equal(files.Sum(f => new FileInfo(f).Length), m.TotalBytes);
    }

    private static string Abandon(string dataRoot, string name, bool claimed, bool sealedPoint, TimeSpan age)
    {
        var dir = Path.Combine(dataRoot, "restore-points", name);
        Directory.CreateDirectory(Path.Combine(dir, "games", "x"));
        File.WriteAllText(Path.Combine(dir, "games", "x", "half.bin"), "half");
        if (claimed)
        {
            var claim = Path.Combine(dir, RestorePointOrchestrator.CaptureClaimFile);
            File.WriteAllText(claim, name);
            File.SetLastWriteTimeUtc(claim, DateTime.UtcNow - age);
        }
        if (sealedPoint)
            RestorePointManifestStore.WriteSealed(dir, new RestorePointManifest(1, "0.25.0", name, true, true, 1, 1, Array.Empty<GameArchive>(), "patchDay"));
        return dir;
    }

    [Fact]
    public async Task A_later_capture_sweeps_an_old_unsealed_claimed_folder_and_nothing_else()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var old = TimeSpan.FromHours(2);
        var stale = Abandon(dataRoot, "20260101-000000-x", claimed: true, sealedPoint: false, age: old);
        var young = Abandon(dataRoot, "20260101-000001-x", claimed: true, sealedPoint: false, age: TimeSpan.FromSeconds(5));
        var unclaimed = Abandon(dataRoot, "20260101-000002", claimed: false, sealedPoint: false, age: old);   // a Safe Clear's partial
        var sealedOld = Abandon(dataRoot, "20260101-000003-x", claimed: true, sealedPoint: true, age: old);

        Assert.True((await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default)).Ok);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(young));       // might be another process mid-copy
        Assert.True(Directory.Exists(unclaimed));   // not ours: recovery's to deal with
        Assert.True(Directory.Exists(sealedOld));   // a real restore point
    }

    [Fact]
    public async Task A_capture_blocked_by_a_safe_clear_lock_sweeps_nothing()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        var stale = Abandon(dataRoot, "20260101-000000-x", claimed: true, sealedPoint: false, age: TimeSpan.FromHours(2));
        File.WriteAllText(Path.Combine(dataRoot, SafeClearLock.FileName), "20260101-000000-x");

        Assert.False((await orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", default)).Ok);

        Assert.True(Directory.Exists(stale));
    }

    [Fact]
    public async Task Restore_of_a_safe_clear_point_still_clears_the_last_clear_marker()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, "20261016-130000", default)).Ok);
        Assert.NotNull(RestoreMarkers.ReadLastClear(dataRoot));

        Assert.True((await orch.RestoreAsync("20261016-130000", default)).Ok);

        Assert.Null(RestoreMarkers.ReadLastClear(dataRoot));
    }

    [Fact]
    public async Task A_cancelled_token_captures_nothing()
    {
        var (_, _, dataRoot, orch, _, _) = await Fixture();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orch.CaptureGameOnlyAsync("t", RestorePointKinds.PatchDay, "20261016-120000", cts.Token));
        Assert.False(Directory.Exists(RpDir(dataRoot, "20261016-120000-t")));
    }

    [Fact]
    public void Manifest_kind_round_trips_as_camelCase_and_is_omitted_when_null()
    {
        var dir = Path.Combine(_root, "manifest"); Directory.CreateDirectory(dir);
        var withKind = new RestorePointManifest(1, "0.25.0", "20261016-120000", true, true, 10, 1,
            Array.Empty<GameArchive>(), Kind: "patchDay");
        RestorePointManifestStore.WriteSealed(dir, withKind);
        var json = File.ReadAllText(Path.Combine(dir, RestorePointManifestStore.FileName));
        Assert.Contains("\"kind\": \"patchDay\"", json);
        Assert.DoesNotContain("\"Kind\"", json);
        Assert.Equal("patchDay", RestorePointManifestStore.Read(dir)!.Kind);

        RestorePointManifestStore.WriteSealed(dir, withKind with { Kind = null });
        Assert.DoesNotContain("kind", File.ReadAllText(Path.Combine(dir, RestorePointManifestStore.FileName)), StringComparison.OrdinalIgnoreCase);
        Assert.Null(RestorePointManifestStore.Read(dir)!.Kind);
    }
}
