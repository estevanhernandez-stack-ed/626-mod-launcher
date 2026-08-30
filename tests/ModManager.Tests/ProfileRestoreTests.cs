using System.IO.Compression;
using ModManager.Core;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// Putting a profile archive back — the half that can hurt you, built last on purpose so the reading
/// was known-good before anything acted on it.
///
/// <para>Every guard here was learned rather than assumed: the snapshot from the file-op laws, the
/// running-game refusal from a Palworld world that came back after being deleted under a live game,
/// and path resolution happening NOW because a fresh install looks nothing like the machine the
/// archive was made on.</para>
/// </summary>
public class ProfileRestoreTests
{
    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static bool NotRunning(string _) => false;

    /// <summary>An archive holding one game with saves, mods and settings.</summary>
    private static string Archive(string prefix, string gameId = "palworld")
    {
        var save = TestSupport.TempDir(prefix + "-src-save-");
        Directory.CreateDirectory(Path.Combine(save, "W1"));
        File.WriteAllText(Path.Combine(save, "W1", "Level.sav"), "archived-world");

        var modRoot = TestSupport.TempDir(prefix + "-src-mods-");
        File.WriteAllText(Path.Combine(modRoot, "cool.pak"), "archived-mod");

        var data = TestSupport.TempDir(prefix + "-src-data-");
        File.WriteAllText(Path.Combine(data, "metadata.json"), "{\"archived\":true}");

        var path = Path.Combine(TestSupport.TempDir(prefix + "-out-"), "p" + ProfileArchive.Extension);
        ProfileArchive.Create(new[]
        {
            new ProfileGameSource(
                new BundleGame(gameId, "1", gameId), save,
                new[] { new BundlePlanFile(Path.Combine(modRoot, "cool.pak"), "cool.pak") },
                new[] { new BundleMod("Cool", "1", 7, true) }, data),
        }, path, Stamp, "0.19.0");
        return path;
    }

    [Fact]
    public void Saves_mods_and_settings_land_where_this_machine_keeps_them_now()
    {
        // The whole point: the archive records what a game HAD, never where it lived. These
        // destinations are nothing like the folders it was made from.
        var archive = Archive("res-basic");
        var save = TestSupport.TempDir("res-dest-save-");
        var mods = TestSupport.TempDir("res-dest-mods-");
        var data = TestSupport.TempDir("res-dest-data-");

        var result = ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves | RestoreParts.Mods | RestoreParts.Settings,
                               save, mods, data, TestSupport.TempDir("res-snaps-")),
        }, NotRunning);

        Assert.Equal("archived-world", File.ReadAllText(Path.Combine(save, "W1", "Level.sav")));
        Assert.Equal("archived-mod", File.ReadAllText(Path.Combine(mods, "cool.pak")));
        Assert.Equal("{\"archived\":true}", File.ReadAllText(Path.Combine(data, "metadata.json")));
        Assert.Equal(3, result.TotalFiles);
    }

    [Fact]
    public void Only_the_parts_asked_for_are_touched()
    {
        var archive = Archive("res-parts");
        var save = TestSupport.TempDir("res-parts-save-");
        var mods = TestSupport.TempDir("res-parts-mods-");

        ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, save, mods, null, null),
        }, NotRunning);

        Assert.True(File.Exists(Path.Combine(save, "W1", "Level.sav")));
        Assert.False(File.Exists(Path.Combine(mods, "cool.pak")));   // mods were not asked for
    }

    [Fact]
    public void What_is_about_to_be_replaced_is_snapshotted_first()
    {
        var archive = Archive("res-snap");
        var save = TestSupport.TempDir("res-snap-save-");
        Directory.CreateDirectory(Path.Combine(save, "W1"));
        File.WriteAllText(Path.Combine(save, "W1", "Level.sav"), "the-one-that-was-here");
        var snaps = TestSupport.TempDir("res-snap-snaps-");

        var result = ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, save, null, null, snaps),
        }, NotRunning);

        var kept = Assert.Single(SaveManager.ListSnapshots(snaps));
        Assert.Contains("before-restore", kept.Label);
        Assert.True(Assert.Single(result.Games).SnapshotTaken);
        Assert.Equal("archived-world", File.ReadAllText(Path.Combine(save, "W1", "Level.sav")));
    }

    [Fact]
    public void A_running_game_is_refused_and_nothing_of_its_is_written()
    {
        // Learned on Palworld: a folder changed under a live game is silently undone on exit, which
        // reports as "it didn't work" with nothing in any log to see.
        var archive = Archive("res-running");
        var save = TestSupport.TempDir("res-running-save-");
        File.WriteAllText(Path.Combine(save, "keep.sav"), "untouched");

        var result = ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, save, null, null, null),
        }, _ => true);

        var g = Assert.Single(result.Games);
        Assert.Equal("the game is running", g.Skipped);
        Assert.Equal(0, g.FileCount);
        Assert.False(File.Exists(Path.Combine(save, "W1", "Level.sav")));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(save, "keep.sav")));
    }

    [Fact]
    public void An_unknown_answer_about_whether_the_game_runs_counts_as_running()
    {
        // Fails CLOSED. Not being able to tell is not permission.
        var archive = Archive("res-failclosed");
        var save = TestSupport.TempDir("res-failclosed-save-");

        var result = ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, save, null, null, null),
        }, _ => throw new InvalidOperationException("cannot enumerate processes"));

        Assert.Equal("the game is running", Assert.Single(result.Games).Skipped);
        Assert.Empty(Directory.GetFiles(save, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_game_the_backup_does_not_hold_is_skipped_with_a_reason()
    {
        var result = ProfileRestore.Restore(Archive("res-absent"), new[]
        {
            new RestoreRequest("elden-ring", RestoreParts.Saves, TestSupport.TempDir("res-absent-dest-")),
        }, NotRunning);

        Assert.Equal("this backup does not contain that game", Assert.Single(result.Games).Skipped);
    }

    [Fact]
    public void Restoring_adds_and_overwrites_but_never_empties_the_destination()
    {
        // A save bundle's restore wipes first, because it replaces ONE game's saves and that is
        // exactly what was asked. A profile restore runs across everything at once and a mod folder
        // holds the GAME'S OWN CONTENT intermixed with mods - emptying it would take the game with it.
        var archive = Archive("res-additive");
        var mods = TestSupport.TempDir("res-additive-mods-");
        File.WriteAllText(Path.Combine(mods, "BaseGameContent.pak"), "the game itself");
        File.WriteAllText(Path.Combine(mods, "cool.pak"), "an older copy");

        ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Mods, null, mods, null, null),
        }, NotRunning);

        Assert.Equal("the game itself", File.ReadAllText(Path.Combine(mods, "BaseGameContent.pak")));
        Assert.Equal("archived-mod", File.ReadAllText(Path.Combine(mods, "cool.pak")));   // overwritten
    }

    [Fact]
    public void An_entry_that_resolves_outside_its_destination_is_refused()
    {
        // An archive is a file from another machine, and therefore untrusted input.
        var dir = TestSupport.TempDir("res-evil-");
        var path = Path.Combine(dir, "evil" + ProfileArchive.Extension);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry(ProfileArchive.ManifestEntry).Open()))
                w.Write("""{"archiveVersion":1,"games":[{"game":{"id":"palworld"}}]}""");
            using var e = new StreamWriter(zip.CreateEntry("games/palworld/save/../../../escaped.txt").Open());
            e.Write("nope");
        }

        var ex = Assert.Throws<InvalidOperationException>(() => ProfileRestore.Restore(path, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, TestSupport.TempDir("res-evil-dest-")),
        }, NotRunning));

        Assert.Contains("outside the folder", ex.Message);
    }

    [Fact]
    public void A_backup_from_a_newer_launcher_is_refused_rather_than_half_understood()
    {
        var dir = TestSupport.TempDir("res-future-");
        var path = Path.Combine(dir, "future" + ProfileArchive.Extension);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry(ProfileArchive.ManifestEntry).Open()))
            w.Write("""{"archiveVersion":99,"games":[]}""");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ProfileRestore.Restore(path, Array.Empty<RestoreRequest>(), NotRunning));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void Nothing_selected_changes_nothing_and_says_so()
    {
        var result = ProfileRestore.Restore(Archive("res-none"), new[]
        {
            new RestoreRequest("palworld", RestoreParts.None, TestSupport.TempDir("res-none-dest-")),
        }, NotRunning);

        Assert.Empty(result.Games);
        Assert.Equal("Nothing was selected, so nothing changed.", result.Summary);
    }

    [Fact]
    public void The_summary_names_what_could_not_be_done_as_plainly_as_what_could()
    {
        // A restore that half-worked and said "done" is the failure this wording exists to prevent.
        var archive = Archive("res-summary");
        var result = ProfileRestore.Restore(archive, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves, TestSupport.TempDir("res-summary-ok-"),
                               null, null, null),
            new RestoreRequest("elden-ring", RestoreParts.Saves, TestSupport.TempDir("res-summary-no-")),
        }, NotRunning);

        Assert.Contains("Restored 1 game", result.Summary);
        Assert.Contains("1 skipped", result.Summary);
        Assert.Contains("elden-ring", result.Summary);
        Assert.Contains("does not contain that game", result.Summary);
    }

    [Fact]
    public void A_file_that_is_not_a_backup_is_refused_before_anything_is_opened()
    {
        var path = Path.Combine(TestSupport.TempDir("res-junk-"), "junk" + ProfileArchive.Extension);
        File.WriteAllText(path, "not a zip");

        Assert.Throws<InvalidOperationException>(
            () => ProfileRestore.Restore(path, Array.Empty<RestoreRequest>(), NotRunning));
    }
}
