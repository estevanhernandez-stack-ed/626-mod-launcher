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

    /// <summary>
    /// Progressively broader search queries for one cleaned mod name, widest match first.
    ///
    /// <para>SEARCH BROAD, SCORE NARROW. Upstream mod search wants a few central words; a filename
    /// hands us every token the author used, including ones that appear nowhere in the mod's title.
    /// Live example: the file <c>#ApartmentCatsCustoms_Dogtown_Black</c> cleans to
    /// "Apartment Cats Customs Dogtown Black", which returns ZERO hits — while "apartment cat"
    /// returns the mod. Neither "Customs" nor "Black" is in the title "Apartment Cats - Dogtown".</para>
    ///
    /// <para>The scoring was never the problem: that pair scores 0.6 against
    /// <see cref="PickBestMatch{T}"/>'s 0.5 threshold and would have matched comfortably. We simply
    /// never got a candidate to score. So the caller searches with these rungs and scores with the
    /// FULL name — broadening the retrieval must never broaden the acceptance.</para>
    ///
    /// <para>Rungs are leading token prefixes (a mod name leads with its distinctive part), never
    /// shorter than two tokens — a one-word search returns noise, and a spurious 0.5 is worse than
    /// no match. Capped at three rungs: each one is an API call against the user's personal
    /// key.</para>
    /// </summary>
    public static IReadOnlyList<string> QueryLadder(string? cleanName)
    {
        var words = (cleanName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<string>();

        var rungs = new List<string> { string.Join(' ', words) };
        foreach (var take in new[] { 3, 2 })
        {
            if (take >= words.Length) continue;   // never re-issue the full query
            var rung = string.Join(' ', words.Take(take));
            if (!rungs.Contains(rung, StringComparer.OrdinalIgnoreCase)) rungs.Add(rung);
        }
        return rungs;
    }

    /// <summary>Fold a trailing plural so <c>customs</c> and <c>custom</c> are one token. Used ONLY
    /// by the contiguous-run matcher: a run must survive an author writing the singular in a filename
    /// and the plural in the title ("ApartmentCatsCustoms" vs "Apartment Cats - Custom Cats"), and
    /// without it the run breaks on that one word and the true owner loses to a sibling.
    ///
    /// <para>Deliberately naive and deliberately NOT applied to <see cref="Jaccard"/>: a stemmer that
    /// conflates real words would change every score in the app, and the measurement that justified
    /// this change only ever tested folding inside the run.</para>
    ///
    /// <para>Only a trailing <c>s</c> comes off, only when the word survives it and does not end in
    /// <c>ss</c> — so <c>boss</c>, <c>less</c> and <c>gas</c> are left alone.</para></summary>
    private static string Singular(string token) =>
        token.Length > 3 && token[^1] == 's' && token[^2] != 's' ? token[..^1] : token;

    private static List<string> RunTokens(string? s) => Tokens(CleanModName(s)).Select(Singular).ToList();

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
        var list = (candidates ?? Enumerable.Empty<T>()).ToList();

        // A title that the filename SPELLS OUT, in order, is the strongest evidence available and is
        // checked first — see BestByContiguousRun.
        if (BestByContiguousRun(query, list, name) is { } spelled) return spelled;

        var q = Tokens(query);
        T? best = null;
        double bestScore = 0;
        foreach (var c in list)
        {
            var s = Jaccard(q, Tokens(name(c)));
            if (s > bestScore) { bestScore = s; best = c; }
        }
        return bestScore >= threshold ? best : null;
    }

    /// <summary>
    /// The candidate whose whole title the filename spells out, in order, as an unbroken run.
    ///
    /// <para>A variant file is its parent's title PLUS a suffix; a SIBLING mod shares words but
    /// breaks the run. That distinction is invisible to Jaccard and to coverage, because both throw
    /// word order away — which is why both kept choosing siblings. Measured on six real files that
    /// all ship from one Nexus page, the shipped matcher attached the wrong mod to three of them:</para>
    ///
    /// <code>
    /// "Apartment Cats Customs Dogtown Black"  contains  "Apartment Cats Custom(s)"  unbroken
    ///                                         does NOT  "Apartment Cats Dogtown"    (customs intervenes)
    /// </code>
    ///
    /// <para>Both sides go through <see cref="CleanModName"/> first. Without that the comparison is
    /// asymmetric — the query has its short all-caps tokens filtered and a raw title does not, so
    /// "Slaught-O-Matic Platinum" could never align with a query already missing that "O".</para>
    ///
    /// <para>Longest run wins, ties refuse. A tie means the filename spells out two titles equally
    /// well and the evidence does not choose; guessing there is how a wrong identity gets attached,
    /// which is the failure this repo fears most. Runs shorter than two words are ignored — one
    /// shared word is a coincidence, not a spelling-out.</para>
    /// </summary>
    private static T? BestByContiguousRun<T>(string query, List<T> candidates, Func<T, string?> name)
        where T : class
    {
        const int MinRun = 2;

        var q = RunTokens(query);
        T? best = null;
        var bestRun = 0;
        var tied = false;

        foreach (var c in candidates)
        {
            var run = RunLength(q, RunTokens(name(c)));
            if (run < MinRun) continue;
            if (run > bestRun) { bestRun = run; best = c; tied = false; }
            else if (run == bestRun) tied = true;
        }
        return tied ? null : best;
    }

    /// <summary>How many of <paramref name="title"/>'s distinct tokens appear consecutively, in
    /// order, inside <paramref name="query"/> — or 0 if they never do. Distinct because a title that
    /// repeats a word ("Apartment Cats - Custom Cats") still names one run.</summary>
    private static int RunLength(IReadOnlyList<string> query, IEnumerable<string> title)
    {
        var want = new List<string>();
        foreach (var t in title) if (!want.Contains(t)) want.Add(t);
        if (want.Count == 0) return 0;

        for (var i = 0; i + want.Count <= query.Count; i++)
        {
            var all = true;
            for (var j = 0; j < want.Count && all; j++) all = query[i + j] == want[j];
            if (all) return want.Count;
        }
        return 0;
    }
}
