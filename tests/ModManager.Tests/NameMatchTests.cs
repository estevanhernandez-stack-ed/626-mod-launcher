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
    // Live case, Cyberpunk: "#ApartmentCatsCustoms_Dogtown_Black" searched as its full cleaned name
    // returns ZERO hits, because neither "Customs" nor "Black" appears in the real title
    // "Apartment Cats - Dogtown". Searching "apartment cat" returns it immediately.

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
            "Apartment Cats",                        // the rung that actually finds the mod
        }, ladder);
    }

    // The broadening is retrieval only. The last rung must still score against the FULL name, and
    // that pairing has to clear the threshold — otherwise widening the search buys nothing.
    [Fact]
    public void The_full_name_still_scores_the_hit_that_the_broad_rung_retrieved()
    {
        var query = NameMatch.CleanModName("#ApartmentCatsCustoms_Dogtown_Black.archive");

        var hit = NameMatch.PickBestMatch(query, new[]
        {
            new Cand("Apartment Cats - Dogtown"),
            new Cand("Giant Cat Plush for V's Apartment"),
        }, c => c.Name);

        Assert.Equal("Apartment Cats - Dogtown", hit?.Name);
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
}
