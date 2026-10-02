namespace ModManager.Core;

/// <summary>
/// Which of a game's extra mod trees (<see cref="Manifest.GameManifestEntry.ExtraModTrees"/>, B4) a mod
/// also has files in — "see first, toggle later". A Cyberpunk mod named <c>CoolMod</c> lists from its
/// <c>CoolMod.archive</c> in <c>archive/pc/mod</c>; its <c>r6/scripts/CoolMod</c> and
/// <c>red4ext/plugins/CoolMod</c> are the same mod, and the row should say so, because toggling it
/// still moves only the primary folder's files and the user deserves to know what stays behind.
///
/// <para><b>Conservative on purpose.</b> An entry belongs to a mod only when its cleaned name EQUALS the
/// mod's (<see cref="NameMatch.CleanModName"/>, case-insensitive), at the top of a tree. A fuzzy match
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
    public static ModTrees Build(string? gameRoot, IEnumerable<string>? trees)
    {
        var index = new ModTrees();
        if (string.IsNullOrWhiteSpace(gameRoot) || trees is null) return index;

        foreach (var tree in trees.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(gameRoot, tree.Replace('\\', '/').Trim('/'));
            IEnumerable<string> entries;
            try { entries = Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).ToList() : Enumerable.Empty<string>(); }
            catch { continue; }   // unreadable: say nothing about it rather than guess

            foreach (var entry in entries)
            {
                var key = Key(Path.GetFileName(entry));
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

    // The name a file or folder is known by, minus extension and version noise, compared on letters and
    // digits only: CleanModName splits "CoolMod" into "Cool Mod" but leaves "coolmod" whole, and those
    // are the same mod. A name that cleans to nothing (a bare "1.0") matches nothing.
    private static string Key(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? ""
            : new string(NameMatch.CleanModName(Path.GetFileNameWithoutExtension(name))
                .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
