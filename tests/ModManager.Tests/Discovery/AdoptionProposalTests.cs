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

    // ---- Swept candidates get named on the FIRST run (backlog C3) ----
    // The sweep finds files the mod list has never seen. Pass 4 only ever searched EXISTING rows, so
    // a swept mod was proposed as "not identified", adopted, became a row — and only a SECOND run of
    // the whole action would name it. The user had no way to know that.

    private static DiscoveryCandidate Swept(string file, DiscoveryKind kind = DiscoveryKind.EngineShaped)
        => new($"mods/{file}", file, kind);

    private static SourceSearchHit SearchHit(string name, int modId = 77) =>
        new("cyberpunk2077", modId, name, "SomeAuthor", "a summary", 250, "https://nexusmods.com/x/" + modId);

    [Fact]
    public void A_search_hit_names_a_swept_candidate_and_records_how_it_was_matched()
    {
        var p = AdoptionProposal.FromSearch(Swept("QuietFootsteps_P.pak"), SearchHit("Quiet Footsteps"));

        Assert.Equal(AdoptionEvidence.NameSearch, p.Evidence);
        Assert.Equal("Quiet Footsteps", p.Title);
        Assert.Equal(77, p.ModId);
        Assert.Equal("SomeAuthor", p.Author);
        Assert.Equal(250, p.Endorsements);
    }

    // The confidence written to disk is what tells a later, STRONGER identify it may supersede this.
    // A name search must never persist as anything else.
    [Fact]
    public void A_searched_adoption_persists_as_a_name_search()
    {
        var meta = AdoptionProposal.FromSearch(Swept("QuietFootsteps_P.pak"), SearchHit("Quiet Footsteps")).ToMeta();

        Assert.Equal("nameSearch", meta.SourceConfidence);
        Assert.Equal("Quiet Footsteps", meta.Title);
        Assert.Equal(77, meta.NexusModId);
        Assert.Equal("a summary", meta.Description);   // full search-hit mapping, not a hand-copied subset
        Assert.False(meta.IsManual);                   // an approved proposal is not a manual paste
    }

    // A name match says WHICH MOD, never which FILE — so it must not invent version state. The same
    // rule the loose-identify path follows; a version compare against nothing lights the UPDATE chip
    // on every row at once.
    [Fact]
    public void A_searched_adoption_never_invents_version_state()
    {
        var meta = AdoptionProposal.FromSearch(Swept("X_P.pak"), SearchHit("X Mod")).ToMeta();

        Assert.Null(meta.Version);
        Assert.Null(meta.NexusLatestVersion);
    }

    // ---- Which swept candidates are worth a search ----

    [Fact]
    public void Only_unidentified_candidates_are_worth_searching()
    {
        var proposals = new[]
        {
            AdoptionProposal.Unidentified(Swept("Unknown_P.pak")),
            AdoptionProposal.FromIndex(Swept("Known_P.pak"), new ModNameIndexEntry(1, "Known", "A", 5, null)),
        };

        var worth = AdoptionProposal.WorthSearching(proposals).ToList();

        var one = Assert.Single(worth);
        Assert.Equal("Unknown_P.pak", one.Candidate.FileName);
    }

    // A proxy DLL is the loader other mods ride on, and it cannot be named from its filename —
    // several different loaders ship as version.dll. Searching Nexus for "version" wastes a call and
    // risks attaching some unrelated mod to the thing every other mod depends on.
    [Fact]
    public void A_proxy_loader_is_never_searched()
    {
        var proposals = new[] { AdoptionProposal.Unidentified(Swept("version.dll", DiscoveryKind.ProxyLoader)) };

        Assert.Empty(AdoptionProposal.WorthSearching(proposals));
    }

    [Fact]
    public void Nothing_to_search_yields_nothing_rather_than_throwing()
        => Assert.Empty(AdoptionProposal.WorthSearching(Array.Empty<AdoptionProposal>()));
}
