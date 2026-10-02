namespace ModManager.Core;

/// <summary>
/// Which of a game's extra mod trees (<see cref="Manifest.GameManifestEntry.ExtraModTrees"/>, B4) a mod
/// also has files in — "see first, toggle later". A Cyberpunk mod named <c>CoolMod</c> lists from its
/// <c>CoolMod.archive</c> in <c>archive/pc/mod</c>; its <c>r6/scripts/CoolMod</c> and
/// <c>red4ext/plugins/CoolMod</c> are the same mod, and the row should say so, because toggling it
/// still moves only the primary folder's files and the user deserves to know what stays behind.
///
/// <para><b>Conservative on purpose.</b> An entry belongs to a mod only when its name EQUALS the mod's,
/// compared on letters and digits case-insensitively, at the top of a tree. A fuzzy match
/// would tell the user a file is part of a mod it isn't, which is worse than saying nothing; the
/// unmatched case is the honest "626 can't tell". Reading only: nothing here moves anything.</para>
///
/// <para>One directory listing per tree per build, not per row: <see cref="Build"/> reads each tree once
/// and every row looks itself up.</para>
/// </summary>
public sealed class ModTrees
{
    private readonly Dictionary<string, List<string>> _treesByName = new(StringComparer.OrdinalIgnoreCase);
    // Stage two: every top-level entry, not only which trees have one, keyed by name key. Insertion
    // order is the manifest's tree order, then name order within a tree.
    private readonly Dictionary<string, List<ModTreeEntry>> _entriesByKey = new(StringComparer.OrdinalIgnoreCase);
    // Every declared tree's absolute folder plus the game's own mod folders: the places an entry must
    // not be, or hold, to be safe to move.
    private readonly List<string> _protectedDirs = new();

    public static readonly ModTrees Empty = new();

    private ModTrees() { }

    /// <param name="gameRoot">The game's install folder.</param>
    /// <param name="trees">Relative tree paths, in the manifest's order. Missing or unreadable trees are
    /// skipped: an absent <c>red4ext/plugins</c> just means no mod has files there.</param>
    /// <param name="ownLocations">The game's own mod folders, absolute, as this game actually resolves
    /// them (engine preset, the user's own choice, a second location). A tree that IS one of them, holds
    /// one, or sits inside one is not "somewhere else": its files either are the main folder's or would
    /// list the main folder's parents as mods, and toggling moves them with the main folder either way.
    /// The game root itself and anything outside it are skipped too.</param>
    public static ModTrees Build(string? gameRoot, IEnumerable<string>? trees, IEnumerable<string>? ownLocations = null)
    {
        var index = new ModTrees();
        if (string.IsNullOrWhiteSpace(gameRoot) || trees is null) return index;

        var root = FullDir(gameRoot);
        if (root.Length == 0) return index;
        var own = (ownLocations ?? Enumerable.Empty<string>())
            .Select(FullDir).Where(p => p.Length > 0).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        index._protectedDirs.AddRange(own);

        foreach (var raw in trees)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // One spelling per tree ("r6\scripts/" and "r6/scripts" are the same folder), shown as the
            // manifest's forward-slash form.
            var tree = raw.Replace('\\', '/').Trim('/');
            if (tree.Length == 0 || !seen.Add(tree)) continue;

            var dir = FullDir(Path.Combine(gameRoot, tree));
            if (dir.Length == 0 || !IsBelow(dir, root)) continue;
            index._protectedDirs.Add(dir);
            if (own.Any(o => SameDir(o, dir) || IsBelow(o, dir) || IsBelow(dir, o))) continue;

            IEnumerable<string> entries;
            try { entries = Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).ToList() : Enumerable.Empty<string>(); }
            catch { continue; }   // unreadable: say nothing about it rather than guess

            foreach (var entry in entries.OrderBy(e => Path.GetFileName(e), StringComparer.OrdinalIgnoreCase))
            {
                // A folder is known by its whole name ("Foo.Bar" stays "Foo.Bar"); a file by its stem
                // ("CoolMod.yaml" is CoolMod's).
                var name = Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
                var key = Key(name);
                if (key.Length == 0) continue;
                if (!index._treesByName.TryGetValue(key, out var list))
                    index._treesByName[key] = list = new List<string>();
                if (!list.Contains(tree, StringComparer.OrdinalIgnoreCase)) list.Add(tree);

                if (!index._entriesByKey.TryGetValue(key, out var moves))
                    index._entriesByKey[key] = moves = new List<ModTreeEntry>();
                moves.Add(new ModTreeEntry(tree, Path.GetFileName(entry), entry, dir));
            }
        }
        return index;
    }

    /// <summary>The extra trees holding an entry with this mod's name, in the manifest's order; empty
    /// when none do or when 626 can't tell.</summary>
    public IReadOnlyList<string> For(string? modName)
    {
        var key = Key(modName);
        return key.Length > 0 && _treesByName.TryGetValue(key, out var trees) ? trees : Array.Empty<string>();
    }

    /// <summary>
    /// Stage two, "toggle": the entries in the extra trees that may move with this mod, and the trees
    /// where an entry with its name exists but a safety rule kept it where it is. Decided here, in Core,
    /// per entry; the manifest only says where to look.
    ///
    /// <para>An entry moves when its name key equals the mod's (stage one's comparison), no other row
    /// shares that key, it is not and does not hold another declared tree or one of the game's own mod
    /// folders, and its tree is not tool-owned. A framework's folder is named after the framework, so
    /// it never matches a mod's key and moves only with the framework's own row.</para>
    ///
    /// <para>Two claimants (<c>Cool_Mod</c> and <c>CoolMod</c>) is the one case where neither may take
    /// the entry: every matching tree is held back and nothing moves. A folder and a file with the same
    /// key (<c>r6/scripts/CoolMod/</c>, <c>r6/tweaks/CoolMod.yaml</c>) are both the mod's. The row's own
    /// read-only rule is not decided here.</para>
    /// </summary>
    /// <param name="modName">The mod's row name.</param>
    /// <param name="otherRowNames">Every OTHER row's name in the game; the mod's own name is not one.</param>
    /// <param name="isOwned">Whether a tree's absolute folder is owned by another tool.</param>
    public ModTreeMoves MovableFor(string? modName, IEnumerable<string> otherRowNames, Func<string, bool> isOwned)
    {
        var key = Key(modName);
        if (key.Length == 0 || !_entriesByKey.TryGetValue(key, out var entries)) return ModTreeMoves.None;

        var contested = otherRowNames.Any(n => Key(n) == key);
        var movable = new List<ModTreeEntry>();
        var held = new List<HeldTree>();
        foreach (var e in entries)
        {
            // The reason is the row's to say, so each cause is named: two claimants first (it holds every
            // tree back), then a tree another tool owns (true of every entry in it), then the one entry
            // that is or holds a protected folder.
            HeldReason? reason = contested ? HeldReason.Contested
                : isOwned(e.TreeDir) ? HeldReason.OwnedTree
                : HoldsProtected(e.AbsPath) ? HeldReason.Protected
                : null;
            if (reason is { } r)
            {
                if (!held.Any(h => string.Equals(h.Tree, e.Tree, StringComparison.OrdinalIgnoreCase)))
                    held.Add(new HeldTree(e.Tree, r));
            }
            else movable.Add(e);
        }
        return new ModTreeMoves(movable, held);
    }

    // Equal to, or an ancestor of, a declared tree or an own mod folder.
    private bool HoldsProtected(string entryPath)
    {
        var full = FullDir(entryPath);
        return _protectedDirs.Any(d => SameDir(d, full) || IsBelow(d, full));
    }

    // A name compared on its letters and digits only, case-insensitively: "CoolMod", "coolmod" and
    // "Cool_Mod" are one name; "BetterHUD" and "BetterUI" stay two. Deliberately NOT NameMatch's cleaner,
    // which drops short all-caps and version tokens to help a SEARCH find candidates; here a collapsed
    // name would hand one mod another mod's files.
    private static string Key(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? ""
            : new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool SameDir(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Strictly inside: "C:/g/r6" is below "C:/g"; "C:/g" and "C:/game" are not below "C:/g".
    private static bool IsBelow(string path, string parent)
        => path.Length > parent.Length + 1
           && path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
           && (path[parent.Length] == Path.DirectorySeparatorChar || path[parent.Length] == Path.AltDirectorySeparatorChar);

    private static string FullDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return ""; }
    }
}

/// <summary>One top-level entry in an extra tree: its tree (manifest spelling), its name on disk, its
/// absolute path, and the tree's absolute folder (what ownership is asked about).</summary>
public sealed record ModTreeEntry(string Tree, string EntryName, string AbsPath, string TreeDir);

/// <summary>What <see cref="ModTrees.MovableFor"/> decided: <see cref="Movable"/> in manifest order, and
/// <see cref="Held"/>, the trees (manifest order, once each) with an entry of the mod's name that a
/// safety rule kept in place, each with the rule that kept it.</summary>
public sealed record ModTreeMoves(IReadOnlyList<ModTreeEntry> Movable, IReadOnlyList<HeldTree> Held)
{
    public static readonly ModTreeMoves None = new(Array.Empty<ModTreeEntry>(), Array.Empty<HeldTree>());

    /// <summary>The held trees' names alone, in order.</summary>
    public IReadOnlyList<string> HeldBack => Held.Select(h => h.Tree).ToList();
}

/// <summary>A tree where an entry with the mod's name stays put, and why.</summary>
public sealed record HeldTree(string Tree, HeldReason Reason);

/// <summary>Why an extra-tree entry stays where it is when its mod is turned off.</summary>
public enum HeldReason
{
    /// <summary>Another row's name reduces to the same key, so 626 can't tell whose it is.</summary>
    Contested,
    /// <summary>The entry is, or holds, a declared tree or one of the game's own mod folders.</summary>
    Protected,
    /// <summary>Another tool manages the tree.</summary>
    OwnedTree,
    /// <summary>The row itself moves no extra-tree files: read-only, loader-driven, or a game whose lane
    /// is not the scanner's.</summary>
    RowNotMoved,
}
