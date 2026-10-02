using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>Hydrated inputs the engine needs to capture one game. All derivable from Core; the App
/// supplies the GameEntry + a built GameContext and picks the end-state.</summary>
public sealed record GameCaptureInput(GameEntry Game, GameContext Context, string EndState);

/// <summary>Result of <see cref="RestorePointEngine.ApplyEndState"/>. vanilla populates MovedFiles and
/// TurnOffSkips (the turn-offs that refused; those mods are still active); modsActive populates
/// EnableOutcomes. The lists an end-state does not use are empty.</summary>
public sealed record EndStateResult(
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<Scanner.EnableOutcome> EnableOutcomes,
    IReadOnlyList<ClearSkip> TurnOffSkips);

/// <summary>Result of <see cref="RestorePointEngine.ReplayGame"/>: the mods from the sealed turn-off set
/// that are not on after Restore, each with the lane's reason when it gave one. Empty means every mod the
/// clear turned off is back on (or the archive carried no turn-off record).</summary>
public sealed record ReplayResult(IReadOnlyList<ClearSkip> NotBackOn);

/// <summary>
/// The headless Safe Clear / Restore file engine. Takes explicit archive paths — no %APPDATA%
/// knowledge, no UI. Composes Phase 0 primitives (SafeMove, PathGate) + existing Core. The App
/// orchestrator (Phase 1B) calls these in the Law-A order: capture-all -> seal -> mutate-all.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>Apply the chosen end-state to a game AFTER its capture is sealed.
    /// vanilla: move detected direct-inject game-folder files into the archive (recorded), turn off every
    /// other enabled, switchable mod through <see cref="ModToggle"/> (held in the data dir; a refusal is
    /// returned in TurnOffSkips, never thrown), uninstall frameworks (their files were captured), flip
    /// loader manifests off; owned mods untouched.
    /// modsActive: re-enable everything from holding, returning per-mod outcomes (skips surfaced).
    /// <para>When <paramref name="plannedVanillaMoves"/> is supplied, MUTATE executes EXACTLY that set
    /// (sealed by the orchestrator in CAPTURE-ALL — single source of truth, no re-detect drift).
    /// When null (skip-archive path, no sealed manifest), the moves are planned on the spot. The same holds
    /// for <paramref name="plannedTurnOffs"/>, the sealed <c>turnedOffByClear</c> set.</para></summary>
    public static EndStateResult ApplyEndState(GameContext c, string endState, string gameArchiveDir,
        IReadOnlyList<MovedFile>? plannedVanillaMoves = null, IReadOnlyList<ClearedMod>? plannedTurnOffs = null)
    {
        if (string.Equals(endState, "modsActive", StringComparison.OrdinalIgnoreCase))
            return new EndStateResult(Array.Empty<MovedFile>(), ReEnableAll(c), Array.Empty<ClearSkip>());
        if (!string.Equals(endState, "vanilla", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown end-state \"{endState}\" (expected \"vanilla\" or \"modsActive\").", nameof(endState));

        // Execute EXACTLY the sealed plan when given (single source of truth — no re-detect drift).
        // Skip-archive has no sealed manifest, so plan now.
        var planned = plannedVanillaMoves ?? PlanVanillaMoves(c);
        // Planned against the moves BEFORE they run: a row the direct-inject step owns is left to it.
        var turnOffs = plannedTurnOffs ?? PlanVanillaTurnOffs(c, planned);
        var moved = ExecuteVanillaMoves(c, gameArchiveDir, planned);
        // Before the framework uninstall: the listing must look the way it did when the set was sealed (a
        // UE4SS mods folder is a location because UE4SS is there), and a mod goes before what loads it.
        var skips = TurnOff(c, turnOffs);
        UninstallFrameworks(c);
        FlipLoadersOff(c);
        return new EndStateResult(moved, Array.Empty<Scanner.EnableOutcome>(), skips);
    }

    /// <summary>
    /// Plan (do NOT execute) the vanilla turn-offs for a game: every enabled, switchable row of the one
    /// listing (<see cref="ModListing.Resolve"/>), in the order they will be turned off, ordinary rows
    /// first and loader rows last. The orchestrator seals this as <c>turnedOffByClear</c> BEFORE anything
    /// moves (Law A), and Restore turns exactly this set back on.
    ///
    /// <para>Left out, each because something else already owns it: a <c>ReadOnly</c> row (another tool's
    /// files, or a library the listing refuses to switch, as Play vanilla does); a UE4SS/BepInEx manifest
    /// mod (captured in <c>loaderMods</c> and flipped by the loader sweep); and any row whose files sit at
    /// or under a planned direct-inject move (that step moves it to <c>vanilla-moved</c>; never twice).</para>
    /// </summary>
    public static IReadOnlyList<ClearedMod> PlanVanillaTurnOffs(GameContext c, IReadOnlyList<MovedFile> plannedMoves)
    {
        var moved = plannedMoves.Select(mf => FullNorm(Path.Combine(c.GameRoot, mf.Rel)))
            .Where(p => p is not null).Select(p => p!).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ModListing.Resolve(c.Game)
            .Where(m => m.Enabled && !m.ReadOnly && m.Loader is not ("ue4ss" or "bepinex"))
            .Where(m => !CoveredByMoves(c, m, moved))
            .Where(m => seen.Add(m.Location + "\u0000" + m.Name))
            .OrderBy(m => IsLoaderRow(m) ? 1 : 0)   // stable: listing order within each group
            .Select(m => new ClearedMod(m.Name, m.Location))
            .ToList();
    }

    /// <summary>
    /// Roughly how many bytes vanilla will copy into the restore point for this game: the main files of
    /// every row it would turn off (folders summed whole). Extra-tree entries are not counted, so this runs
    /// a little low on a Cyberpunk-shaped game; it is a pre-flight free-space estimate, read-only.
    /// </summary>
    public static long EstimateTurnOffBytes(GameContext c)
    {
        long total = 0;
        var rows = ModListing.Resolve(c.Game);
        foreach (var cm in PlanVanillaTurnOffs(c, PlanVanillaMoves(c)))
        {
            var row = FindRow(rows, cm);
            var baseDir = row is null ? null : BaseDirFor(c, row);
            if (row is null || baseDir is null) continue;
            foreach (var f in row.Files)
            {
                var abs = Path.Combine(baseDir, f);
                try
                {
                    if (File.Exists(abs)) total += new FileInfo(abs).Length;
                    else if (Directory.Exists(abs)) total += FileTally.ByteSize(abs);
                }
                catch { /* unreadable: an estimate */ }
            }
        }
        return total;
    }

    // Where a row's Files are relative to, per its lane. Null when it has no folder of its own (Mod Engine 2).
    private static string? BaseDirFor(GameContext c, Mod m) => m.Location switch
    {
        "direct-inject" or ProxyLoaderRows.LocationTag => DirectInjectListing.PlayFolder(c.GameRoot),
        LooseMods.LooseRootListing.LooseRootLocation => LooseMods.LooseRootListing.PlayFolder(c.GameRoot),
        _ => c.Locations.FirstOrDefault(l => l.Name == m.Location)?.Abs,
    };

    // A loader row: what other mods load through. Turned off last, turned back on first.
    private static bool IsLoaderRow(Mod m) => m.IsLoader || m.Location == ProxyLoaderRows.LocationTag;

    // True when any of the row's files is, or sits under, a path the direct-inject step will move.
    private static bool CoveredByMoves(GameContext c, Mod m, IReadOnlyList<string> moved)
    {
        if (moved.Count == 0) return false;
        var baseDir = BaseDirFor(c, m);
        if (baseDir is null) return false;
        var files = m.Files.Count > 0 ? m.Files : new List<string> { m.Name };
        foreach (var f in files)
        {
            var abs = FullNorm(Path.Combine(baseDir, f));
            if (abs is null) continue;
            if (moved.Any(mp => string.Equals(abs, mp, StringComparison.OrdinalIgnoreCase)
                    || abs.StartsWith(mp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static string? FullNorm(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return null; }
    }

    // The sealed row, found again in a fresh listing: by name and location, then by name alone.
    private static Mod? FindRow(IReadOnlyList<Mod> rows, ClearedMod cm)
        => rows.FirstOrDefault(m => string.Equals(m.Name, cm.Name, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(m.Location, cm.Location, StringComparison.OrdinalIgnoreCase))
           ?? rows.FirstOrDefault(m => string.Equals(m.Name, cm.Name, StringComparison.OrdinalIgnoreCase));

    // Turn the sealed set off through the one write path. A refusal is recorded and the rest go on: vanilla
    // is best-effort, and the toggle rolls back its own partial move, so a refused mod is left as it was.
    // Judged by one listing at the end as well, so a lane that refuses without throwing is still caught.
    private static IReadOnlyList<ClearSkip> TurnOff(GameContext c, IReadOnlyList<ClearedMod> set)
    {
        if (set.Count == 0) return Array.Empty<ClearSkip>();
        var skips = new List<ClearSkip>();
        var rows = ModListing.Resolve(c.Game);
        // One scope for the whole set, so the scanner's lane lists the game and reads its extra trees once per
        // clear rather than once per mod. Read before the first move (BulkScope says why that is safe).
        var scope = new Scanner.BulkScope(c);
        if (c.ExtraModTrees is { Count: > 0 } && ModListing.MechanismFor(c.Game, c) == ListingMechanism.Scanner)
            _ = scope.Rows;
        foreach (var cm in set)
        {
            var row = FindRow(rows, cm);
            if (row is null || !row.Enabled) continue;   // gone or already off since the seal: nothing live
            try { ModToggle.SetEnabledAsync(c, row, false, scope).GetAwaiter().GetResult(); }
            catch (Exception e) { skips.Add(new ClearSkip(cm.Name, e.Message)); }
        }
        var now = ModListing.Resolve(c.Game);
        foreach (var cm in set)
            if (!skips.Any(s => s.Name == cm.Name) && FindRow(now, cm) is { Enabled: true })
                skips.Add(new ClearSkip(cm.Name, "it was still on after the turn-off"));
        return skips;
    }

    /// <summary>The archive sub-folder holding the copies of the mods vanilla turned off.</summary>
    public const string HeldDirName = "held";

    /// <summary>
    /// The data-dir folders a turned-off mod is held in, per its lane: the scanner's <c>disabled/</c> folder
    /// and its <c>disabled-trees/</c> folder (both by <see cref="HoldingName"/>), or the direct-inject or
    /// loose-root holding folder. A proxy loader is held inside the play folder and a Mod Engine 2 mod is a
    /// config flip, so neither has anything in the data dir to copy. Only folders that exist are returned.
    /// </summary>
    private static IReadOnlyList<string> HeldFoldersFor(GameContext c, ClearedMod cm)
    {
        var found = new List<string>();
        void Add(string? dir) { if (dir is not null && Directory.Exists(dir)) found.Add(dir); }
        switch (cm.Location)
        {
            case ProxyLoaderRows.LocationTag:
            case "mod engine 2":
                break;
            case "direct-inject":
                Add(Path.Combine(DirectInjectListing.Holding(c.Game), EnginePresets.Slugify(cm.Name)));
                break;
            case LooseMods.LooseRootListing.LooseRootLocation:
                Add(Path.Combine(LooseMods.LooseRootListing.Holding(c.Game), EnginePresets.Slugify(cm.Name)));
                break;
            default:
                if (HoldingName.Folder(cm.Name) is { } folder) Add(Path.Combine(c.DisabledRoot, folder));
                try { if (TreeHolding.CanHold(c, cm.Name)) Add(TreeHolding.ModDir(c, cm.Name)); }
                catch { /* an unreadable disabled-trees: nothing known to copy */ }
                break;
        }
        return found;
    }

    /// <summary>
    /// Copy every turned-off mod's data-dir holding folders into <c>&lt;gameArchiveDir&gt;/held/</c>, so the
    /// restore point carries the mods themselves and deleting a <c>_626mods</c> folder cannot lose them.
    /// Copy only: the held originals stay where the toggle put them. Each file is size-verified on copy and
    /// recorded with its SHA-256 (relative to the data dir) for Restore to check. A refused turn-off is not
    /// copied (that mod is still live). Throws on a failed copy; the caller records nothing in that case.
    /// </summary>
    public static IReadOnlyList<HeldCopy> CopyHeldIntoArchive(GameContext c, IReadOnlyList<ClearedMod> turnedOff,
        IReadOnlyList<ClearSkip> skips, string gameArchiveDir)
    {
        var refused = new HashSet<string>(skips.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var heldRoot = Path.Combine(gameArchiveDir, HeldDirName);
        var copies = new List<HeldCopy>();
        foreach (var cm in turnedOff)
        {
            if (refused.Contains(cm.Name)) continue;
            var files = new List<MovedFile>();
            foreach (var folder in HeldFoldersFor(c, cm))
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(c.DataDir, f);
                    var dest = Path.Combine(heldRoot, rel);
                    // A rerun over a half-written held/ (an interrupted copy) replaces what is there: these are
                    // the launcher's own copies inside its own archive, never the user's originals.
                    if (File.Exists(dest)) File.Delete(dest);
                    SafeMove.CopyFileVerified(f, dest);
                    files.Add(new MovedFile(rel, new FileInfo(dest).Length, FileTally.Sha256(dest)));
                }
            if (files.Count > 0) copies.Add(new HeldCopy(cm.Name, files));
        }
        return copies;
    }

    // Put a mod's archived copy back into the data dir when its held files are gone from there: every
    // archived file checked against its recorded SHA-256 FIRST (a damaged copy refuses the whole mod and
    // writes nothing), every destination gated against the data dir (Law B), only MISSING files written
    // (never over a file the data dir still has), each verified after the write (Law C).
    // Returns null when the mod's holding is usable, or the reason it is not.
    private static string? PutBackHeldCopy(HeldCopy h, string gameArchiveDir, GameContext liveCtx)
    {
        var dataDirFull = Path.GetFullPath(liveCtx.DataDir);
        var missing = h.Files.Where(f => !File.Exists(Path.Combine(liveCtx.DataDir, f.Rel))).ToList();
        if (missing.Count == 0) return null;   // still held in the data folder: enable from there, as before

        foreach (var f in missing)
        {
            if (!PathGate.IsContained(f.Rel, dataDirFull))
                return $"its copy in the restore point names a path outside the data folder (\"{f.Rel}\"); nothing was restored for it";
            var src = Path.Combine(gameArchiveDir, HeldDirName, f.Rel);
            if (!File.Exists(src))
                return $"its copy in the restore point is missing \"{f.Rel}\"; nothing was restored for it";
            if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(src), f.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"its copy in the restore point is damaged (checksum mismatch on \"{f.Rel}\"); nothing was restored for it";
        }

        var written = new List<string>();
        try
        {
            foreach (var f in missing)
            {
                var dest = Path.Combine(liveCtx.DataDir, f.Rel);
                SafeMove.CopyFileVerified(Path.Combine(gameArchiveDir, HeldDirName, f.Rel), dest);
                written.Add(dest);
                if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(dest), f.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"checksum mismatch after copying \"{f.Rel}\" back");
            }
            return null;
        }
        catch (Exception e)
        {
            // Take back only what this call wrote: copies of the archive's files, the archive keeps its own.
            foreach (var w in written) try { File.Delete(w); } catch { }
            return $"its copy couldn't be put back into the data folder ({e.Message})";
        }
    }

    private static IReadOnlyList<Scanner.EnableOutcome> ReEnableAll(GameContext c)
    {
        var outcomes = new List<Scanner.EnableOutcome>();
        // A holding folder is the mod's name or its HoldingName encoding ("Foo." is held in ~626~466f6f2e);
        // the toggle wants the name. An older build's raw-named hold (disabled/Aux) decodes to itself and the
        // toggle reads it through HoldingName.LegacyPath.
        foreach (var name in DirectoryNames(c.DisabledRoot).Select(HoldingName.ModName).Distinct(StringComparer.Ordinal))
            outcomes.Add(Scanner.EnableModWithOutcomeAsync(name, c).GetAwaiter().GetResult());
        return outcomes;
    }

    /// <summary>Plan (do NOT execute) the vanilla direct-inject moves for a game: detect the catalog
    /// direct-inject files in the play folder and record each as a MovedFile (rel + size + sha) while the
    /// files are STILL IN PLACE. The orchestrator seals this into the manifest BEFORE ApplyEndState moves
    /// anything (Law A: seal before destroy). ApplyEndState then executes exactly this set.</summary>
    public static IReadOnlyList<MovedFile> PlanVanillaMoves(GameContext c)
    {
        var playFolder = c.GameRoot;
        if (!Directory.Exists(playFolder)) return Array.Empty<MovedFile>();
        var fileNames = Directory.GetFiles(playFolder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
        var dirNames = Directory.GetDirectories(playFolder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
        var planned = new List<MovedFile>();
        foreach (var di in DirectInject.Detect(fileNames, dirNames))
            foreach (var rel in di.Entries)
            {
                var srcAbs = Path.Combine(playFolder, rel);
                if (File.Exists(srcAbs)) planned.Add(new MovedFile(rel, new FileInfo(srcAbs).Length, FileTally.Sha256(srcAbs)));
                else if (Directory.Exists(srcAbs)) planned.Add(new MovedFile(rel, FileTally.ByteSize(srcAbs), null));
            }
        return planned;
    }

    private static IReadOnlyList<MovedFile> ExecuteVanillaMoves(GameContext c, string gameArchiveDir, IReadOnlyList<MovedFile> planned)
    {
        // Catalog direct-inject only (loader sub-mods stay; see PlanVanillaMoves / the return-to-vanilla caveat).
        if (planned.Count == 0) return planned;
        var vanillaMoved = Path.Combine(gameArchiveDir, "vanilla-moved");
        Directory.CreateDirectory(vanillaMoved);
        foreach (var mf in planned)
        {
            var srcAbs = Path.Combine(c.GameRoot, mf.Rel);
            if (File.Exists(srcAbs) || Directory.Exists(srcAbs))
                SafeMove.Move(srcAbs, Path.Combine(vanillaMoved, mf.Rel));
        }
        return planned;
    }

    private static void UninstallFrameworks(GameContext c)
    {
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
            FrameworkRegistry.Uninstall(c.DataDir, fw.FrameworkId, c.GameRoot);
    }

    private static void FlipLoadersOff(GameContext c)
    {
        foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
        {
            if (!m.Enabled) continue;
            var abs = c.Locations.FirstOrDefault(l => l.Name == m.Location)?.Abs;
            if (abs is null) continue;
            try
            {
                if (m.Loader == "ue4ss") Ue4ssManifest.SetEnabled(abs, m.Name, enabled: false);
                else if (m.Loader == "bepinex") BepInExPlugins.SetEnabled(abs, m.Name, enable: false);
            }
            catch { /* best effort — loader manifest may be absent; vanilla is still safe */ }
        }
    }

    private static IEnumerable<string> DirectoryNames(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(d => Path.GetFileName(d)!)
            : Enumerable.Empty<string>();

    /// <summary>Restore one game from its archive: data dir copy-back, vanilla-moved files back into
    /// the game folder (PathGate-gated per destination — Law B; sha-verified — Law C), framework
    /// files back to InstallPath, loader enable-state re-applied, then the sealed <c>turnedOffByClear</c>
    /// set turned back on through ModToggle and reconciled (what is not back on is returned, never thrown;
    /// a ban-risk game without its acknowledgment is left held). No File.Delete loop in the game
    /// folder — verified per-file overwrite only. Note: PathGate validates path strings, not resolved
    /// symlink targets; a symlink inside the archive could redirect a write. Low threat on Windows
    /// since symlink creation requires elevation, but noted for completeness.
    /// <para>For the modsActive end-state: <see cref="ApplyEndState"/> already re-enabled all mods
    /// (emptied the holding folder). The archived <c>data/disabled/</c> sub-tree is therefore stale
    /// and is intentionally NOT restored — resurrecting it would place the mod in both the live mods
    /// folder and the holding folder, breaking a subsequent user-initiated disable.</para></summary>
    public static ReplayResult ReplayGame(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        var gameRootFull = Path.GetFullPath(liveCtx.GameRoot);

        // 1. Copy data dir back over the live data dir (launcher-owned; overwrite is safe).
        //    modsActive: ApplyEndState re-enabled all mods and emptied the holding folder.
        //    The archived disabled/ sub-tree is stale — skip it to prevent double-state
        //    (mod live in mods/ AND resurrected in holding), which would break a later disable.
        //    disabled-trees/ (B4 stage two: a turned-off mod's extra-tree entries) is stale for the same
        //    reason: re-enabling moved those entries back into the game, and a resurrected copy would make
        //    the next turn-off refuse on it as an earlier turned-off copy.
        var archivedData = Path.Combine(gameArchiveDir, "data");
        if (Directory.Exists(archivedData))
        {
            var skip = string.Equals(ga.EndState, "modsActive", StringComparison.OrdinalIgnoreCase)
                ? new[] { "disabled", "disabled-trees" } : null;
            CopyTreeVerifiedOverwrite(archivedData, liveCtx.DataDir, skip);
        }

        // 2. Move vanilla-moved files back into the game folder.
        foreach (var mf in ga.MovedFiles)
        {
            // Law B: gate every destination against the game root.
            if (!PathGate.IsContained(mf.Rel, gameRootFull))
                throw new InvalidOperationException($"Restore refused: \"{mf.Rel}\" escapes the game folder.");

            var srcAbs = Path.Combine(gameArchiveDir, "vanilla-moved", mf.Rel);
            var destAbs = Path.Combine(liveCtx.GameRoot, mf.Rel);

            if (File.Exists(srcAbs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
                // Overwrite-safe copy (temp + verify + atomic replace).
                var tmp = destAbs + ".rp-tmp";
                File.Copy(srcAbs, tmp, overwrite: true);
                if (new FileInfo(tmp).Length != new FileInfo(srcAbs).Length)
                { try { File.Delete(tmp); } catch { } throw new IOException($"Restore verify failed copying \"{mf.Rel}\"."); }
                if (File.Exists(destAbs)) File.Delete(destAbs);
                File.Move(tmp, destAbs);

                // Law C: sha-verify after write.
                if (mf.Sha256 is not null && !string.Equals(FileTally.Sha256(destAbs), mf.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Restore checksum mismatch on \"{mf.Rel}\".");
            }
            else if (Directory.Exists(srcAbs))
            {
                CopyTreeVerifiedOverwrite(srcAbs, destAbs);
            }
        }

        // 3. Restore framework files back to their InstallPath.
        foreach (var fw in ga.Frameworks)
        {
            if (fw.CapturedStateRel is null) continue;
            var capturedAbs = Path.Combine(gameArchiveDir, fw.CapturedStateRel);
            if (!Directory.Exists(capturedAbs)) continue;
            var installFull = Path.GetFullPath(fw.InstallPath);
            foreach (var rel in fw.InstalledFiles)
            {
                if (!PathGate.IsContained(rel, installFull))
                    throw new InvalidOperationException($"Restore refused: framework file \"{rel}\" escapes the install root.");
                var srcAbs = Path.Combine(capturedAbs, rel);
                if (!File.Exists(srcAbs)) continue;
                var destAbs = Path.Combine(fw.InstallPath, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
                var tmp = destAbs + ".rp-tmp";
                File.Copy(srcAbs, tmp, overwrite: true);
                if (new FileInfo(tmp).Length != new FileInfo(srcAbs).Length)
                { try { File.Delete(tmp); } catch { } throw new IOException($"Restore verify failed copying framework file \"{rel}\"."); }
                if (File.Exists(destAbs)) File.Delete(destAbs);
                File.Move(tmp, destAbs);
            }
        }

        // 4. Re-apply loader enable state (best effort — loader manifest may be absent).
        foreach (var lm in ga.LoaderMods)
        {
            var abs = liveCtx.Locations.FirstOrDefault(l => l.Name == lm.Location)?.Abs;
            if (abs is null) continue;
            try
            {
                if (lm.Loader == "ue4ss") Ue4ssManifest.SetEnabled(abs, lm.Name, lm.Enabled);
                else if (lm.Loader == "bepinex") BepInExPlugins.SetEnabled(abs, lm.Name, lm.Enabled);
            }
            catch { /* best effort */ }
        }

        // 5. Turn back on exactly what the vanilla clear turned off (null = an archive from before the
        //    record: nothing to do, as before). Last, so the files and loaders those mods need are back.
        var notBackOn = TurnBackOn(ga, gameArchiveDir, liveCtx);

        // 6. Remove the launcher-authored off-boarding sheet if present.
        // Law B: gate the manifest-supplied path against the game root before deleting.
        if (ga.OffboardingSheetGameFolderPath is not null
            && PathGate.IsContainedAbsolute(ga.OffboardingSheetGameFolderPath, liveCtx.GameRoot)
            && File.Exists(ga.OffboardingSheetGameFolderPath))
            try { File.Delete(ga.OffboardingSheetGameFolderPath); } catch { /* best effort */ }

        return new ReplayResult(notBackOn);
    }

    /// <summary>Why Restore left a ban-risk game's mods held.</summary>
    public const string BanRiskLeftOff =
        "this game carries a ban risk, so 626 won't turn mods on without your say-so. "
        + "Turn them on from the launcher, which asks first.";

    // Turn the sealed set back on through ModToggle, loader rows first (the reverse of the turn-off order),
    // minus the turn-offs that refused (they never went off). Then reconcile against ONE listing: whatever
    // is not on is reported, with the lane's reason when it gave one. Judging by the final listing, not by
    // each call, is what lets a restore point from a clear that died partway finish cleanly: a mod MUTATE
    // never reached is still on, and is not reported.
    private static IReadOnlyList<ClearSkip> TurnBackOn(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        if (ga.TurnedOffByClear is not { Count: > 0 } set
            || !string.Equals(ga.EndState, "vanilla", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<ClearSkip>();
        var refused = new HashSet<string>((ga.TurnOffSkipped ?? Array.Empty<ClearSkip>()).Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);
        var wanted = set.Where(cm => !refused.Contains(cm.Name)).Reverse().ToList();
        if (wanted.Count == 0) return Array.Empty<ClearSkip>();

        var game = liveCtx.Game;
        var notBack = new List<ClearSkip>();

        // Where the data folder lost a mod's held files, put the restore point's copy back first, so every
        // mod below is turned on the one way: from the data folder, through the toggle path. A damaged or
        // incomplete copy refuses that mod alone (reported, nothing written); the others carry on.
        var copies = (ga.HeldCopies ?? Array.Empty<HeldCopy>())
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new HeldCopy(g.Key, g.SelectMany(h => h.Files).ToList()), StringComparer.OrdinalIgnoreCase);
        foreach (var cm in wanted.ToList())
        {
            if (!copies.TryGetValue(cm.Name, out var h)) continue;
            if (PutBackHeldCopy(h, gameArchiveDir, liveCtx) is { } why)
            {
                notBack.Add(new ClearSkip(cm.Name, why));
                wanted.Remove(cm);
            }
        }
        if (wanted.Count == 0) return notBack;

        var rows = ModListing.Resolve(game);

        // Never enable on a ban-risk game without its acknowledgment. The ack lives in the data dir, which
        // step 1 has just put back, so a game acknowledged before the clear is acknowledged now. The held
        // copies are back in the data folder, so the mods list as turned off, ready for the user to switch on.
        if (BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(game), BanRiskAckStore.IsAcked(liveCtx.DataDir, game.Id ?? "")))
            return notBack.Concat(wanted.Where(cm => FindRow(rows, cm) is not { Enabled: true })
                .Select(cm => new ClearSkip(cm.Name, BanRiskLeftOff))).ToList();

        // One scope for every turn-on, so the scanner's lane lists the game once, not once per mod.
        var scope = new Scanner.BulkScope(liveCtx);
        var reasons = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cm in wanted)
        {
            var found = FindRow(rows, cm);
            if (found is { Enabled: true }) continue;   // never went off (a clear that died partway): already back
            var row = found ?? new Mod { Name = cm.Name, Location = cm.Location };
            try
            {
                var o = ModToggle.SetEnabledInScopeAsync(liveCtx, row, true, scope).GetAwaiter().GetResult();
                if (o is { Enabled: false }) reasons[cm.Name] = o.Reason;
            }
            catch (Exception e) { reasons[cm.Name] = e.Message; }
        }
        return notBack.Concat(ModToggle.NotApplied(game, wanted.Select(cm => (cm.Name, true)))
            .Select(n => new ClearSkip(n, reasons.GetValueOrDefault(n) ?? "it didn't come back on")))
            .ToList();
    }

    // Verified copy that OVERWRITES existing files (restore replays over a known layout — NOT a
    // delete-then-extract). Per-file: copy to temp sibling, verify size, atomic replace. No game-folder
    // File.Delete loop. (SafeMove.CopyDirVerified refuses pre-existing dests, so it can't be used here.)
    // skipTopLevel: top-level sub-directory names to skip (case-insensitive). Only honoured at the first
    // level of recursion; nested calls never propagate the skip set.
    private static void CopyTreeVerifiedOverwrite(string src, string dest, IReadOnlyCollection<string>? skipTopLevel = null)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
        {
            var target = Path.Combine(dest, Path.GetFileName(f));
            var tmp = target + ".rp-tmp";
            File.Copy(f, tmp, overwrite: true);
            if (new FileInfo(tmp).Length != new FileInfo(f).Length)
            { try { File.Delete(tmp); } catch { } throw new IOException($"Restore verify failed copying \"{f}\"."); }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }
        foreach (var d in Directory.GetDirectories(src))
        {
            var name = Path.GetFileName(d);
            if (skipTopLevel is not null && skipTopLevel.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyTreeVerifiedOverwrite(d, Path.Combine(dest, name));   // nested calls do NOT propagate the skip
        }
    }

    /// <summary>Copy the game's data dir + framework install state into the archive, and build its
    /// manifest entry. Non-destructive: the live data dir and game folder are untouched.
    /// <paramref name="gameArchiveDir"/> must be a fresh directory — if its <c>data</c>
    /// sub-directory already exists this method throws <see cref="InvalidOperationException"/>.</summary>
    public static GameArchive CaptureGame(GameCaptureInput input, string gameArchiveDir)
    {
        var c = input.Context;

        if (Directory.Exists(Path.Combine(gameArchiveDir, "data")))
            throw new InvalidOperationException(
                $"Restore-point archive dir already populated: \"{gameArchiveDir}\". Capture requires a fresh directory.");

        Directory.CreateDirectory(gameArchiveDir);

        if (Directory.Exists(c.DataDir))
            SafeMove.CopyDirVerified(c.DataDir, Path.Combine(gameArchiveDir, "data"));

        var frameworks = new List<FrameworkArchive>();
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
        {
            var capturedRel = Path.Combine("frameworks-state", fw.FrameworkId);
            var capturedAbs = Path.Combine(gameArchiveDir, capturedRel);
            foreach (var rel in fw.InstalledFiles)
            {
                var srcAbs = Path.Combine(fw.InstallPath, rel);
                if (File.Exists(srcAbs)) SafeMove.CopyFileVerified(srcAbs, Path.Combine(capturedAbs, rel));
            }
            frameworks.Add(new FrameworkArchive(fw.FrameworkId, fw.DisplayName, fw.Author,
                fw.InstallPath, fw.InstalledFiles, capturedRel));
        }

        var modList = Scanner.BuildModListAsync(c).GetAwaiter().GetResult();
        var meta = Scanner.LoadMetadata(c);

        var mods = new List<ArchivedMod>();
        var loaderMods = new List<LoaderModState>();
        foreach (var m in modList)
        {
            // Mod.Base is only populated by ListWithClass (variant parsing); BuildModListAsync leaves
            // it empty. Derive the variant-stripped base ourselves — MergeMetadata keys by base, so
            // a mod named "MoreStamina_5x" must look up metadata under "MoreStamina", not the full name.
            var metaKey = Variant.ParseVariant(m.Name).Base;
            if (string.IsNullOrEmpty(metaKey)) metaKey = m.Name;
            meta.TryGetValue(metaKey, out var md);
            mods.Add(new ArchivedMod(m.Name, m.Enabled, md?.Url, md?.SourceConfidence, md?.InstalledUtc?.ToString("o")));
            if (m.Loader is "ue4ss" or "bepinex")
                loaderMods.Add(new LoaderModState(m.Name, m.Loader, m.Enabled, m.Location));
        }

        var ownedMods = new List<OwnedModNote>();
        foreach (var loc in c.Locations)
        {
            var owner = ToolOwnership.Detect(loc.Abs);
            if (owner is null) continue;
            foreach (var m in modList)
                if (m.ReadOnly && m.Location == loc.Name)
                    ownedMods.Add(new OwnedModNote(m.Name, owner.ToString()!));
        }

        // Saves: record the live save folder (Safe Clear NEVER touches it) + how many launcher-made
        // save backups got copied into this restore point's data/saves. Both are reported on the
        // off-boarding sheet so a reset is never silent about the user's irreplaceable data.
        var saveLocation = string.IsNullOrEmpty(c.SaveDir) ? null : c.SaveDir;
        var saveBackupCount = CountSaveBackups(c.SavesDir);

        return new GameArchive(
            Id: input.Game.Id,
            GameName: input.Game.GameName,
            GameRoot: c.GameRoot,
            EndState: input.EndState,
            LaunchTargets: input.Game.LaunchTargets,
            RequiredLauncher: input.Game.RequiredLauncher,
            Frameworks: frameworks,
            LoaderMods: loaderMods,
            OwnedMods: ownedMods,
            MovedFiles: Array.Empty<MovedFile>(),
            Mods: mods,
            OffboardingSheetGameFolderPath: null,
            SaveLocation: saveLocation,
            SaveBackupCount: saveBackupCount,
            DataDir: c.DataDir);
    }

    // How many launcher-made save backups live under the per-game saves dir (each backup is a
    // timestamped subfolder). Best-effort, read-only — a missing/unreadable dir is simply zero.
    private static int CountSaveBackups(string savesDir)
    {
        try { return Directory.Exists(savesDir) ? Directory.GetDirectories(savesDir).Length : 0; }
        catch { return 0; }
    }
}
