namespace ModManager.Core;

/// <summary>
/// Pure drop-file classification. 'zip' -> extract &amp; route mod entries (any supported archive
/// format — see <see cref="ArchiveExtensions"/>); 'mod' -> place directly; 'skip' -> ignore.
/// Mirrors intake-core.js. (The extraction + folder walk with the path-traversal guard live in
/// Scanner / DirectInject, where the IO is, behind the ArchiveReader seam.)
/// </summary>
public static class Intake
{
    /// <summary>Archive containers routed through the ArchiveReader seam (SharpCompress reads all).
    /// The classifier still labels them "zip" so existing intake branches stay unchanged.</summary>
    public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };

    /// <summary>Whether a path names an archive by its extension (any case). One check for every caller.</summary>
    public static bool IsArchive(string path)
        => ArchiveExtensions.Any(a => path.EndsWith(a, StringComparison.OrdinalIgnoreCase));

    public static string ClassifyDrop(string filePath, IEnumerable<string>? exts)
    {
        var lower = filePath.ToLowerInvariant();
        if (ArchiveExtensions.Any(a => lower.EndsWith(a))) return "zip";
        var dot = lower.LastIndexOf('.');
        var ext = dot >= 0 ? lower[(dot + 1)..] : lower;
        var set = (exts ?? Enumerable.Empty<string>()).Select(e => e.ToLowerInvariant());
        return set.Contains(ext) ? "mod" : "skip";
    }

    /// <summary>
    /// The files of one drop that intake carries: every file wearing a declared extension, PLUS the companions the
    /// scanner understands, judged by the scanner's own rules so a mod lands whole and lists as one row. Per folder,
    /// because companions pair by name inside one folder: a declared file's sidecars (<see cref="Scanner.SidecarsFor"/>:
    /// <c>Foo.archive.xl</c> beside <c>Foo.archive</c>, and the <c>.ba2</c> archives a Fallout 4 / Starfield plugin
    /// owns) and the files that stand alone as mods (<see cref="StandaloneSidecars"/>: an orphan <c>.ba2</c>, a lone
    /// <c>.xl</c>).
    ///
    /// <para>Before, intake kept declared extensions only, and no preset declares <c>ba2</c> or <c>xl</c>: a Fallout 4
    /// zip installed its plugin without the archives it loads, and an <c>.xl</c>-only Cyberpunk zip installed nothing
    /// (found building #396 / #398). Readmes and images still stay out. A game whose extensions carry no companions
    /// (a pak game) gets exactly what it got before.</para>
    ///
    /// <para><paramref name="paths"/> are archive entry names or file paths, in either slash; the result holds the
    /// carried ones exactly as given. Read-only: validation and placement stay with the caller.</para>
    /// </summary>
    public static IReadOnlySet<string> CarriedFiles(IEnumerable<string> paths, GameContext c)
    {
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in (paths ?? Enumerable.Empty<string>()).Where(p => !IsArchive(p))
                     .GroupBy(p => FolderOf(p), StringComparer.OrdinalIgnoreCase))
        {
            var byName = folder.GroupBy(p => NameOf(p), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var names = byName.Keys.ToList();
            var declared = names.Where(n => ClassifyDrop(n, c.Exts) == "mod").ToList();
            var keep = new HashSet<string>(declared, StringComparer.OrdinalIgnoreCase);
            foreach (var m in declared) keep.UnionWith(Scanner.SidecarsFor(m, names));
            keep.UnionWith(StandaloneSidecars.Find(names, names.Where(n => c.FileRe.IsMatch(n)).ToList(), c));
            foreach (var n in keep)
                if (byName.TryGetValue(n, out var originals)) carried.UnionWith(originals);
        }
        return carried;
    }

    private static string NameOf(string p)
    {
        var s = p.Replace('\\', '/').TrimEnd('/');
        var i = s.LastIndexOf('/');
        return i >= 0 ? s[(i + 1)..] : s;
    }

    private static string FolderOf(string p)
    {
        var s = p.Replace('\\', '/').TrimEnd('/');
        var i = s.LastIndexOf('/');
        return i >= 0 ? s[..i] : "";
    }
}
