using ModManager.Plugins.Abstractions;

namespace ModManager.Core.LooseMods;

/// <summary>One name-search proposal for a candidate row: the cleaned query that was searched,
/// and the best hit (or null if nothing cleared <see cref="NameMatch.PickBestMatch{T}"/>'s
/// threshold, or the search delegate threw for this row).</summary>
public sealed record LooseIdentifyProposal(string ModKey, string CleanQuery, SourceSearchHit? Match);

/// <summary>How far along a <see cref="LooseIdentify.ProposeAsync"/> run is. Reported after each
/// row settles, so <paramref name="Completed"/> climbs monotonically to <paramref name="Total"/>
/// unless the run is cancelled.</summary>
public sealed record LooseIdentifyProgress(int Completed, int Total);

/// <summary>
/// Name-search identify for mods with no embedded metadata to lean on (no md5-identify hit, no
/// CurseForge fingerprint surface) — loose files carry none of that, so the only lead is the
/// filename. Originally loose-root only (decima / Death Stranding 2 shape); <see cref="Candidates"/>
/// now offers this for an unidentified row in ANY location — a Bethesda Data folder, a UE Paks
/// folder, wherever — since the filename-only problem isn't unique to loose-root. Pure Core, no
/// plugin dependency at runtime: the App supplies the search as a delegate (typically
/// <see cref="IModTextSearch.SearchAsync"/> on whatever source is active for the game), and the
/// user approves/rejects each proposal before <see cref="ToMeta"/> is persisted. Mirrors the same
/// review-before-write posture as fingerprint/md5 identify elsewhere in the launcher.
/// </summary>
public static class LooseIdentify
{
    private const string LoaderClass = "loader";

    /// <summary>Rows worth proposing a search for, in ANY location (loose-root, a Bethesda Data
    /// folder, a UE Paks folder — an unidentified mod is worth identifying wherever it sits): not
    /// a loader row (dinput8/dxgi/version proxies aren't "mods" a user would search Nexus for),
    /// not manually pinned by the user (<see cref="ModMeta.IsManual"/> locks the entry —
    /// auto-identify never clobbers it), and not already identified (a Nexus id or any prior
    /// source confidence means a search would be redundant, and could overwrite a stronger match
    /// with a weaker name-search one).</summary>
    public static IReadOnlyList<Mod> Candidates(IReadOnlyList<Mod> rows, IReadOnlyDictionary<string, ModMeta> meta)
    {
        var result = new List<Mod>();
        foreach (var row in rows)
        {
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
    /// <summary>How many searches are allowed in flight at once. A real library is far bigger than
    /// this method was first sized for — a hand-modded Cyberpunk install runs to ~200 rows, and
    /// one-at-a-time turns that into a multi-minute wall with nothing to look at. Four is chosen to
    /// be a real speedup while staying polite to the upstream API: this runs against a user's
    /// personal Nexus key, and a burst wide enough to trip their rate limiter would cost the user
    /// their whole run, not just one row.</summary>
    public const int DefaultConcurrency = 4;

    /// <summary>
    /// Search each candidate by cleaned name and pick the best hit, up to
    /// <paramref name="maxConcurrency"/> searches at a time.
    ///
    /// ORDER IS PRESERVED regardless of the order results arrive in — proposals come back in the
    /// candidate order the caller passed, because the review dialog lists them in that order and a
    /// concurrency detail must never reshuffle what the user reads.
    ///
    /// CANCELLATION returns the proposals that finished, in order, rather than throwing: nothing
    /// here writes anything (every proposal is still review-gated), so partial work is safe to keep
    /// and discarding 150 completed searches because the user stopped at 151 would be hostile. A
    /// caller that needs to know it was cut short checks its own token.
    /// </summary>
    public static async Task<IReadOnlyList<LooseIdentifyProposal>> ProposeAsync(
        IReadOnlyList<Mod> candidates,
        Func<string, Task<IReadOnlyList<SourceSearchHit>>> search,
        int maxConcurrency = DefaultConcurrency,
        IProgress<LooseIdentifyProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0) return Array.Empty<LooseIdentifyProposal>();

        // Slot i holds candidate i's proposal, so the result order never depends on completion
        // order. A slot stays null only if its row was never started (cancelled before it ran).
        var slots = new LooseIdentifyProposal?[candidates.Count];
        var next = -1;
        var completed = 0;

        async Task WorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= candidates.Count || ct.IsCancellationRequested) return;

                var query = NameMatch.CleanModName(candidates[index].Base);
                SourceSearchHit? match = null;
                try
                {
                    var hits = await search(query).ConfigureAwait(false);
                    match = NameMatch.PickBestMatch(query, hits, h => h.Name);
                }
                catch
                {
                    // A throwing search delegate must never take down the whole run — this row
                    // simply gets no proposal; every other candidate still gets its own attempt.
                    match = null;
                }

                slots[index] = new LooseIdentifyProposal(candidates[index].Base, query, match);
                progress?.Report(new LooseIdentifyProgress(
                    Interlocked.Increment(ref completed), candidates.Count));
            }
        }

        // Never spin up more workers than there is work for them to do.
        var workers = Math.Clamp(maxConcurrency, 1, candidates.Count);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => WorkerAsync())).ConfigureAwait(false);

        return slots.Where(p => p is not null).Select(p => p!).ToList();
    }

    /// <summary>Maps an APPROVED search hit to the <see cref="ModMeta"/> fields to persist. Merge-in
    /// only — callers apply this over the existing entry, not replace it — and it never sets
    /// <see cref="ModMeta.IsManual"/>: a name-search hit is a proposal the user approved, not a
    /// manual paste, so a later stronger identify (fingerprint/md5) can still supersede it.</summary>
    public static ModMeta ToMeta(SourceSearchHit hit)
    {
        // Delegates to the ONE search-hit mapper rather than hand-copying a subset. The hand-rolled
        // version listed six fields and dropped everything else the API had already paid for —
        // Summary and ThumbnailUrl among them — so identified rows rendered with no description and
        // no picture even though the UI has always bound both.
        var meta = Plugins.SourceMetadataMapper.FromSearchHit(hit);
        meta.SourceConfidence = "nameSearch";
        return meta;
    }
}
