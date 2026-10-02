using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Discovery;

/// <summary>
/// C5: one mod found by both the hash pass and the name search. The rows stay linked rather than
/// folded, so neither choice disappears, and "Apply N" counts what the write will do.
/// </summary>
public class IdentifyReviewOverlapTests
{
    private static AdoptionProposal Adoption(string file, AdoptionEvidence evidence, AdoptionReach? reach, params string[] keys)
        => new AdoptionProposal(new DiscoveryCandidate("downloads/" + file, file, DiscoveryKind.Archive),
                evidence, 1, "Cool Mod", "someone", null)
            { Reach = reach, WriteKeys = keys };

    private static (string, SourceSearchHit) Match(string key, string name = "Cool Mod")
        => (key, new SourceSearchHit("game", 2, name, null, null, null, null));

    [Fact]
    public void A_hash_adoption_that_will_write_names_the_mod()
    {
        var a = Adoption("cool.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod, "CoolMod");

        Assert.Same(a, IdentifyReviewOverlap.Coverer(new[] { a }, "coolmod"));   // case-insensitive
    }

    // Review on #363: an unidentified file is not pre-checked, so it must never hide a ticked name
    // match behind a row the user would have to tick.
    [Fact]
    public void An_unidentified_adoption_never_names_the_mod()
        => Assert.Null(IdentifyReviewOverlap.Coverer(
            new[] { Adoption("cool.zip", AdoptionEvidence.None, AdoptionReach.NamesAMod, "CoolMod") }, "CoolMod"));

    [Theory]
    [InlineData(AdoptionReach.NothingToNameYet)]
    [InlineData(AdoptionReach.AlreadyNamed)]
    public void An_adoption_that_writes_nothing_never_names_the_mod(AdoptionReach reach)
        => Assert.Null(IdentifyReviewOverlap.Coverer(new[] { Adoption("cool.zip", AdoptionEvidence.Md5, reach, "CoolMod") }, "CoolMod"));

    [Fact]
    public void An_unresolved_adoption_never_names_the_mod()
        => Assert.Null(IdentifyReviewOverlap.Coverer(new[] { Adoption("cool.zip", AdoptionEvidence.Md5, null) with { WriteKeys = null } }, "CoolMod"));

    // Review on #363: the list is not strongest-first, so pick by evidence.
    [Fact]
    public void Of_two_adoptions_for_one_mod_the_strongest_evidence_names_it()
    {
        var byName = Adoption("a.zip", AdoptionEvidence.NameIndex, AdoptionReach.NamesAMod, "CoolMod");
        var byHash = Adoption("b.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod, "CoolMod");

        Assert.Same(byHash, IdentifyReviewOverlap.Coverer(new[] { byName, byHash }, "CoolMod"));
    }

    // The count: a name match lands only when no APPROVED adoption names its mod, exactly as the
    // apply's ExcludeKeys drops it. Unticking the adoption brings the name match back.
    [Fact]
    public void A_name_match_counts_only_when_its_adoption_is_not_approved()
    {
        var a = Adoption("cool.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod, "CoolMod");
        var approved = new[] { Match("CoolMod"), Match("OtherMod") };

        Assert.Equal(new[] { "OtherMod" }, IdentifyReviewOverlap.Effective(approved, new[] { a }).Select(m => m.ModKey));
        Assert.Equal(2, IdentifyReviewOverlap.Effective(approved, Array.Empty<AdoptionProposal>()).Count);
    }

    // Review on #363: a different name is a disagreement, not corroboration.
    [Fact]
    public void The_adoption_row_says_whether_the_name_search_agrees()
    {
        var a = Adoption("cool.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod, "CoolMod");

        Assert.Equal(" The name search agrees.", AdoptionReviewText.NameSearchAlso(a, new[] { "Cool Mod", "cool mod" }));
        Assert.Contains("suggested Cool Mod Redux instead", AdoptionReviewText.NameSearchAlso(a, new[] { "Cool Mod Redux" }));
        Assert.Equal("", AdoptionReviewText.NameSearchAlso(a, Array.Empty<string>()));
    }

    [Fact]
    public void A_linked_name_match_says_which_row_names_it_and_how_to_choose_it()
    {
        var text = AdoptionReviewText.CoveredNameMatch(Adoption("cool.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod, "CoolMod"));

        Assert.Contains("file-hash match above (cool.zip)", text);
        Assert.Contains("only if you untick that one", text);
    }

    // One pre-check rule and one will-write rule for both dialogs (review on #363).
    [Fact]
    public void Pre_check_and_will_write_are_one_rule()
    {
        Assert.True(AdoptionReviewText.PreChecked(Adoption("a.zip", AdoptionEvidence.Md5, AdoptionReach.NamesAMod)));
        Assert.False(AdoptionReviewText.PreChecked(Adoption("a.zip", AdoptionEvidence.None, AdoptionReach.NamesAMod)));
        Assert.False(AdoptionReviewText.PreChecked(Adoption("a.zip", AdoptionEvidence.Md5, AdoptionReach.NothingToNameYet)));
        Assert.False(AdoptionReviewText.PreChecked(Adoption("a.zip", AdoptionEvidence.Md5, AdoptionReach.AlreadyNamed)));
        Assert.True(AdoptionReviewText.WillWrite(Adoption("a.zip", AdoptionEvidence.Md5, null)));
        Assert.False(AdoptionReviewText.WillWrite(Adoption("a.zip", AdoptionEvidence.Md5, AdoptionReach.AlreadyNamed)));
    }
}
