using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// Built-in save snapshots: zip the save folder, restore a snapshot (backing up current first),
// list + delete. Snapshots live outside the save folder so clearing it never touches them.
public class SaveManagerTests
{
    private static (string saveDir, string snaps) Fixture()
    {
        var root = TestSupport.TempDir("saves-");
        var saveDir = Path.Combine(root, "save");
        var snaps = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(saveDir);
        return (saveDir, snaps);
    }

    [Fact]
    public void Backup_creates_a_snapshot_from_the_save_folder()
    {
        var (saveDir, snaps) = Fixture();
        File.WriteAllText(Path.Combine(saveDir, "slot1.dat"), "V1");

        var snap = SaveManager.Backup(saveDir, snaps, "before raid");

        Assert.True(File.Exists(snap.Path));
        Assert.Equal("before raid", snap.Label);
        Assert.Single(SaveManager.ListSnapshots(snaps));
    }

    [Fact]
    public void Restore_replaces_save_contents_and_backs_up_current_first()
    {
        var (saveDir, snaps) = Fixture();
        File.WriteAllText(Path.Combine(saveDir, "slot1.dat"), "V1");
        var v1 = SaveManager.Backup(saveDir, snaps, "v1");

        // Move the live save forward to V2, then restore the V1 snapshot.
        File.WriteAllText(Path.Combine(saveDir, "slot1.dat"), "V2");
        File.WriteAllText(Path.Combine(saveDir, "extra.dat"), "junk");

        SaveManager.Restore(v1.Path, saveDir, snaps);

        Assert.Equal("V1", File.ReadAllText(Path.Combine(saveDir, "slot1.dat")));
        Assert.False(File.Exists(Path.Combine(saveDir, "extra.dat")), "restore replaces, not merges");
        Assert.True(SaveManager.ListSnapshots(snaps).Count >= 2, "current state was snapshotted before restore");
    }

    [Fact]
    public void ListSnapshots_newest_first_and_parses_label()
    {
        var (saveDir, snaps) = Fixture();
        File.WriteAllText(Path.Combine(saveDir, "s.dat"), "x");
        SaveManager.Backup(saveDir, snaps, "first");
        Thread.Sleep(1100); // distinct second-resolution timestamps
        SaveManager.Backup(saveDir, snaps, "second");

        var list = SaveManager.ListSnapshots(snaps);
        Assert.Equal(2, list.Count);
        Assert.Equal("second", list[0].Label);
        Assert.Equal("first", list[1].Label);
    }

    [Fact]
    public void Delete_removes_a_snapshot()
    {
        var (saveDir, snaps) = Fixture();
        File.WriteAllText(Path.Combine(saveDir, "s.dat"), "x");
        var snap = SaveManager.Backup(saveDir, snaps);

        SaveManager.Delete(snap.Path);

        Assert.False(File.Exists(snap.Path));
        Assert.Empty(SaveManager.ListSnapshots(snaps));
    }

    [Fact]
    public void Backup_of_a_missing_save_folder_throws()
    {
        var (saveDir, snaps) = Fixture();
        Directory.Delete(saveDir, true);
        Assert.ThrowsAny<Exception>(() => SaveManager.Backup(saveDir, snaps));
    }

    // ---- Snapshots record their folder, and a restore refuses another one ----
    // How a Windrose profile came to sit nested inside its own RocksDB_v2: the save folder moved, and a
    // snapshot of the old folder was restored into the new one, which emptied it and unpacked one level off.

    [Fact]
    public void A_snapshot_records_the_folder_it_was_taken_from()
    {
        var (saveDir, snaps) = Fixture();
        File.WriteAllText(Path.Combine(saveDir, "slot1.dat"), "V1");

        var snap = SaveManager.Backup(saveDir, snaps, "v1");

        Assert.Equal(Path.GetFullPath(saveDir), SaveManager.SourceOf(snap.Path));
    }

    [Fact]
    public void A_snapshot_still_holds_files_and_empty_folders_as_before()
    {
        var (saveDir, snaps) = Fixture();
        Directory.CreateDirectory(Path.Combine(saveDir, "Worlds", "A"));
        File.WriteAllText(Path.Combine(saveDir, "Worlds", "A", "level.db"), "W");
        Directory.CreateDirectory(Path.Combine(saveDir, "Empty"));

        var snap = SaveManager.Backup(saveDir, snaps);

        using var zip = ZipFile.OpenRead(snap.Path);
        Assert.Equal(new[] { "Empty/", "Worlds/A/level.db" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Restoring_a_snapshot_of_another_folder_is_refused_and_changes_nothing()
    {
        var (saveDir, snaps) = Fixture();
        var parent = Path.GetDirectoryName(saveDir)!;
        var profiles = Path.Combine(parent, "SaveProfiles");
        Directory.CreateDirectory(Path.Combine(profiles, "123", "RocksDB"));
        var ofProfiles = SaveManager.Backup(profiles, snaps, "old folder");
        File.WriteAllText(Path.Combine(saveDir, "live.db"), "LIVE");

        var e = Assert.Throws<InvalidOperationException>(() => SaveManager.Restore(ofProfiles.Path, saveDir, snaps));
        Assert.Throws<InvalidOperationException>(() => SaveManager.RestoreType(ofProfiles.Path, saveDir, snaps, ".db"));

        Assert.Contains("taken from", e.Message);
        Assert.Equal("LIVE", File.ReadAllText(Path.Combine(saveDir, "live.db")));
        Assert.False(Directory.Exists(Path.Combine(saveDir, "123")));
        Assert.Single(SaveManager.ListSnapshots(snaps));   // no before-restore snapshot either: refused first
    }

    [Fact]
    public void A_snapshot_from_before_626_recorded_its_folder_restores_as_it_always_has()
    {
        var (saveDir, snaps) = Fixture();
        Directory.CreateDirectory(snaps);
        var legacy = Path.Combine(snaps, "20260101-000000__old.zip");
        using (var zip = ZipFile.Open(legacy, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("slot1.dat").Open())) w.Write("OLD");

        Assert.Null(SaveManager.SourceOf(legacy));
        SaveManager.Restore(legacy, saveDir, snaps);

        Assert.Equal("OLD", File.ReadAllText(Path.Combine(saveDir, "slot1.dat")));
    }

    // Review on #380: a snapshot from before 626 recorded its folder is refused when it is plainly of a folder
    // above this one, which is the incident itself.
    [Theory]
    [InlineData("123/RocksDB_v2/0.10.0/a.db")]   // a snapshot of the profiles folder, restored into <profiles>/123/RocksDB_v2
    [InlineData("RocksDB_v2/0.10.0/a.db")]       // a snapshot of the profile, restored into its RocksDB_v2
    public void A_pre_recording_snapshot_of_a_folder_above_is_refused(string entry)
    {
        var root = TestSupport.TempDir("saves-");
        var saveDir = Path.Combine(root, "SaveProfiles", "123", "RocksDB_v2");
        Directory.CreateDirectory(saveDir);
        File.WriteAllText(Path.Combine(saveDir, "live.db"), "LIVE");
        var snaps = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(snaps);
        var legacy = Path.Combine(snaps, "20260101-000000__old.zip");
        using (var zip = ZipFile.Open(legacy, ZipArchiveMode.Create))
            zip.CreateEntry(entry);

        var e = Assert.Throws<InvalidOperationException>(() => SaveManager.Restore(legacy, saveDir, snaps));

        Assert.Contains("folder above", e.Message);
        Assert.Equal("LIVE", File.ReadAllText(Path.Combine(saveDir, "live.db")));
    }

    [Fact]
    public void A_pre_recording_snapshot_that_merely_shares_a_folder_name_deeper_down_still_restores()
    {
        var (saveDir, snaps) = Fixture();
        Directory.CreateDirectory(snaps);
        var legacy = Path.Combine(snaps, "20260101-000000__old.zip");
        using (var zip = ZipFile.Open(legacy, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("worlds/save/slot.dat").Open())) w.Write("OLD");   // "save" two levels down

        SaveManager.Restore(legacy, saveDir, snaps);

        Assert.Equal("OLD", File.ReadAllText(Path.Combine(saveDir, "worlds", "save", "slot.dat")));
    }

    // Review on #380: a save folder that moved (OneDrive, a renamed account) is the same folder under a new path.
    [Fact]
    public void A_snapshot_of_a_folder_that_moved_restores_into_its_new_place()
    {
        var root = TestSupport.TempDir("saves-");
        var oldDir = Path.Combine(root, "Documents", "Game");
        var newDir = Path.Combine(root, "OneDrive", "Documents", "Game");
        var snaps = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "slot.dat"), "V1");
        var snap = SaveManager.Backup(oldDir, snaps);
        Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
        Directory.Move(oldDir, newDir);

        SaveManager.Restore(snap.Path, newDir, snaps);

        Assert.Equal("V1", File.ReadAllText(Path.Combine(newDir, "slot.dat")));
    }
}
