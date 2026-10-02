using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>What <see cref="RestorePointEngine.PlanVanillaRemainder"/> decided: the files to move into the
/// restore point (relative to the game root, with size and SHA-256), and what stays and why.</summary>
public sealed record RemainderPlan(IReadOnlyList<MovedFile> Files, IReadOnlyList<InPlaceNote> LeftInPlace);

/// <summary>
/// "Return to vanilla" means vanilla (2026-10-02 round 3). After the per-mod turn-offs, whatever is still in
/// the game's MOD-ONLY folders is something no mod row claims: loose redscript files, CET mods, red4ext
/// plugins, ArchiveXL sidecars. It goes into the restore point too, which is what the phase-1 spec's honesty
/// section already promised for unclaimed loose files.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>The archive sub-folder holding the vanilla remainder.</summary>
    public const string RemainderDirName = "vanilla-remainder";

    /// <summary>Tests only: called with each game-folder file the remainder sweep is about to move.
    /// Thread-static like the scanner's hooks: the sweep runs synchronously on the caller's thread.</summary>
    [ThreadStatic] internal static Action<string>? BeforeRemainderMoveForTests;

    /// <summary>
    /// Plan (do NOT execute) the vanilla remainder: every file still in the game's mod-only folders.
    ///
    /// <para><b>Mod-only folders</b> are the declared extra trees, and the mod locations that are not the game
    /// root, not the direct-inject / loose-root play folder, and not a base-content folder (a UE <c>Paks</c>
    /// root the loader-less pak lane uses). A folder another tool owns or claims is left whole and named.</para>
    ///
    /// <para><b>Left alone inside them:</b> the launcher's own bookkeeping (any <c>_626</c> folder); a
    /// registered framework's installed files (its uninstall and captured state own them); the files of a mod
    /// whose turn-off refused (that mod is reported as still active, not swept by a second mechanism); a pak
    /// <see cref="PakClassifier.IsBaseGamePak"/> would protect; and a file only reachable by its exact name
    /// (it can't be moved safely). Each is named in <see cref="RemainderPlan.LeftInPlace"/>.</para>
    ///
    /// <para>Read-only: hashes every planned file so the record can be sealed before anything moves.</para>
    /// </summary>
    public static RemainderPlan PlanVanillaRemainder(GameContext c, IReadOnlyList<ClearSkip> refusedTurnOffs)
    {
        var left = new List<InPlaceNote>();
        var gameRoot = FullNorm(c.GameRoot);
        if (gameRoot is null || !Directory.Exists(gameRoot)) return new RemainderPlan(Array.Empty<MovedFile>(), left);

        var roots = ModOnlyRoots(c, gameRoot, left);

        // What stays inside the roots.
        var frameworkFiles = FrameworkRegistry.List(c.DataDir)
            .SelectMany(fw => fw.InstalledFiles.Select(f => FullNorm(Path.Combine(fw.InstallPath, f))))
            .Where(p => p is not null).Select(p => p!).ToList();
        var refusedPaths = RefusedModPaths(c, refusedTurnOffs);

        var files = new List<MovedFile>();
        foreach (var root in roots)
        {
            IEnumerable<string> found;
            try { found = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList(); }
            catch (Exception e)
            {
                left.Add(new InPlaceNote(Rel(gameRoot, root), $"couldn't be read ({e.Message})"));
                continue;
            }
            foreach (var f in found)
            {
                var full = FullNorm(f);
                if (full is null) continue;
                var rel = Rel(gameRoot, full);
                if (rel.Split('\\', '/').Any(s => string.Equals(s, "_626", StringComparison.OrdinalIgnoreCase))) continue;
                if (frameworkFiles.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase))) continue;
                if (refusedPaths.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase) || IsUnder(full, p))) continue;
                if (HasUncopyableSegment(rel))
                {
                    left.Add(new InPlaceNote(rel, "its name can only be reached exactly, so 626 can't move it safely"));
                    continue;
                }
                long size;
                try { size = new FileInfo(full).Length; }
                catch (Exception e) { left.Add(new InPlaceNote(rel, $"couldn't be read ({e.Message})")); continue; }
                var ext = Path.GetExtension(full);
                if ((ext.Equals(".pak", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ucas", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".utoc", StringComparison.OrdinalIgnoreCase))
                    && PakClassifier.IsBaseGamePak(Path.GetFileName(full), size))
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

    // The game's mod-only folders, de-duplicated (a tree inside a location is the location's). What is
    // knowingly not swept (another tool's folder, base content) is added to <paramref name="left"/>.
    private static List<string> ModOnlyRoots(GameContext c, string gameRoot, List<InPlaceNote> left)
    {
        var playFolders = new[] { DirectInjectListing.PlayFolder(c.GameRoot), LooseMods.LooseRootListing.PlayFolder(c.GameRoot) }
            .Where(p => p is not null).Select(p => FullNorm(p!)).Where(p => p is not null).Select(p => p!).ToList();

        // The roots to sweep, de-duplicated (a tree inside a location is the location's).
        var roots = new List<string>();
        void AddRoot(string? dir, string label)
        {
            var full = dir is null ? null : FullNorm(dir);
            if (full is null || !Directory.Exists(full)) return;
            if (string.Equals(full, gameRoot, StringComparison.OrdinalIgnoreCase)
                || !full.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || playFolders.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
                return;   // the game root, a play folder, or outside the game: never mod-only
            var own = ToolOwnership.Resolve(full, c.TakenOver);
            if (own.State != OwnershipState.NotOwned)
            {
                left.Add(new InPlaceNote(Rel(gameRoot, full), $"managed by {own.Owner} — clean it up there"));
                return;
            }
            if (roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase) || IsUnder(full, r))) return;
            roots.RemoveAll(r => IsUnder(r, full));
            roots.Add(full);
        }

        foreach (var loc in c.Locations)
        {
            if (loc.Form == "paks-root")
            {
                var full = FullNorm(loc.Abs);
                if (full is not null && Directory.Exists(full))
                    left.Add(new InPlaceNote(Rel(gameRoot, full), "the base game's own content folder — 626 doesn't sweep it"));
                continue;
            }
            if (loc.Form == "loose-root") continue;   // the game root itself
            if (!string.IsNullOrEmpty(loc.Managed))
            {
                var full = FullNorm(loc.Abs);
                if (full is not null && Directory.Exists(full))
                    left.Add(new InPlaceNote(Rel(gameRoot, full), $"managed by {loc.Managed} — clean it up there"));
                continue;
            }
            AddRoot(loc.Abs, loc.Name);
        }
        foreach (var tree in c.ExtraModTrees ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(tree))
                AddRoot(Path.Combine(c.GameRoot, Path.Combine(tree.Replace('\\', '/').Trim('/').Split('/'))), tree);

        return roots;
    }

    /// <summary>Pre-flight only: the bytes in the game's mod-only folders right now, read-only and without
    /// hashing. Everything vanilla copies or moves out of them into the restore point (held copies of the
    /// scanner's mods, and the remainder) is at most this.</summary>
    public static long EstimateModOnlyBytes(GameContext c)
    {
        var gameRoot = FullNorm(c.GameRoot);
        if (gameRoot is null || !Directory.Exists(gameRoot)) return 0;
        long total = 0;
        foreach (var root in ModOnlyRoots(c, gameRoot, new List<InPlaceNote>()))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList(); }
            catch { continue; }   // unreadable: an estimate
            // Per file and tolerant: a name only reachable exactly can't be sized by its plain path, and an
            // estimate must never be what stops a clear.
            foreach (var f in files)
                try { total += new FileInfo(f).Length; } catch { }
        }
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
    /// checked against the record; on a mismatch it goes back. A file that can't be moved (locked, read-only)
    /// stays live and is returned, and the rest carry on.
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
                if (f.Sha256 is not null && !string.Equals(FileTally.Sha256(dest), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SafeMove.Move(dest, src);   // not what was recorded: back where it was
                    stayed.Add(new InPlaceNote(f.Rel, "it changed while 626 was moving it, so it was left in place"));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stayed.Add(new InPlaceNote(f.Rel, $"couldn't be moved ({e.Message})"));
            }
        }
        return stayed;
    }

    // Put the vanilla remainder back into the game folder, verified: every path refused if rooted or
    // escaping; a file already live with the recorded content is left (a sweep that died partway, or one that
    // never moved); a DIFFERENT live file is never overwritten and is reported; the archived copy is
    // SHA-checked before the write, written to a temp sibling, checked again, then moved into place.
    private static IReadOnlyList<ClearSkip> RestoreRemainder(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        if (ga.VanillaRemainder is not { Count: > 0 } files) return Array.Empty<ClearSkip>();
        var issues = new List<ClearSkip>();
        var gameRootFull = Path.GetFullPath(liveCtx.GameRoot);
        foreach (var f in files)
        {
            if (IsRooted(f.Rel) || !PathGate.IsContained(f.Rel, gameRootFull))
            {
                issues.Add(new ClearSkip(f.Rel, "its path is outside the game folder, so it was refused"));
                continue;
            }
            var dest = Path.Combine(liveCtx.GameRoot, f.Rel);
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

    private static string Rel(string gameRoot, string full) => Path.GetRelativePath(gameRoot, full);

    private static bool IsUnder(string path, string dir)
        => path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
