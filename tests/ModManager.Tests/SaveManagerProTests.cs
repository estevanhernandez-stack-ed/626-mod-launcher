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
}
