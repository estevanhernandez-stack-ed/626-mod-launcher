using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A <c>LevelMeta.sav</c> shaped like the real thing: length-prefixed, NUL-terminated strings after a
/// 12-byte PlM1 head whose size fields a write must never invalidate.
///
/// <para>Synthetic rather than a captured save on purpose — the fixture STATES the format the code
/// depends on, so a change to either surfaces as a failing test instead of as a corrupted save on
/// somebody's machine.</para>
/// </summary>
internal static class PalworldMetaFixture
{
    public static byte[] Meta(string name, int budget)
    {
        var b = new List<byte>();
        b.AddRange(new byte[] { 0xb7, 0x08, 0, 0, 0xd3, 0x07, 0, 0 });   // declared uncompressed / compressed
        b.AddRange(Encoding.ASCII.GetBytes("PlM1"));                     // magic
        b.AddRange(new byte[] { 0x8c, 0x0a, 0x00, 0x07, 0xcd, 0x30 });   // payload bytes ahead of the tail

        void Str(string s)
        {
            b.Add((byte)(s.Length + 1));
            b.AddRange(Encoding.UTF8.GetBytes(s));
            b.Add(0);
        }

        Str("WorldName");
        Str("StrProperty");
        b.Add((byte)(budget + 5));       // the property's own size field
        b.Add((byte)(budget + 1));       // string length, including the NUL
        b.AddRange(Encoding.UTF8.GetBytes(name.PadRight(budget)));
        b.Add(0);
        Str("HostPlayerName");
        Str("None");
        return b.ToArray();
    }

    /// <summary>
    /// The shape Palworld leaves behind after it re-saves a world whose name did NOT fill its budget.
    ///
    /// <para>Copied byte-for-byte in structure from the real thing. A six-byte name was padded to
    /// seventeen; overnight the game re-saved the world and the payload came back three bytes shorter,
    /// because the codec turned the eleven-space run into a back-reference. The markers and both length
    /// bytes are still literal - it is the name itself that stopped being one.</para>
    /// </summary>
    public static byte[] MetaAfterGameResave()
    {
        var b = new List<byte>();
        b.AddRange(new byte[] { 0xb7, 0x08, 0, 0, 0xd0, 0x07, 0, 0 });   // uncompressed unchanged, compressed SHORTER
        b.AddRange(Encoding.ASCII.GetBytes("PlM1"));
        b.AddRange(new byte[] { 0x8c, 0x0a, 0x00, 0x07, 0xcd, 0x30 });

        void Str(string s)
        {
            b.Add((byte)(s.Length + 1));
            b.AddRange(Encoding.UTF8.GetBytes(s));
            b.Add(0);
        }

        Str("WorldName");
        Str("StrProperty");
        b.Add(0x16);                                                      // property size, still literal
        b.Add(0x12);                                                      // string length, still literal
        // ...but the 18 bytes that should follow are now compression tokens, so nothing NUL-terminates
        // at the declared length.
        b.AddRange(new byte[] { 0x20 });
        b.AddRange(Encoding.UTF8.GetBytes("Padded"));
        b.AddRange(new byte[] { 0x00, 0x00, 0x0f, 0x00, 0x00, 0x00 });
        b.AddRange(Encoding.UTF8.GetBytes("HostPlayerName"));
        b.AddRange(new byte[] { 0x00, 0x0c, 0x00, 0x00, 0x00 });
        return b.ToArray();
    }

    /// <summary>A world folder containing just the meta file.</summary>
    public static string World(string prefix, string name, int budget)
    {
        var dir = TestSupport.TempDir(prefix);
        File.WriteAllBytes(Path.Combine(dir, PalworldWorldName.MetaFileName), Meta(name, budget));
        return dir;
    }
}

/// <summary>
/// Reading and rewriting the name Palworld itself shows for a world. See
/// <c>docs/superpowers/specs/2026-08-19-the-world-name-is-readable-design.md</c>.
/// </summary>
public class PalworldWorldNameTests
{
    [Fact]
    public void The_name_and_its_budget_are_read_out_of_the_save()
    {
        var site = PalworldWorldName.Read(PalworldMetaFixture.World("pwn-read-", "ItjustEst Islands", 17));

        Assert.NotNull(site);
        Assert.Equal("ItjustEst Islands", site!.Name);
        Assert.Equal(17, site.BudgetBytes);
    }

    [Fact]
    public void A_rename_changes_the_name_bytes_and_absolutely_nothing_else()
    {
        // The load-bearing assertion of the whole feature. Every size field in the file - the header's
        // declared uncompressed length, the property size, the string length - stays true only because
        // the file's length does not move.
        var dir = PalworldMetaFixture.World("pwn-exact-", "ItjustEst Islands", 17);
        var path = Path.Combine(dir, PalworldWorldName.MetaFileName);
        var before = File.ReadAllBytes(path);

        PalworldWorldName.Write(dir, "COPY - TEST WORLD");

        var after = File.ReadAllBytes(path);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Take(12), after.Take(12));      // header untouched

        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        Assert.Equal(changed.Min() + changed.Count - 1, changed.Max());   // one contiguous run
        Assert.Equal("COPY - TEST WORLD", PalworldWorldName.Read(dir)!.Name);
    }

    [Fact]
    public void A_shorter_name_is_padded_to_the_exact_budget()
    {
        var dir = PalworldMetaFixture.World("pwn-pad-", "ItjustEst Islands", 17);
        var path = Path.Combine(dir, PalworldWorldName.MetaFileName);
        var before = File.ReadAllBytes(path);

        PalworldWorldName.Write(dir, "Padded");

        var after = File.ReadAllBytes(path);
        Assert.Equal(before.Length, after.Length);
        Assert.Contains("Padded           ", Encoding.UTF8.GetString(after));   // padded on disk
        Assert.Equal("Padded", PalworldWorldName.Read(dir)!.Name);              // trimmed on the way out
    }

    [Fact]
    public void An_over_budget_name_is_refused_and_the_file_is_untouched()
    {
        var dir = PalworldMetaFixture.World("pwn-over-", "Short", 5);
        var path = Path.Combine(dir, PalworldWorldName.MetaFileName);
        var before = File.ReadAllBytes(path);

        var ex = Assert.Throws<InvalidOperationException>(() => PalworldWorldName.Write(dir, "Much too long"));

        Assert.Contains("room for 5", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void The_budget_is_measured_in_bytes_so_accents_and_emoji_cost_more_than_one()
    {
        // A UI counting CHARACTERS would tell someone their five-character name fits a five-byte budget
        // and then refuse it. Everything here measures UTF-8.
        Assert.Equal(3, PalworldWorldName.ByteLength("abc"));
        Assert.Equal(5, PalworldWorldName.ByteLength("café"));       // e-acute is two bytes
        Assert.Equal(4, PalworldWorldName.ByteLength("\U0001F411")); // a sheep is four

        Assert.True(PalworldWorldName.Fits("abcde", 5));
        Assert.False(PalworldWorldName.Fits("ééééé", 5));   // five characters, ten bytes

        var dir = PalworldMetaFixture.World("pwn-utf8-", "0123456789", 10);
        Assert.Throws<InvalidOperationException>(
            () => PalworldWorldName.Write(dir, "éééééé"));
    }

    [Fact]
    public void A_blank_name_is_refused_rather_than_writing_a_world_with_no_name()
    {
        var dir = PalworldMetaFixture.World("pwn-blank-", "Named", 5);

        Assert.Throws<InvalidOperationException>(() => PalworldWorldName.Write(dir, "   "));
        Assert.Equal("Named", PalworldWorldName.Read(dir)!.Name);
    }

    [Fact]
    public void A_save_with_no_name_in_it_reads_as_null_and_never_throws()
    {
        var dir = TestSupport.TempDir("pwn-none-");
        File.WriteAllBytes(Path.Combine(dir, PalworldWorldName.MetaFileName), new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(PalworldWorldName.Read(dir));
        Assert.Throws<InvalidOperationException>(() => PalworldWorldName.Write(dir, "Anything"));
    }

    [Fact]
    public void A_padded_name_the_game_has_re_saved_reads_as_null_but_the_save_is_still_there()
    {
        // Found by the smoke, on the real game, overnight. A name that did not fill its budget was
        // padded with spaces; Palworld re-saved the world and the codec compressed the run away, so the
        // name region stopped being literal bytes. We can no longer read or rewrite it - and the panel
        // MUST tell those two apart, because "this world never had a name" and "we can no longer change
        // the name it has" want completely different sentences.
        var dir = TestSupport.TempDir("pwn-resaved-");
        File.WriteAllBytes(Path.Combine(dir, PalworldWorldName.MetaFileName),
                           PalworldMetaFixture.MetaAfterGameResave());

        Assert.Null(PalworldWorldName.Read(dir));      // unreadable...
        Assert.True(PalworldWorldName.HasOwnSave(dir)); // ...but it is not a joined world
    }

    [Fact]
    public void A_name_that_fills_its_budget_exactly_is_the_durable_one()
    {
        // The other half of the same finding: no padding means no run for the codec to collapse. The
        // real world called ItjustEst Islands fills its 17 bytes and has survived months of re-saves.
        var dir = PalworldMetaFixture.World("pwn-exactfit-", "ItjustEst Islands", 17);

        PalworldWorldName.Write(dir, "COPY - TEST WORLD");   // also exactly 17

        var site = PalworldWorldName.Read(dir)!;
        Assert.Equal("COPY - TEST WORLD", site.Name);
        // The property that makes it durable: the stored name fills the budget, so there is no run of
        // padding for the codec to collapse into a back-reference next time the game saves.
        Assert.Equal(site.BudgetBytes, PalworldWorldName.ByteLength(site.Name));
    }

    [Fact]
    public void A_joined_world_has_no_meta_file_and_that_is_an_answer_not_a_failure()
    {
        // The second world on the real install is LocalData.sav and nothing else - the world itself is
        // on the host's machine, so there is no name in it to read or write.
        var dir = TestSupport.TempDir("pwn-joined-");
        File.WriteAllText(Path.Combine(dir, "LocalData.sav"), "local");

        Assert.Null(PalworldWorldName.Read(dir));
        Assert.False(PalworldWorldName.HasOwnSave(dir));
        var ex = Assert.Throws<InvalidOperationException>(() => PalworldWorldName.Write(dir, "Nope"));
        Assert.Contains("somebody else hosts", ex.Message);
    }

    [Fact]
    public void A_rename_snapshots_the_meta_file_first_and_the_snapshot_stays_out_of_the_zip_list()
    {
        var dir = PalworldMetaFixture.World("pwn-snap-", "Before", 6);
        var snaps = TestSupport.TempDir("pwn-snap-dir-");

        PalworldWorldName.Write(dir, "After", snaps);

        var kept = Assert.Single(Directory.GetFiles(snaps));
        Assert.Contains("before-rename", Path.GetFileName(kept));
        Assert.Equal("After", PalworldWorldName.Read(dir)!.Name);
        Assert.Empty(SaveManager.ListSnapshots(snaps));      // ListSnapshots globs *.zip and cannot see it

        // And the snapshot really is the previous file, not a truncated stand-in.
        var restored = TestSupport.TempDir("pwn-snap-back-");
        File.Copy(kept, Path.Combine(restored, PalworldWorldName.MetaFileName));
        Assert.Equal("Before", PalworldWorldName.Read(restored)!.Name);
    }
}

/// <summary>
/// Duplicating a world — dropped once because a copy could not be told apart from its original, and
/// back because the copy can now be renamed.
/// </summary>
public class DuplicateWorldTests
{
    private static string Saves(string prefix, string world, string name, int budget)
    {
        var root = TestSupport.TempDir(prefix);
        var dir = Path.Combine(root, world);
        Directory.CreateDirectory(Path.Combine(dir, "Players"));
        Directory.CreateDirectory(Path.Combine(dir, "backup", "2026.08.16-22.52.36"));
        File.WriteAllText(Path.Combine(dir, "Level.sav"), "the-world");
        File.WriteAllText(Path.Combine(dir, "Players", "p1.sav"), "player");
        File.WriteAllText(Path.Combine(dir, "backup", "2026.08.16-22.52.36", "Level.sav"), "old");
        File.WriteAllBytes(Path.Combine(dir, PalworldWorldName.MetaFileName),
                           PalworldMetaFixture.Meta(name, budget));
        return root;
    }

    [Fact]
    public void A_duplicate_lands_in_a_new_folder_and_leaves_the_original_byte_identical()
    {
        var save = Saves("dupe-", "W1", "ItjustEst Islands", 17);
        var before = Directory.GetFiles(Path.Combine(save, "W1"), "*", SearchOption.AllDirectories)
                              .ToDictionary(f => f, File.ReadAllBytes);

        var id = SaveManager.DuplicateWorld(save, "W1");

        Assert.NotEqual("W1", id);
        Assert.Equal(32, id.Length);
        Assert.True(Directory.Exists(Path.Combine(save, id)));
        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));
    }

    [Fact]
    public void The_copy_carries_the_world_but_not_the_games_own_backup_history()
    {
        // Twelve snapshots of somebody else's past would multiply the copy's size for nothing.
        var save = Saves("dupe-backup-", "W1", "ItjustEst Islands", 17);

        var id = SaveManager.DuplicateWorld(save, "W1");

        Assert.True(File.Exists(Path.Combine(save, id, "Level.sav")));
        Assert.True(File.Exists(Path.Combine(save, id, "Players", "p1.sav")));
        Assert.False(Directory.Exists(Path.Combine(save, id, "backup")));
    }

    [Fact]
    public void Duplicating_under_a_new_name_is_the_whole_reason_this_feature_exists()
    {
        var save = Saves("dupe-name-", "W1", "ItjustEst Islands", 17);

        var id = SaveManager.DuplicateWorld(save, "W1", "COPY - TEST WORLD");

        Assert.Equal("COPY - TEST WORLD", PalworldWorldName.Read(Path.Combine(save, id))!.Name);
        Assert.Equal("ItjustEst Islands", PalworldWorldName.Read(Path.Combine(save, "W1"))!.Name);
    }

    [Fact]
    public void A_world_with_no_name_to_change_still_duplicates()
    {
        // A joined world has no LevelMeta.sav. Asking for a name there is not an error - there is
        // simply nothing for Palworld to show.
        var save = TestSupport.TempDir("dupe-joined-");
        Directory.CreateDirectory(Path.Combine(save, "J1"));
        File.WriteAllText(Path.Combine(save, "J1", "LocalData.sav"), "local");

        var id = SaveManager.DuplicateWorld(save, "J1", "Anything");

        Assert.True(File.Exists(Path.Combine(save, id, "LocalData.sav")));
        Assert.Null(PalworldWorldName.Read(Path.Combine(save, id)));
    }

    [Fact]
    public void ListWorlds_carries_the_games_own_name_and_the_budget_for_changing_it()
    {
        var save = Saves("dupe-list-", "W1", "ItjustEst Islands", 17);
        Directory.CreateDirectory(Path.Combine(save, "J1"));
        File.WriteAllText(Path.Combine(save, "J1", "LocalData.sav"), "local");

        var worlds = SaveManager.ListWorlds(save).ToDictionary(w => w.Name);

        Assert.Equal("ItjustEst Islands", worlds["W1"].GameName);
        Assert.Equal(17, worlds["W1"].NameBudgetBytes);
        Assert.Null(worlds["J1"].GameName);          // joined - nothing to read
        Assert.Equal(0, worlds["J1"].NameBudgetBytes);
    }

    [Fact]
    public void Duplicating_a_world_that_is_not_there_says_so()
        => Assert.Throws<DirectoryNotFoundException>(
            () => SaveManager.DuplicateWorld(Saves("dupe-missing-", "W1", "N", 1), "nope"));
}
