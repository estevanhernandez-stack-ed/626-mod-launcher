using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

/// <summary>C5: one mod found by the hash pass AND the name search is one review row, not two.</summary>
public class IdentifyReviewOverlapTests
{
    private static AdoptionProposal Adoption(string file, AdoptionReach? reach, params string[] keys)
        => new AdoptionProposal(new DiscoveryCandidate("downloads/" + file, file, DiscoveryKind.Archive),
                AdoptionEvidence.Md5, 1, "Cool Mod", "someone", null)
            { Reach = reach, WriteKeys = keys };

    [Fact]
    public void An_adoption_that_will_write_covers_its_keys()
    {
        var a = Adoption("cool.zip", AdoptionReach.NamesAMod, "CoolMod", "CoolMod_Extra");

        var covered = IdentifyReviewOverlap.CoveredKeys(new[] { a });

        Assert.Same(a, covered["coolmod"]);   // case-insensitive, as mod keys are everywhere
        Assert.Same(a, covered["CoolMod_Extra"]);
    }

    // Writes nothing, so the name match is still the only change and keeps its own row.
    [Theory]
    [InlineData(AdoptionReach.NothingToNameYet)]
    [InlineData(AdoptionReach.AlreadyNamed)]
    public void An_adoption_that_writes_nothing_covers_nothing(AdoptionReach reach)
        => Assert.Empty(IdentifyReviewOverlap.CoveredKeys(new[] { Adoption("cool.zip", reach, "CoolMod") }));

    // Unresolved reach: the dialog says nothing it cannot back, so nothing is folded either.
    [Fact]
    public void An_unresolved_adoption_covers_nothing()
        => Assert.Empty(IdentifyReviewOverlap.CoveredKeys(new[]
        {
            new AdoptionProposal(new DiscoveryCandidate("d/x.zip", "x.zip", DiscoveryKind.Archive), AdoptionEvidence.Md5, 1, "X", null, null),
        }));

    [Fact]
    public void The_first_adoption_to_claim_a_key_keeps_it()
    {
        var first = Adoption("a.zip", AdoptionReach.NamesAMod, "Shared");
        var second = Adoption("b.zip", AdoptionReach.NamesAMod, "Shared");

        Assert.Same(first, IdentifyReviewOverlap.CoveredKeys(new[] { first, second })["Shared"]);
    }

    [Fact]
    public void The_folded_row_names_the_second_piece_of_evidence_once()
    {
        Assert.Equal(" The name search found it too, as Cool Mod.", AdoptionReviewText.AlsoNamed(new[] { "Cool Mod", "cool mod" }));
        Assert.Equal("", AdoptionReviewText.AlsoNamed(Array.Empty<string>()));
    }
}
