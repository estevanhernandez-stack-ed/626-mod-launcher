using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

/// <summary>
/// Wave 3 / A14. Adding Monster Hunter Wilds raised the adoption dialog over thirteen Nexus downloads
/// sitting in Fluffy's library, with no <c>natives/</c> directory anywhere in the game folder — every
/// one of them downloaded, none of them installed. The dialog was headed "Mods already installed" and
/// offered a button reading "Adopt 13 mods".
///
/// <para>Adoption attaches metadata to mods that ARE installed. Nothing there was, so the button would
/// have written nothing — and the only honest outcome the code could manage was a status line saying
/// so afterwards. Cancelling did not keep those mods off the list; accepting would not have put them
/// on it.</para>
///
/// <para>The apply already told these three cases apart, to pick which "nothing happened" line to
/// print. It just knew too late. This is the same rule, asked earlier.</para>
/// </summary>
public class AdoptionReachTests
{
    private static Dictionary<string, ModMeta> Existing(params (string Key, ModMeta Meta)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Meta, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void An_archive_that_resolves_to_no_mod_key_has_nothing_to_name()
    {
        // The Wilds case. The archive's CONTENTS hold nothing this game would list as a mod, so there
        // is no row for the metadata to land on.
        Assert.Equal(AdoptionReach.NothingToNameYet, AdoptionReachRules.For(Array.Empty<string>(), Existing()));
        Assert.Equal(AdoptionReach.NothingToNameYet, AdoptionReachRules.For(null, Existing()));
    }

    [Fact]
    public void A_key_with_no_metadata_yet_is_exactly_what_adoption_is_for()
        => Assert.Equal(AdoptionReach.NamesAMod, AdoptionReachRules.For(new[] { "FasterShips10" }, Existing()));

    [Fact]
    public void A_key_that_is_already_identified_is_not_a_change()
    {
        var existing = Existing(("FasterShips10", new ModMeta { NexusModId = 285 }));

        Assert.Equal(AdoptionReach.AlreadyNamed, AdoptionReachRules.For(new[] { "FasterShips10" }, existing));
    }

    [Fact]
    public void One_unwritten_key_among_several_still_makes_it_a_change()
    {
        // An archive can install several mods. If any one of them would gain a name, the row is worth
        // approving — reporting "already named" because the first key was would lose the other two.
        var existing = Existing(("a", new ModMeta { NexusModId = 1 }));

        Assert.Equal(AdoptionReach.NamesAMod, AdoptionReachRules.For(new[] { "a", "b" }, existing));
    }

    [Theory]
    [InlineData(true, null, null)]      // manually pinned
    [InlineData(false, 285, null)]      // carries a Nexus id
    [InlineData(false, null, "md5")]    // carries source confidence
    public void Every_form_of_existing_identity_counts_as_already_named(bool manual, int? nexusId, string? confidence)
    {
        var existing = Existing(("k", new ModMeta { IsManual = manual, NexusModId = nexusId, SourceConfidence = confidence }));

        Assert.True(AdoptionReachRules.IsAlreadyIdentified(existing, "k"));
        Assert.Equal(AdoptionReach.AlreadyNamed, AdoptionReachRules.For(new[] { "k" }, existing));
    }

    [Fact]
    public void A_bare_metadata_entry_is_not_an_identity()
    {
        // A row that exists but has never been named is precisely what adoption should name. Treating
        // "we have a metadata entry" as "already identified" would refuse the whole feature.
        var existing = Existing(("k", new ModMeta { Title = "Something" }));

        Assert.False(AdoptionReachRules.IsAlreadyIdentified(existing, "k"));
        Assert.Equal(AdoptionReach.NamesAMod, AdoptionReachRules.For(new[] { "k" }, existing));
    }

    [Fact]
    public void The_count_a_button_shows_is_what_will_be_written()
    {
        // "Adopt 13 mods" over thirteen inert downloads is the whole entry, in badge form.
        var thirteenInert = Enumerable.Repeat(AdoptionReach.NothingToNameYet, 13);

        Assert.Equal(0, AdoptionReachRules.CountThatWillWrite(thirteenInert));
        Assert.Equal(1, AdoptionReachRules.CountThatWillWrite(thirteenInert.Append(AdoptionReach.NamesAMod)));
    }

    [Fact]
    public void A_proposal_carries_no_reach_until_something_resolves_it()
    {
        // Null means "not worked out", never "fine". Resolving needs disk I/O, and Core does not decide
        // when that happens.
        var proposal = AdoptionProposal.Unidentified(new DiscoveryCandidate("mods/a.zip", "a.zip", DiscoveryKind.Archive));

        Assert.Null(proposal.Reach);
        Assert.Equal(AdoptionReach.NothingToNameYet, (proposal with { Reach = AdoptionReach.NothingToNameYet }).Reach);
    }
}

/// <summary>
/// The other half of A14's sweep problem: the run offered
/// <c>Vortex Extension Update - Monster Hunter Wilds Vortex Extension v0.1.4.zip</c> as a mod. Sitting
/// in the same folder is not enough to make something a mod, and this one's filename says what it is.
/// </summary>
public class ManagerExtensionTests
{
    private static IReadOnlyList<DiscoveryCandidate> Sweep(params string[] paths)
        => DiscoverySweep.Classify(
            paths.Select(p => new SweptFile(p, 1024)).ToList(),
            new DiscoverySweepOptions(
                ModPaths: new[] { new DiscoverySweepModPath("mods", false) },
                EngineExtensions: new[] { "pak" },
                SkipFolders: Array.Empty<string>()));

    [Fact]
    public void The_real_false_positive_is_no_longer_offered()
    {
        var found = Sweep("downloads/Vortex Extension Update - Monster Hunter Wilds Vortex Extension v0.1.4.zip");

        Assert.Empty(found);
    }

    [Fact]
    public void An_ordinary_archive_in_the_same_folder_is_untouched()
    {
        var found = Sweep("downloads/CatLib-65-1-2-3-1739000000.zip");

        Assert.Equal(DiscoveryKind.Archive, Assert.Single(found).Kind);
    }

    [Theory]
    [InlineData("Vortex Armour Pack.zip")]          // a manager's name in a mod's name
    [InlineData("Extension Cord Mod.zip")]          // the word alone
    [InlineData("MO2 Preset.zip")]
    public void The_refusal_is_narrow_enough_not_to_eat_real_mods(string fileName)
    {
        // It takes the manager's name AND the word "extension" together. A refusal that over-reaches
        // hides a real mod, which is worse than a row the user has to untick.
        Assert.Single(Sweep("downloads/" + fileName));
    }
}
