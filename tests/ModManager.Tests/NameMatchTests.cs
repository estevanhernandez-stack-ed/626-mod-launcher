using ModManager.Core;

namespace ModManager.Tests;

// Ports name-match-core.test.js — clean a filename into a search query, then score
// CurseForge hits by token overlap and refuse weak matches (no wrong-metadata attach).
public class NameMatchTests
{
    private sealed record Cand(string Name);

    [Fact]
    public void CleanModName_strips_P_suffix_loadorder_tags_and_multipliers()
    {
        Assert.Equal("BLACK MARKET SHIPYARD", NameMatch.CleanModName("ZZZ.CF.JSON.AL_BLACK_MARKET_SHIPYARD_P"));
        Assert.Equal("Cool Mod", NameMatch.CleanModName("AAA_Cool_Mod"));
        Assert.Equal("Strength Parry", NameMatch.CleanModName("StrengthParry_P"));
        Assert.Equal("More Stacks", NameMatch.CleanModName("MoreStacks_10x"));
        Assert.Equal("No Fog Of War", NameMatch.CleanModName("NoFogOfWar_v2"));
    }

    [Fact]
    public void CleanModName_drops_a_known_file_extension()
    {
        Assert.Equal("No Fog of War", NameMatch.CleanModName("No_Fog_of_War.pak"));
    }

    [Fact]
    public void CleanModName_splits_trailing_digits_glued_to_a_word_instead_of_dropping_them()
    {
        // "FasterShips10" has no case boundary between the word and the version digits — split
        // them into their own token so overlap still sees "ships", or the match dies at the gate.
        Assert.Equal("Faster Ships 10", NameMatch.CleanModName("FasterShips10.pak"));

        // The split must never drop the digits — "Fallout4" and "Fallout 3" are different games,
        // and a normalizer that eats trailing numbers would silently merge their mods.
        Assert.Equal("Fallout 4", NameMatch.CleanModName("Fallout4"));
    }

    [Fact]
    public void PickBestMatch_returns_closest_candidate_above_threshold()
    {
        var cands = new[] { new Cand("Black Market Shipyard"), new Cand("Some Other Mod") };
        Assert.Equal("Black Market Shipyard", NameMatch.PickBestMatch("black market shipyard", cands, c => c.Name)!.Name);
    }

    [Fact]
    public void PickBestMatch_tolerates_residual_noise_tokens()
    {
        var cands = new[] { new Cand("Black Market Shipyard") };
        Assert.Equal("Black Market Shipyard", NameMatch.PickBestMatch("json black market shipyard", cands, c => c.Name)!.Name);
    }

    [Fact]
    public void PickBestMatch_returns_null_when_nothing_clears_threshold()
    {
        Assert.Null(NameMatch.PickBestMatch("black market shipyard", new[] { new Cand("Totally Unrelated Thing") }, c => c.Name));
        Assert.Null(NameMatch.PickBestMatch("anything", Array.Empty<Cand>(), c => c.Name));
    }

    // ---- Real filenames from a 194-mod Cyberpunk 2077 install (live smoke, 2026-08-05) ----
    // REDengine mods wear ".archive" and are prefixed with #/!/### to force alphabetical load
    // order. Both were surviving into the upstream search query.

    [Theory]
    [InlineData("#DeceptiousBugFixes.archive", "Deceptious Bug Fixes")]
    [InlineData("###MuteMenuInventoryScanHumming.archive", "Mute Menu Inventory Scan Humming")]
    [InlineData("#ApartmentCatsCustoms_Corpo_BlackAndWhite.archive", "Apartment Cats Customs Corpo Black And White")]
    [InlineData("#GoneAway.archive", "Gone Away")]
    [InlineData("!Fix_Advert_Animations.archive", "Fix Advert Animations")]
    [InlineData("~SomeUeLoadOrderHack_P.pak", "Some Ue Load Order Hack")]
    public void Load_order_sigils_and_mod_extensions_leave_the_query(string fileName, string expected)
        => Assert.Equal(expected, NameMatch.CleanModName(fileName));

    // ArchiveXL ships a compound "Foo.archive.xl" beside "Foo.archive" — both must clean to the
    // SAME query, or the sidecar searches for a mod called "something xl".
    [Fact]
    public void Archive_xl_sidecar_cleans_to_the_same_query_as_its_archive()
    {
        var archive = NameMatch.CleanModName("#DeceptiousBugFixes.archive");

        Assert.Equal(archive, NameMatch.CleanModName("#DeceptiousBugFixes.archive.xl"));
    }

    // The junk token was not merely cosmetic: it costs real Jaccard score against the true mod.
    [Fact]
    public void A_stripped_extension_no_longer_drags_the_score_down()
    {
        var query = NameMatch.CleanModName("#GoneAway.archive");

        Assert.Equal("Gone Away", query);
        Assert.NotNull(NameMatch.PickBestMatch(query, new[] { new Cand("Gone Away") }, c => c.Name));
    }

    // Guard the widened splitter: a sigil is a separator, but it must not fuse or drop real words.
    [Fact]
    public void Widened_splitter_still_keeps_every_real_token()
        => Assert.Equal("Faster Ships", NameMatch.CleanModName("FasterShips_P.pak"));

    // ---- QueryLadder: search broad, score narrow ----
    // Live case, Cyberpunk: a filename carries every word its author used, and searching all of
    // them upstream returns ZERO hits when several appear nowhere in the mod's title. Searching
    // the leading two words returns it immediately. The ladder is about RETRIEVAL only — which
    // candidates we get to score — and says nothing about which one is correct.

    [Fact]
    public void The_ladder_broadens_toward_the_words_a_title_actually_shares()
    {
        var query = NameMatch.CleanModName("#ApartmentCatsCustoms_Dogtown_Black.archive");
        Assert.Equal("Apartment Cats Customs Dogtown Black", query);

        var ladder = NameMatch.QueryLadder(query);

        Assert.Equal(new[]
        {
            "Apartment Cats Customs Dogtown Black",  // as-is first — a precise name should win outright
            "Apartment Cats Customs",
            "Apartment Cats",                        // the rung that actually returns hits
        }, ladder);
    }

    // The broadening is retrieval only. The last rung must still score against the FULL name, and
    // that pairing has to clear the threshold — otherwise widening the search buys nothing.
    [Fact]
    public void The_full_name_still_scores_the_hit_that_the_broad_rung_retrieved()
    {
        var query = NameMatch.CleanModName("#QuietFootstepsRedux_Leather_Boots.archive");

        var hit = NameMatch.PickBestMatch(query, new[]
        {
            new Cand("Quiet Footsteps Redux"),
            new Cand("Something Entirely Else"),
        }, c => c.Name);

        Assert.Equal("Quiet Footsteps Redux", hit?.Name);
    }

    [Theory]
    [InlineData("One Two Three Four Five", new[] { "One Two Three Four Five", "One Two Three", "One Two" })]
    [InlineData("One Two Three Four", new[] { "One Two Three Four", "One Two Three", "One Two" })]
    [InlineData("One Two Three", new[] { "One Two Three", "One Two" })]
    [InlineData("One Two", new[] { "One Two" })]          // already at the floor
    [InlineData("Solo", new[] { "Solo" })]                // one word: search it, never widen below two
    public void The_ladder_never_repeats_a_rung_and_never_goes_below_two_words(string clean, string[] expected)
        => Assert.Equal(expected, NameMatch.QueryLadder(clean));

    [Fact]
    public void An_empty_name_yields_no_search_at_all()
    {
        Assert.Empty(NameMatch.QueryLadder(""));
        Assert.Empty(NameMatch.QueryLadder("   "));
        Assert.Empty(NameMatch.QueryLadder(null));
    }

    // ---- A verbose filename now resolves, via the run rule ----
    // This used to be a documented limitation: Jaccard is symmetric, so every extra word in a
    // filename counted against the mod it belongs to, and a title-coverage fallback tried for it
    // attached the wrong mod on real data and was reverted. The contiguous-run rule closes it from
    // the other direction — not by ignoring the extra words, but by requiring the title to be
    // SPELLED OUT in order, which a sibling mod cannot do.

    [Fact]
    public void A_filename_that_buries_its_title_in_extra_words_still_resolves()
    {
        // 3 shared of 7 union = 0.43 Jaccard — under threshold — but the title is an unbroken run.
        var picked = NameMatch.PickBestMatch("Quiet Footsteps Redux Leather Boots Variant Extra",
            new[] { new Cand("Quiet Footsteps Redux"), new Cand("Loud Doors") }, c => c.Name);

        Assert.Equal("Quiet Footsteps Redux", picked?.Name);
    }

    [Fact]
    public void A_filename_that_mostly_IS_its_title_still_matches()
    {
        var picked = NameMatch.PickBestMatch("Quiet Footsteps Redux Boots",
            new[] { new Cand("Quiet Footsteps Redux"), new Cand("Loud Doors") }, c => c.Name);

        Assert.Equal("Quiet Footsteps Redux", picked?.Name);
    }

    // The primary rule still governs whenever it can decide — coverage is a fallback, not a
    // replacement, so a clear Jaccard winner is never second-guessed.
    [Fact]
    public void A_clear_jaccard_winner_is_still_chosen_first()
    {
        var picked = NameMatch.PickBestMatch("Faster Ships",
            new[] { new Cand("Faster Ships"), new Cand("Faster Ships Deluxe Overhaul Edition") }, c => c.Name);

        Assert.Equal("Faster Ships", picked?.Name);
    }

    // ---- The contiguous-run rule (backlog C6) ----
    // A variant file is its parent's title PLUS a suffix. A SIBLING mod shares words but breaks the
    // run. Jaccard and coverage both discard word order, which is why both kept choosing siblings —
    // measured live, the shipped matcher attached the wrong mod to three of these six files.
    //
    // Every case below is real: six files that all ship from ONE Nexus mod page, "Apartment Cats -
    // Custom Cats", scored against that page and the five sibling mods the user does NOT have.

    private static readonly Cand[] ApartmentCats =
    {
        new("Apartment Cats - Custom Cats"),      // <- the true owner of all six files
        new("Apartment Cats - Dogtown"), new("Apartment Cats - Corpo Plaza"),
        new("Apartment Cats - The Glen"), new("Apartment Cats - Northside Motel"),
        new("Apartment Cats - Japantown"),
    };

    [Theory]
    [InlineData("#ApartmentCatsCustoms_Base")]
    [InlineData("#ApartmentCatsCustoms_Dogtown_Black")]
    [InlineData("#ApartmentCatsCustoms_Corpo_BlackAndWhite")]
    [InlineData("#ApartmentCatsCustoms_Glen_GreyTiger")]
    [InlineData("#ApartmentCatsCustoms_Japan_OrangeTiger")]
    [InlineData("#ApartmentCatsCustoms_Motel_OrangeWhite")]
    public void A_variant_file_matches_its_parent_not_a_sibling(string fileName)
    {
        var picked = NameMatch.PickBestMatch(NameMatch.CleanModName(fileName), ApartmentCats, c => c.Name);

        Assert.Equal("Apartment Cats - Custom Cats", picked?.Name);
    }

    // The answer must come from evidence, not from where the candidate happened to sit in the list.
    // An earlier attempt at this rule passed only because the true owner was first in the array;
    // reversing the candidates flipped it to a wrong mod.
    [Fact]
    public void The_answer_does_not_depend_on_candidate_order()
    {
        var query = NameMatch.CleanModName("#ApartmentCatsCustoms_Dogtown_Black");
        var reversed = ApartmentCats.Reverse().ToArray();

        Assert.Equal(NameMatch.PickBestMatch(query, ApartmentCats, c => c.Name)?.Name,
                     NameMatch.PickBestMatch(query, reversed, c => c.Name)?.Name);
    }

    // Real matches from the same live run — the rule must not cost us any of them.
    [Theory]
    [InlineData("Slaught-O-Matic Platinum Semi-Auto", "Slaught-O-Matic Platinum", "Slaught-O-Matic Chrome")]
    [InlineData("VehicleSummonTweaksDismiss", "Vehicle Summon Tweaks", "Vehicle Combat Tweaks")]
    [InlineData("LootIconsExtensionLight", "Loot Icons Extension", "Loot Filter")]
    public void A_real_variant_still_finds_its_parent(string fileName, string expected, string distractor)
    {
        var picked = NameMatch.PickBestMatch(NameMatch.CleanModName(fileName),
            new[] { new Cand(distractor), new Cand(expected) }, c => c.Name);

        Assert.Equal(expected, picked?.Name);
    }

    // Slaught-O-Matic is the case that proves both sides must be cleaned the same way: CleanModName
    // drops the short all-caps "O" from the query, so a candidate still carrying it could never
    // align. Pin the symmetry directly rather than only through the case that revealed it.
    [Fact]
    public void Both_sides_are_normalised_the_same_way_before_matching()
    {
        var picked = NameMatch.PickBestMatch(NameMatch.CleanModName("Slaught-O-Matic Platinum Semi-Auto"),
            new[] { new Cand("Slaught-O-Matic Platinum") }, c => c.Name);

        Assert.NotNull(picked);
    }

    // Sharing a prefix is not owning the file. "Apartment Cats" leads every one of these titles and
    // must never be enough on its own.
    [Fact]
    public void A_shared_prefix_alone_never_wins()
    {
        var picked = NameMatch.PickBestMatch("Apartment Cats Something Entirely Different",
            new[] { new Cand("Apartment Cats - Dogtown"), new Cand("Apartment Cats - Japantown") }, c => c.Name);

        Assert.Null(picked);
    }

    [Fact]
    public void An_unrelated_candidate_is_still_refused()
        => Assert.Null(NameMatch.PickBestMatch("Quiet Footsteps Redux",
            new[] { new Cand("Totally Unrelated Thing") }, c => c.Name));
}
