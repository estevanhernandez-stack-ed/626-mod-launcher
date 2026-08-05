using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The index turns "which of THIS game's mods is this file?" into a local lookup. Scoped to one
// game's domain, so the haystack is small — but a wrong hit still must not outrank a right one.
public class ModNameIndexTests
{
    private static ModNameIndex Index(params ModNameIndexEntry[] entries)
        => ModNameIndex.Merge(ModNameIndex.Empty, entries);

    private static ModNameIndexEntry Entry(int id, string name, int endorsements = 10)
        => new(id, name, "Author", endorsements);

    [Fact]
    public void Matches_a_file_name_to_a_known_mod()
    {
        var index = Index(Entry(1, "Faster Ships"), Entry(2, "More Stacks"));

        var hit = index.Match("FasterShips10.pak");

        Assert.NotNull(hit);
        Assert.Equal(1, hit!.ModId);
    }

    [Fact]
    public void Version_suffixes_do_not_defeat_the_match()
    {
        var index = Index(Entry(1, "Faster Ships"));

        Assert.NotNull(index.Match("Faster_Ships_v1.2.3.zip"));
    }

    [Fact]
    public void An_unrelated_name_matches_nothing()
    {
        var index = Index(Entry(1, "Faster Ships"), Entry(2, "More Stacks"));

        Assert.Null(index.Match("SomeRandomEngineFile.pak"));
    }

    [Fact]
    public void Merge_deduplicates_by_mod_id_and_keeps_the_newer_entry()
    {
        var first = Index(Entry(1, "Faster Ships", endorsements: 10));

        var merged = ModNameIndex.Merge(first, new[] { Entry(1, "Faster Ships Redux", endorsements: 99) });

        var only = Assert.Single(merged.Entries);
        Assert.Equal("Faster Ships Redux", only.Name);
        Assert.Equal(99, only.Endorsements);
    }

    [Fact]
    public void Merge_caps_the_index_dropping_lowest_endorsement_first()
    {
        var incoming = new[] { Entry(1, "Keep Me", 500), Entry(2, "Drop Me", 1), Entry(3, "Keep Me Too", 400) };

        var merged = ModNameIndex.Merge(ModNameIndex.Empty, incoming, cap: 2);

        Assert.Equal(2, merged.Entries.Count);
        Assert.DoesNotContain(merged.Entries, e => e.Name == "Drop Me");
    }

    [Fact]
    public void Empty_index_matches_nothing_and_never_throws()
    {
        Assert.Null(ModNameIndex.Empty.Match("Anything.pak"));
    }

    // Final-review IMPORTANT 4: PickBestMatch's 0.5 Jaccard threshold lets a single-token query
    // match ANY two-token candidate sharing that one token (1/2 = 0.5, clears the bar). Safe under
    // LooseIdentify (loose-root non-loader rows only); unsafe here, where discovery feeds this
    // arbitrary filenames including vanilla game files. The concrete failure this pins: a Bethesda
    // vanilla master file ("Skyrim.esm", candidate.FileName the caller passes in — cleans to the
    // single token "Skyrim") must never pre-check against an unrelated two-token mod ("Skyrim
    // Together") just because they share that one token.
    [Fact]
    public void Single_token_query_does_not_fuzzy_match_a_two_token_candidate_sharing_that_token()
    {
        var index = Index(Entry(1, "Skyrim Together"));

        Assert.Null(index.Match("Skyrim.esm"));
    }

    [Fact]
    public void Single_token_query_still_matches_an_exact_single_token_entry()
    {
        var index = Index(Entry(1, "Skyrim Together"), Entry(2, "Skyrim"));

        var hit = index.Match("Skyrim.esm");

        Assert.NotNull(hit);
        Assert.Equal(2, hit!.ModId);
    }

    [Fact]
    public void Two_token_query_still_uses_the_fuzzy_threshold_unaffected_by_the_short_query_rule()
    {
        // Unchanged behavior for the common two-token-plus case (this is the same fixture
        // Matches_a_file_name_to_a_known_mod above already exercises via PickBestMatch).
        var index = Index(Entry(1, "Faster Ships"), Entry(2, "More Stacks"));

        var hit = index.Match("FasterShips10.pak");

        Assert.NotNull(hit);
        Assert.Equal(1, hit!.ModId);
    }
}
