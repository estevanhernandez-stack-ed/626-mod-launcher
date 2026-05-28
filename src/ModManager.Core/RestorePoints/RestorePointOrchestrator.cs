namespace ModManager.Core.RestorePoints;

/// <summary>
/// Drives the Phase 1A engine through the Law-A Safe Clear sequence: pre-flight (game-running,
/// reachable, free space) -> capture-all -> seal LAST -> mutate-all -> reset (honoring keep-Nexus).
/// Pure Core: takes the data root + restore-points root as paths, plus the App seams. The App's
/// RestorePointService adapts %APPDATA%/DPAPI/Process to these. (Restore + recovery are added in
/// Tasks 4 + 5.) NOTE on Option B: this is in Core so it's headless-testable; the timestamp is
/// passed in (no DateTime.Now in Core).
/// </summary>
public sealed class RestorePointOrchestrator
{
    private readonly string _dataRoot;
    private readonly string _restorePointsRoot;
    private readonly string _launcherVersion;
    private readonly IGameProvider _provider;
    private readonly INexusGate _nexus;
    private readonly IGameRunningProbe _probe;
    private readonly SemaphoreSlim _gate = new(1, 1);   // Law F — one writer

    // Top-level launcher state the orchestrator owns (relative to dataRoot). nexus.json is NEVER
    // archived (Law D) — it's handled only by the keep/skip branch via the nexus gate.
    private static readonly string[] TopLevelDirs = { "themes", "profile" };
    private static readonly string[] TopLevelFiles = { "games.json", "app-settings.json" };
    private const string LockName = "safe-clear.lock";

    public RestorePointOrchestrator(string dataRoot, string restorePointsRoot, string launcherVersion,
        IGameProvider provider, INexusGate nexus, IGameRunningProbe probe)
    {
        _dataRoot = dataRoot; _restorePointsRoot = restorePointsRoot; _launcherVersion = launcherVersion;
        _provider = provider; _nexus = nexus; _probe = probe;
    }

    public async Task<SafeClearResult> SafeClearAsync(SafeClearOptions opts, string timestamp, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var games = _provider.Games;

            // PRE-FLIGHT (Law E) — change nothing.
            foreach (var g in games)
                if (_probe.AnyRunning(g))
                    return new SafeClearResult(false, $"Close {g.GameName} before resetting the launcher.", null,
                        Array.Empty<string>(), Array.Empty<string>());
            foreach (var g in games)
            {
                var ctx = _provider.ContextFor(g);
                if (!DriveReachable(ctx.GameRoot) || !DriveReachable(ctx.DataDir))
                    return new SafeClearResult(false, $"{g.GameName}'s drive is unavailable — reconnect it and try again.",
                        null, Array.Empty<string>(), Array.Empty<string>());
            }
            if (opts.CreateRestorePoint)
            {
                long payload = TopLevelBytes();
                foreach (var g in games) payload += FileTally.ByteSize(_provider.ContextFor(g).DataDir);
                var space = SpaceCheck.Require(_restorePointsRoot, payload);
                if (!space.Ok)
                    return new SafeClearResult(false,
                        $"Not enough free space: need ~{Gb(space.RequiredBytes)} GB, have {Gb(space.AvailableBytes)} GB on {space.VolumeRoot}.",
                        null, Array.Empty<string>(), Array.Empty<string>());
            }

            // LOCK (crash breadcrumb).
            Directory.CreateDirectory(_dataRoot);
            var lockPath = Path.Combine(_dataRoot, LockName);
            File.WriteAllText(lockPath, timestamp);

            var rpDir = Path.Combine(_restorePointsRoot, timestamp);
            var sheetPaths = new List<string>();
            var warnings = new List<string>();

            // CAPTURE-ALL + SEAL (Law A) — only when archiving.
            if (opts.CreateRestorePoint)
            {
                Directory.CreateDirectory(rpDir);
                var archives = new List<GameArchive>();
                foreach (var g in games)
                {
                    var ctx = _provider.ContextFor(g);
                    archives.Add(RestorePointEngine.CaptureGame(
                        new GameCaptureInput(g, ctx, EndStateFor(g.Id, opts)),
                        Path.Combine(rpDir, "games", g.Id)));
                }
                CopyTopLevelInto(rpDir);   // games.json + themes/ + profile/ + app-settings.json — NOT nexus.json
                var manifest = new RestorePointManifest(
                    RestorePoint.SchemaVersion, _launcherVersion, timestamp,
                    Complete: true, opts.KeepNexus,
                    FileTally.ByteSize(rpDir), FileTally.FileCount(rpDir), archives);
                RestorePointManifestStore.WriteSealed(rpDir, manifest);   // THE SEAL — written last
            }

            // MUTATE-ALL (after the seal).
            foreach (var g in games)
            {
                var ctx = _provider.ContextFor(g);
                // Even with skip-archive this MOVES (never deletes) — vanilla-moved files land under rpDir.
                RestorePointEngine.ApplyEndState(ctx, EndStateFor(g.Id, opts), Path.Combine(rpDir, "games", g.Id));
                if (opts.CreateRestorePoint) RestoreMarkers.WriteRestoreAvailable(ctx.DataDir, timestamp);
                sheetPaths.Add(Path.Combine(ctx.GameRoot, "626-launcher-how-to-launch.txt"));
            }

            // RESET — delete top-level launcher state (archived); nexus only if not keeping it.
            DeleteTopLevelState();
            if (!opts.KeepNexus) _nexus.DeleteStoredKey();
            _provider.Reload();
            if (opts.CreateRestorePoint) RestoreMarkers.WriteLastClear(_dataRoot, timestamp, timestamp);
            File.Delete(lockPath);

            return new SafeClearResult(true, null, opts.CreateRestorePoint ? timestamp : null, sheetPaths, warnings);
        }
        finally { _gate.Release(); }
    }

    private static string EndStateFor(string id, SafeClearOptions opts)
        => opts.PerGameEndState.TryGetValue(id, out var s) ? s : opts.DefaultEndState;

    private static bool DriveReachable(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return string.IsNullOrEmpty(root) || Directory.Exists(root);
    }

    private long TopLevelBytes()
    {
        long n = 0;
        foreach (var d in TopLevelDirs) n += FileTally.ByteSize(Path.Combine(_dataRoot, d));
        foreach (var f in TopLevelFiles) { var p = Path.Combine(_dataRoot, f); if (File.Exists(p)) n += new FileInfo(p).Length; }
        return n;
    }

    private void CopyTopLevelInto(string rpDir)
    {
        foreach (var f in TopLevelFiles)
        {
            var src = Path.Combine(_dataRoot, f);
            if (File.Exists(src)) { Directory.CreateDirectory(rpDir); File.Copy(src, Path.Combine(rpDir, f), overwrite: true); }
        }
        foreach (var d in TopLevelDirs)
        {
            var src = Path.Combine(_dataRoot, d);
            if (Directory.Exists(src)) SafeMove.CopyDirVerified(src, Path.Combine(rpDir, d));
        }
    }

    private void DeleteTopLevelState()
    {
        foreach (var f in TopLevelFiles) { try { var p = Path.Combine(_dataRoot, f); if (File.Exists(p)) File.Delete(p); } catch { } }
        foreach (var d in TopLevelDirs) { try { var p = Path.Combine(_dataRoot, d); if (Directory.Exists(p)) Directory.Delete(p, recursive: true); } catch { } }
    }

    private static long Gb(long bytes) => Math.Max(1, bytes / (1024 * 1024 * 1024));
}
