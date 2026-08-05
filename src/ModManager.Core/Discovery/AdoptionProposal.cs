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
    int? Endorsements)
{
    /// <summary>A leftover archive matched by md5 — exact, authoritative.</summary>
    public static AdoptionProposal FromMd5(
        DiscoveryCandidate candidate, int modId, string? title, string? author, int? endorsements)
        => new(candidate, AdoptionEvidence.Md5, modId, title, author, endorsements);

    /// <summary>An extracted mod matched by name against this game's index — a proposal, not a fact.</summary>
    public static AdoptionProposal FromIndex(DiscoveryCandidate candidate, ModNameIndexEntry entry)
        => new(candidate, AdoptionEvidence.NameIndex, entry.ModId, entry.Name, entry.Author, entry.Endorsements);

    /// <summary>Found, unidentified. Still worth adopting: visible and toggleable beats invisible.</summary>
    public static AdoptionProposal Unidentified(DiscoveryCandidate candidate)
        => new(candidate, AdoptionEvidence.None, null, null, null, null);

    /// <summary>The metadata to merge in on approval. Never sets <see cref="ModMeta.IsManual"/> —
    /// an approved proposal is not a manual paste, so a stronger identify can still supersede it.</summary>
    public ModMeta ToMeta() => new()
    {
        Title = Title,
        Author = Author,
        NexusModId = ModId,
        EndorsementCount = Endorsements,
        SourceConfidence = Evidence switch
        {
            AdoptionEvidence.Md5 => "md5",
            AdoptionEvidence.NameIndex => "nameSearch",
            _ => null,
        },
    };
}
