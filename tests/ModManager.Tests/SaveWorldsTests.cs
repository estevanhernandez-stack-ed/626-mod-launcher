using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Palworld keeps a folder per world, and 72 of its 74 save files sit a level below the folder the
/// panel reads. <c>ListSaveFiles</c> reads one directory level, so it found only
/// <c>GlobalPalStorage.sav</c> — shared storage, not anybody's save. Listing that single file would
/// have implied it WAS the save, which is worse than listing nothing.
///
/// <para>Measured on the real install before any of this was designed; see
/// <c>docs/2026-08-19-saves-are-three-shapes.md</c>.</para>
/// </summary>
public class SaveWorldsTests
{
    private static string World(string root, string name, params (string Rel, string Body)[] files)
    {
        var dir = Path.Combine(root, name);
        foreach (var (rel, body) in files)
        {
            var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, body);
        }
        return dir;
    }

    [Fact]
    public void Each_subfolder_with_files_in_it_is_a_world()
    {
        var root = TestSupport.TempDir("worlds-");
        World(root, "905979404BC61E4EF56946B155337D7F",
            ("Level.sav", "x"), ("LevelMeta.sav", "y"), ("Players/00000001.sav", "z"));
        World(root, "F8238F784BB514DAB37B5A99699695FD", ("Level.sav", "abc"));
        File.WriteAllText(Path.Combine(root, "GlobalPalStorage.sav"), "shared");

        var worlds = SaveManager.ListWorlds(root);

        Assert.Equal(2, worlds.Count);
        // The loose top-level file is not a world and must never become one.
        Assert.DoesNotContain(worlds, w => w.Name.EndsWith(".sav"));
    }

    [Fact]
    public void A_world_reports_what_it_costs_and_how_many_files_it_holds()
    {
        var root = TestSupport.TempDir("worlds-size-");
        World(root, "w1", ("Level.sav", "1234567890"), ("Players/p.sav", "12345"));

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal(15, w.Bytes);
        Assert.Equal(2, w.FileCount);
    }

    [Fact]
    public void Nested_files_count_including_the_games_own_backup_folder()
    {
        // Palworld writes a backup/ inside each world. Excluding it would make the size disagree with
        // Explorer, and the number is meant to answer "what does this world cost on disk".
        var root = TestSupport.TempDir("worlds-nested-");
        World(root, "w1", ("Level.sav", "aa"), ("backup/world/old.sav", "bbbb"));

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal(6, w.Bytes);
        Assert.Equal(2, w.FileCount);
    }

    [Fact]
    public void An_empty_subfolder_is_a_leftover_and_not_a_world()
    {
        var root = TestSupport.TempDir("worlds-empty-");
        Directory.CreateDirectory(Path.Combine(root, "abandoned"));
        World(root, "real", ("Level.sav", "x"));

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal("real", w.Name);
    }

    [Fact]
    public void Most_recently_played_comes_first()
    {
        // With GUID folder names the date is the only thing telling two worlds apart until they can be
        // labelled, so the ordering is doing real work rather than being tidy.
        var root = TestSupport.TempDir("worlds-order-");
        var older = World(root, "older", ("Level.sav", "x"));
        var newer = World(root, "newer", ("Level.sav", "x"));
        File.SetLastWriteTimeUtc(Path.Combine(older, "Level.sav"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(newer, "Level.sav"), new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var worlds = SaveManager.ListWorlds(root);

        Assert.Equal("newer", worlds[0].Name);
        Assert.Equal("older", worlds[1].Name);
    }

    [Fact]
    public void A_missing_folder_is_an_empty_list_and_never_a_throw()
        => Assert.Empty(SaveManager.ListWorlds(Path.Combine(TestSupport.TempDir("worlds-gone-"), "nope")));

    [Fact]
    public void Palworld_declares_the_worlds_layout_and_other_games_do_not()
    {
        // Per-GAME, not per-engine. Palworld and Windrose are both ue-pak and arrange saves entirely
        // differently — worlds in folders versus a RocksDB database — so keying this on engine would
        // have been wrong for one of them whichever way it went.
        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve("ue-pak", "1623730").Layout);
        Assert.Equal(SaveLayout.TypedFiles, GameSaveTypesCatalog.Resolve("ue-pak", "999999").Layout);
        Assert.Equal(SaveLayout.TypedFiles, GameSaveTypesCatalog.Resolve("fromsoft", "1245620").Layout);
    }
}

/// <summary>
/// Telling a hosted world from a joined one, read off the filesystem rather than out of the save
/// format. Palworld keeps <c>Level.sav</c> for a world you host and only <c>LocalData.sav</c> for one
/// you joined — which is why, on the machine this was measured on, one world is 25 MB and the other
/// is 319 KB.
/// </summary>
public class SaveWorldRoleTests
{
    private static string Make(string root, string name, params string[] rel)
    {
        var dir = Path.Combine(root, name);
        foreach (var r in rel)
        {
            var p = Path.Combine(dir, r.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
        }
        return dir;
    }

    [Fact]
    public void A_world_holding_Level_sav_is_one_you_host()
    {
        var root = TestSupport.TempDir("role-hosted-");
        Make(root, "w", "Level.sav", "LevelMeta.sav", "Players/0001.sav");

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal(SaveManager.WorldRole.Hosted, w.Role);
        Assert.Equal(1, w.PlayerCount);
    }

    [Fact]
    public void A_world_with_only_local_data_is_one_you_joined()
    {
        // The real second world on the measured machine: LocalData.sav and a backup folder, no
        // Level.sav, no Players. The world lives on the host's machine.
        var root = TestSupport.TempDir("role-joined-");
        Make(root, "w", "LocalData.sav", "backup/world/old.sav");

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal(SaveManager.WorldRole.Joined, w.Role);
        Assert.Contains("Someone else's world", w.RoleLabel);
        // And the thing a player would otherwise report as a bug: the game does not list this one.
        Assert.Contains("does not list this one", w.RoleCaveat);
    }

    [Fact]
    public void More_than_one_player_file_means_others_have_played_here()
    {
        var root = TestSupport.TempDir("role-mp-");
        Make(root, "w", "Level.sav", "Players/0001.sav", "Players/0002.sav", "Players/0003.sav");

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Contains("You started this", w.RoleLabel);
        Assert.Contains("3 characters", w.RoleLabel);
        Assert.Empty(w.RoleCaveat);
    }

    [Fact]
    public void One_player_is_solo_SO_FAR_and_never_claimed_as_single_player()
    {
        // Nothing on disk separates a world nobody has joined YET from one that never will. Saying
        // "single-player" would be inventing a fact to get a tidier label, and the label would be
        // wrong the first time a friend drops in.
        var root = TestSupport.TempDir("role-solo-");
        Make(root, "w", "Level.sav", "Players/0001.sav");

        var w = Assert.Single(SaveManager.ListWorlds(root));

        // Never "solo" and never "single-player". Palworld marks both of the real worlds
        // Multiplayer: ON while only one character has played in them, so a headcount of one says
        // nothing about the setting - and claiming otherwise is how the label got this wrong before.
        Assert.Contains("You started this", w.RoleLabel);
        Assert.Contains("1 character", w.RoleLabel);
        Assert.DoesNotContain("solo", w.RoleLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("single-player", w.RoleLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("multiplayer", w.RoleLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_hosted_world_nobody_has_played_yet_still_reads_as_solo()
    {
        var root = TestSupport.TempDir("role-noplayers-");
        Make(root, "w", "Level.sav", "LevelMeta.sav");

        var w = Assert.Single(SaveManager.ListWorlds(root));

        Assert.Equal(0, w.PlayerCount);
        Assert.Contains("You started this", w.RoleLabel);
    }
}
