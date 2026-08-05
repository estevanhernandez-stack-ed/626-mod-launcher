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
}
