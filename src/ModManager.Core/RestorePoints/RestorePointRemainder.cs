using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>What <see cref="RestorePointEngine.PlanVanillaRemainder"/> decided: the files to move into the
/// restore point (relative to the game root, with size and SHA-256), and what stays and why.</summary>
public sealed record RemainderPlan(IReadOnlyList<MovedFile> Files, IReadOnlyList<InPlaceNote> LeftInPlace);

/// <summary>
/// "Return to vanilla" means vanilla (round 3), but only where the launcher KNOWS a folder holds nothing but
/// mods (round 4). After the per-mod turn-offs, what is still in such a folder is something no mod row claims
/// (loose redscript, CET mods, red4ext plugins, ArchiveXL sidecars) and it goes into the restore point.
/// Every other folder is left exactly as it is and named on the sheet, because there the launcher can't tell
/// the game's own files from mods: sweeping Skyrim's Data would move Skyrim.esm.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>The archive sub-folder holding the vanilla remainder.</summary>
    public const string RemainderDirName = "vanilla-remainder";

    /// <summary>The words for a folder the launcher can't sweep. {0} is the folder, {1} the file count.</summary>
    public const string CantTellNote = "626 can't tell the game's own files from mods in {0}; {1} files no mod claims are still in place";

    /// <summary>How a row left on in a folder 626 can't read starts: an item, not necessarily a mod.</summary>
    public const string CantTellRowPrefix = "still active: it sits in ";

    /// <summary>How a row left on because it holds a base-game pak reads.</summary>
    public const string BasePakRowNote = "still active: it looks like the base game's own pak, so 626 left it on";

    /// <summary>How a system folder location starts on the sheet.</summary>
    public const string SystemFolderPrefix = "not a mod folder: ";

    /// <summary>Tests only: called with each game-folder file the remainder sweep is about to move.
    /// Thread-static like the scanner's hooks: the sweep runs synchronously on the caller's thread.</summary>
    [ThreadStatic] internal static Action<string>? BeforeRemainderMoveForTests;

    /// <summary>Tests only: called with each archived file right after its move, before it is checked.</summary>
    [ThreadStatic] internal static Action<string>? AfterRemainderMoveForTests;

    /// <summary>
    /// Plan (do NOT execute) the vanilla remainder: every file still in the game's mod-only folders.
    ///
    /// <para><b>Mod-only folders</b> (an allowlist): the declared extra trees, and the locations
    /// <see cref="ModOnlyFolders"/> vouches for (an engine's mod-only shape, the launcher's UE4SS folder, or a
    /// modPath the game's definition marks <c>modPathModOnly</c>). Never another tool's folder, never one that
    /// is itself a link. Every other location is left whole and named, and so is the play folder of a
    /// direct-inject or loose-root game.</para>
    ///
    /// <para><b>Left alone inside them:</b> the launcher's own bookkeeping (any <c>_626</c> folder); the
    /// installed files of the frameworks the clear captured (their uninstall and captured state own them);
    /// the files of a mod whose turn-off refused (it is reported as still active, not swept by a second
    /// mechanism); a Mod Engine 2 mod's folder (its config flip owns it); and, named, a link, a pak
    /// <see cref="PakClassifier.IsBaseGamePak"/> would protect, or a file only reachable by its exact name.</para>
    ///
    /// <para>Read-only: hashes every planned file so the record can be sealed before anything moves.</para>
    /// </summary>
    /// <param name="frameworks">The frameworks the clear captured (the sealed <c>ga.Frameworks</c>). The live
    /// registry is empty by now (the uninstall ran), so it is only the fallback when this is null.</param>
    public static RemainderPlan PlanVanillaRemainder(GameContext c, IReadOnlyList<ClearSkip> refusedTurnOffs,
        IReadOnlyList<FrameworkArchive>? frameworks = null)
    {
        var left = new List<InPlaceNote>();
        var gameRoot = FullNorm(c.GameRoot);
        if (gameRoot is null || !Directory.Exists(gameRoot)) return new RemainderPlan(Array.Empty<MovedFile>(), left);

        var rows = ModListing.Resolve(c.Game);
        var roots = ModOnlyRoots(c, gameRoot, left, rows);

        var frameworkFiles = (frameworks is not null
                ? frameworks.SelectMany(fw => fw.InstalledFiles.Select(f => Path.Combine(fw.InstallPath, f)))
                : FrameworkRegistry.List(c.DataDir).SelectMany(fw => fw.InstalledFiles.Select(f => Path.Combine(fw.InstallPath, f))))
            .Select(FullNorm).Where(p => p is not null).Select(p => p!).ToList();
        // A row vanilla left ON because it holds a base-game pak (any form): its files, sidecars included,
        // stay with it, and it is named as still active.
        var basePakRows = rows.Where(m => m.Enabled && HasBaseGamePak(c, m)).ToList();
        foreach (var m in basePakRows)
            left.Add(new InPlaceNote(m.Name, BasePakRowNote));
        var basePakPaths = basePakRows.SelectMany(m => BaseDirFor(c, m) is { } bd
                ? m.Files.Select(f => FullNorm(Path.Combine(bd, f))) : Enumerable.Empty<string?>())
            .Where(p => p is not null).Select(p => p!).ToList();
        // A base pak's same-stem companions (its .sig, .ucas, .utoc) are the game's too, listed or not.
        foreach (var p in basePakPaths.ToList())
        {
            var dir = Path.GetDirectoryName(p);
            var stem = Path.GetFileNameWithoutExtension(p);
            if (dir is null || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var sib in Directory.EnumerateFiles(dir, stem + ".*"))
                    if (FullNorm(sib) is { } fs && string.Equals(Path.GetFileNameWithoutExtension(fs), stem, StringComparison.OrdinalIgnoreCase))
                        basePakPaths.Add(fs);
            }
            catch { /* unreadable: the files themselves are still excluded */ }
        }
        var ownedElsewhere = RefusedModPaths(c, refusedTurnOffs).Concat(ModEngine2Paths(c, rows)).Concat(basePakPaths).ToList();

        var files = new List<MovedFile>();
        foreach (var root in roots)
        {
            foreach (var full in FilesNoLinks(root, gameRoot, left))
            {
                var rel = Rel(gameRoot, full);
                if (rel.Split('\\', '/').Any(s => string.Equals(s, "_626", StringComparison.OrdinalIgnoreCase))) continue;
                if (frameworkFiles.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase))) continue;
                if (ownedElsewhere.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase) || IsUnder(full, p))) continue;
                if (HasUncopyableSegment(rel))
                {
                    left.Add(new InPlaceNote(rel, "its name can only be reached exactly, so 626 can't move it safely"));
                    continue;
                }
                long size;
                try { size = new FileInfo(full).Length; }
                catch (Exception e) { left.Add(new InPlaceNote(rel, $"couldn't be read ({e.Message})")); continue; }
                if (IsBaseGameArchiveName(Path.GetFileName(full), size))
                {
                    left.Add(new InPlaceNote(rel, "looks like the base game's own pak — 626 doesn't move it"));
                    continue;
                }
                try { files.Add(new MovedFile(rel, size, FileTally.Sha256(full))); }
                catch (Exception e) { left.Add(new InPlaceNote(rel, $"couldn't be read ({e.Message})")); }
            }
        }
        return new RemainderPlan(files, left);
    }

    /// <summary>
    /// The game's sweepable roots: the extra trees, and the locations <see cref="ModOnlyFolders"/> vouches
    /// for, each inside the game root, present, not another tool's and not a link. De-duplicated (a tree
    /// inside a location is the location's). When <paramref name="left"/> is given, every location NOT swept
    /// is named in it, with its rows still active and its unclaimed file count, so the sheet never claims a
    /// vanilla game over a folder nobody looked in.
    /// </summary>
    private static List<string> ModOnlyRoots(GameContext c, string gameRoot, List<InPlaceNote>? left, IReadOnlyList<Mod>? rows)
    {
        var replaced = left is null ? ReplacedFiles.None : ReplacedGameFiles(c);   // once per plan
        var roots = new List<string>();
        bool TryAdd(string full)
        {
            var own = ToolOwnership.Resolve(full, c.TakenOver);
            if (own.State != OwnershipState.NotOwned)
            {
                left?.Add(new InPlaceNote(Rel(gameRoot, full), $"managed by {own.Owner} — clean it up there"));
                return false;
            }
            if (IsLink(full))
            {
                left?.Add(new InPlaceNote(Rel(gameRoot, full), "it is a link to somewhere else, so 626 doesn't sweep it"));
                return false;
            }
            if (roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase) || IsUnder(full, r))) return true;
            roots.RemoveAll(r => IsUnder(r, full));
            roots.Add(full);
            return true;
        }

        foreach (var loc in c.Locations)
        {
            if (RealPath.IsDriveRelative(loc.DeclaredPath ?? loc.StoredPath))
            {
                left?.Add(new InPlaceNote(loc.DeclaredPath ?? loc.StoredPath ?? loc.Name,
                    "this location names a drive but no folder (it would mean whatever folder is current), so 626 ignored it"));
                continue;
            }
            var full = FullNorm(loc.Abs);
            if (full is null || !Directory.Exists(full)) continue;   // nothing there to sweep or to name
            if (left is not null && IsLink(full))
            {
                left.Add(new InPlaceNote(full, "it is a link to somewhere else, so 626 doesn't sweep it"));
                foreach (var m in (rows ?? Array.Empty<Mod>()).Where(r => r.Enabled && r.Location == loc.Name && !HasBaseGamePak(c, r)))
                    left.Add(new InPlaceNote(m.Name, $"{CantTellRowPrefix}{full}, a link to somewhere else"));
                continue;
            }
            var inside = ModOnlyFolders.RelativeToRoot(gameRoot, full) is not null;
            if (inside && string.IsNullOrEmpty(loc.Managed) && ModOnlyFolders.WhyModOnly(c, loc) is not null)
            {
                TryAdd(full);
                continue;
            }
            if (left is null) continue;
            if (!string.IsNullOrEmpty(loc.Managed))
            {
                left.Add(new InPlaceNote(Rel(gameRoot, full), $"managed by {loc.Managed} — clean it up there"));
                continue;
            }
            if (inside && ToolOwnership.Resolve(full, c.TakenOver) is { State: not OwnershipState.NotOwned } owned)
            {
                left.Add(new InPlaceNote(Rel(gameRoot, full), $"managed by {owned.Owner} — clean it up there"));
                continue;
            }
            // A system folder (an ancestor of the game, a drive, the profile, Windows, Program Files): named
            // once, never walked, counted or listed row by row (review r5, I-C).
            if (SystemFolderReason(c, full) is { } sysWhy)
            {
                left.Add(new InPlaceNote(full, $"{SystemFolderPrefix}{sysWhy}, so 626 left it alone"));
                continue;
            }
            // Not a folder the launcher knows holds only mods: the game root, a base-content folder (Data,
            // data, Content/Paks), a user's own path, a folder outside the game. Named, never swept.
            var where = !inside ? (string.Equals(full, gameRoot, StringComparison.OrdinalIgnoreCase) ? "the game folder itself" : full)
                                : Rel(gameRoot, full);
            var active = (rows ?? Array.Empty<Mod>())
                .Where(m => m.Enabled && string.Equals(m.Location, loc.Name, StringComparison.Ordinal))
                .Where(m => !HasBaseGamePak(c, m))   // named once, by the base-pak note
                .ToList();
            foreach (var m in active)
                left.Add(new InPlaceNote(m.Name, ReplacedAGameFile(c, m, replaced)
                    ? ReplacedGameFileNote
                    : $"{CantTellRowPrefix}{where}, where 626 can't tell the game's own files from mods"));
            left.Add(new InPlaceNote(where, string.Format(CantTellNote, where, inside ? UnclaimedCount(c, full, rows) : "any")));
        }
        foreach (var tree in c.ExtraModTrees ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(tree)) continue;
            var full = FullNorm(Path.Combine(c.GameRoot, Path.Combine(tree.Replace('\\', '/').Trim('/').Split('/'))));
            if (full is null || !Directory.Exists(full) || ModOnlyFolders.RelativeToRoot(gameRoot, full) is null) continue;
            TryAdd(full);
        }

        // A direct-inject or loose-root game's mods sit in its play folder beside the game's own files. The
        // lane moved what it recognises; anything else there stays, and the sheet says so.
        if (left is not null && ModListing.MechanismFor(c.Game, c) is ListingMechanism.DirectInject or ListingMechanism.LooseRoot)
        {
            var play = DirectInjectListing.PlayFolder(c.GameRoot) ?? c.GameRoot;
            var where = string.Equals(FullNorm(play), gameRoot, StringComparison.OrdinalIgnoreCase) ? "the game folder itself" : Rel(gameRoot, FullNorm(play)!);
            if (!left.Any(n => n.Path == where))
                left.Add(new InPlaceNote(where, "626 moved the mods it recognises there; it can't tell any other file there from the game's own, so it left them"));
        }
        return roots;
    }

    // How many files under a not-swept folder no listed row owns. Counted (never hashed), links not followed.
    private static string UnclaimedCount(GameContext c, string folder, IReadOnlyList<Mod>? rows)
    {
        var claimed = new List<string>();
        foreach (var m in rows ?? Array.Empty<Mod>())
            if (BaseDirFor(c, m) is { } baseDir)
                foreach (var f in m.Files)
                    if (FullNorm(Path.Combine(baseDir, f)) is { } p) claimed.Add(p);
        var n = 0;
        foreach (var f in FilesNoLinks(folder, folder, null))
            if (!claimed.Any(p => string.Equals(p, f, StringComparison.OrdinalIgnoreCase) || IsUnder(f, p))) n++;
        return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // Every file under root, never following a link (junction or symlink): a link is named and skipped, so
    // the sweep can't reach through one into a folder that was never the game's.
    private static IEnumerable<string> FilesNoLinks(string root, string gameRoot, List<InPlaceNote>? left)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            List<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception e)
            {
                left?.Add(new InPlaceNote(Rel(gameRoot, dir.FullName), $"couldn't be read ({e.Message})"));
                continue;
            }
            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    left?.Add(new InPlaceNote(Rel(gameRoot, entry.FullName), "it is a link to somewhere else, so 626 doesn't follow or move it"));
                    continue;
                }
                if (entry is DirectoryInfo d) pending.Push(d);
                else if (FullNorm(entry.FullName) is { } f) yield return f;
            }
        }
    }

    private static bool IsLink(string dir)
    {
        try { return new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }   // unreadable: not known to be safe to walk
    }

    // The folders Mod Engine 2 rows point at: the config flip owns them, so the sweep leaves them (M3).
    private static IEnumerable<string> ModEngine2Paths(GameContext c, IReadOnlyList<Mod> rows)
    {
        var configDir = string.IsNullOrEmpty(c.Game.ModEngineConfig) ? null : Path.GetDirectoryName(c.Game.ModEngineConfig);
        if (configDir is null) yield break;
        foreach (var m in rows.Where(r => r.Location == "mod engine 2"))
            foreach (var f in m.Files)
                if (FullNorm(Path.Combine(configDir, f)) is { } p) yield return p;
    }

    /// <summary>Pre-flight only: the bytes in the game's mod-only folders right now, read-only, unhashed, links
    /// not followed. Everything the clear moves or copies out of them into the restore point is at most this.</summary>
    public static long EstimateModOnlyBytes(GameContext c)
    {
        var gameRoot = FullNorm(c.GameRoot);
        if (gameRoot is null || !Directory.Exists(gameRoot)) return 0;
        long total = 0;
        foreach (var root in ModOnlyRoots(c, gameRoot, null, null))
            foreach (var f in FilesNoLinks(root, gameRoot, null))
                // Per file and tolerant: a name only reachable exactly can't be sized by its plain path, and an
                // estimate must never be what stops a clear.
                try { total += new FileInfo(f).Length; } catch { }
        return total;
    }

    // The paths a refused turn-off's row owns (its files and the extra-tree entries its turn-off would have
    // moved), so the sweep doesn't move by a second mechanism what the toggle just refused to.
    private static IReadOnlyList<string> RefusedModPaths(GameContext c, IReadOnlyList<ClearSkip> refused)
    {
        if (refused.Count == 0) return Array.Empty<string>();
        var rows = ModListing.Resolve(c.Game);
        var extraRows = Scanner.ExtraTreeRowsFor(c);
        var paths = new List<string>();
        foreach (var s in refused)
        {
            var row = rows.FirstOrDefault(m => string.Equals(m.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null) continue;
            if (BaseDirFor(c, row) is { } baseDir)
                foreach (var f in row.Files)
                    if (FullNorm(Path.Combine(baseDir, f)) is { } p) paths.Add(p);
            try
            {
                foreach (var x in extraRows.MovesFor(row).Movable)
                    if (FullNorm(x.AbsPath) is { } p) paths.Add(p);
            }
            catch { /* an unreadable tree: nothing known to keep */ }
        }
        return paths;
    }

    /// <summary>
    /// Move the planned remainder into <c>&lt;gameArchiveDir&gt;/vanilla-remainder/</c>. Call only AFTER the
    /// plan is recorded in the manifest. Each file goes by <see cref="SafeMove.Move"/>: a rename on one volume,
    /// copy-verify-delete across volumes, so at every instant it is live or archived. The archived file is
    /// checked against the record; on a mismatch it goes back. Returns the files that are still LIVE (and
    /// only those): a file that reached the archive keeps its record even if a later step failed (I2).
    /// </summary>
    public static IReadOnlyList<InPlaceNote> SweepRemainder(GameContext c, IReadOnlyList<MovedFile> plan, string gameArchiveDir)
    {
        var stayed = new List<InPlaceNote>();
        var archiveRoot = Path.Combine(gameArchiveDir, RemainderDirName);
        foreach (var f in plan)
        {
            var src = Path.Combine(c.GameRoot, f.Rel);
            var dest = Path.Combine(archiveRoot, f.Rel);
            BeforeRemainderMoveForTests?.Invoke(src);
            try
            {
                if (!File.Exists(src)) { stayed.Add(new InPlaceNote(f.Rel, "it was gone before 626 could move it")); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                SafeMove.Move(src, dest);
                AfterRemainderMoveForTests?.Invoke(dest);
                if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(dest), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SafeMove.Move(dest, src);   // not what was recorded: back where it was
                    stayed.Add(new InPlaceNote(f.Rel, "it changed while 626 was moving it, so it was left in place"));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Record where the file REALLY is. Archived (moved, then a check or a move-back failed): it
                // keeps its record, so Restore brings it back. Only a file that is still live "stayed".
                if (!File.Exists(src) && File.Exists(dest)) continue;
                stayed.Add(new InPlaceNote(f.Rel, $"couldn't be moved ({e.Message})"));
            }
        }
        return stayed;
    }

    /// <summary>Why Restore kept the remainder in the restore point on a ban-risk game.</summary>
    public const string RemainderKeptForBanRisk =
        "kept in your restore point because of ban risk; restore it after turning mods on with the acknowledgment";

    // Put the vanilla remainder back into the game folder, verified: every path refused if rooted, escaping,
    // or not under a folder the launcher knows holds only mods (M4); a file already live with the recorded
    // content is left (a sweep that died partway); a DIFFERENT live file is never overwritten and is reported;
    // the archived copy is SHA-checked before the write, written to a temp sibling, checked again, then moved.
    private static IReadOnlyList<ClearSkip> RestoreRemainder(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        if (ga.VanillaRemainder is not { Count: > 0 } files) return Array.Empty<ClearSkip>();
        var issues = new List<ClearSkip>();
        var gameRootFull = Path.GetFullPath(liveCtx.GameRoot);
        var gameRootNorm = FullNorm(liveCtx.GameRoot) ?? gameRootFull;
        var roots = ModOnlyRoots(liveCtx, gameRootNorm, null, null);
        foreach (var f in files)
        {
            if (IsRooted(f.Rel) || !PathGate.IsContained(f.Rel, gameRootFull))
            {
                issues.Add(new ClearSkip(f.Rel, "its path is outside the game folder, so it was refused"));
                continue;
            }
            var dest = Path.Combine(liveCtx.GameRoot, f.Rel);
            var destFull = FullNorm(dest);
            if (destFull is null || !roots.Any(r => IsUnder(destFull, r)))
            {
                issues.Add(new ClearSkip(f.Rel, "it isn't in a folder 626 knows holds only mods now, so it was left in your restore point at "
                    + Path.Combine(gameArchiveDir, RemainderDirName, f.Rel)));
                continue;
            }
            // Re-checked at restore time: a junction planted after the clear (mods\lnk -> bin\) would carry the
            // write out of the mod folder. Any existing folder on the way down that is a link refuses (r4, m3).
            if (LinkOnTheWay(gameRootNorm, destFull) is { } link)
            {
                issues.Add(new ClearSkip(f.Rel, $"\"{Rel(gameRootNorm, link)}\" is now a link to somewhere else, so 626 won't write through it"));
                continue;
            }
            var src = Path.Combine(gameArchiveDir, RemainderDirName, f.Rel);
            try
            {
                if (File.Exists(dest))
                {
                    if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(dest), f.Sha256, StringComparison.OrdinalIgnoreCase))
                        issues.Add(new ClearSkip(f.Rel, "a different file is in its place now, so 626 left that file as it is"));
                    continue;   // same content: already back
                }
                if (!File.Exists(src)) { issues.Add(new ClearSkip(f.Rel, "it is missing from the restore point")); continue; }
                if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(src), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ClearSkip(f.Rel, "its copy in the restore point is damaged (checksum mismatch)"));
                    continue;
                }
                var tmp = dest + ".rp-tmp";
                var moved = false;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    SafeMove.CopyFileVerified(src, tmp);
                    if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(tmp), f.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("checksum mismatch after the copy");
                    File.Move(tmp, dest);   // dest was missing: never over a live file
                    moved = true;
                }
                finally { if (!moved) try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            }
            catch (Exception e) { issues.Add(new ClearSkip(f.Rel, $"couldn't be put back ({e.Message})")); }
        }
        return issues;
    }

    // The first existing folder between the game root (exclusive) and dest's parent that is a reparse point.
    private static string? LinkOnTheWay(string gameRoot, string destFull)
    {
        var parent = Path.GetDirectoryName(destFull);
        var chain = new List<string>();
        for (var d = parent; d is not null && IsUnder(d, gameRoot); d = Path.GetDirectoryName(d)) chain.Add(d);
        chain.Reverse();
        foreach (var d in chain)
        {
            if (!Directory.Exists(d)) break;   // nothing below a missing folder exists yet
            if (IsLink(d)) return d;
        }
        return null;
    }

    private static string Rel(string gameRoot, string full) => Path.GetRelativePath(gameRoot, full);

    private static bool IsUnder(string path, string dir)
        => path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
