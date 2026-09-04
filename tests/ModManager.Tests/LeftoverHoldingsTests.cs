using ModManager.Core;

namespace ModManager.Tests;

public class LeftoverHoldingsTests
{
    [Fact]
    public void A_folder_matching_a_registered_game_is_not_an_orphan()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose" }));

    [Fact]
    public void A_folder_matching_no_registered_game_is_an_orphan()
        => Assert.Equal(new[] { "demonologist" },
                        LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose", "demonologist" }));

    // Ids are slugs and the folder is named from one, but a case difference between the registry and
    // the disk must never make a live game look abandoned — that is the one mistake here that offers
    // to delete files still in use.
    [Fact]
    public void Case_does_not_make_a_registered_game_look_orphaned()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "Windrose" }, new[] { "windrose" }));

    [Fact]
    public void Find_describes_an_orphan_and_leaves_a_registered_game_out()
    {
        var lib = TestSupport.TempDir("leftovers-");
        var gameRoot = Path.Combine(lib, "steamapps", "common", "Windrose");
        Directory.CreateDirectory(gameRoot);

        var holdings = Path.Combine(lib, "_626mods");
        Directory.CreateDirectory(Path.Combine(holdings, "windrose"));
        var orphan = Path.Combine(holdings, "demonologist");
        Directory.CreateDirectory(Path.Combine(orphan, "SomeMod"));
        File.WriteAllText(Path.Combine(orphan, "SomeMod", "a.pak"), "0123456789");
        File.WriteAllText(Path.Combine(orphan, "profiles.json"), "{}");

        var found = LeftoverHoldings.Find(new[]
        {
            new GameEntry { Id = "windrose", GameName = "Windrose", GameRoot = gameRoot },
        });

        var one = Assert.Single(found);
        Assert.Equal("demonologist", one.FolderName);
        Assert.Equal(2, one.FileCount);                       // counts the whole tree, not just the top
        Assert.Equal(12, one.Bytes);
        // It is NOT only mods — the folder holds profiles and metadata too, and the UI has to be able
        // to say so rather than call it all "mods".
        Assert.Contains("SomeMod", one.TopLevelNames);
        Assert.Contains("profiles.json", one.TopLevelNames);
    }

    // The accepted blind spot, pinned so it is a decision rather than a surprise: roots come from the
    // registered games. A library whose every game has been removed is a library nothing points at.
    // The alternative is walking drives for a folder name, which is how a tool offers to delete a
    // directory it merely recognised.
    [Fact]
    public void A_root_no_registered_game_points_at_is_never_scanned()
    {
        var lib = TestSupport.TempDir("leftovers-unseen-");
        Directory.CreateDirectory(Path.Combine(lib, "_626mods", "demonologist"));

        Assert.Empty(LeftoverHoldings.Find(Array.Empty<GameEntry>()));
    }
}
