using System.IO.Compression;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// Holding a game's contents until the game itself comes back.
///
/// <para>Step four, and the case the whole archive exists for: the normal state of a fresh Windows
/// install is that the backup holds twelve games and the machine has none of them. There is nowhere
/// to put a game's files until it is registered, and the honest answer to that is to wait — not to
/// guess a path, and not to make somebody keep a backup file findable for a week.</para>
///
/// <para><b>Nothing is resolved at hold time.</b> A game can come back on a different drive, in a
/// different Steam library, under a different folder. The only correct moment to ask where it lives
/// is the moment it is registered, which is why what gets held is the CONTENT, never a path.</para>
/// </summary>
public class PendingRestoreTests
{
    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A two-game archive: one with saves, mods and settings, and a bare second.</summary>
    private static string Archive(string prefix)
    {
        var save = TestSupport.TempDir(prefix + "-save-");
        File.WriteAllText(Path.Combine(save, "Level.sav"), "the-world");

        var modRoot = TestSupport.TempDir(prefix + "-mods-");
        File.WriteAllText(Path.Combine(modRoot, "cool.pak"), "mod-bytes");

        var data = TestSupport.TempDir(prefix + "-data-");
        File.WriteAllText(Path.Combine(data, "metadata.json"), "{}");

        var path = Path.Combine(TestSupport.TempDir(prefix + "-out-"), "p" + ProfileArchive.Extension);
        ProfileArchive.Create(new[]
        {
            new ProfileGameSource(new BundleGame("palworld", "1623730", "Palworld"), save,
                new[] { new BundlePlanFile(Path.Combine(modRoot, "cool.pak"), "mods/cool.pak") },
                new[] { new BundleMod("Cool", "1", 7, true) }, data)
            { ModLocations = new[] { "mods" } },
            new ProfileGameSource(new BundleGame("witchfire", "1063730", "Witchfire"), null,
                Array.Empty<BundlePlanFile>(), Array.Empty<BundleMod>(), null),
        }, path, Stamp, "0.19.0");
        return path;
    }

    [Fact]
    public void What_is_held_is_a_real_backup_of_its_own_that_the_restore_reads_unchanged()
    {
        // Not a second format. Holding writes a one-game archive, so putting it back later is the
        // SAME code path reading the SAME shape - there is no second restore to keep in step.
        var held = TestSupport.TempDir("hold-shape-");
        var file = PendingRestore.Hold(Archive("hold-shape"), "palworld", held);

        var m = ProfileArchive.ReadManifest(file);
        Assert.NotNull(m);
        Assert.Equal("palworld", Assert.Single(m!.Games).Game.Id);
        Assert.Equal(ProfileArchive.CurrentVersion, m.ArchiveVersion);

        using var zip = ZipFile.OpenRead(file);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("games/palworld/save/Level.sav", names);
        Assert.Contains("games/palworld/mods/mods/cool.pak", names);
        Assert.Contains("games/palworld/data/metadata.json", names);
        Assert.DoesNotContain(names, n => n.Contains("witchfire"));   // only the game asked for
    }

    [Fact]
    public void Holding_COPIES_because_the_backup_it_came_from_is_about_to_be_unplugged()
    {
        // The premise of the whole feature is a machine being rebuilt, so the archive is on a USB
        // stick or a network share. Keeping a pointer to it would work right up until the moment it
        // matters, which is the worst possible time to find out.
        var source = Archive("hold-copy");
        var held = TestSupport.TempDir("hold-copy-held-");
        var file = PendingRestore.Hold(source, "palworld", held);

        File.Delete(source);

        Assert.NotNull(ProfileArchive.ReadManifest(file));
        using var zip = ZipFile.OpenRead(file);
        using var r = new StreamReader(zip.GetEntry("games/palworld/save/Level.sav")!.Open());
        Assert.Equal("the-world", r.ReadToEnd());
    }

    [Fact]
    public void Held_games_are_listed_with_enough_to_describe_them_without_opening_anything()
    {
        var held = TestSupport.TempDir("hold-list-");
        var src = Archive("hold-list");
        PendingRestore.Hold(src, "palworld", held);
        PendingRestore.Hold(src, "witchfire", held);

        var list = PendingRestore.List(held).OrderBy(h => h.GameId).ToList();

        Assert.Equal(new[] { "palworld", "witchfire" }, list.Select(h => h.GameId));
        var pal = list[0];
        Assert.Equal("Palworld", pal.GameName);
        Assert.True(pal.Bytes > 0);
        Assert.True(pal.SaveIncluded);
        Assert.Equal(1, pal.ModFileCount);
    }

    [Fact]
    public void Holding_the_same_game_twice_replaces_it_rather_than_growing_a_pile()
    {
        // Somebody opening the same backup twice must not end up with two copies of a 4 GB game and
        // no way to tell which is current.
        var held = TestSupport.TempDir("hold-twice-");
        var src = Archive("hold-twice");
        PendingRestore.Hold(src, "palworld", held);
        PendingRestore.Hold(src, "palworld", held);

        Assert.Single(PendingRestore.List(held));
    }

    [Fact]
    public void A_game_the_backup_does_not_hold_is_refused_rather_than_held_empty()
    {
        var held = TestSupport.TempDir("hold-absent-");
        Assert.Throws<InvalidOperationException>(
            () => PendingRestore.Hold(Archive("hold-absent"), "elden-ring", held));
        Assert.Empty(PendingRestore.List(held));
    }

    [Fact]
    public void Finding_what_is_waiting_for_ONE_game_is_the_question_the_add_path_asks()
    {
        var held = TestSupport.TempDir("hold-find-");
        PendingRestore.Hold(Archive("hold-find"), "palworld", held);

        Assert.NotNull(PendingRestore.For("palworld", held));
        Assert.NotNull(PendingRestore.For("PALWORLD", held));       // ids are matched case-insensitively
        Assert.Null(PendingRestore.For("witchfire", held));
    }

    [Fact]
    public void What_was_held_goes_back_through_the_ordinary_restore_with_paths_resolved_NOW()
    {
        // The point of holding rather than recording a path: a game can come back on a different
        // drive, in a different library, under a different folder. These destinations are nothing
        // like the ones the archive was made from.
        var held = TestSupport.TempDir("hold-apply-");
        var file = PendingRestore.Hold(Archive("hold-apply"), "palworld", held);

        var save = TestSupport.TempDir("hold-apply-save-");
        var mods = TestSupport.TempDir("hold-apply-mods-");

        var result = ProfileRestore.Restore(file, new[]
        {
            new RestoreRequest("palworld", RestoreParts.Saves | RestoreParts.Mods, save, mods),
        }, _ => false);

        Assert.Equal("the-world", File.ReadAllText(Path.Combine(save, "Level.sav")));
        Assert.Equal("mod-bytes", File.ReadAllText(Path.Combine(mods, "cool.pak")));
        Assert.Equal(2, result.TotalFiles);
    }

    [Fact]
    public void Discarding_removes_it_and_says_whether_there_was_anything_to_remove()
    {
        var held = TestSupport.TempDir("hold-discard-");
        PendingRestore.Hold(Archive("hold-discard"), "palworld", held);

        Assert.True(PendingRestore.Discard("palworld", held));
        Assert.Empty(PendingRestore.List(held));
        Assert.False(PendingRestore.Discard("palworld", held));    // already gone
    }

    [Fact]
    public void A_stray_file_in_the_holding_folder_is_ignored_rather_than_crashing_the_list()
    {
        // It is a folder on somebody's disk. Something else will end up in it eventually.
        var held = TestSupport.TempDir("hold-junk-");
        PendingRestore.Hold(Archive("hold-junk"), "palworld", held);
        File.WriteAllText(Path.Combine(held, "notes.txt"), "hello");
        File.WriteAllText(Path.Combine(held, "broken" + ProfileArchive.Extension), "not a zip");

        Assert.Equal("palworld", Assert.Single(PendingRestore.List(held)).GameId);
    }

    [Fact]
    public void A_missing_holding_folder_is_an_empty_list_not_a_throw()
    {
        // Asked on every game registration, including the very first one on a fresh install.
        Assert.Empty(PendingRestore.List(Path.Combine(TestSupport.TempDir("hold-none-"), "never-made")));
        Assert.Null(PendingRestore.For("palworld", Path.Combine(TestSupport.TempDir("hold-none2-"), "nope")));
    }

    [Fact]
    public void What_is_waiting_is_described_in_COUNTS_not_size()
    {
        // "3.9 GB is waiting" tells somebody what it costs them. "12 mod files and 79 save files"
        // tells them what they get back, which is the thing they are actually deciding about.
        HeldGame H(int mods, int saves) => new("g", "G", "p", 4_000_000_000, saves > 0, mods, saves, null);

        Assert.Equal("12 mod files and 79 save files", PendingRestore.Describe(H(12, 79)));
        Assert.Equal("1 mod file", PendingRestore.Describe(H(1, 0)));
        Assert.Equal("1 save file", PendingRestore.Describe(H(0, 1)));
        Assert.Equal("Settings", PendingRestore.Describe(H(0, 0)));
        Assert.DoesNotContain("GB", PendingRestore.Describe(H(12, 79)));
    }
}
