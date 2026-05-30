using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>Hydrated inputs the engine needs to capture one game. All derivable from Core; the App
/// supplies the GameEntry + a built GameContext and picks the end-state.</summary>
public sealed record GameCaptureInput(GameEntry Game, GameContext Context, string EndState);

/// <summary>Result of <see cref="RestorePointEngine.ApplyEndState"/>. vanilla populates MovedFiles;
/// modsActive populates EnableOutcomes. The other list is always empty.</summary>
public sealed record EndStateResult(
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<Scanner.EnableOutcome> EnableOutcomes);

/// <summary>
/// The headless Safe Clear / Restore file engine. Takes explicit archive paths — no %APPDATA%
/// knowledge, no UI. Composes Phase 0 primitives (SafeMove, PathGate) + existing Core. The App
/// orchestrator (Phase 1B) calls these in the Law-A order: capture-all -> seal -> mutate-all.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>Apply the chosen end-state to a game AFTER its capture is sealed.
    /// vanilla: move detected direct-inject game-folder files into the archive (recorded), uninstall
    /// frameworks (their files were captured), flip loader manifests off; owned mods untouched.
    /// modsActive: re-enable everything from holding, returning per-mod outcomes (skips surfaced).
    /// <para>When <paramref name="plannedVanillaMoves"/> is supplied, MUTATE executes EXACTLY that set
    /// (sealed by the orchestrator in CAPTURE-ALL — single source of truth, no re-detect drift).
    /// When null (skip-archive path, no sealed manifest), the moves are planned on the spot.</para></summary>
    public static EndStateResult ApplyEndState(GameContext c, string endState, string gameArchiveDir,
        IReadOnlyList<MovedFile>? plannedVanillaMoves = null)
    {
        if (string.Equals(endState, "modsActive", StringComparison.OrdinalIgnoreCase))
            return new EndStateResult(Array.Empty<MovedFile>(), ReEnableAll(c));
        if (!string.Equals(endState, "vanilla", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown end-state \"{endState}\" (expected \"vanilla\" or \"modsActive\").", nameof(endState));

        // Execute EXACTLY the sealed plan when given (single source of truth — no re-detect drift).
        // Skip-archive has no sealed manifest, so plan now.
        var planned = plannedVanillaMoves ?? PlanVanillaMoves(c);
        var moved = ExecuteVanillaMoves(c, gameArchiveDir, planned);
        UninstallFrameworks(c);
        FlipLoadersOff(c);
        return new EndStateResult(moved, Array.Empty<Scanner.EnableOutcome>());
    }

    private static IReadOnlyList<Scanner.EnableOutcome> ReEnableAll(GameContext c)
    {
        var outcomes = new List<Scanner.EnableOutcome>();
        foreach (var name in DirectoryNames(c.DisabledRoot))
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
    /// files back to InstallPath, loader enable-state re-applied. No File.Delete loop in the game
    /// folder — verified per-file overwrite only. Note: PathGate validates path strings, not resolved
    /// symlink targets; a symlink inside the archive could redirect a write. Low threat on Windows
    /// since symlink creation requires elevation, but noted for completeness.
    /// <para>For the modsActive end-state: <see cref="ApplyEndState"/> already re-enabled all mods
    /// (emptied the holding folder). The archived <c>data/disabled/</c> sub-tree is therefore stale
    /// and is intentionally NOT restored — resurrecting it would place the mod in both the live mods
    /// folder and the holding folder, breaking a subsequent user-initiated disable.</para></summary>
    public static void ReplayGame(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        var gameRootFull = Path.GetFullPath(liveCtx.GameRoot);

        // 1. Copy data dir back over the live data dir (launcher-owned; overwrite is safe).
        //    modsActive: ApplyEndState re-enabled all mods and emptied the holding folder.
        //    The archived disabled/ sub-tree is stale — skip it to prevent double-state
        //    (mod live in mods/ AND resurrected in holding), which would break a later disable.
        var archivedData = Path.Combine(gameArchiveDir, "data");
        if (Directory.Exists(archivedData))
        {
            var skip = string.Equals(ga.EndState, "modsActive", StringComparison.OrdinalIgnoreCase)
                ? new[] { "disabled" } : null;
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

        // 5. Remove the launcher-authored off-boarding sheet if present.
        // Law B: gate the manifest-supplied path against the game root before deleting.
        if (ga.OffboardingSheetGameFolderPath is not null
            && PathGate.IsContainedAbsolute(ga.OffboardingSheetGameFolderPath, liveCtx.GameRoot)
            && File.Exists(ga.OffboardingSheetGameFolderPath))
            try { File.Delete(ga.OffboardingSheetGameFolderPath); } catch { /* best effort */ }
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
            SaveBackupCount: saveBackupCount);
    }

    // How many launcher-made save backups live under the per-game saves dir (each backup is a
    // timestamped subfolder). Best-effort, read-only — a missing/unreadable dir is simply zero.
    private static int CountSaveBackups(string savesDir)
    {
        try { return Directory.Exists(savesDir) ? Directory.GetDirectories(savesDir).Length : 0; }
        catch { return 0; }
    }
}
