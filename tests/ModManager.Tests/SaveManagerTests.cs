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
}
