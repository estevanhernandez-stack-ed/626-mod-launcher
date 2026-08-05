using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure name matching for search-by-name metadata. Mod files in the wild are renamed for
/// load order (ZZZ.CF.JSON.AL_...) and carry engine suffixes (_P), so (a) clean a filename
/// into a human-ish search query and (b) score CurseForge hits by token overlap, refusing
/// weak matches so the wrong mod's metadata is never attached. Mirrors name-match-core.js.
/// </summary>
public static partial class NameMatch
{
    // Every extension a mod file actually wears in the wild. An extension missing here is NOT
    // harmless: it survives as a token in the search query sent upstream, so it both pollutes the
    // Jaccard score and degrades the search itself. "archive" (REDengine/Cyberpunk) was absent,
    // which put a junk "archive" token on every query for a 194-mod library. The optional (\.xl)
    // tail catches ArchiveXL's compound "Foo.archive.xl" sidecar in the same pass.
    [GeneratedRegex(@"\.(pak|ucas|utoc|esp|esl|esm|bsa|jar|dll|vpk|zip|archive|reds|asi)(\.xl)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtRe();

    [GeneratedRegex(@"_[Pp]$")]
    private static partial Regex PSuffixRe();

    // Load-order sigils count as separators, not name characters. Games that load a mod folder
    // alphabetically get modded with brute-force prefixes — Cyberpunk's "#", "!", "###" and UE's
    // "~" all exist to sort a file to the front, exactly like the "ZZZ.CF.JSON.AL_" shape this
    // splitter already handled. Left in, they ride into the upstream search query ("###Mute Menu
    // ..."), which is a worse query than the same name without them.
    [GeneratedRegex(@"[._\-\s#!~]+")]
    private static partial Regex SplitRe();

    [GeneratedRegex(@"^(.)\1+$", RegexOptions.IgnoreCase)]
    private static partial Regex AllSameCharRe();

    [GeneratedRegex(@"^\d+[xh]$", RegexOptions.IgnoreCase)]
    private static partial Regex MultiplierRe();

    [GeneratedRegex(@"^v\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRe();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelRe();

    // Trailing digits glued to a word with no case boundary (FasterShips10, Fallout4) are the
    // dominant real-world mod-name shape, not an edge case — split, don't drop, so token overlap
    // still sees "ships" while a version/variant number never silently merges two different mods.
    [GeneratedRegex(@"([a-zA-Z])(\d)")]
    private static partial Regex LetterDigitRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WsRe();

    [GeneratedRegex(@"[A-Z]")]
    private static partial Regex HasUpperRe();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRe();

    public static string CleanModName(string? name)
    {
        var s = PSuffixRe().Replace(ExtRe().Replace(name ?? "", ""), "");
        var kept = SplitRe().Split(s)
            .Where(t => t.Length > 0)
            .Where(t =>
            {
                if (AllSameCharRe().IsMatch(t)) return false;                                 // ZZZ, AAA
                if (t.Length <= 4 && t == t.ToUpperInvariant() && HasUpperRe().IsMatch(t)) return false; // CF, AL, ZEN, JSON
                if (MultiplierRe().IsMatch(t)) return false;                                  // 2x, 6h, 10x
                if (VersionRe().IsMatch(t)) return false;                                     // v2
                return true;
            })
            .Select(t => LetterDigitRe().Replace(CamelRe().Replace(t, "$1 $2"), "$1 $2"));
        return WsRe().Replace(string.Join(" ", kept), " ").Trim();
    }

    private static List<string> Tokens(string? s) =>
        NonAlnumRe().Replace((s ?? "").ToLowerInvariant(), " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Public seam onto the SAME tokenizer <see cref="Jaccard"/>/<see cref="PickBestMatch{T}"/>
    /// score with — for a caller that needs to know how many tokens a name/query breaks into
    /// (e.g. to decide when Jaccard's fuzzy threshold is too permissive: a single shared token
    /// against a two-token candidate scores exactly 0.5, clearing the default threshold). Not a
    /// second tokenizer — same regex, same casing, same split.</summary>
    public static IReadOnlyList<string> Tokenize(string? s) => Tokens(s);

    private static double Jaccard(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        var setA = new HashSet<string>(a);
        var setB = new HashSet<string>(b);
        var inter = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - inter;
        return union != 0 ? (double)inter / union : 0;
    }

    /// <summary>Best candidate (by name) for <paramref name="query"/>, or null if none clears the threshold.</summary>
    public static T? PickBestMatch<T>(string query, IEnumerable<T>? candidates, Func<T, string?> name, double threshold = 0.5)
        where T : class
    {
        var q = Tokens(query);
        T? best = null;
        double bestScore = 0;
        foreach (var c in candidates ?? Enumerable.Empty<T>())
        {
            var s = Jaccard(q, Tokens(name(c)));
            if (s > bestScore) { bestScore = s; best = c; }
        }
        return bestScore >= threshold ? best : null;
    }
}
