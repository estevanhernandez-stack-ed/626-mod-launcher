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
    private const string LockName = SafeClearLock.FileName;
    private const string SheetFileName = "626-launcher-how-to-launch.txt";

    /// <summary>Tests only: runs just before the manifest rewrite that records a game's held copies.</summary>
    internal Action? BeforeManifestRewriteForTests { get; set; }

    /// <summary>Tests only: runs after a Kind restore's record is written, just before the overlay's first write.</summary>
    internal Action? BeforeOverlayForTests { get; set; }

    /// <summary>Tests only: the clock the undo-snapshot retention rule measures age against.</summary>
    internal Func<DateTime>? UtcNowForTests { get; set; }

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
                foreach (var g in games)
                {
                    var ctx = _provider.ContextFor(g);
                    payload += FileTally.ByteSize(ctx.DataDir);
                    // Vanilla copies every mod it turns off into the restore point: count them up front.
                    if (string.Equals(EndStateFor(g.Id, opts), "vanilla", StringComparison.OrdinalIgnoreCase))
                        // Held copies plus the swept remainder: both come out of the mod-only folders, except
                        // direct-inject / loose-root holds, which are added on top (M1).
                        payload += RestorePointEngine.EstimateModOnlyBytes(ctx)
                                   + RestorePointEngine.EstimateTurnOffBytes(ctx, outsideModOnlyOnly: true);
                }
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
            // The vanilla turn-off set per game, sealed alongside the moves (turnedOffByClear) and handed to
            // MUTATE the same way: MUTATE turns off exactly what the seal says Restore will turn back on.
            var turnOffsByGame = new Dictionary<string, IReadOnlyList<ClearedMod>>();
            RestorePointManifest? sealedManifest = null;

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
                        var turnOffs = RestorePointEngine.PlanVanillaTurnOffs(ctx, planned);
                        plannedByGame[g.Id] = planned;
                        turnOffsByGame[g.Id] = turnOffs;
                        ga = ga with { MovedFiles = planned, TurnedOffByClear = turnOffs, OffboardingSheetGameFolderPath = sheetPath };
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
                // Schema 2 only when a game carries the turn-off record; a modsActive-only point stays readable
                // by builds that predate it.
                var schema = archives.Any(a => a.TurnedOffByClear is not null) ? RestorePoint.SchemaVersion : 1;
                var manifest = new RestorePointManifest(
                    schema, _launcherVersion, timestamp,
                    Complete: true, opts.KeepNexus,
                    FileTally.ByteSize(rpDir) + movedBytes, FileTally.FileCount(rpDir) + movedCount, archives);
                RestorePointManifestStore.WriteSealed(rpDir, manifest);   // THE SEAL — before any move (Law A)
                sealedManifest = manifest;
            }

            // MUTATE-ALL — executes the planned moves (and other end-state work) AFTER the seal.
            // For vanilla games with a sealed plan, passes the plan so ApplyEndState moves EXACTLY
            // that set. For skip-archive (CreateRestorePoint=false), plannedByGame is empty → null →
            // ApplyEndState plans itself.
            // NOTE: with skip-archive + vanilla, ApplyEndState still MOVES direct-inject files into
            // rpDir/games/<id>/vanilla-moved (never deletes) — but no manifest is sealed and no marker
            // is written, so this folder is NOT a managed restore point. Files are preserved on disk
            // (recoverable manually); ListRestorePoints shows only sealed points.
            // A turn-off that refuses (a held earlier copy, a locked file) does not abort: that mod stays as it
            // was, the rest carry on, and the skip is recorded below and returned as a warning.
            // After each vanilla game's turn-offs, its held mods are COPIED into the restore point and the
            // manifest is rewritten (atomically) to describe them: turnOffSkipped and heldCopies are appended
            // to the already-sealed manifest. The seal stays where Law A puts it, before anything moved; this
            // second write only ever adds a description of files that are already in the archive and verified.
            // A crash before it leaves heldCopies null, the mods still held in the data folder, and Restore
            // turns them on from there. A failed copy is a warning, never an abort: the mods are still held.
            var current = sealedManifest;
            foreach (var g in games)
            {
                var ctx = _provider.ContextFor(g);
                var gameArchiveDir = Path.Combine(rpDir, "games", g.Id);
                var end = RestorePointEngine.ApplyEndState(ctx, EndStateFor(g.Id, opts), gameArchiveDir,
                    plannedByGame.TryGetValue(g.Id, out var pm) ? pm : null,
                    turnOffsByGame.TryGetValue(g.Id, out var to) ? to : null);
                foreach (var s in end.TurnOffSkips)
                    warnings.Add($"{g.GameName}: \"{s.Name}\" is still active: {s.Reason}");

                if (current is not null && to is { Count: > 0 })
                {
                    IReadOnlyList<HeldCopy>? copies = null;
                    try { copies = RestorePointEngine.CopyHeldIntoArchive(ctx, to, end.TurnOffSkips, gameArchiveDir); }
                    catch (Exception e)
                    {
                        // The half-made copy is the launcher's own, inside its own archive: it goes, so the
                        // restore point holds only what its manifest describes.
                        try { var held = Path.Combine(gameArchiveDir, RestorePointEngine.HeldDirName); if (Directory.Exists(held)) Directory.Delete(held, recursive: true); } catch { }
                        warnings.Add($"{g.GameName}: the turned-off mods couldn't be copied into the restore point ({e.Message}). "
                            + $"They are still held in {ctx.DataDir}; keep that folder until you restore.");
                    }
                    var heldFiles = copies?.SelectMany(h => h.Files).ToList() ?? new List<MovedFile>();
                    var next = current with
                    {
                        TotalBytes = current.TotalBytes + heldFiles.Sum(f => f.Bytes),
                        FileCount = current.FileCount + heldFiles.Count,
                        Games = current.Games.Select(ga => ga.Id != g.Id ? ga : ga with
                        {
                            TurnOffSkipped = end.TurnOffSkips.Count > 0 ? end.TurnOffSkips : null,
                            HeldCopies = copies,
                        }).ToList(),
                    };
                    try
                    {
                        BeforeManifestRewriteForTests?.Invoke();
                        RestorePointManifestStore.WriteSealed(rpDir, next);
                        current = next;
                    }
                    catch (Exception e)
                    {
                        // The sealed manifest still restores, from the data folder. Copies it doesn't describe go,
                        // so the restore point holds only what its manifest says.
                        try { var held = Path.Combine(gameArchiveDir, RestorePointEngine.HeldDirName); if (Directory.Exists(held)) Directory.Delete(held, recursive: true); } catch { }
                        if (copies is { Count: > 0 })
                            warnings.Add($"{g.GameName}: the restore point's record of the copied mods couldn't be saved ({e.Message}). "
                                + $"They are still held in {ctx.DataDir}; keep that folder until you restore.");
                        else
                            warnings.Add($"{g.GameName}: the restore point's record couldn't be updated ({e.Message}).");
                    }
                }
                // Vanilla means vanilla: whatever is still in the game's mod-only folders (files no mod row
                // claims) goes into the restore point. The plan is RECORDED first (an atomic manifest rewrite),
                // and only then does anything move, so after a crash every file is live or archived under its
                // record, never neither. If the record can't be written, nothing moves and the sheet says the
                // folders weren't cleared. Files that won't move stay live and are named.
                if (current is not null && turnOffsByGame.ContainsKey(g.Id))
                {
                    RemainderPlan? plan = null;
                    // The framework exclusion comes from the SEALED capture: the uninstall has already
                    // emptied the live registry (M2).
                    var sealedFrameworks = current.Games.FirstOrDefault(a => a.Id == g.Id)?.Frameworks;
                    try { plan = RestorePointEngine.PlanVanillaRemainder(ctx, end.TurnOffSkips, sealedFrameworks); }
                    catch (Exception e)
                    {
                        warnings.Add($"{g.GameName}: 626 couldn't read the mod folders to clear what's left in them ({e.Message}). Nothing more was moved.");
                    }
                    if (plan is not null)
                    {
                        var recorded = current with
                        {
                            TotalBytes = current.TotalBytes + plan.Files.Sum(f => f.Bytes),
                            FileCount = current.FileCount + plan.Files.Count,
                            Games = current.Games.Select(ga => ga.Id != g.Id ? ga : ga with
                            {
                                VanillaRemainder = plan.Files,
                                LeftInPlace = plan.LeftInPlace,
                            }).ToList(),
                        };
                        var isRecorded = false;
                        try
                        {
                            BeforeManifestRewriteForTests?.Invoke();
                            RestorePointManifestStore.WriteSealed(rpDir, recorded);
                            current = recorded;
                            isRecorded = true;
                        }
                        catch (Exception e)
                        {
                            warnings.Add($"{g.GameName}: the restore point's record of the files left in the mod folders couldn't be saved ({e.Message}), so 626 left them in place.");
                        }

                        if (isRecorded && plan.Files.Count > 0)
                        {
                            var stayed = RestorePointEngine.SweepRemainder(ctx, plan.Files, gameArchiveDir);
                            if (stayed.Count > 0)
                            {
                                var stayedRels = new HashSet<string>(stayed.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
                                var kept = plan.Files.Where(f => !stayedRels.Contains(f.Rel)).ToList();
                                var gone = plan.Files.Where(f => stayedRels.Contains(f.Rel)).ToList();
                                var settled = current with
                                {
                                    TotalBytes = current.TotalBytes - gone.Sum(f => f.Bytes),
                                    FileCount = current.FileCount - gone.Count,
                                    Games = current.Games.Select(ga => ga.Id != g.Id ? ga : ga with
                                    {
                                        VanillaRemainder = kept,
                                        LeftInPlace = plan.LeftInPlace.Concat(stayed).ToList(),
                                    }).ToList(),
                                };
                                try { RestorePointManifestStore.WriteSealed(rpDir, settled); current = settled; }
                                catch (Exception e)
                                {
                                    // The record still lists them; Restore finds each live with its recorded
                                    // content and leaves it. Only the sheet's "still in place" list is short.
                                    warnings.Add($"{g.GameName}: the restore point's record couldn't be updated ({e.Message}).");
                                }
                                warnings.Add($"{g.GameName}: {stayed.Count} file(s) in the mod folders couldn't be moved and are still in place, "
                                    + $"e.g. {stayed[0].Path} ({stayed[0].Reason}).");
                            }
                        }
                    }
                }
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

    /// <summary>Tests only: replaces the free-space check of <see cref="CaptureGameOnlyAsync"/>.</summary>
    internal Func<string, long, SpaceCheck.Result>? SpaceCheckForTests { get; set; }

    /// <summary>Marks a restore-point folder as one <see cref="CaptureGameOnlyAsync"/> started, created with
    /// <c>FileMode.CreateNew</c> so two writers can't both own a folder. A claimed folder with no manifest is
    /// an unsealed leftover and is swept (see <see cref="SweepAbandonedCaptures"/>).</summary>
    internal const string CaptureClaimFile = ".capture-only";

    /// <summary>How old an unsealed, claimed folder must be before a later capture removes it. A capture in
    /// another process that is still copying is younger than this.</summary>
    internal static readonly TimeSpan AbandonedCaptureAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Snapshot ONE game and change nothing. The Patch Day restore point: it copies the game's launcher-owned
    /// data folder and its framework install state into a new restore point, seals the manifest, and stops.
    /// It never calls <see cref="RestorePointEngine.ApplyEndState"/>, never writes to the game folder or the
    /// mod folders, never touches games.json, nexus.json or the reset markers, and writes no off-boarding sheet.
    /// <para>The restore point's folder (and the <c>RestorePointTimestamp</c> returned, which is what
    /// <see cref="ListRestorePoints"/> and <see cref="RestoreAsync"/> are keyed by) is
    /// <c>{timestamp}-{gameId}</c>, so captures of two games in the same second don't collide. Safe Clear's
    /// folders are still the bare timestamp. Both arguments must be plain names: a path separator, "..", or a
    /// character a file name can't hold is refused.</para>
    /// <para>Guards, as Safe Clear's where they apply: the one-writer gate; refuses while a
    /// <c>safe-clear.lock</c> is on disk (a reset is running, or died and awaits recovery); a reachable-drive
    /// check; a free-space pre-flight sized to the copy. It does NOT refuse for a running game: Safe Clear's
    /// Law E protects files it is about to mutate, and this only reads them (every copy is size-verified).</para>
    /// <para>A failure returns <see cref="CaptureOnlyResult"/> with a reason. A restore point this call started
    /// is removed on failure so no partial point is left; one that already exists under the name is refused and
    /// never touched. A crash mid-copy leaves a claimed, unsealed folder, which the next capture sweeps once it
    /// is old enough; <see cref="ListRestorePoints"/> never shows it.</para>
    /// <para>Restore: <see cref="RestoreAsync"/> restores a point that has a <c>kind</c> as an overlay that takes a
    /// <c>beforeRestore</c> snapshot first and toggles no mod (see <see cref="RestorePointEngine.PlanOverlay"/>).</para>
    /// </summary>
    public Task<CaptureOnlyResult> CaptureGameOnlyAsync(string gameId, string kind, string timestamp, CancellationToken ct)
        => CaptureGameOnlyCoreAsync(gameId, kind, timestamp, gateHeld: false, ct);

    // The capture itself. <paramref name="gateHeld"/>: the caller (a restore taking its pre-restore snapshot)
    // already holds the one-writer gate, which is not reentrant, so this must not wait on it again.
    private async Task<CaptureOnlyResult> CaptureGameOnlyCoreAsync(string gameId, string kind, string timestamp, bool gateHeld, CancellationToken ct)
    {
        if (!gateHeld) await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!IsPlainName(timestamp))
                return Refused("Couldn't save a restore point: the restore point's name isn't a plain name.");
            if (!IsPlainName(gameId))
                return Refused($"Couldn't find the game \"{gameId}\" to save a restore point.");
            var game = _provider.Games.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase));
            if (game is null) return Refused($"Couldn't find the game \"{gameId}\" to save a restore point.");

            if (File.Exists(Path.Combine(_dataRoot, LockName)))
                return Refused("A reset didn't finish. Deal with it first, then 626 can save a restore point.");

            var ctx = _provider.ContextFor(game);
            if (!DriveReachable(ctx.GameRoot) || !DriveReachable(ctx.DataDir))
                return Refused($"{game.GameName}'s drive is unavailable, so 626 couldn't save a restore point.");

            var name = $"{timestamp}-{game.Id}";
            if (!IsPlainName(name))
                return Refused($"Couldn't save a restore point for {game.GameName}: its id can't be used in a name.");
            var rpDir = Path.Combine(_restorePointsRoot, name);
            SweepAbandonedCaptures();
            if (Directory.Exists(rpDir) || File.Exists(rpDir))
                return Refused($"A restore point named {name} already exists, so 626 didn't save another.");

            var payload = CaptureBytes(ctx);
            var space = SpaceCheckForTests is { } fake ? fake(_restorePointsRoot, payload) : SpaceCheck.Require(_restorePointsRoot, payload);
            if (!space.Ok)
                return Refused($"Not enough free space: need ~{Gb(space.RequiredBytes)} GB, have {Gb(space.AvailableBytes)} GB on {space.VolumeRoot}.");

            var claim = Path.Combine(rpDir, CaptureClaimFile);
            try { Directory.CreateDirectory(rpDir); }
            catch (Exception e) when (e is not OperationCanceledException)
            { return Refused($"Couldn't save a restore point for {game.GameName}: {e.Message}"); }
            long claimBytes;
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(name);
                using (var f = new FileStream(claim, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.Write(bytes, 0, bytes.Length);
                claimBytes = bytes.Length;
            }
            catch (IOException) when (File.Exists(claim))
            {
                // Someone else claimed this folder between the check and here: it is theirs, not ours to remove.
                return Refused($"A restore point named {name} already exists, so 626 didn't save another.");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                DeleteRestorePoint(name);
                return Refused($"Couldn't save a restore point for {game.GameName}: {e.Message}");
            }

            try
            {
                var ga = RestorePointEngine.CaptureGame(
                    new GameCaptureInput(game, ctx, "modsActive"), Path.Combine(rpDir, "games", game.Id));
                // The claim marker is bookkeeping, not part of what was captured.
                var manifest = new RestorePointManifest(
                    1, _launcherVersion, name, Complete: true, KeepNexus: true,
                    FileTally.ByteSize(rpDir) - claimBytes, FileTally.FileCount(rpDir) - 1, new[] { ga }, kind);
                RestorePointManifestStore.WriteSealed(rpDir, manifest);   // the seal, last
                return new CaptureOnlyResult(true, null, name);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Ours alone: this call created rpDir (it did not exist above) and holds its claim, so it
                // holds only what this call copied.
                DeleteRestorePoint(name);
                return Refused($"Couldn't save a restore point for {game.GameName}: {e.Message}");
            }
        }
        finally { if (!gateHeld) _gate.Release(); }

        static CaptureOnlyResult Refused(string reason) => new(false, reason, null);
    }

    // A plain file name: no separators, no "..", nothing a file name can't hold.
    private static bool IsPlainName(string? s)
        => !string.IsNullOrWhiteSpace(s) && s != "." && !s.Contains("..")
           && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && s.IndexOfAny(new[] { '/', '\\' }) < 0;

    // Remove the unsealed leftovers of a capture that died: a folder carrying our claim file, with no manifest,
    // untouched for AbandonedCaptureAge. Only folders WE claimed (a Safe Clear's partial point has no claim and
    // is recovery's to deal with), and never the folder an interrupted reset's lock names.
    private void SweepAbandonedCaptures()
    {
        try
        {
            if (!Directory.Exists(_restorePointsRoot)) return;
            string? lockedName = null;
            var lockPath = Path.Combine(_dataRoot, LockName);
            if (File.Exists(lockPath)) lockedName = File.ReadAllText(lockPath).Trim();
            foreach (var dir in Directory.GetDirectories(_restorePointsRoot))
            {
                var claim = Path.Combine(dir, CaptureClaimFile);
                if (!File.Exists(claim)) continue;
                if (string.Equals(Path.GetFileName(dir), lockedName, StringComparison.Ordinal)) continue;
                if (File.Exists(Path.Combine(dir, RestorePointManifestStore.FileName))) continue;   // sealed: a real point
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(claim) < AbandonedCaptureAge) continue;
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort: it is only wasted space */ }
            }
        }
        catch { /* best effort */ }
    }

    // What the capture copies: the data folder plus each framework's installed files (CaptureGame's two payloads).
    private static long CaptureBytes(GameContext ctx)
    {
        long n = FileTally.ByteSize(ctx.DataDir);
        try
        {
            foreach (var fw in Frameworks.FrameworkRegistry.List(ctx.DataDir))
                foreach (var rel in fw.InstalledFiles)
                {
                    var p = Path.Combine(fw.InstallPath, rel);
                    if (File.Exists(p)) n += new FileInfo(p).Length;
                }
        }
        catch { /* an estimate */ }
        return n;
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
        // Under the registry's lock (A6): games.json is one of these, and a writer mid-Update must not
        // see it vanish between its read and its save, or save it straight back.
        Persistence.RegistryStore.WithLock(_dataRoot, () =>
        {
            foreach (var f in TopLevelFiles) { try { UnderFileLock(f, () => { var p = Path.Combine(_dataRoot, f); if (File.Exists(p)) File.Delete(p); }); } catch { } }
        });
        foreach (var d in TopLevelDirs) { try { var p = Path.Combine(_dataRoot, d); if (Directory.Exists(p)) Directory.Delete(p, recursive: true); } catch { } }
    }

    private static string Gb(long bytes) => (bytes / 1_073_741_824.0).ToString("0.0");

    // ── Tasks 4 + 5 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restore a restore point. A Safe Clear point is replayed exactly as before (it assumes the clear emptied
    /// the game). A point with a <c>kind</c> (the Patch Day snapshot, or a <c>beforeRestore</c> one) is restored
    /// over the LIVE game by <see cref="RestoreKindPoint"/>: refused while a Safe Clear lock is on disk, the
    /// game is running or it has moved; a <c>beforeRestore</c> snapshot of the current state is taken FIRST and
    /// the restore is refused, changing nothing, if that fails. <paramref name="snapshotTimestamp"/> names
    /// that snapshot (<c>{snapshotTimestamp}-{gameId}</c>); null = now, UTC, in Safe Clear's format.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(string timestamp, CancellationToken ct, string? snapshotTimestamp = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var rpDir = Path.Combine(_restorePointsRoot, timestamp);
            var m = RestorePointManifestStore.Read(rpDir);
            if (m?.Kind is not null)
                return await RestoreKindPoint(timestamp, rpDir, m, snapshotTimestamp, ct);
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
                var replay = RestorePointEngine.ReplayGame(ga, Path.Combine(rpDir, "games", ga.Id), _provider.ContextFor(game));
                // The mods the clear turned off that are not back on, grouped by reason so a 200-mod game
                // that hit one cause reads as one line, not two hundred.
                // Remainder files not put back, grouped by reason, a few named per line.
                foreach (var grp in replay.RemainderIssues.GroupBy(n => n.Reason))
                {
                    var names = grp.Select(n => $"\"{n.Name}\"").ToList();
                    var shown = string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" and {names.Count - 5} more" : "");
                    warnings.Add($"{ga.GameName}: {shown} {(names.Count == 1 ? "was" : "were")} not put back: {grp.Key}");
                }
                foreach (var rec in replay.Recovered)
                    warnings.Add($"{ga.GameName}: \"{rec.Name}\" had stopped partway through turning off; it is back on now.");
                foreach (var grp in replay.NotBackOn.GroupBy(n => n.Reason))
                {
                    var names = grp.Select(n => $"\"{n.Name}\"").ToList();
                    warnings.Add($"{ga.GameName}: {string.Join(", ", names)} {(names.Count == 1 ? "is" : "are")} not back on: {grp.Key}");
                }
            }

            RestoreMarkers.ClearLastClear(_dataRoot);
            return new RestoreResult(true, null, Array.Empty<RestoreConflict>(), warnings);
        }
        finally { _gate.Release(); }
    }

    // Restore a kind point over the live game (caller holds the gate). Every refusal below happens before
    // anything is written. Then: snapshot the current state (kind beforeRestore; if that fails, refuse), then
    // the overlay. Deliberately NOT done here, because they belong to undoing a reset and would be wrong
    // over a live game: restoring games.json / themes / profile / app-settings, reloading the registry,
    // and clearing the "undo your reset" marker.
    private async Task<RestoreResult> RestoreKindPoint(string timestamp, string rpDir, RestorePointManifest m,
        string? snapshotTimestamp, CancellationToken ct)
    {
        static RestoreResult Refused(string reason, IReadOnlyList<RestoreConflict>? conflicts = null)
            => new(false, reason, conflicts ?? Array.Empty<RestoreConflict>(), Array.Empty<string>());

        ct.ThrowIfCancellationRequested();
        if (!IsPlainName(timestamp)) return Refused("Couldn't restore: the restore point's name isn't a plain name.");
        var v = RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion);
        if (!v.Ok) return Refused(v.Reason!);
        if (File.Exists(Path.Combine(_dataRoot, LockName)))
            return Refused("A reset didn't finish. Deal with it first, then 626 can restore this snapshot.");
        if (m.Games.Count != 1) return Refused("This snapshot isn't of a single game, so 626 can't restore it.");
        var ga = m.Games[0];
        var gameArchiveDir = Path.Combine(rpDir, "games", ga.Id);
        if (!IsPlainName(ga.Id) || !Directory.Exists(gameArchiveDir))
            return Refused("This snapshot's files are missing, so nothing was restored.");
        var conflicts = RestoreReconcile.Check(m, _provider.Games);
        if (conflicts.Count > 0)
            return Refused($"{ga.GameName} has moved since this snapshot, so 626 didn't restore it.", conflicts);
        var game = _provider.Games.FirstOrDefault(g => string.Equals(g.Id, ga.Id, StringComparison.OrdinalIgnoreCase));
        if (game is null) return Refused($"{ga.GameName} isn't in your library any more, so 626 can't restore its snapshot.");
        bool running;
        try { running = _probe.AnyRunning(game); }
        catch { return Refused($"Couldn't verify whether {game.GameName} is running. Close it and try again."); }   // fails closed, as Safe Clear does
        if (running) return Refused($"Close {game.GameName} before restoring its snapshot.");

        // Plan (and validate) first: a snapshot that can't be applied is refused before a pre-restore snapshot
        // is taken, so a refusal leaves nothing behind.
        var ctx = _provider.ContextFor(game);
        OverlayPlan plan;
        try { plan = RestorePointEngine.PlanOverlay(ga, gameArchiveDir, ctx); }
        catch (Exception e) when (e is not OperationCanceledException)
        { return Refused($"This snapshot can't be restored safely: {e.Message} Nothing was changed."); }

        var snap = await CaptureGameOnlyCoreAsync(game.Id, RestorePointKinds.BeforeRestore,
            snapshotTimestamp ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), gateHeld: true, ct);
        if (!snap.Ok) return Refused($"Couldn't save your current setup first, so nothing was restored. {snap.RefusedReason}");
        var undo = snap.RestorePointTimestamp!;

        // The breadcrumb goes down before the overlay's first write, so a launcher that dies partway can say so
        // at the next start. If it can't be written, nothing has been overlaid yet: refuse, as for the snapshot.
        try
        {
            RestoreJournal.Write(_dataRoot, new InterruptedRestore(timestamp, undo, game.Id!, ga.GameName, DateTime.UtcNow));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Refused($"Couldn't note the restore first, so nothing was restored. {e.Message} Your setup from before is saved as {undo}.");
        }
        BeforeOverlayForTests?.Invoke();

        IReadOnlyList<string> applied;
        try { applied = RestorePointEngine.ApplyOverlay(plan, gameArchiveDir, ctx); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The record STAYS: some files may be from the snapshot, some from before.
            return Refused($"626 couldn't finish the restore: {e.Message} Your setup from before it is saved as {undo}; restore that to go back to where you were.");
        }
        RestoreJournal.Clear(_dataRoot);
        var warnings = plan.Notes.Concat(applied).Select(n => $"{ga.GameName}: {n}")
            .Append($"Your setup from before this restore is saved as {undo}. Restore it to go back.").ToList();
        // Only here: the restore has fully succeeded and its record is cleared. A refusal or a failure above
        // returned already, and Safe Clear never comes through this method.
        PruneOlderUndoSnapshots(game.Id, ga.GameName, undo, timestamp, warnings);
        return new RestoreResult(true, null, Array.Empty<RestoreConflict>(), warnings);
    }

    // Keep a game's newest RestorePointRetention.KeepBeforeRestore undo snapshots (see RestorePointRetention for
    // the rule). Runs under the caller's gate. Deletes only folders the rule picked, each a direct child of the
    // restore-points root, through the same folder removal as DeleteRestorePoint. It never fails the restore: a
    // delete that doesn't take is a note, and anything unexpected, or a journal it can't read, means nothing is
    // removed. `undo` (the snapshot this restore just took) and `restoredFrom` are never selected, nor is
    // anything the lock or the pending-restore record names.
    private void PruneOlderUndoSnapshots(string gameId, string gameName, string undo, string restoredFrom, List<string> notes)
    {
        try
        {
            if (!Directory.Exists(_restorePointsRoot)) return;
            var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { undo, restoredFrom };
            var journalNames = RestoreJournal.NamedPoints(_dataRoot);
            if (journalNames is null) return;   // a record that is there but can't be read: don't guess what it names
            protectedNames.UnionWith(journalNames);
            var lockPath = Path.Combine(_dataRoot, LockName);
            if (File.Exists(lockPath)) protectedNames.Add(File.ReadAllText(lockPath).Trim());

            var facts = new List<RestorePointFacts>();
            foreach (var dir in Directory.GetDirectories(_restorePointsRoot))
            {
                try
                {
                    // A link out of the restore-points folder is not ours to follow, let alone delete through.
                    if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var m = RestorePointManifestStore.Read(dir);
                    if (m is null) continue;
                    facts.Add(new RestorePointFacts(Path.GetFileName(dir), m.Complete, m.SchemaVersion, m.Kind,
                        m.CreatedUtc, m.Games.Select(g => g.Id).ToList()));
                }
                catch { /* unreadable: never a candidate */ }
            }

            var doomed = RestorePointRetention.BeforeRestoreToPrune(facts, gameId, UtcNowForTests?.Invoke() ?? DateTime.UtcNow, protectedNames);
            var removed = 0;
            var stuck = new List<string>();
            foreach (var name in doomed)
            {
                if (!IsPlainName(name)) continue;
                if (TryDeleteUnsealedFirst(name)) removed++; else stuck.Add(name);
            }
            if (removed > 0)
                notes.Add($"Removed {removed} older undo snapshot{(removed == 1 ? "" : "s")} for {gameName} "
                    + $"(kept the newest {RestorePointRetention.KeepBeforeRestore}).");
            if (stuck.Count > 0)
                notes.Add($"Couldn't fully remove {stuck.Count} older undo snapshot{(stuck.Count == 1 ? "" : "s")} for {gameName}: "
                    + $"{string.Join(", ", stuck)}. 626 will try again later.");
        }
        catch { /* tidying is a courtesy; the restore it follows has already succeeded */ }
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
                m.Complete,
                m.Kind));
        }
        return list.OrderByDescending(r => r.Timestamp, StringComparer.Ordinal).ToList();
    }

    public void DeleteRestorePoint(string timestamp) => TryDeleteRestorePoint(timestamp);

    // The prune's removal: the seal goes FIRST, then the folder. A folder that then fails partway is unsealed
    // and still carries its capture claim, so ListRestorePoints never shows it (no half-restorable point) and
    // SweepAbandonedCaptures removes it once it is AbandonedCaptureAge old. If the seal itself can't be
    // removed, nothing else is touched and the point stays whole. A point with no claim file (not one a
    // capture made) would be an orphan nothing sweeps, so it gets the plain removal instead; no claim is added.
    private bool TryDeleteUnsealedFirst(string timestamp)
    {
        var dir = Path.Combine(_restorePointsRoot, timestamp);
        if (File.Exists(Path.Combine(dir, CaptureClaimFile)))
        {
            var manifest = Path.Combine(dir, RestorePointManifestStore.FileName);
            try { File.Delete(manifest); } catch { /* checked below */ }
            if (File.Exists(manifest)) return false;
        }
        return TryDeleteRestorePoint(timestamp);
    }

    // DeleteRestorePoint's removal, reporting whether the folder is gone afterwards.
    private bool TryDeleteRestorePoint(string timestamp)
    {
        var dir = Path.Combine(_restorePointsRoot, timestamp);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        return !Directory.Exists(dir);
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
            var m = RestorePointManifestStore.Read(Path.Combine(_restorePointsRoot, ts));
            // Sealed means complete, whoever wrote it. A complete manifest from a newer build is sealed too:
            // reading it as "unsealed" would offer to discard a whole restore point this build just can't read.
            if (m is { Complete: true } && m.SchemaVersion > RestorePoint.SchemaVersion)
                return new InterruptedClear(ts, Sealed: true, NewerSchema: true);
            var sealed_ = RestorePointManifestStore.Validate(m, RestorePoint.SchemaVersion).Ok;
            return new InterruptedClear(ts, sealed_);
        }
        catch { return null; }
    }

    /// <summary>A Kind restore that stopped partway (see <see cref="RestoreJournal"/>), or null. Looking does
    /// not consume it: the record stays until <see cref="AcknowledgeInterruptedRestore"/>.</summary>
    public InterruptedRestore? DetectInterruptedRestore() => RestoreJournal.Read(_dataRoot);

    /// <summary>The player has seen it. Removes the record and only the record: no restore point is touched
    /// and nothing is restored.</summary>
    public void AcknowledgeInterruptedRestore() => RestoreJournal.Clear(_dataRoot);

    /// <summary>What to tell the user about an interrupted clear sealed by a newer 626.</summary>
    public const string InterruptedNewerMessage =
        "A reset made by a newer version of 626 didn't finish. Its restore point is complete, but this version "
        + "can't read it. Update 626 to restore it. Nothing has been discarded.";

    /// <summary>The second paragraph of that dialog: what acknowledging does, and what it doesn't.</summary>
    public const string InterruptedNewerAcknowledge =
        "Choose Got it to stop this reminder. Only the reminder goes: the restore point stays where it is, "
        + "and the newer version of 626 can still restore it.";

    /// <summary>Acknowledge an interrupted reset this build can't act on: removes only the lock, never the
    /// restore point (see <see cref="SafeClearLock.Acknowledge"/>).</summary>
    public void AcknowledgeInterruptedClear(string timestamp) => SafeClearLock.Acknowledge(_dataRoot, timestamp);

    public void DiscardPartial(string timestamp)
    {
        // Only ever a PARTIAL point: a complete manifest (any schema) is a restore point, never discarded here.
        if (RestorePointManifestStore.Read(Path.Combine(_restorePointsRoot, timestamp)) is { Complete: true }) return;
        DeleteRestorePoint(timestamp);
        try { var p = Path.Combine(_dataRoot, LockName); if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }

    // Copy top-level launcher state from the archive back over the live data root. These are small
    // JSON + theme/avatar files — a plain overwrite copy, not the gated game-folder path.
    private void RestoreTopLevelFrom(string rpDir)
    {
        // Under the registry's lock (A6), so a writer mid-Update cannot save over the restored games.json.
        Persistence.RegistryStore.WithLock(_dataRoot, () =>
        {
            foreach (var f in TopLevelFiles)
            {
                var src = Path.Combine(rpDir, f);
                if (File.Exists(src))
                    UnderFileLock(f, () => { Directory.CreateDirectory(_dataRoot); File.Copy(src, Path.Combine(_dataRoot, f), overwrite: true); });
            }
        });
        foreach (var d in TopLevelDirs)
        {
            var src = Path.Combine(rpDir, d);
            if (Directory.Exists(src)) CopyDirOverwrite(src, Path.Combine(_dataRoot, d));
        }
    }

    // app-settings.json has its own lock (the app and the agent's apply_theme merge keys into it), so a
    // save mid-merge can't rename its copy over the one a restore just put back. games.json's lock is
    // already held by the caller. Lock order is always games.json, then app-settings.json.
    private void UnderFileLock(string topLevelFile, Action action)
    {
        if (topLevelFile == AppSettingsFile.FileName) AppSettingsFile.WithLock(AppSettingsFile.PathIn(_dataRoot), action);
        else action();
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
