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
/// clear turned off is back on (or the archive carried no turn-off record).
/// <para><c>Recovered</c>: turn-offs that had refused partway (their files stranded in holding) and are back on.</para>
/// <para><c>RemainderIssues</c>: vanilla-remainder files not put back (Name is the path relative to the game
/// root), each with why: a different file is live there, the archived copy is damaged, or the path is refused.</para></summary>
public sealed record ReplayResult(IReadOnlyList<ClearSkip> NotBackOn, IReadOnlyList<ClearSkip> Recovered,
    IReadOnlyList<ClearSkip>? RemainderIssues = null)
{
    public IReadOnlyList<ClearSkip> RemainderIssues { get; init; } = RemainderIssues ?? Array.Empty<ClearSkip>();
}

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
        // A framework's installed files are the framework's: its uninstall removes them and its captured state
        // puts them back. A row made of them (the DLL mod loader Elden Mod Loader installs, a proxy DLL a
        // framework dropped) turned off as well would be held AND restored twice, so it is left to the framework.
        var frameworkOwned = FrameworkRegistry.List(c.DataDir)
            .SelectMany(fw => fw.InstalledFiles.Select(f => FullNorm(Path.Combine(fw.InstallPath, f))))
            .Where(p => p is not null).Select(p => p!).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installed = ModInstallRegistry.List(c.DataDir);
        return ModListing.Resolve(c.Game)
            .Where(m => m.Enabled && !m.ReadOnly && m.Loader is not ("ue4ss" or "bepinex"))
            .Where(m => InTurnOffScope(c, m, installed))
            .Where(m => !HasBaseGamePak(c, m))
            .Where(m => !CoveredByMoves(c, m, moved))
            .Where(m => !CoveredByMoves(c, m, frameworkOwned))
            .Where(m => seen.Add(m.Location + "\u0000" + m.Name))
            .OrderBy(m => IsLoaderRow(m) ? 1 : 0)   // stable: listing order within each group
            .Select(m => new ClearedMod(m.Name, m.Location))
            .ToList();
    }

    /// <summary>
    /// Roughly how many bytes vanilla will copy into the restore point for this game: the main files of
    /// every row it would turn off (folders summed whole) plus the extra-tree entries each turn-off would move
    /// (the same <see cref="ExtraTreeRows"/> selection the toggle uses). A pre-flight free-space estimate, read-only.
    /// </summary>
    public static long EstimateTurnOffBytes(GameContext c) => EstimateTurnOffBytes(c, outsideModOnlyOnly: false);

    /// <summary><see cref="EstimateTurnOffBytes(GameContext)"/>, optionally counting only the rows whose held
    /// copies come from OUTSIDE the mod-only folders (direct-inject and loose-root holds), so a pre-flight
    /// can add them to <see cref="EstimateModOnlyBytes"/> without counting a mod-only folder twice.</summary>
    public static long EstimateTurnOffBytes(GameContext c, bool outsideModOnlyOnly)
    {
        long total = 0;
        var rows = ModListing.Resolve(c.Game);
        // The extra-tree selection, read once: what each row's turn-off would move out of the extra trees.
        var extraRows = Scanner.ExtraTreeRowsFor(c);
        foreach (var cm in PlanVanillaTurnOffs(c, PlanVanillaMoves(c)))
        {
            var row = FindRow(rows, cm);
            var baseDir = row is null ? null : BaseDirFor(c, row);
            if (row is null || baseDir is null) continue;
            // Rows in a swept (vouched) folder are counted by EstimateModOnlyBytes; everything else here
            // (direct-inject, loose-root, proxy-loader, paks-root, outside-root, 626-installed) is added.
            if (outsideModOnlyOnly && c.Locations.FirstOrDefault(l => l.Name == row.Location) is { } vl
                && string.IsNullOrEmpty(vl.Managed) && ModOnlyFolders.WhyModOnly(c, vl) is not null) continue;
            try
            {
                foreach (var x in extraRows.MovesFor(row).Movable)
                    total += File.Exists(x.AbsPath) ? new FileInfo(x.AbsPath).Length
                           : Directory.Exists(x.AbsPath) ? FileTally.ByteSize(x.AbsPath) : 0;
            }
            catch { /* unreadable: an estimate */ }
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

    // A row vanilla may turn off (review r4, I5): one on a lane with its own safe mechanism (direct-inject,
    // loose-root, Mod Engine 2's config, a proxy step-aside), or one whose location the launcher KNOWS holds
    // only mods. A scanner row anywhere else (Skyrim.esm in Data, a base pak in a files-form Content/Paks)
    // could be the game itself: it is left on, and the sheet lists it as still active.
    //
    // Round 5 widens it where the review showed it was safe, and where Este ruled it must stay useful:
    //  - a paks-root location (Scanner.GuardNoBasePakMove refuses a base pak there, and base-pak rows are
    //    skipped up front by HasBaseGamePak anyway);
    //  - a location outside the game folder (Documents\...\Mods): base content can't plausibly live there;
    //  - anywhere else, a row whose every file an install record says 626 placed (ModInstallRegistry):
    //    the launcher wrote those bytes, so they are a mod by evidence. Skyrim.esm never has a record.
    private static bool InTurnOffScope(GameContext c, Mod m, IReadOnlyList<ModInstallManifest> installed)
    {
        if (m.Location is "direct-inject" or "mod engine 2" or ProxyLoaderRows.LocationTag
            || m.Location == LooseMods.LooseRootListing.LooseRootLocation)
            return true;
        var loc = c.Locations.FirstOrDefault(l => l.Name == m.Location);
        if (loc is null || !string.IsNullOrEmpty(loc.Managed)) return false;
        // A system folder (a drive root, the profile, Windows, Program Files, an ancestor of the game) is never
        // a mod folder, whatever a location says: no turn-offs there at all (review r5, I-C).
        if (SystemFolderReason(c, loc.Abs) is not null) return false;
        if (ModOnlyFolders.WhyModOnly(c, loc) is not null) return true;
        if (loc.Form == "paks-root") return true;
        if (IsOutsideGame(c, loc.Abs)) return true;
        return PlacedBy626(c, m, loc, installed);
    }

    /// <summary>
    /// Why <paramref name="folder"/> is a system folder vanilla never touches, or null. A location set to one
    /// of these is a mistake, and acting on it would hit the game itself or Windows: an ancestor of the game
    /// folder (a <c>steamapps\common</c> holds the game), a drive root, the user profile or its AppData,
    /// Documents or Desktop themselves, Windows, Program Files or ProgramData (review r5, I-C).
    /// </summary>
    internal static string? SystemFolderReason(GameContext c, string folder)
    {
        var full = FullNorm(folder);
        var root = FullNorm(c.GameRoot);
        if (full is null) return null;
        if (root is not null && IsUnder(root, full)) return "it contains the game folder itself";
        try { if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), full, StringComparison.OrdinalIgnoreCase)) return "it is a whole drive"; }
        catch { }
        foreach (var (sf, what) in SystemFolders)
        {
            string? p;
            try { p = Environment.GetFolderPath(sf); } catch { continue; }
            if (string.IsNullOrEmpty(p) || FullNorm(p) is not { } sys) continue;
            if (string.Equals(sys, full, StringComparison.OrdinalIgnoreCase)) return $"it is {what}";
        }
        return null;
    }

    private static readonly (Environment.SpecialFolder Folder, string What)[] SystemFolders =
    {
        (Environment.SpecialFolder.UserProfile, "your user profile folder"),
        (Environment.SpecialFolder.ApplicationData, "your AppData folder"),
        (Environment.SpecialFolder.LocalApplicationData, "your AppData folder"),
        (Environment.SpecialFolder.MyDocuments, "your Documents folder"),
        (Environment.SpecialFolder.DesktopDirectory, "your Desktop folder"),
        (Environment.SpecialFolder.Desktop, "your Desktop folder"),
        (Environment.SpecialFolder.Windows, "the Windows folder"),
        (Environment.SpecialFolder.System, "the Windows system folder"),
        (Environment.SpecialFolder.ProgramFiles, "the Program Files folder"),
        (Environment.SpecialFolder.ProgramFilesX86, "the Program Files folder"),
        (Environment.SpecialFolder.CommonApplicationData, "the ProgramData folder"),
    };

    // A location outside the game folder that isn't a system folder (Documents\...\The Sims 4\Mods).
    private static bool IsOutsideGame(GameContext c, string folder)
    {
        var full = FullNorm(folder);
        var root = FullNorm(c.GameRoot);
        return full is not null && root is not null && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
               && ModOnlyFolders.RelativeToRoot(c.GameRoot, folder) is null
               && SystemFolderReason(c, folder) is null;
    }

    /// <summary>The note for a row vanilla leaves on because 626 replaced one of the game's own files with it.</summary>
    public const string ReplacedGameFileNote =
        "still active: 626 replaced a game file here; turning it off would leave the game without it";

    // Every file 626 replaced (ReplacedStore batch manifests under <dataDir>\replaced), by full path. Turning
    // such a row off would move the mod's copy to holding and leave the game with neither file (review r5, I-B).
    internal static HashSet<string> ReplacedGameFiles(GameContext c)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(c.DataDir, "replaced");
        if (!Directory.Exists(root)) return set;
        IEnumerable<string> manifests;
        try { manifests = Directory.EnumerateFiles(root, "__626replaced.json", SearchOption.AllDirectories).ToList(); }
        catch { return set; }
        foreach (var mf in manifests)
        {
            try
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<ReplacedStore.ReplacedEntry>>(File.ReadAllText(mf),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (var e in entries ?? new List<ReplacedStore.ReplacedEntry>())
                    if (!string.IsNullOrEmpty(e.OriginalPath) && FullNorm(e.OriginalPath) is { } p) set.Add(p);
            }
            catch { /* a torn record: nothing known from it */ }
        }
        return set;
    }

    // True when any of the row's files (or, for a folder row, any file under it) is one 626 replaced.
    internal static bool ReplacedAGameFile(GameContext c, Mod m, HashSet<string> replaced)
    {
        if (replaced.Count == 0 || BaseDirFor(c, m) is not { } baseDir) return false;
        foreach (var f in m.Files)
        {
            if (FullNorm(Path.Combine(baseDir, f)) is not { } p) continue;
            if (replaced.Contains(p) || replaced.Any(r => IsUnder(r, p))) return true;
        }
        return false;
    }

    /// <summary>
    /// The row is a mod by evidence: an install record for its location lists every one of its files, so 626
    /// wrote those bytes. A file row: each of its files is recorded and exists as a file. A folder row: every
    /// file under the folder (links not followed) is recorded under the folder's prefix, and at least one is.
    ///
    /// <para>Never a row with a file 626 REPLACED (I-B): the record claims it, but the original was the game's.
    /// Record entries that are rooted or climb with <c>..</c> are ignored, and a file changed after the install
    /// (written later than the record, beyond a small slack) doesn't count.</para>
    ///
    /// <para>Names only, otherwise: <see cref="ModInstallManifest"/> carries no per-file size or hash today, so
    /// there is nothing to compare content against. When it gains them, compare them here.</para>
    /// </summary>
    private static bool PlacedBy626(GameContext c, Mod m, ModLocationCtx loc, IReadOnlyList<ModInstallManifest> installed)
    {
        if (m.Files.Count == 0) return false;
        if (ReplacedAGameFile(c, m, ReplacedGameFiles(c))) return false;
        static string Norm(string f) => f.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
        var records = installed.Where(i => string.Equals(i.Location, m.Location, StringComparison.OrdinalIgnoreCase)).ToList();
        var recorded = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            foreach (var f in r.Files)
            {
                if (string.IsNullOrWhiteSpace(f) || IsRooted(f) || f.Replace('\\', '/').Split('/').Contains("..")) continue;
                var key = Norm(f.Replace('\\', '/'));
                if (!recorded.TryGetValue(key, out var at) || r.InstalledUtc > at) recorded[key] = r.InstalledUtc;
            }

        bool Placed(string rel)
        {
            if (!recorded.TryGetValue(Norm(rel.Replace('\\', '/')), out var installedUtc)) return false;
            var abs = Path.Combine(loc.Abs, rel);
            if (!File.Exists(abs)) return false;   // a folder or nothing: never matched as a placed file
            try { return File.GetLastWriteTimeUtc(abs) <= installedUtc.ToUniversalTime().AddMinutes(10); }
            catch { return false; }
        }

        if (m.IsFolder)
        {
            var folder = Path.Combine(loc.Abs, m.Files[0]);
            if (!Directory.Exists(folder) || IsLink(folder)) return false;
            var files = FilesNoLinks(folder, folder, null).ToList();
            return files.Count > 0 && files.All(f => Placed(Path.GetRelativePath(loc.Abs, f)));
        }
        return m.Files.All(Placed);
    }
    // A row with a file that looks like the base game's own pak is never turned off by vanilla, in any form
    // (GuardNoBasePakMove only guards paks-root). Named on the sheet as still active (review r4, m2).
    private static bool HasBaseGamePak(GameContext c, Mod m)
    {
        if (BaseDirFor(c, m) is not { } baseDir) return false;
        foreach (var f in m.Files)
        {
            var name = Path.GetFileName(f);
            long size = 0;
            try { var fi = new FileInfo(Path.Combine(baseDir, f)); if (fi.Exists) size = fi.Length; } catch { }
            if (IsBaseGameArchiveName(name, size)) return true;
        }
        return false;
    }

    /// <summary>A pak/ucas/utoc file that is the base game's own: <see cref="PakClassifier.IsBaseGamePak"/>, or
    /// a UE5 <c>global.ucas</c> / <c>global.utoc</c> (which its regex never matched).</summary>
    internal static bool IsBaseGameArchiveName(string fileName, long size)
    {
        var ext = Path.GetExtension(fileName);
        if (!(ext.Equals(".pak", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ucas", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".utoc", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Path.GetFileNameWithoutExtension(fileName).Equals("global", StringComparison.OrdinalIgnoreCase)) return true;
        return PakClassifier.IsBaseGamePak(Path.ChangeExtension(fileName, ".pak"), size);
    }

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
        var plan = new List<(string Mod, string Src, string Rel)>();
        foreach (var cm in turnedOff)
        {
            if (refused.Contains(cm.Name)) continue;
            foreach (var folder in HeldFoldersFor(c, cm))
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    plan.Add((cm.Name, f, Path.GetRelativePath(c.DataDir, f)));
        }

        // Fail closed, before anything is copied: a name ending in a dot or a space only exists by its exact
        // name, and a plain copy would read or write a DIFFERENT file Windows normalises it to. The caller
        // records no copy and the data folder stays the source, which still restores.
        foreach (var x in plan)
            if (HasUncopyableSegment(x.Rel))
                throw new IOException($"\"{x.Rel}\" has a name Windows can only reach exactly, so it can't be copied safely");

        var copies = new List<HeldCopy>();
        foreach (var grp in plan.GroupBy(x => x.Mod, StringComparer.OrdinalIgnoreCase))
        {
            var files = new List<MovedFile>();
            foreach (var (_, src, rel) in grp)
            {
                var dest = Path.Combine(heldRoot, rel);
                // Temp sibling, verified, then moved into place over whatever an interrupted earlier run left
                // there: the final path is only ever a complete, checked copy, and never briefly absent.
                var tmp = dest + ".rp-tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
                SafeMove.CopyFileVerified(src, tmp);
                var sha = FileTally.Sha256(tmp);
                if (!string.Equals(sha, FileTally.Sha256(src), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    throw new IOException($"checksum mismatch copying \"{rel}\" into the restore point");
                }
                var bytes = new FileInfo(tmp).Length;
                File.Move(tmp, dest, overwrite: true);
                files.Add(new MovedFile(rel, bytes, sha));
            }
            if (files.Count > 0) copies.Add(new HeldCopy(grp.Key, files));
        }
        return copies;
    }

    // A path segment ending in "." or " " (other than "." / ".." themselves): reachable only by exact name.
    private static bool HasUncopyableSegment(string rel)
        => rel.Split('\\', '/').Any(s => s.Length > 0 && s is not ("." or "..") && (s.EndsWith('.') || s.EndsWith(' ')));

    // A manifest path must be relative. PathGate trims a leading separator (a backslash-led path reads as
    // relative), so a rooted or separator-led path is refused here first, at every restore site.
    private static bool IsRooted(string rel)
        => string.IsNullOrEmpty(rel) || Path.IsPathRooted(rel) || rel[0] is '\\' or '/';

    /// <summary>Tests only: called with each data-folder destination a held put-back is about to write.
    /// Thread-static like the scanner's hooks: ReplayGame runs synchronously on the caller's thread.</summary>
    [ThreadStatic] internal static Action<string>? BeforeHeldPutBackFileForTests;

    // Put a mod's archived copy back into the data dir when some of its held files are gone from there.
    //  - Nothing missing: usable as is (today's behaviour), enable from the data folder.
    //  - Something missing: every PRESENT recorded file is hashed too; one that differs from the record means
    //    the data folder holds a different copy, and the mod is left exactly as it is (no merge).
    //  - Every path is refused if rooted or escaping (Law B), and every missing file's archived copy is
    //    SHA-checked, before anything is written. A damaged copy refuses the whole mod and writes nothing.
    //  - Each file goes to a temp sibling, is size- and SHA-checked, then moved into place: no partial file ever
    //    sits on a final path. Destinations are tracked BEFORE writing, so a failure rolls all of them back.
    // Returns null when the mod's holding is usable, or the reason it is not.
    private static string? PutBackHeldCopy(HeldCopy h, string gameArchiveDir, GameContext liveCtx)
    {
        var dataDirFull = Path.GetFullPath(liveCtx.DataDir);
        foreach (var f in h.Files)
            if (IsRooted(f.Rel) || !PathGate.IsContained(f.Rel, dataDirFull))
                return $"its copy in the restore point names a path outside the data folder (\"{f.Rel}\"); nothing was restored for it";

        var missing = h.Files.Where(f => !File.Exists(Path.Combine(liveCtx.DataDir, f.Rel))).ToList();
        if (missing.Count == 0) return null;   // still held in the data folder: enable from there, as before

        foreach (var f in h.Files.Except(missing))
            if (f.Sha256 is not null
                && !string.Equals(FileTally.Sha256(Path.Combine(liveCtx.DataDir, f.Rel)), f.Sha256, StringComparison.OrdinalIgnoreCase))
                return DifferentCopy;

        foreach (var f in missing)
        {
            var src = Path.Combine(gameArchiveDir, HeldDirName, f.Rel);
            if (!File.Exists(src))
                return $"its copy in the restore point is missing \"{f.Rel}\"; nothing was restored for it";
            if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(src), f.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"its copy in the restore point is damaged (checksum mismatch on \"{f.Rel}\"); nothing was restored for it";
        }

        var temps = new List<string>();   // tracked BEFORE writing: a failing file's temp is rolled back too
        var placed = new List<string>();  // finals this call's own move put there, and only those
        try
        {
            foreach (var f in missing)
            {
                var dest = Path.Combine(liveCtx.DataDir, f.Rel);
                var tmp = dest + ".rp-tmp";
                temps.Add(tmp);
                BeforeHeldPutBackFileForTests?.Invoke(dest);
                SafeMove.CopyFileVerified(Path.Combine(gameArchiveDir, HeldDirName, f.Rel), tmp);
                if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(tmp), f.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"checksum mismatch after copying \"{f.Rel}\" back");
                File.Move(tmp, dest);   // dest was missing: never over a file the data folder has
                placed.Add(dest);
            }
            return null;
        }
        catch (Exception e)
        {
            // Take back only what this call wrote: its temps, and the finals ITS moves placed. A file that
            // appeared at a destination from elsewhere (the move onto it failed) is not this call's to delete.
            foreach (var w in temps.Concat(placed)) try { if (File.Exists(w)) File.Delete(w); } catch { }
            return $"its copy couldn't be put back into the data folder ({e.Message}); nothing was restored for it";
        }
    }

    /// <summary>Why Restore left a mod that is live under the same name from another location.</summary>
    public const string AlreadyOnDifferentCopy = "already on (a different copy), so 626 left it as it is";

    /// <summary>Why Restore left a mod whose data-folder copy differs from the restore point's.</summary>
    public const string DifferentCopy =
        "the data folder holds a different copy than the restore point, so 626 left it as it is";

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
            if (IsRooted(mf.Rel) || !PathGate.IsContained(mf.Rel, gameRootFull))
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
                if (IsRooted(rel) || !PathGate.IsContained(rel, installFull))
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

        // 3b. The vanilla remainder back into the game's mod folders, verified, BEFORE the loader manifests
        //     (a UE4SS mods.txt can be part of it) and before the turn-ons (sidecars beside held mods).
        //     On a ban-risk game with no acknowledgment, the remainder stays in the restore point (I3): it is
        //     mod files, and putting them live would enable mods without the say-so step 5 also waits for.
        var banGated = BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(liveCtx.Game),
            BanRiskAckStore.IsAcked(liveCtx.DataDir, liveCtx.Game.Id ?? ""));
        IReadOnlyList<ClearSkip> remainderIssues = banGated
            ? (ga.VanillaRemainder is { Count: > 0 } rem
                ? new[] { new ClearSkip($"{rem.Count} file(s) no mod claims", RemainderKeptForBanRisk) }
                : Array.Empty<ClearSkip>())
            : RestoreRemainder(ga, gameArchiveDir, liveCtx);

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
        var (notBackOn, recovered) = TurnBackOn(ga, gameArchiveDir, liveCtx, banGated ? Array.Empty<ClearSkip>() : remainderIssues);

        // 6. Remove the launcher-authored off-boarding sheet if present.
        // Law B: gate the manifest-supplied path against the game root before deleting.
        if (ga.OffboardingSheetGameFolderPath is not null
            && PathGate.IsContainedAbsolute(ga.OffboardingSheetGameFolderPath, liveCtx.GameRoot)
            && File.Exists(ga.OffboardingSheetGameFolderPath))
            try { File.Delete(ga.OffboardingSheetGameFolderPath); } catch { /* best effort */ }

        return new ReplayResult(notBackOn, recovered, remainderIssues);
    }

    /// <summary>Why Restore left a ban-risk game's mods held.</summary>
    public const string BanRiskLeftOff =
        "this game carries a ban risk, so 626 won't turn mods on without your say-so. "
        + "Turn them on from the launcher, which asks first.";

    // Turn the sealed set back on through ModToggle, loader rows first (the reverse of the turn-off order).
    // Then reconcile against ONE listing: whatever is not on is reported, with the lane's reason when it
    // gave one. Judging by the final listing, not by each call, is what lets a restore point from a clear
    // that died partway finish cleanly: a mod MUTATE never reached is still on, and is not reported.
    // A turn-off that refused is included too: usually it is still live and nothing happens, but one whose
    // rollback stranded its files in holding lists as off, and is turned back on and reported as recovered.
    private static (IReadOnlyList<ClearSkip> NotBack, IReadOnlyList<ClearSkip> Recovered) TurnBackOn(
        GameArchive ga, string gameArchiveDir, GameContext liveCtx, IReadOnlyList<ClearSkip> remainderIssues)
    {
        var none = ((IReadOnlyList<ClearSkip>)Array.Empty<ClearSkip>(), (IReadOnlyList<ClearSkip>)Array.Empty<ClearSkip>());
        if (ga.TurnedOffByClear is not { Count: > 0 } set
            || !string.Equals(ga.EndState, "vanilla", StringComparison.OrdinalIgnoreCase))
            return none;
        var refused = new HashSet<string>((ga.TurnOffSkipped ?? Array.Empty<ClearSkip>()).Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);
        var wanted = set.AsEnumerable().Reverse().ToList();

        var game = liveCtx.Game;
        var notBack = new List<ClearSkip>();

        // Read BEFORE any put-back: a mod the user already turned back on by hand (from the data folder) is
        // live, and putting its archived copy into holding would make a second, phantom held copy (I2).
        var rowsBefore = ModListing.Resolve(game);

        // Where the data folder lost a mod's held files, put the restore point's copy back first, so every
        // mod below is turned on the one way: from the data folder, through the toggle path. A damaged,
        // incomplete or conflicting copy refuses that mod alone (reported, nothing written); the rest go on.
        var copies = (ga.HeldCopies ?? Array.Empty<HeldCopy>())
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new HeldCopy(g.Key, g.SelectMany(h => h.Files).ToList()), StringComparer.OrdinalIgnoreCase);
        foreach (var cm in wanted.ToList())
        {
            var exact = rowsBefore.FirstOrDefault(m => string.Equals(m.Name, cm.Name, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(m.Location, cm.Location, StringComparison.OrdinalIgnoreCase));
            if (exact is { Enabled: true }) continue;   // already live: nothing to put back
            if (exact is null && rowsBefore.FirstOrDefault(m => string.Equals(m.Name, cm.Name, StringComparison.OrdinalIgnoreCase)) is { Enabled: true })
            {
                // Live under the same name somewhere else: a different copy. Neither put back nor turned on.
                notBack.Add(new ClearSkip(cm.Name, AlreadyOnDifferentCopy));
                wanted.Remove(cm);
                continue;
            }
            if (!copies.TryGetValue(cm.Name, out var h)) continue;
            if (PutBackHeldCopy(h, gameArchiveDir, liveCtx) is { } why)
            {
                notBack.Add(new ClearSkip(cm.Name, why));
                wanted.Remove(cm);
            }
        }
        if (wanted.Count == 0) return (notBack, Array.Empty<ClearSkip>());

        // Re-read after the put-backs: the restored holds now list as turned off.
        var rows = ModListing.Resolve(game);

        // Never enable on a ban-risk game without its acknowledgment. The ack lives in the data dir, which
        // step 1 has just put back, so a game acknowledged before the clear is acknowledged now. The held
        // copies are back in the data folder, so the mods list as turned off, ready for the user to switch on.
        if (BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(game), BanRiskAckStore.IsAcked(liveCtx.DataDir, game.Id ?? "")))
            return (notBack.Concat(wanted.Where(cm => FindRow(rows, cm) is not { Enabled: true })
                .Select(cm => new ClearSkip(cm.Name, BanRiskLeftOff))).ToList(), Array.Empty<ClearSkip>());

        // A mod some of whose files are among the remainder that didn't come back isn't turned on half
        // there (M3): it stays off and says why.
        var missingPaths = remainderIssues
            .Select(i => IsRooted(i.Name) ? null : FullNorm(Path.Combine(liveCtx.GameRoot, i.Name)))
            .Where(p => p is not null).Select(p => p!).ToList();
        if (missingPaths.Count > 0)
            foreach (var cm in wanted.ToList())
            {
                if (FindRow(rows, cm) is not { } r || BaseDirFor(liveCtx, r) is not { } bd) continue;
                // A held row may list no files; its name is then the folder or stem it is held under.
                var mine = (r.Files.Count > 0 ? r.Files : new List<string> { r.Name })
                    .Select(f => FullNorm(Path.Combine(bd, f))).Where(p => p is not null).Select(p => p!).ToList();
                if (missingPaths.Any(mp => mine.Any(p => string.Equals(p, mp, StringComparison.OrdinalIgnoreCase) || IsUnder(mp, p))))
                {
                    notBack.Add(new ClearSkip(cm.Name, "some of its files didn't come back from the restore point, so 626 left it off"));
                    wanted.Remove(cm);
                }
            }

        // One scope for every turn-on, so the scanner's lane lists the game once, not once per mod.
        var scope = new Scanner.BulkScope(liveCtx);
        var reasons = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var strandedTried = new List<string>();
        foreach (var cm in wanted)
        {
            var found = FindRow(rows, cm);
            if (found is { Enabled: true }) continue;   // never went off, or turned on by hand: already back
            if (refused.Contains(cm.Name)) strandedTried.Add(cm.Name);
            var row = found ?? new Mod { Name = cm.Name, Location = cm.Location };
            try
            {
                var o = ModToggle.SetEnabledInScopeAsync(liveCtx, row, true, scope).GetAwaiter().GetResult();
                if (o is { Enabled: false }) reasons[cm.Name] = o.Reason;
            }
            catch (Exception e) { reasons[cm.Name] = e.Message; }
        }
        var notOn = ModToggle.NotApplied(game, wanted.Select(cm => (cm.Name, true)));
        var notOnSet = new HashSet<string>(notOn, StringComparer.OrdinalIgnoreCase);
        var recovered = strandedTried.Where(n => !notOnSet.Contains(n))
            .Select(n => new ClearSkip(n, "it had stopped partway through turning off; it is back on")).ToList();
        return (notBack.Concat(notOn
            .Select(n => new ClearSkip(n, reasons.GetValueOrDefault(n) ?? "it didn't come back on"))).ToList(), recovered);
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
