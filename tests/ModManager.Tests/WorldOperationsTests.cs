using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Backing up and restoring ONE world. See
/// <c>docs/superpowers/specs/2026-08-19-world-level-saves-design.md</c>.
///
/// <para>The load-bearing property is separation. <c>Backup</c> is
/// <c>ZipFile.CreateFromDirectory</c> and will zip a single world with no changes at all — so a world
/// zip could land in the whole-folder snapshot list, look identical to one, and be restored through
/// the whole-folder path, which deletes every other world and extracts that one loose at the top
/// level. These tests pin the separation that makes that impossible.</para>
/// </summary>
public class WorldOperationsTests
{
    private static string Saves(string prefix, params string[] worlds)
    {
        var root = TestSupport.TempDir(prefix);
        foreach (var w in worlds)
        {
            Directory.CreateDirectory(Path.Combine(root, w, "Players"));
            File.WriteAllText(Path.Combine(root, w, "Level.sav"), $"level-of-{w}");
            File.WriteAllText(Path.Combine(root, w, "Players", "p1.sav"), $"player-of-{w}");
        }
        File.WriteAllText(Path.Combine(root, "GlobalPalStorage.sav"), "shared");
        return root;
    }

    [Fact]
    public void A_world_snapshot_is_invisible_to_the_whole_folder_list()
    {
        // The whole point. ListSnapshots reads one directory and does not recurse, so putting world
        // snapshots in a subdirectory means nobody can restore one through the whole-folder path by
        // accident — enforced by existing code rather than by remembering a naming rule.
        var save = Saves("wops-sep-", "w1");
        var snaps = TestSupport.TempDir("wops-sep-snaps-");

        SaveManager.BackupWorld(save, "w1", snaps, "mine");

        Assert.Empty(SaveManager.ListSnapshots(snaps));
        Assert.Single(SaveManager.ListWorldSnapshots(snaps, "w1"));
    }

    [Fact]
    public void Restoring_one_world_leaves_every_other_world_byte_identical()
    {
        var save = Saves("wops-iso-", "w1", "w2");
        var snaps = TestSupport.TempDir("wops-iso-snaps-");
        var snap = SaveManager.BackupWorld(save, "w1", snaps, "good");

        // Wreck both worlds, then restore only w1.
        File.WriteAllText(Path.Combine(save, "w1", "Level.sav"), "RUINED");
        File.WriteAllText(Path.Combine(save, "w2", "Level.sav"), "w2-was-here");

        SaveManager.RestoreWorld(snap.Path, save, "w1", snaps);

        Assert.Equal("level-of-w1", File.ReadAllText(Path.Combine(save, "w1", "Level.sav")));
        // Untouched — not restored, not deleted, exactly as the test left it.
        Assert.Equal("w2-was-here", File.ReadAllText(Path.Combine(save, "w2", "Level.sav")));
        Assert.True(File.Exists(Path.Combine(save, "GlobalPalStorage.sav")));
    }

    [Fact]
    public void A_snapshot_from_another_world_is_refused_rather_than_silently_applied()
    {
        // The failure this prevents looks like SUCCESS: the panel says "Restored" and the player finds
        // somebody else's base. The world id is already in the snapshot's path, so the check is free.
        var save = Saves("wops-mix-", "w1", "w2");
        var snaps = TestSupport.TempDir("wops-mix-snaps-");
        var fromW2 = SaveManager.BackupWorld(save, "w2", snaps, "w2s");

        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveManager.RestoreWorld(fromW2.Path, save, "w1", snaps));

        Assert.Contains("different world", ex.Message);
        // And nothing was touched on the way to refusing.
        Assert.Equal("level-of-w1", File.ReadAllText(Path.Combine(save, "w1", "Level.sav")));
    }

    [Fact]
    public void Restoring_a_world_snapshots_it_first_under_before_restore()
    {
        var save = Saves("wops-safety-", "w1");
        var snaps = TestSupport.TempDir("wops-safety-snaps-");
        var snap = SaveManager.BackupWorld(save, "w1", snaps, "original");
        File.WriteAllText(Path.Combine(save, "w1", "Level.sav"), "about-to-be-replaced");

        SaveManager.RestoreWorld(snap.Path, save, "w1", snaps);

        var autos = SaveManager.ListWorldSnapshots(snaps, "w1").Where(x => x.IsAuto).ToList();
        var before = Assert.Single(autos);
        Assert.Contains("before-restore", before.Label);
    }

    [Fact]
    public void Restoring_removes_files_the_snapshot_does_not_have()
    {
        // A restore that only overwrites would leave whatever the mod run added behind, which is the
        // opposite of going back.
        var save = Saves("wops-extra-", "w1");
        var snaps = TestSupport.TempDir("wops-extra-snaps-");
        var snap = SaveManager.BackupWorld(save, "w1", snaps, "clean");
        File.WriteAllText(Path.Combine(save, "w1", "AddedLater.sav"), "junk");

        SaveManager.RestoreWorld(snap.Path, save, "w1", snaps);

        Assert.False(File.Exists(Path.Combine(save, "w1", "AddedLater.sav")));
        Assert.True(File.Exists(Path.Combine(save, "w1", "Players", "p1.sav")));
    }

    [Fact]
    public void Backing_up_a_world_that_is_not_there_says_so()
        => Assert.Throws<DirectoryNotFoundException>(
            () => SaveManager.BackupWorld(Saves("wops-missing-", "w1"), "nope", TestSupport.TempDir("wops-missing-snaps-")));
}

/// <summary>
/// World names. Ours rather than the game's: Palworld's real world name lives behind a PlM1 container
/// and a GVAS tree in a game that patches often, and reading it would make every panel showing a world
/// name depend on somebody else's reverse-engineering of a moving format — to render a label.
/// </summary>
public class WorldLabelsTests
{
    [Fact]
    public void A_label_round_trips_as_camelCase_on_disk()
    {
        var dir = TestSupport.TempDir("labels-");
        WorldLabels.Save(dir, WorldLabels.Empty.With("905979", "Ridgeline Base"));

        var json = File.ReadAllText(Path.Combine(dir, WorldLabels.FileName));
        Assert.Contains("\"byWorldId\"", json);
        Assert.DoesNotContain("\"ByWorldId\"", json);

        Assert.Equal("Ridgeline Base", WorldLabels.Load(dir).For("905979"));
    }

    [Fact]
    public void An_unnamed_world_falls_back_to_an_ordinal_and_never_to_blank()
    {
        var labels = WorldLabels.Empty.With("a", "Home");

        Assert.Equal("Home", labels.Display("a", 1));
        Assert.Equal("World 2", labels.Display("b", 2));
    }

    [Fact]
    public void Clearing_a_name_removes_it_rather_than_storing_whitespace()
    {
        var labels = WorldLabels.Empty.With("a", "Home").With("a", "   ");

        Assert.Null(labels.For("a"));
    }

    [Fact]
    public void A_label_is_kept_for_a_world_that_is_not_on_disk_right_now()
    {
        // A world can come back from a snapshot under the same id. Silently forgetting the one thing
        // the user typed, at the moment they are recovering from something, would be a small betrayal.
        var dir = TestSupport.TempDir("labels-stale-");
        WorldLabels.Save(dir, WorldLabels.Empty.With("gone", "Old Base").With("here", "Current"));

        var reloaded = WorldLabels.Load(dir).With("here", "Renamed");

        Assert.Equal("Old Base", reloaded.For("gone"));
    }

    [Fact]
    public void The_games_own_name_beats_an_ordinal_but_loses_to_a_label_the_user_typed()
    {
        // A label is only set now when the rename could NOT reach the save, so a label sitting beside
        // a game name is the user's newer choice losing to the one they already tried to replace.
        var labels = WorldLabels.Empty.With("a", "Ridgeline Base");

        Assert.Equal("Ridgeline Base", labels.Display("a", 1, "Home"));      // label wins
        Assert.Equal("ItjustEst Islands", labels.Display("b", 2, "ItjustEst Islands"));
        Assert.Equal("World 3", labels.Display("c", 3, null));               // joined world, no name
        Assert.Equal("World 4", labels.Display("d", 4, "   "));              // blank is not a name
    }

    [Fact]
    public void A_missing_or_broken_file_is_no_labels_and_never_a_throw()
    {
        var dir = TestSupport.TempDir("labels-bad-");
        Assert.Empty(WorldLabels.Load(dir).ByWorldId);

        File.WriteAllText(Path.Combine(dir, WorldLabels.FileName), "{ not json");
        Assert.Empty(WorldLabels.Load(dir).ByWorldId);
    }
}
