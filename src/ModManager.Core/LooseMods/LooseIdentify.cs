using ModManager.Plugins.Abstractions;

namespace ModManager.Core.LooseMods;

/// <summary>One name-search proposal for a loose-root row: the cleaned query that was searched,
/// and the best hit (or null if nothing cleared <see cref="NameMatch.PickBestMatch{T}"/>'s
/// threshold, or the search delegate threw for this row).</summary>
public sealed record LooseIdentifyProposal(string ModKey, string CleanQuery, SourceSearchHit? Match);

/// <summary>
/// Name-search identify for loose-root mods (decima / Death Stranding 2 shape): loose files carry
/// no embedded metadata (no md5-identify, no CurseForge fingerprint surface), so the only lead is
/// the filename. This proposes a Nexus name-search match per unidentified loose-root row — pure
/// Core, no plugin dependency at runtime: the App supplies the search as a delegate (typically
/// <see cref="IModTextSearch.SearchAsync"/> on whatever source is active for the game), and the
/// user approves/rejects each proposal before <see cref="ToMeta"/> is persisted. Mirrors the same
/// review-before-write posture as fingerprint/md5 identify elsewhere in the launcher.
/// </summary>
public static class LooseIdentify
{
    private const string LoaderClass = "loader";

    /// <summary>Loose-root rows worth proposing a search for: not a loader row (dinput8/dxgi/version
    /// proxies aren't "mods" a user would search Nexus for), not manually pinned by the user
    /// (<see cref="ModMeta.IsManual"/> locks the entry — auto-identify never clobbers it), and not
    /// already identified (a Nexus id or any prior source confidence means a search would be
    /// redundant, and could overwrite a stronger match with a weaker name-search one).</summary>
    public static IReadOnlyList<Mod> Candidates(IReadOnlyList<Mod> rows, IReadOnlyDictionary<string, ModMeta> meta)
    {
        var result = new List<Mod>();
        foreach (var row in rows)
        {
            if (row.Location != LooseRootListing.LooseRootLocation) continue;
            if (row.Class == LoaderClass) continue;
            if (meta.TryGetValue(row.Base, out var m))
            {
                if (m.IsManual) continue;
                if (m.NexusModId is not null) continue;
                if (m.SourceConfidence is not null) continue;
            }
            result.Add(row);
        }
        return result;
    }

    /// <summary>One proposal per candidate: clean the filename into a search query
    /// (<see cref="NameMatch.CleanModName"/>), run it through the injected search, and pick the
    /// best hit by name overlap (<see cref="NameMatch.PickBestMatch{T}"/>, default threshold 0.5).
    /// Never throws — a bad/throwing/empty search yields <c>Match = null</c> for that row only;
    /// every other candidate still gets its own attempt.</summary>
    public static async Task<IReadOnlyList<LooseIdentifyProposal>> ProposeAsync(
        IReadOnlyList<Mod> candidates, Func<string, Task<IReadOnlyList<SourceSearchHit>>> search)
    {
        var proposals = new List<LooseIdentifyProposal>();
        foreach (var candidate in candidates)
        {
            var query = NameMatch.CleanModName(candidate.Base);
            SourceSearchHit? match = null;
            try
            {
                var hits = await search(query).ConfigureAwait(false);
                match = NameMatch.PickBestMatch(query, hits, h => h.Name);
            }
            catch
            {
                // A throwing search delegate must never take down the whole sweep — this row simply
                // gets no proposal; the rest of the candidates still get their own attempt.
                match = null;
            }
            proposals.Add(new LooseIdentifyProposal(candidate.Base, query, match));
        }
        return proposals;
    }

    /// <summary>Maps an APPROVED search hit to the <see cref="ModMeta"/> fields to persist. Merge-in
    /// only — callers apply this over the existing entry, not replace it — and it never sets
    /// <see cref="ModMeta.IsManual"/>: a name-search hit is a proposal the user approved, not a
    /// manual paste, so a later stronger identify (fingerprint/md5) can still supersede it.</summary>
    public static ModMeta ToMeta(SourceSearchHit hit) => new()
    {
        Title = hit.Name,
        Author = hit.Author,
        Url = hit.Url,
        NexusModId = hit.ModId,
        EndorsementCount = hit.EndorsementCount,
        SourceConfidence = "nameSearch",
    };
}
