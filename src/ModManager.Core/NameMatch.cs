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
    [GeneratedRegex(@"\.(pak|ucas|utoc|esp|esl|esm|bsa|jar|dll|vpk|zip)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtRe();

    [GeneratedRegex(@"_[Pp]$")]
    private static partial Regex PSuffixRe();

    [GeneratedRegex(@"[._\-\s]+")]
    private static partial Regex SplitRe();

    [GeneratedRegex(@"^(.)\1+$", RegexOptions.IgnoreCase)]
    private static partial Regex AllSameCharRe();

    [GeneratedRegex(@"^\d+[xh]$", RegexOptions.IgnoreCase)]
    private static partial Regex MultiplierRe();

    [GeneratedRegex(@"^v\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRe();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelRe();

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
            .Select(t => CamelRe().Replace(t, "$1 $2"));
        return WsRe().Replace(string.Join(" ", kept), " ").Trim();
    }

    private static List<string> Tokens(string? s) =>
        NonAlnumRe().Replace((s ?? "").ToLowerInvariant(), " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

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
