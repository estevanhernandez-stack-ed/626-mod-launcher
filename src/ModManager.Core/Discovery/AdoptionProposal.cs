using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Discovery;

/// <summary>How strongly a discovered file was identified. Ordered best-first — this is what
/// <see cref="ModMeta.SourceConfidence"/> records, so a weak match can never pass as a strong one.</summary>
public enum AdoptionEvidence { Md5, NameIndex, None }

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

        return new ModMeta
        {
            Title = Title,
            Author = Author,
            Url = Url,
            NexusModId = ModId,
            EndorsementCount = Endorsements,
            SourceConfidence = Evidence switch
            {
                AdoptionEvidence.NameIndex => "nameSearch",
                _ => null,
            },
        };
    }
}
