using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Discovery;

/// <summary>What a live name search over swept candidates did, and what it left undone.</summary>
/// <param name="Proposals">The input list, in its order, with every candidate the search named
/// upgraded to <see cref="AdoptionEvidence.NameSearch"/>. A miss is left exactly as it was.</param>
/// <param name="Worth">How many candidates were worth searching (unidentified, not a loader).</param>
/// <param name="Searched">How many of those were actually asked about and settled.</param>
/// <param name="Named">How many of the searched ones came back with a confident match.</param>
/// <param name="LeftByCap">Worth searching, but past this run's cap: never asked about.</param>
/// <param name="RateLimited">The source throttled the run, so it stopped early.</param>
/// <param name="Hits">Every match found, for the caller to fold into the per-game name index.</param>
public sealed record DiscoveryNameSearchResult(
    IReadOnlyList<AdoptionProposal> Proposals,
    int Worth,
    int Searched,
    int Named,
    int LeftByCap,
    bool RateLimited,
    IReadOnlyList<SourceSearchHit> Hits);

/// <summary>
/// Tier 2b of "find what's already there" (B2): a swept candidate the per-game name index could not
/// place (the index holds the top ~500 browsable mods; a big library is mostly outside that) gets a
/// live name search, the same one unidentified rows get. Same engine as the row search
/// (<see cref="LooseIdentify.SearchEachAsync{T}"/>), so the ladder, the threshold, the concurrency and
/// the rate-limit rules cannot drift between the two.
///
/// <para><b>Capped per run, and the cap is never silent.</b> The search is Nexus's GraphQL mods search,
/// which does not draw on the user's v1 day budget the md5 tier spends, so the cap is about
/// politeness and wall time, not budget: each candidate can cost up to the three rungs of the query
/// ladder. Candidates past the cap are not dropped; they stay listed as unidentified, and the result
/// says how many were left so the caller can say so.</para>
///
/// <para>Nothing here writes. Every named candidate is still a proposal behind the review dialog.</para>
/// </summary>
public static class DiscoveryNameSearch
{
    /// <summary>The cap for a run the user asked for ("Identify my mods"), which shows progress and
    /// has a Stop button. 200 covers a heavily modded library in one go: at the default
    /// concurrency, worst case ~600 calls, about a minute.</summary>
    public const int RequestedCap = 200;

    /// <summary>The cap for the silent sweep when a game is added. It has no Stop button and sits
    /// between the user and their new game, so it searches the first 50 (worst case ~150 calls,
    /// seconds) and says how many it left for "Identify my mods".</summary>
    public const int AutoCap = 50;

    public static async Task<DiscoveryNameSearchResult> NameAsync(
        IReadOnlyList<AdoptionProposal> proposals,
        Func<string, Task<IReadOnlyList<SourceSearchHit>>> search,
        int cap,
        int maxConcurrency = LooseIdentify.DefaultConcurrency,
        IProgress<LooseIdentifyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var worth = AdoptionProposal.WorthSearching(proposals).ToList();
        var asked = worth.Take(Math.Max(0, cap)).ToList();
        var rateLimited = false;

        var results = asked.Count == 0
            ? Array.Empty<(AdoptionProposal Item, string Query, SourceSearchHit? Match)>()
            : await LooseIdentify.SearchEachAsync(asked, p => p.Candidate.FileName, search, maxConcurrency,
                progress, ct, onRateLimited: () => rateLimited = true).ConfigureAwait(false);

        // Keyed by the candidate's path: two candidates never share one (the sweep deduplicates).
        var named = new Dictionary<string, SourceSearchHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
            if (r.Match is { } hit) named[r.Item.Candidate.RelativePath] = hit;

        var upgraded = named.Count == 0
            ? proposals
            : proposals.Select(p => p.Evidence == AdoptionEvidence.None
                                    && named.TryGetValue(p.Candidate.RelativePath, out var hit)
                    ? AdoptionProposal.FromSearch(p.Candidate, hit)
                    : p)
                .ToList();

        return new DiscoveryNameSearchResult(
            upgraded, worth.Count, results.Count, named.Count,
            LeftByCap: worth.Count - asked.Count,
            RateLimited: rateLimited,
            Hits: named.Values.ToList());
    }

    /// <summary>The line that says what the search did NOT do, or null when it did everything it
    /// was worth doing. A truncated run that says nothing reads as a complete one, and a candidate
    /// left unnamed by a cap or a throttle must not read as one Nexus had no match for.</summary>
    /// <param name="auto">The silent add-game sweep, which can point at "Identify my mods": that run
    /// searches four times as many, and a find adopted as unidentified becomes a row, which its row
    /// search covers without a cap. The requested run makes no such promise: nothing records which
    /// finds were already searched, so running it again would ask about the same first ones.</param>
    public static string? Note(DiscoveryNameSearchResult r, bool auto)
    {
        if (r.RateLimited)
            return $"Nexus rate-limited the name search after {r.Searched} of {r.Worth} unnamed finds, "
                   + "so the rest are listed as not identified. Try Identify my mods again later.";
        if (r.LeftByCap > 0)
            return auto
                ? $"Searched Nexus by name for {r.Worth - r.LeftByCap} of {r.Worth} unnamed finds; Identify my mods searches more."
                : $"Searched Nexus by name for the first {r.Worth - r.LeftByCap} of {r.Worth} unnamed finds; the rest are listed as not identified.";
        return null;
    }
}
