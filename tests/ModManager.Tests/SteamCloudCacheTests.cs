using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Reading what Steam Cloud holds for a game. See
/// <c>docs/superpowers/specs/2026-08-20-saves-beyond-palworld-design.md</c>.
///
/// <para>The fixture is the real shape of a <c>remotecache.vdf</c>, reduced. It cost us twice on the
/// same game in one week: a world deleted from disk came back the next morning because Steam still
/// had it, and the only way to confirm the eventual in-game delete had worked was reading this file
/// by hand.</para>
/// </summary>
public class SteamCloudCacheTests
{
    private const string Vdf = """
"1623730"
{
	"ChangeNumber"		"96"
	"OSType"		"0"
	"Pal/Saved/SaveGames/765/GlobalPalStorage.sav"
	{
		"root"		"3"
		"size"		"8200"
		"localtime"		"1785520872"
		"sha"		"e9445dd5b0ea1b5de0da501ca77d8669dbe522f9"
		"syncstate"		"1"
		"persiststate"		"0"
	}
	"Pal/Saved/SaveGames/765/AAA111/Level.sav"
	{
		"root"		"3"
		"size"		"2200000"
		"sha"		"aaa"
		"syncstate"		"1"
	}
	"Pal/Saved/SaveGames/765/AAA111/Players/0001.sav"
	{
		"root"		"3"
		"size"		"21000"
		"sha"		"bbb"
		"syncstate"		"1"
	}
	"Pal/Saved/SaveGames/765/BBB222/LocalData.sav"
	{
		"root"		"3"
		"size"		"29000"
		"sha"		"ccc"
		"syncstate"		"1"
	}
}
""";

    [Fact]
    public void The_top_level_scalars_are_not_files()
    {
        // ChangeNumber and OSType are quoted keys with quoted values, exactly like a path line. Only
        // the brace block after a path distinguishes them, and reading them as zero-byte files would
        // inflate every count the panel shows.
        var files = SteamCloudCache.Parse(Vdf);

        Assert.Equal(4, files.Count);
        Assert.DoesNotContain(files, f => f.Path is "ChangeNumber" or "OSType");
    }

    [Fact]
    public void Each_entry_carries_its_path_size_and_hash()
    {
        var f = Assert.Single(SteamCloudCache.Parse(Vdf), x => x.Path.EndsWith("GlobalPalStorage.sav"));

        Assert.Equal("Pal/Saved/SaveGames/765/GlobalPalStorage.sav", f.Path);
        Assert.Equal(8200, f.Bytes);
        Assert.Equal("e9445dd5b0ea1b5de0da501ca77d8669dbe522f9", f.Sha1);
        Assert.Equal(1, f.SyncState);
    }

    [Fact]
    public void One_save_unit_is_matched_by_its_folder_name_not_by_a_path_prefix()
    {
        // The cache stores paths relative to a Steam root constant we do not decode, so a caller's
        // absolute local path and Steam's relative one share only their tail. The folder name is the
        // part both agree on.
        var files = SteamCloudCache.Parse(Vdf);

        var a = CloudCoverage.For(files, "AAA111");
        Assert.True(a.IsTracked);
        Assert.Equal(2, a.FileCount);
        Assert.Equal(2_221_000, a.Bytes);

        var b = CloudCoverage.For(files, "BBB222");
        Assert.Equal(1, b.FileCount);
        Assert.Equal(29_000, b.Bytes);
    }

    [Fact]
    public void A_segment_that_matches_nothing_is_not_tracked()
    {
        // The answer that decides whether a delete will stick. It has to be able to say no.
        var coverage = CloudCoverage.For(SteamCloudCache.Parse(Vdf), "CCC333");

        Assert.False(coverage.IsTracked);
        Assert.Equal(0, coverage.Bytes);
    }

    [Fact]
    public void A_segment_matches_a_whole_folder_name_and_never_half_of_one()
    {
        // "AAA" must not match "AAA111" - a partial match would report a sibling world's files as
        // this one's, and the number it feeds is a delete-safety answer.
        Assert.False(CloudCoverage.For(SteamCloudCache.Parse(Vdf), "AAA").IsTracked);
        Assert.False(CloudCoverage.For(SteamCloudCache.Parse(Vdf), "Level").IsTracked);
    }

    [Fact]
    public void No_segment_means_the_whole_game()
    {
        var all = CloudCoverage.For(SteamCloudCache.Parse(Vdf), null);

        Assert.Equal(4, all.FileCount);
        Assert.Equal(2_258_200, all.Bytes);
    }

    [Fact]
    public void A_windows_shaped_segment_still_matches_steams_forward_slashes()
        => Assert.True(CloudCoverage.For(SteamCloudCache.Parse(Vdf), @"\AAA111\").IsTracked);

    [Fact]
    public void Missing_empty_and_malformed_input_is_no_knowledge_and_never_a_throw()
    {
        // An unreadable cache means we do not know, and every caller degrades to the behaviour it had
        // before cloud awareness existed. It must never take a save panel down.
        Assert.Empty(SteamCloudCache.Parse(null));
        Assert.Empty(SteamCloudCache.Parse(""));
        Assert.Empty(SteamCloudCache.Parse("{ not vdf at all"));
        Assert.Empty(SteamCloudCache.Read(Path.Combine(TestSupport.TempDir("cloud-none-"), "nope.vdf")));

        Assert.False(CloudCoverage.For(null).IsTracked);
        Assert.False(CloudCoverage.For(null, "AAA111").IsTracked);
    }

    [Fact]
    public void A_block_with_no_size_is_not_a_file_record()
    {
        // Defensive: other nested structures may appear in future Steam versions, and a
        // zero-byte phantom entry would read as "tracked" when nothing is.
        var odd = "\"1\"\n{\n\t\"SomethingElse\"\n\t{\n\t\t\"root\"\t\t\"3\"\n\t}\n}";
        Assert.Empty(SteamCloudCache.Parse(odd));
    }

    [Fact]
    public void The_cache_path_is_composed_the_way_steam_lays_it_out()
        => Assert.Equal(
            Path.Combine("C:/Steam", "userdata", "8945417", "1623730", "remotecache.vdf"),
            SteamCloudCache.PathFor("C:/Steam", "8945417", "1623730"));
}
