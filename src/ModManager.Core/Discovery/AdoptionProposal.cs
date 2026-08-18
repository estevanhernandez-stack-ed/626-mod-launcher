using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Discovery;

/// <summary>How strongly a discovered file was identified. Ordered best-first — this is what
/// <see cref="ModMeta.SourceConfidence"/> records, so a weak match can never pass as a strong one.</summary>
public enum AdoptionEvidence { Md5, NameIndex, NameSearch, None }

/// <summary>
/// One reviewable row: what was found, what we think it is, and how sure we are. Adoption writes
/// METADATA ONLY — nothing here moves, renames, or deletes a file. The first file move is the
/// user's first toggle, through the existing reversible path.
/// </summary>
public sealed record AdoptionProposal(
    DiscoveryCandidate Candidate,
    AdoptionEvidence Evidence,
    int? ModId,
    string? Title,
    string? Author,
    int? Endorsements,
    string? Url = null,
    SourceIdentifyResult? Identify = null)
{
    /// <summary>A leftover archive matched by md5 — exact, authoritative. Carries the full identify
    /// result so <see cref="ToMeta"/> can route it through <see cref="SourceMetadataMapper.FromIdentify"/> —
    /// the one writer for "md5-identify to ModMeta" — instead of hand-copying a subset of fields and
    /// silently dropping others (Version, Downloads, Url, ...).</summary>
    public static AdoptionProposal FromMd5(DiscoveryCandidate candidate, SourceIdentifyResult identify)
        => new(candidate, AdoptionEvidence.Md5, identify.Ref.ModId, identify.Metadata.Title,
            identify.Metadata.Author, identify.Metadata.Endorsements, identify.Metadata.ModUrl, identify);

    /// <summary>An extracted mod matched by name against this game's index — a proposal, not a fact.
    /// Carries the entry's mod-page URL through so a name-index adoption gets a link the same way a
    /// manual match / <c>LooseIdentify</c> hit does (<see cref="ModMeta.Url"/>) — an evidence tier
    /// shouldn't yield a link one way and not the other.</summary>
    public static AdoptionProposal FromIndex(DiscoveryCandidate candidate, ModNameIndexEntry entry)
        => new(candidate, AdoptionEvidence.NameIndex, entry.ModId, entry.Name, entry.Author, entry.Endorsements, entry.Url);

    /// <summary>
    /// A swept file named by a live upstream search — the same evidence tier <c>LooseIdentify</c>
    /// produces for rows already in the list, now available on the FIRST run for files that are not
    /// rows yet.
    ///
    /// <para>Without this a swept mod was proposed as "not identified", adopted, became a row, and
    /// only a SECOND run of the whole action would name it — with nothing telling the user a second
    /// run was worth doing.</para>
    ///
    /// <para>Weaker than <see cref="FromMd5"/> and deliberately so: a hash identifies a FILE, a name
    /// identifies a guess. <see cref="ToMeta"/> records it as <c>nameSearch</c> so a later md5 or
    /// fingerprint hit can still supersede it.</para>
    /// </summary>
    public static AdoptionProposal FromSearch(DiscoveryCandidate candidate, SourceSearchHit hit)
        => new(candidate, AdoptionEvidence.NameSearch, hit.ModId, hit.Name, hit.Author,
            hit.EndorsementCount, hit.Url) { Hit = hit };

    /// <summary>The search hit behind a <see cref="AdoptionEvidence.NameSearch"/> proposal, so
    /// <see cref="ToMeta"/> can route it through the ONE search-hit mapper rather than hand-copying a
    /// subset and dropping whatever the API also returned.</summary>
    /// <summary>What adopting this will actually do — <see cref="AdoptionReachRules"/>. Null until
    /// something resolves it, because working it out means reading the archive and Core does not
    /// decide when that I/O happens. A surface that finds it null should say nothing about reach
    /// rather than assume the optimistic answer, which is how "Adopt 13 mods" came to be offered for
    /// thirteen archives that would have written nothing (A14).</summary>
    public AdoptionReach? Reach { get; init; }

    public SourceSearchHit? Hit { get; init; }

    /// <summary>
    /// The swept candidates worth spending an upstream search on: unidentified, and not a loader.
    ///
    /// <para>An already-identified candidate has stronger evidence than a name search could add. A
    /// proxy loader is excluded for the same reason <c>LooseIdentify.Candidates</c> excludes loader
    /// rows — it is the thing other mods ride on, it cannot be named from its filename (several
    /// different loaders ship as <c>version.dll</c>), and attaching some unrelated mod's identity to
    /// it would be the worst possible place to guess.</para>
    /// </summary>
    public static IEnumerable<AdoptionProposal> WorthSearching(IEnumerable<AdoptionProposal> proposals)
        => proposals.Where(p => p.Evidence == AdoptionEvidence.None
                                && p.Candidate.Kind != DiscoveryKind.ProxyLoader);

    /// <summary>Found, unidentified. Still worth adopting: visible and toggleable beats invisible.</summary>
    public static AdoptionProposal Unidentified(DiscoveryCandidate candidate)
        => new(candidate, AdoptionEvidence.None, null, null, null, null);

    /// <summary>The metadata to merge in on approval. Never sets <see cref="ModMeta.IsManual"/> —
    /// an approved proposal is not a manual paste, so a stronger identify can still supersede it.
    /// The <see cref="AdoptionEvidence.Md5"/> branch delegates to <see cref="SourceMetadataMapper.FromIdentify"/>
    /// so md5-adopted mods get every field that mapper populates (notably the installed <c>Version</c>,
    /// without which the UPDATE chip reads a false positive on every one of them) — never hand-copied.</summary>
    public ModMeta ToMeta()
    {
        if (Evidence == AdoptionEvidence.Md5 && Identify is not null)
        {
            var meta = SourceMetadataMapper.FromIdentify(Identify);
            meta.SourceConfidence = "md5";
            return meta;
        }

        // A search hit carries more than the four fields this record keeps (summary, thumbnail,
        // category, downloads). Route it through the ONE search-hit mapper so none of that is
        // dropped — the same lesson LooseIdentify.ToMeta learned when rows came back blank.
        if (Evidence == AdoptionEvidence.NameSearch && Hit is not null)
        {
            var searched = SourceMetadataMapper.FromSearchHit(Hit);
            searched.SourceConfidence = "nameSearch";
            return searched;
        }

        return new ModMeta
        {
            Title = Title,
            Author = Author,
            Url = Url,
            NexusModId = ModId,
            EndorsementCount = Endorsements,
            SourceConfidence = Evidence switch
            {
                AdoptionEvidence.NameIndex or AdoptionEvidence.NameSearch => "nameSearch",
                _ => null,
            },
        };
    }
}
