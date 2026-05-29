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
    private const string SheetFileName = "626-launcher-how-to-launch.txt";

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
            {
                bool running;
                try { running = _probe.AnyRunning(g); }
                catch
                {
                    // Law E fails CLOSED on a destructive op: if the probe can't verify whether the
                    // game is running, refuse rather than risk resetting over possibly-live files.
                    return new SafeClearResult(false,
                        $"Couldn't verify whether {g.GameName} is running — close it and try again.", null,
                        Array.Empty<string>(), Array.Empty<string>());
                }
                if (running)
                    return new SafeClearResult(false, $"Close {g.GameName} before resetting the launcher.", null,
                        Array.Empty<string>(), Array.Empty<string>());
            }
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

            // Sealed vanilla-move plans keyed by game ID: populated in CAPTURE-ALL, consumed in
            // MUTATE-ALL. MUTATE passes the sealed plan to ApplyEndState so it executes EXACTLY that
            // set — no re-detect drift if the game folder changes between the two phases.
            var plannedByGame = new Dictionary<string, IReadOnlyList<MovedFile>>();

            // CAPTURE-ALL (Law A) — non-destructive. For vanilla games, plan the moves NOW while
            // files are still in place and seal those planned MovedFiles into the manifest BEFORE
            // any move executes. Law A: seal before destroy.
            if (opts.CreateRestorePoint)
            {
                Directory.CreateDirectory(rpDir);
                var archives = new List<GameArchive>();
                foreach (var g in games)
                {
                    var ctx = _provider.ContextFor(g);
                    var endState = EndStateFor(g.Id, opts);
                    var ga = RestorePointEngine.CaptureGame(new GameCaptureInput(g, ctx, endState), Path.Combine(rpDir, "games", g.Id));
                    var sheetPath = Path.Combine(ctx.GameRoot, SheetFileName);
                    // Law A: record the PLANNED vanilla moves NOW (files still in place) so the SEALED
                    // manifest carries them. The actual move happens in MUTATE-ALL, AFTER the seal.
                    if (string.Equals(endState, "vanilla", StringComparison.OrdinalIgnoreCase))
                    {
                        var planned = RestorePointEngine.PlanVanillaMoves(ctx);
                        plannedByGame[g.Id] = planned;
                        ga = ga with { MovedFiles = planned, OffboardingSheetGameFolderPath = sheetPath };
                    }
                    else
                    {
                        ga = ga with { OffboardingSheetGameFolderPath = sheetPath };
                    }
                    archives.Add(ga);
                }
                CopyTopLevelInto(rpDir);   // games.json + themes/ + profile/ + app-settings.json — NOT nexus.json
                // TotalBytes/FileCount include the planned vanilla-moved payload (moved after the seal).
                long movedBytes = archives.Sum(a => a.MovedFiles.Sum(mf => mf.Bytes));
                int movedCount = archives.Sum(a => a.MovedFiles.Count);
                var manifest = new RestorePointManifest(
                    RestorePoint.SchemaVersion, _launcherVersion, timestamp,
                    Complete: true, opts.KeepNexus,
                    FileTally.ByteSize(rpDir) + movedBytes, FileTally.FileCount(rpDir) + movedCount, archives);
                RestorePointManifestStore.WriteSealed(rpDir, manifest);   // THE SEAL — before any move (Law A)
            }

            // MUTATE-ALL — executes the planned moves (and other end-state work) AFTER the seal.
            // For vanilla games with a sealed plan, passes the plan so ApplyEndState moves EXACTLY
            // that set. For skip-archive (CreateRestorePoint=false), plannedByGame is empty → null →
            // ApplyEndState plans itself.
            // NOTE: with skip-archive + vanilla, ApplyEndState still MOVES direct-inject files into
            // rpDir/games/<id>/vanilla-moved (never deletes) — but no manifest is sealed and no marker
            // is written, so this folder is NOT a managed restore point. Files are preserved on disk
            // (recoverable manually); ListRestorePoints shows only sealed points.
            foreach (var g in games)
            {
                var ctx = _provider.ContextFor(g);
                RestorePointEngine.ApplyEndState(ctx, EndStateFor(g.Id, opts), Path.Combine(rpDir, "games", g.Id),
                    plannedByGame.TryGetValue(g.Id, out var pm) ? pm : null);
                if (opts.CreateRestorePoint) RestoreMarkers.WriteRestoreAvailable(ctx.DataDir, timestamp);
                sheetPaths.Add(Path.Combine(ctx.GameRoot, SheetFileName));
            }

            // RESET — delete top-level launcher state (archived); nexus only if not keeping it.
            DeleteTopLevelState();
            if (!opts.KeepNexus) _nexus.DeleteStoredKey();
            _provider.Reload();
            if (opts.CreateRestorePoint) RestoreMarkers.WriteLastClear(_dataRoot, timestamp, timestamp);
            try { File.Delete(lockPath); } catch { /* best-effort: startup recovery (Task 5) handles a stale lock */ }

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

    private static string Gb(long bytes) => (bytes / 1_073_741_824.0).ToString("0.0");

    // ── Tasks 4 + 5 ─────────────────────────────────────────────────────────────────────────────

    public async Task<RestoreResult> RestoreAsync(string timestamp, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var rpDir = Path.Combine(_restorePointsRoot, timestamp);
            var m = RestorePointManifestStore.Read(rpDir);
            var v = RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion);
            if (!v.Ok) return new RestoreResult(false, v.Reason, Array.Empty<RestoreConflict>(), Array.Empty<string>());

            var conflicts = RestoreReconcile.Check(m!, _provider.Games);
            if (conflicts.Count > 0)
                return new RestoreResult(false,
                    "Some games have moved since this restore point — resolve the conflicts first.",
                    conflicts, Array.Empty<string>());

            // Top-level launcher state back, verbatim (games.json + themes/ + profile/ + app-settings.json).
            RestoreTopLevelFrom(rpDir);
            _provider.Reload();   // re-read the restored games.json (no-op in tests; real impl re-reads disk)

            var warnings = new List<string>();
            foreach (var ga in m!.Games)
            {
                var game = _provider.Games.FirstOrDefault(g => g.Id == ga.Id);
                if (game is null) { warnings.Add($"{ga.GameName}: not in the restored registry — skipped."); continue; }
                RestorePointEngine.ReplayGame(ga, Path.Combine(rpDir, "games", ga.Id), _provider.ContextFor(game));
            }

            RestoreMarkers.ClearLastClear(_dataRoot);
            return new RestoreResult(true, null, Array.Empty<RestoreConflict>(), warnings);
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<RestorePointInfo> ListRestorePoints()
    {
        if (!Directory.Exists(_restorePointsRoot)) return Array.Empty<RestorePointInfo>();
        var list = new List<RestorePointInfo>();
        foreach (var dir in Directory.GetDirectories(_restorePointsRoot))
        {
            var m = RestorePointManifestStore.Read(dir);
            if (m is null || !m.Complete) continue;   // skip unsealed / partial
            list.Add(new RestorePointInfo(
                Path.GetFileName(dir),
                m.Games.Select(g => g.GameName).ToList(),
                m.TotalBytes,
                m.Complete));
        }
        return list.OrderByDescending(r => r.Timestamp, StringComparer.Ordinal).ToList();
    }

    public void DeleteRestorePoint(string timestamp)
    {
        var dir = Path.Combine(_restorePointsRoot, timestamp);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>If a safe-clear.lock exists, a Safe Clear was interrupted.
    /// <c>Sealed=true</c> means the capture finished (the archive is valid — offer resume/restore).
    /// <c>Sealed=false</c> means it died before the seal (the original is intact — offer to discard
    /// the partial archive and recover by discarding it).</summary>
    public InterruptedClear? DetectInterruptedClear()
    {
        var lockPath = Path.Combine(_dataRoot, LockName);
        if (!File.Exists(lockPath)) return null;
        try
        {
            var ts = File.ReadAllText(lockPath).Trim();
            var sealed_ = RestorePointManifestStore.Validate(
                RestorePointManifestStore.Read(Path.Combine(_restorePointsRoot, ts)),
                RestorePoint.SchemaVersion).Ok;
            return new InterruptedClear(ts, sealed_);
        }
        catch { return null; }
    }

    public void DiscardPartial(string timestamp)
    {
        DeleteRestorePoint(timestamp);
        try { var p = Path.Combine(_dataRoot, LockName); if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }

    // Copy top-level launcher state from the archive back over the live data root. These are small
    // JSON + theme/avatar files — a plain overwrite copy, not the gated game-folder path.
    private void RestoreTopLevelFrom(string rpDir)
    {
        foreach (var f in TopLevelFiles)
        {
            var src = Path.Combine(rpDir, f);
            if (File.Exists(src)) { Directory.CreateDirectory(_dataRoot); File.Copy(src, Path.Combine(_dataRoot, f), overwrite: true); }
        }
        foreach (var d in TopLevelDirs)
        {
            var src = Path.Combine(rpDir, d);
            if (Directory.Exists(src)) CopyDirOverwrite(src, Path.Combine(_dataRoot, d));
        }
    }

    private static void CopyDirOverwrite(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
        foreach (var sub in Directory.GetDirectories(src))
            CopyDirOverwrite(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }
}
