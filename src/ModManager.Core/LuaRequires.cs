using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Which libraries a Lua mod pulls in, read from its source.
///
/// <para>The first draft of the mod-provenance design said we cannot know who depends on a library,
/// so a library row gets no toggle. The refusal was right and the reason was wrong: for a script tree
/// the dependency is <b>declared</b>, so it is readable rather than guessable. On a real install,
/// grepping <c>require("_CatLib")</c> returns mhwilds_overlay and nothing else — CatLib exists there
/// to serve exactly one mod, and the row can say so.</para>
///
/// <para>Read in both directions, per Este's call. Forward: this library is needed by these mods.
/// Reverse: this mod needs a library that is <i>missing</i> — the same job the <c>NEEDS UE4SS</c> chip
/// does for frameworks, applied to libraries inside the mod folder, and the more valuable half
/// because it turns a mod that would silently fail to load into a row that says why.</para>
/// </summary>
public static class LuaRequires
{
    // require "x" / require("x") / require('x'), and the dotted form require("_CatLib.draw") whose
    // ROOT is the library. Anything computed - require(var) - is deliberately not matched: a name we
    // cannot read is a name we do not claim to know.
    private static readonly Regex Pattern = new(
        @"require\s*\(?\s*(['""])(?<mod>[^'""]+)\1",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The library roots one Lua source requires. <c>require("_CatLib.draw")</c> yields
    /// <c>_CatLib</c>, because the root is the folder on disk and the submodule is inside it.</summary>
    public static IReadOnlyList<string> Parse(string? luaSource)
    {
        if (string.IsNullOrEmpty(luaSource)) return Array.Empty<string>();

        var roots = new List<string>();
        foreach (Match m in Pattern.Matches(luaSource))
        {
            var root = m.Groups["mod"].Value.Replace('\\', '/').Split('/', '.')[0].Trim();
            if (root.Length > 0 && !roots.Contains(root, StringComparer.OrdinalIgnoreCase)) roots.Add(root);
        }
        return roots;
    }

    /// <summary>
    /// Which top-level entries require <paramref name="library"/>.
    ///
    /// <param name="sourcesByRelPath">Every Lua file under the mod location, keyed by its path
    /// relative to that location, with its text.</param>
    /// <returns>The distinct top-level owners — the folder or script a dependent belongs to, not the
    /// individual files, because that is what a row is. A library never counts as depending on
    /// itself: CatLib's own 33 files require CatLib constantly.</returns>
    /// </summary>
    public static IReadOnlyList<string> DependentsOf(
        string? library,
        IReadOnlyDictionary<string, string>? sourcesByRelPath)
    {
        if (string.IsNullOrWhiteSpace(library) || sourcesByRelPath is null) return Array.Empty<string>();

        var owners = new List<string>();
        foreach (var (relPath, source) in sourcesByRelPath)
        {
            var owner = TopLevel(relPath);
            if (owner.Length == 0) continue;
            if (string.Equals(owner, library, StringComparison.OrdinalIgnoreCase)) continue; // itself
            if (owners.Contains(owner, StringComparer.OrdinalIgnoreCase)) continue;
            if (Parse(source).Contains(library!, StringComparer.OrdinalIgnoreCase)) owners.Add(owner);
        }
        return owners;
    }

    /// <summary>
    /// Libraries a mod requires that are NOT present in the mod folder — the reverse read.
    ///
    /// <param name="present">Top-level entry names that exist (folders and script stems), which is
    /// what a <c>require</c> can resolve against.</param>
    /// </summary>
    public static IReadOnlyList<string> MissingFor(
        IEnumerable<string>? modSources,
        IEnumerable<string>? present)
    {
        var have = new HashSet<string>(
            (present ?? Enumerable.Empty<string>()).Select(p => (p ?? "").Trim()).Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var src in modSources ?? Enumerable.Empty<string>())
            foreach (var need in Parse(src))
            {
                // A require can also name a stdlib or a REFramework built-in. Only flag what looks
                // like an on-disk library the user could be missing: anything not present AND not
                // already reported. Being wrong here costs a false "you need X" on a working mod, so
                // the caller is expected to treat this as a hint rather than a verdict.
                if (!have.Contains(need) && !missing.Contains(need, StringComparer.OrdinalIgnoreCase))
                    missing.Add(need);
            }
        return missing;
    }

    /// <summary>The top-level entry a relative path belongs to — the folder name, or the script's
    /// own stem when it sits directly in the mod folder.</summary>
    private static string TopLevel(string? relPath)
    {
        var parts = (relPath ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? Path.GetFileNameWithoutExtension(parts[0]) : parts[0];
    }
}
