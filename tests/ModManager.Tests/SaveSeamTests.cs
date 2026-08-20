using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// The line between the place and the person, curated per game.
///
/// <para>Both worked examples are verified against real installs, and they look different on purpose:
/// Palworld's layout is <c>worlds</c>, so its patterns start inside one world folder; Windrose's is
/// not, so its patterns start at the save folder. Getting that wrong in either direction either shares
/// somebody's character or shares nothing.</para>
/// </summary>
public class SaveSeamTests
{
    // Verified on the real install: a Palworld world folder holds Level.sav, LevelMeta.sav,
    // WorldOption.sav, LocalData.sav, Players/ and the game's own backup/.
    private static readonly string[] Palworld = { "Players/**", "LocalData.sav" };

    // Verified on the real install: Accounts/, Players/ and Worlds/ repeat under RocksDB/,
    // RocksDB_v2/, a nested migration tree and a backups tree.
    private static readonly string[] Windrose =
        { "**/Accounts/**", "**/Players/**", "**/AccountDescription.json" };

    [Fact]
    public void Palworlds_character_is_the_players_folder_and_the_local_data()
    {
        Assert.True(SaveSeam.IsPlayerPath("Players/00000000000000000000000000000001.sav", Palworld));
        Assert.True(SaveSeam.IsPlayerPath("LocalData.sav", Palworld));

        // The world itself stays.
        Assert.False(SaveSeam.IsPlayerPath("Level.sav", Palworld));
        Assert.False(SaveSeam.IsPlayerPath("LevelMeta.sav", Palworld));
        Assert.False(SaveSeam.IsPlayerPath("WorldOption.sav", Palworld));
    }

    [Fact]
    public void Windrose_matches_at_any_depth_because_its_folders_repeat()
    {
        // The reason this is globs and not a fixed vocabulary. Every one of these is a real path
        // shape from the install.
        Assert.True(SaveSeam.IsPlayerPath("0.10.0/Players/1559DAC4/000001.sst", Windrose));
        Assert.True(SaveSeam.IsPlayerPath("0.10.0/Accounts/0A1C0FEA/000076.sst", Windrose));
        Assert.True(SaveSeam.IsPlayerPath("steam-user/RocksDB/AccountDescription.json", Windrose));
        Assert.True(SaveSeam.IsPlayerPath("steam-user/RocksDB_v2/0.10.0/Players/x/1.sst", Windrose));

        // Worlds are the place, at every depth.
        Assert.False(SaveSeam.IsPlayerPath("0.10.0/Worlds/0D4FA581/000123.sst", Windrose));
        Assert.False(SaveSeam.IsPlayerPath("steam-user/RocksDB_v2/0.10.0/Worlds/x/1.sst", Windrose));
    }

    [Fact]
    public void A_leading_globstar_also_matches_nothing_so_a_top_level_folder_counts()
    {
        // "**/Players/**" must find Players/ at the root too, or a game that nests on one machine and
        // not another would leak a character on the flat one.
        Assert.True(SaveSeam.IsPlayerPath("Players/a.sst", Windrose));
        Assert.True(SaveSeam.IsPlayerPath("AccountDescription.json", Windrose));
    }

    [Fact]
    public void A_single_star_does_not_cross_a_directory()
    {
        var patterns = new[] { "Players/*.sav" };

        Assert.True(SaveSeam.IsPlayerPath("Players/p1.sav", patterns));
        Assert.False(SaveSeam.IsPlayerPath("Players/deeper/p1.sav", patterns));
    }

    [Fact]
    public void Pattern_punctuation_is_literal_and_never_a_regex()
    {
        // A curated pattern is data arriving from a signed feed. If a dot or a plus were treated as a
        // metacharacter, a feed entry could silently widen what counts as "yours" - and the direction
        // that fails is the one that keeps somebody's character OUT of a share, or worse, matches
        // files it should not.
        var patterns = new[] { "Local.Data.sav" };

        Assert.True(SaveSeam.IsPlayerPath("Local.Data.sav", patterns));
        Assert.False(SaveSeam.IsPlayerPath("LocalXData.sav", patterns));
    }

    [Fact]
    public void Windows_separators_from_a_caller_still_match()
        => Assert.True(SaveSeam.IsPlayerPath(@"Players\p1.sav", Palworld));

    [Fact]
    public void No_curated_seam_means_nothing_is_the_players_and_therefore_nothing_can_be_shared()
    {
        // The load-bearing default. Absent must never read as "there is no character data" - it means
        // nobody has looked, and the caller must not offer to share a world it cannot cut.
        Assert.False(SaveSeam.IsPlayerPath("Players/p1.sav", null));
        Assert.False(SaveSeam.IsPlayerPath("Players/p1.sav", Array.Empty<string>()));
        Assert.False(SaveSeam.IsPlayerPath("", Palworld));
    }

    [Fact]
    public void Split_puts_a_real_palworld_world_on_the_right_sides()
    {
        var files = new[]
        {
            "Level.sav", "LevelMeta.sav", "WorldOption.sav",
            "LocalData.sav",
            "Players/00000000000000000000000000000001.sav",
            "backup/world/2026.08.16-22.52.36/Level.sav",
        };

        var (world, player) = SaveSeam.Split(files, Palworld);

        Assert.Equal(new[] { "LocalData.sav", "Players/00000000000000000000000000000001.sav" }, player);
        Assert.Contains("Level.sav", world);
        Assert.Contains("backup/world/2026.08.16-22.52.36/Level.sav", world);
        Assert.Equal(files.Length, world.Count + player.Count);   // nothing is dropped or counted twice
    }
}
