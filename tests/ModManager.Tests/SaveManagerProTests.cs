using ModManager.Core;

namespace ModManager.Tests;

// The professional save-manager additions: auto-tagged snapshots, retention prune, per-type
// snapshot inspection + restore. All file logic, exercised against temp dirs.
public class SaveManagerProTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-smp-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(_root, "save");
    private string Snaps => Path.Combine(_root, "snaps");

    public SaveManagerProTests()
    {
        Directory.CreateDirectory(Save);
        File.WriteAllText(Path.Combine(Save, "ER0000.sl2"), "S");
    }
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public void Auto_backup_is_flagged_user_backup_is_not()
    {
        var auto = SaveManager.Backup(Save, Snaps, "before-launch", auto: true);
        var user = SaveManager.Backup(Save, Snaps, "my checkpoint");
        Assert.True(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == auto.FileName).IsAuto);
        Assert.False(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == user.FileName).IsAuto);
    }

    [Fact]
    public void A_user_label_cannot_masquerade_as_auto()
    {
        var sneaky = SaveManager.Backup(Save, Snaps, "auto-before-launch"); // user, not auto
        Assert.False(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == sneaky.FileName).IsAuto);
    }

    [Fact]
    public void Prune_keeps_all_user_and_newest_N_auto()
    {
        SaveManager.Backup(Save, Snaps, "keep me");        // user — must survive
        for (var i = 0; i < 5; i++)
        {
            System.Threading.Thread.Sleep(50);             // distinct timestamps for ordering
            SaveManager.Backup(Save, Snaps, "before-launch", auto: true);
        }

        SaveManager.Prune(Snaps, keepLastAuto: 2);

        var left = SaveManager.ListSnapshots(Snaps);
        Assert.Equal(1, left.Count(s => !s.IsAuto));       // user backup kept
        Assert.Equal(2, left.Count(s => s.IsAuto));        // newest 2 autos kept
    }

    [Fact]
    public void TypesInSnapshot_reports_declared_types_present()
    {
        File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "C"); // Seamless save also present
        var snap = SaveManager.Backup(Save, Snaps, "two types");
        var types = GameProfiles.Resolve("fromsoft", null).SaveTypes;

        var present = SaveManager.TypesInSnapshot(snap.Path, types).Select(t => t.Extension).ToList();
        Assert.Contains(".sl2", present);
        Assert.Contains(".co2", present);
        Assert.DoesNotContain(".err", present); // not in the save folder
    }

    [Fact]
    public void RestoreType_restores_only_the_chosen_type_and_snapshots_first()
    {
        File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "COOP-OLD");
        var snap = SaveManager.Backup(Save, Snaps, "checkpoint");
        File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "COOP-NEW");    // co2 changed
        File.WriteAllText(Path.Combine(Save, "ER0000.sl2"), "VANILLA-NEW"); // sl2 changed

        SaveManager.RestoreType(snap.Path, Save, Snaps, ".co2");

        Assert.Equal("COOP-OLD", File.ReadAllText(Path.Combine(Save, "ER0000.co2")));    // co2 rolled back
        Assert.Equal("VANILLA-NEW", File.ReadAllText(Path.Combine(Save, "ER0000.sl2"))); // sl2 untouched
        Assert.Contains(SaveManager.ListSnapshots(Snaps), s => s.IsAuto);                // snapshotted first
    }
}
