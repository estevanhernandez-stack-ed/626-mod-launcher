namespace ModManager.Core.LooseMods;

/// <summary>
/// Intake for loose-root (decima) games: dropped loose mods install into the GAME ROOT through the
/// proven <see cref="DirectInject.Plan"/> / <see cref="DirectInject.Execute"/> machinery
/// (validate-then-extract, path-traversal-safe, no-clobber) behind a recognition gate. A drop is
/// recognized exactly when its post-install TOP-LEVEL names would produce at least one
/// <see cref="LooseRootListing"/> row — the same two detectors the listing runs (the DirectInject
/// signature catalog, then <see cref="LooseModScan"/> by-nature). Anything unrecognized (a readme,
/// a generic DLL that isn't a proxy name) is refused for the root with a clear reason — routed to
/// the existing skip flow, never silently placed among the game's own files.
/// </summary>
public static class LooseRootIntake
{
    /// <summary>The skip reason for a drop the recognition gate refuses.</summary>
    public const string RefusalReason = "not a recognized loose mod";

    private static readonly IArchiveReader Archive = new SharpCompressArchiveReader();

    private static bool IsArchive(string src)
        => Intake.ArchiveExtensions.Any(a => src.EndsWith(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classify a drop against the game root into add / collision / refused — no writes. Recognition
    /// runs over the UNION of all sources' candidate top-level names so a mod split across loose
    /// files (Mod.asi + Mod.ini dropped together) groups the way <see cref="LooseModScan"/> groups
    /// it; each source then plans through <see cref="DirectInject.Plan"/> only when it contributes
    /// at least one recognized entry.
    /// </summary>
    public static IntakePlan Plan(string gameRoot, IEnumerable<string> sources)
    {
        var add = new List<IntakeItem>();
        var collisions = new List<IntakeCollision>();
        var unsafeItems = new List<SkippedItem>();

        var perSource = new List<(string Src, List<string> Files, List<string> Dirs)>();
        foreach (var src in sources ?? Enumerable.Empty<string>())
        {
            try
            {
                var (files, dirs) = Candidates(src);
                perSource.Add((src, files, dirs));
            }
            catch (Exception e) { unsafeItems.Add(new SkippedItem(Path.GetFileName(src), e.Message)); }
        }

        // One recognition pass over the whole drop — catalog first, then by-nature fed the
        // catalog's owned entries, mirroring LooseRootListing.Enabled. The union of detected
        // mods' entries is the set of names allowed into the root.
        var allFiles = perSource.SelectMany(x => x.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var allDirs = perSource.SelectMany(x => x.Dirs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var catalog = DirectInject.Detect(allFiles, allDirs);
        var owned = new HashSet<string>(catalog.SelectMany(m => m.Entries), StringComparer.OrdinalIgnoreCase);
        var nature = LooseModScan.Detect(allFiles, allDirs, owned);
        var recognized = new HashSet<string>(
            catalog.Concat(nature).SelectMany(m => m.Entries), StringComparer.OrdinalIgnoreCase);

        foreach (var (src, files, dirs) in perSource)
        {
            if (!files.Any(recognized.Contains) && !dirs.Any(recognized.Contains))
            {
                unsafeItems.Add(new SkippedItem(Path.GetFileName(src), RefusalReason));
                continue;
            }
            // Recognized: the proven root-placement planner handles wrapper flattening,
            // traversal/absolute refusal, and collision detection.
            var p = DirectInject.Plan(gameRoot, new[] { src });
            add.AddRange(p.ToAdd);
            collisions.AddRange(p.Collisions);
            unsafeItems.AddRange(p.Unsafe);
        }
        return new IntakePlan(add, collisions, unsafeItems);
    }

    /// <summary>
    /// The TOP-LEVEL names a source would put at the game root (pure name analysis, no writes):
    /// a loose file is its basename; an archive is its safe entries after single-wrapper
    /// flattening (unsafe entries contribute nothing — they can never recognize a drop); a
    /// dropped folder is its immediate contents (its own name is a wrapper, like a zip's).
    /// </summary>
    private static (List<string> Files, List<string> Dirs) Candidates(string src)
    {
        var files = new List<string>();
        var dirs = new List<string>();
        if (Directory.Exists(src))
        {
            files.AddRange(Directory.GetFiles(src).Select(f => Path.GetFileName(f)!));
            dirs.AddRange(Directory.GetDirectories(src).Select(d => Path.GetFileName(d)!));
        }
        else if (IsArchive(src))
        {
            using var zip = Archive.Open(src);
            var names = zip.EntryNames; // file entries only (dirs excluded by the seam)
            var prefix = DirectInject.WrapperPrefix(names);
            foreach (var n in names)
            {
                var rel = DirectInject.SafeRelative(n, prefix);
                if (rel is null) continue;
                var norm = rel.Replace('\\', '/');
                var slash = norm.IndexOf('/');
                if (slash < 0) files.Add(norm);
                else if (!dirs.Contains(norm[..slash], StringComparer.OrdinalIgnoreCase)) dirs.Add(norm[..slash]);
            }
        }
        else files.Add(Path.GetFileName(src));
        return (files, dirs);
    }
}
