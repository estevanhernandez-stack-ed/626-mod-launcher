using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Discovery;

// Adoption writes METADATA ONLY — no file op — and it must record how sure we are. A name-index
// hit is weaker evidence than an md5 hit and can never masquerade as one, so a later stronger
// identify (or the manual-match dialog) can still supersede it.
public class AdoptionProposalTests
{
    private static DiscoveryCandidate Candidate(string name = "FasterShips10.pak")
        => new($"Content/Paks/~mods/{name}", name, DiscoveryKind.EngineShaped);

    private static SourceIdentifyResult Identify(
        int modId = 7, string? version = "1.2.0", string? title = "Faster Ships",
        string? author = "Kingtology", int? endorsements = 240)
        => new(
            new SourceModRef("nexus", "kingdomcome2", modId, version ?? ""),
            new SourceModMetadata(
                Endorsements: endorsements, Downloads: 1000, LatestVersion: version, Available: true,
                Endorsed: null, Title: title, Author: author));

    [Fact]
    public void Index_evidence_records_nameSearch_confidence()
    {
        var proposal = AdoptionProposal.FromIndex(
            Candidate(), new ModNameIndexEntry(1, "Faster Ships", "Kingtology", 240));

        var meta = proposal.ToMeta();

        Assert.Equal("nameSearch", meta.SourceConfidence);
        Assert.Equal("Faster Ships", meta.Title);
        Assert.Equal("Kingtology", meta.Author);
        Assert.Equal(1, meta.NexusModId);
        Assert.Equal(240, meta.EndorsementCount);
    }

    // Final-review minor: the same evidence tier used to yield a mod-page link one way
    // (LooseIdentify.ToMeta sets Url from the hit) and not the other (a name-index adoption
    // dropped it on the floor). FromIndex now carries ModNameIndexEntry.Url through to ToMeta.
    [Fact]
    public void Index_evidence_carries_the_entrys_url_through_to_meta()
    {
        var proposal = AdoptionProposal.FromIndex(
            Candidate(), new ModNameIndexEntry(1, "Faster Ships", "Kingtology", 240, "https://www.nexusmods.com/kingdomcome2/mods/1"));

        Assert.Equal("https://www.nexusmods.com/kingdomcome2/mods/1", proposal.ToMeta().Url);
    }

    [Fact]
    public void Md5_evidence_records_md5_confidence()
    {
        var proposal = AdoptionProposal.FromMd5(Candidate("FasterShips10.zip"), Identify());

        var meta = proposal.ToMeta();

        // Regression guard: FromMd5/ToMeta must route through SourceMetadataMapper.FromIdentify
        // (the one writer for "md5-identify -> ModMeta") rather than hand-copying a subset of
        // fields. Version is the field a scalar rebuild silently drops — without it, a later
        // NexusLatestVersion refresh reads NexusLatestVersion != Version as true on every
        // md5-adopted mod, showing a permanent false UPDATE chip.
        Assert.Equal("md5", meta.SourceConfidence);
        Assert.Equal(7, meta.NexusModId);
        Assert.Equal("Faster Ships", meta.Title);
        Assert.Equal("Kingtology", meta.Author);
        Assert.Equal(240, meta.EndorsementCount);
        Assert.Equal("1.2.0", meta.Version);
    }

    [Fact]
    public void Adoption_never_marks_an_entry_manual()
    {
        var fromIndex = AdoptionProposal.FromIndex(Candidate(), new ModNameIndexEntry(1, "Faster Ships", null, null));
        var fromMd5 = AdoptionProposal.FromMd5(Candidate(), Identify());

        Assert.False(fromIndex.ToMeta().IsManual);
        Assert.False(fromMd5.ToMeta().IsManual);
    }

    [Fact]
    public void An_unidentified_find_is_still_adoptable_with_no_false_identity()
    {
        var proposal = AdoptionProposal.Unidentified(Candidate("MysteryThing.pak"));

        var meta = proposal.ToMeta();

        Assert.Equal(AdoptionEvidence.None, proposal.Evidence);
        Assert.Null(meta.SourceConfidence);
        Assert.Null(meta.NexusModId);
        Assert.Null(meta.Title);
    }
}
