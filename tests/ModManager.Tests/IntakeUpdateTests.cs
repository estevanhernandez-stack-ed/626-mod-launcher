using ModManager.Core;

namespace ModManager.Tests;

public class IntakeUpdateTests
{
    [Fact]
    public void Plan_types_and_updated_result_exist()
    {
        var col = new IntakeCollision("ersc.dll", "ersc.dll", @"C:\game\ersc.dll", @"C:\drop\ersc.dll");
        var plan = new IntakePlan(new[] { new IntakeItem("new.dll", "new.dll", @"C:\drop\new.dll") }, new[] { col }, Array.Empty<SkippedItem>());
        Assert.Equal("new.dll", plan.ToAdd[0].Name);
        Assert.Equal("ersc.dll", plan.Collisions[0].Name);

        var result = new IntakeResult();
        result.Updated.Add("ersc.dll");
        Assert.Single(result.Updated);
    }

    [Fact]
    public void ReplacedStore_moves_old_file_into_timestamped_backup_recoverably()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-rs-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(root, "game");
        var backup = Path.Combine(root, "data", "replaced");
        Directory.CreateDirectory(live);
        var existing = Path.Combine(live, "ersc.dll");
        File.WriteAllText(existing, "OLD");

        var dir = ReplacedStore.NewBatch(backup);
        var saved = ReplacedStore.Backup(existing, "ersc.dll", dir);

        Assert.False(File.Exists(existing));
        Assert.True(File.Exists(saved));
        Assert.Equal("OLD", File.ReadAllText(saved));
        try { Directory.Delete(root, true); } catch { }
    }
}
