using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// Adoption writes METADATA ONLY — no file op — and it must record how sure we are. A name-index
// hit is weaker evidence than an md5 hit and can never masquerade as one, so a later stronger
// identify (or the manual-match dialog) can still supersede it.
public class AdoptionProposalTests
{
    private static DiscoveryCandidate Candidate(string name = "FasterShips10.pak")
        => new($"Content/Paks/~mods/{name}", name, DiscoveryKind.EngineShaped);

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

    [Fact]
    public void Md5_evidence_records_md5_confidence()
    {
        var proposal = AdoptionProposal.FromMd5(
            Candidate("FasterShips10.zip"), modId: 7, title: "Faster Ships", author: "Kingtology", endorsements: 240);

        Assert.Equal("md5", proposal.ToMeta().SourceConfidence);
    }

    [Fact]
    public void Adoption_never_marks_an_entry_manual()
    {
        var fromIndex = AdoptionProposal.FromIndex(Candidate(), new ModNameIndexEntry(1, "Faster Ships", null, null));
        var fromMd5 = AdoptionProposal.FromMd5(Candidate(), 7, "Faster Ships", null, null);

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
