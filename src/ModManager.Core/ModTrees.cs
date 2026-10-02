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

    public static readonly ModTrees Empty = new();

    private ModTrees() { }

    /// <param name="gameRoot">The game's install folder.</param>
    /// <param name="trees">Relative tree paths, in the manifest's order. Missing or unreadable trees are
    /// skipped: an absent <c>red4ext/plugins</c> just means no mod has files there.</param>
    /// <param name="ownLocations">The game's own mod folders, absolute. A tree that IS one of them is not
    /// "somewhere else": listing it would tell every row it also has files in its own main folder.</param>
    public static ModTrees Build(string? gameRoot, IEnumerable<string>? trees, IEnumerable<string>? ownLocations = null)
    {
        var index = new ModTrees();
        if (string.IsNullOrWhiteSpace(gameRoot) || trees is null) return index;

        var own = (ownLocations ?? Enumerable.Empty<string>())
            .Select(FullDir).Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in trees)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // One spelling per tree ("r6\scripts/" and "r6/scripts" are the same folder), shown as the
            // manifest's forward-slash form.
            var tree = raw.Replace('\\', '/').Trim('/');
            if (tree.Length == 0 || !seen.Add(tree)) continue;

            var dir = FullDir(Path.Combine(gameRoot, tree));
            if (dir.Length == 0 || own.Contains(dir)) continue;

            IEnumerable<string> entries;
            try { entries = Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).ToList() : Enumerable.Empty<string>(); }
            catch { continue; }   // unreadable: say nothing about it rather than guess

            foreach (var entry in entries)
            {
                // A folder is known by its whole name ("Foo.Bar" stays "Foo.Bar"); a file by its stem
                // ("CoolMod.yaml" is CoolMod's).
                var name = Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
                var key = Key(name);
                if (key.Length == 0) continue;
                if (!index._treesByName.TryGetValue(key, out var list))
                    index._treesByName[key] = list = new List<string>();
                if (!list.Contains(tree, StringComparer.OrdinalIgnoreCase)) list.Add(tree);
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

    // A name compared on its letters and digits only, case-insensitively: "CoolMod", "coolmod" and
    // "Cool_Mod" are one name; "BetterHUD" and "BetterUI" stay two. Deliberately NOT NameMatch's cleaner,
    // which drops short all-caps and version tokens to help a SEARCH find candidates; here a collapsed
    // name would hand one mod another mod's files.
    private static string Key(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? ""
            : new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string FullDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return ""; }
    }
}
