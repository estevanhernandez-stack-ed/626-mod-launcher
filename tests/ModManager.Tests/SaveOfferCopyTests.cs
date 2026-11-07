using ModManager.Core;

namespace ModManager.Tests;

/// <summary>The save-type offer's write: snapshot first (never pruned), atomic copy, source untouched, one-click undo.
/// From the 2026-11-07 reversibility audit of the feature branch.</summary>
public class SaveOfferCopyTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-saveoffer-copy-");
    private string SaveDir => Path.Combine(_root, "saves");
    private string SnapDir => Path.Combine(_root, "snapshots");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static readonly SaveType Sl2 = new(".sl2", "Vanilla", "vanilla");
    private static readonly SaveType Co2 = new(".co2", "Seamless Co-op", "coop");

    public SaveOfferCopyTests()
    {
        Directory.CreateDirectory(SaveDir);
        File.WriteAllText(Path.Combine(SaveDir, "ER0000.sl2"), "EMPTY-PROFILE");
        File.WriteAllText(Path.Combine(SaveDir, "ER0000.co2"), "COOP-CHARACTERS");
    }

    private SaveOfferPlan Plan(bool targetExists = true)
    {
        var src = new SaveOfferSource(Co2, new SaveFileFacts("ER0000.co2", true, 3, DateTime.UtcNow), SaveOfferWarning.Warn);
        return new SaveOfferPlan(Sl2, "ER0000.sl2", targetExists, new[] { src });
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(SaveDir, name));

    [Fact]
    public void Copies_the_source_over_the_target_and_leaves_the_source_alone()
    {
        var plan = Plan();
        var (_, written) = SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]);
        Assert.Equal("ER0000.sl2", written);
        Assert.Equal("COOP-CHARACTERS", Read("ER0000.sl2"));
        Assert.Equal("COOP-CHARACTERS", Read("ER0000.co2"));
        Assert.Empty(Directory.GetFiles(SaveDir, "*.tmp-*"));
    }

    [Fact]
    public void The_copy_is_stamped_now_not_with_the_source_save_date()
    {
        // Live smoke 2026-11-07: a Sep 3 .co2 copied over today's .sl2 kept "Sep 3", older than what Steam Cloud held.
        var co2 = Path.Combine(SaveDir, "ER0000.co2");
        File.SetLastWriteTimeUtc(co2, new DateTime(2026, 9, 3, 21, 24, 13, DateTimeKind.Utc));
        var before = DateTime.UtcNow.AddSeconds(-5);
        var plan = Plan();
        SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]);
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(SaveDir, "ER0000.sl2")) >= before);
        Assert.Equal(new DateTime(2026, 9, 3, 21, 24, 13, DateTimeKind.Utc), File.GetLastWriteTimeUtc(co2)); // source untouched
    }

    [Fact]
    public void The_snapshot_survives_the_launch_backup_and_a_keep_of_one()
    {
        var plan = Plan();
        var (snap, _) = SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]);
        Assert.False(snap.IsAuto);

        // What the launch does next when auto-backup is on, at the lowest keep the Saves dialog allows.
        SaveManager.Backup(SaveDir, SnapDir, "before-launch", auto: true);
        SaveManager.Backup(SaveDir, SnapDir, "before-launch-2", auto: true);
        SaveManager.Prune(SnapDir, keepLastAuto: 1);

        Assert.True(File.Exists(snap.Path));
    }

    [Fact]
    public void Restoring_the_snapshot_puts_the_replaced_target_back()
    {
        var plan = Plan();
        var (snap, _) = SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]);
        SaveManager.Restore(snap.Path, SaveDir, SnapDir);
        Assert.Equal("EMPTY-PROFILE", Read("ER0000.sl2"));
        Assert.Equal("COOP-CHARACTERS", Read("ER0000.co2"));
    }

    [Fact]
    public void A_new_target_also_gets_a_snapshot_whose_restore_removes_it()
    {
        File.Delete(Path.Combine(SaveDir, "ER0000.sl2"));
        var plan = Plan(targetExists: false);
        var (snap, _) = SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]);
        Assert.True(File.Exists(Path.Combine(SaveDir, "ER0000.sl2")));

        SaveManager.Restore(snap.Path, SaveDir, SnapDir);
        Assert.False(File.Exists(Path.Combine(SaveDir, "ER0000.sl2")));
    }

    [Fact]
    public void A_copy_that_fails_leaves_the_target_as_it_was_and_no_temp_behind()
    {
        var plan = Plan();
        // Something (Steam Cloud, the game) holds the target open without sharing delete: the rename can't land.
        using (new FileStream(Path.Combine(SaveDir, "ER0000.sl2"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => SaveOfferCopy.Run(SaveDir, SnapDir, plan, plan.Sources[0]));

        Assert.Equal("EMPTY-PROFILE", Read("ER0000.sl2"));
        Assert.Empty(Directory.GetFiles(SaveDir, "*.tmp-*"));
    }

    [Fact]
    public void Clone_to_type_is_atomic_on_its_own_too()
    {
        File.WriteAllText(Path.Combine(SaveDir, "ER0000.err"), "REFORGED");
        using (new FileStream(Path.Combine(SaveDir, "ER0000.err"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => SaveManager.CloneToType(SaveDir, "ER0000.co2", ".err", overwrite: true));
        Assert.Equal("REFORGED", Read("ER0000.err"));
        Assert.Empty(Directory.GetFiles(SaveDir, "*.tmp-*"));
    }
}
