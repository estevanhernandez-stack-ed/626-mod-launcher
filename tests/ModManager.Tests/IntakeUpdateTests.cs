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

    private static GameContext FlatCtx(string root, params string[] exts)
    {
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(mods);
        return new GameContext
        {
            Game = new GameEntry { Id = "test", GameName = "Test Game", GameRoot = root },
            GameRoot = root,
            DataDir = Path.Combine(root, "_data"),
            DisabledRoot = Path.Combine(root, "_data", "disabled"),
            ProfilesDir = Path.Combine(root, "_data", "profiles"),
            SavesDir = Path.Combine(root, "_data", "saves"),
            ClassificationPath = Path.Combine(root, "_data", "classification.json"),
            MetadataPath = Path.Combine(root, "_data", "metadata.json"),
            LoadOrderPath = Path.Combine(root, "_data", "loadorder.json"),
            Exts = exts,
            FileRe = new System.Text.RegularExpressions.Regex(".*"),
            Locations = new[] { new ModLocationCtx("mods", "Mods", mods, Array.Empty<string>(), true) },
            GroupingRule = "",
            ScanSubfolders = "",
        };
    }

    [Fact]
    public void Scanner_PlanIntake_splits_new_from_colliding()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-sp-" + Guid.NewGuid().ToString("N"));
        var ctx = FlatCtx(root, "dll");
        File.WriteAllText(Path.Combine(ctx.Locations[0].Abs, "old.dll"), "INSTALLED");
        var drop = Path.Combine(root, "drop");
        Directory.CreateDirectory(drop);
        File.WriteAllText(Path.Combine(drop, "old.dll"), "NEWVER");
        File.WriteAllText(Path.Combine(drop, "fresh.dll"), "NEW");

        var plan = Scanner.PlanIntake(new[] { Path.Combine(drop, "old.dll"), Path.Combine(drop, "fresh.dll") }, ctx);

        Assert.Contains(plan.ToAdd, a => a.RelPath == "fresh.dll");
        Assert.Contains(plan.Collisions, c => c.Name == "old.dll");
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void Scanner_ExecuteIntake_replaces_chosen_backs_up_old_skips_unchosen()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-se-" + Guid.NewGuid().ToString("N"));
        var ctx = FlatCtx(root, "dll");
        var mods = ctx.Locations[0].Abs;
        File.WriteAllText(Path.Combine(mods, "old.dll"), "OLD");
        File.WriteAllText(Path.Combine(mods, "keep.dll"), "KEEP");
        var drop = Path.Combine(root, "drop"); Directory.CreateDirectory(drop);
        File.WriteAllText(Path.Combine(drop, "old.dll"), "NEW");
        File.WriteAllText(Path.Combine(drop, "keep.dll"), "NEW2");
        File.WriteAllText(Path.Combine(drop, "fresh.dll"), "ADD");

        var paths = new[] { "old.dll", "keep.dll", "fresh.dll" }.Select(f => Path.Combine(drop, f));
        var plan = Scanner.PlanIntake(paths, ctx);
        var result = Scanner.ExecuteIntake(plan, new HashSet<string> { "old.dll" }, ctx);

        Assert.Equal("NEW", File.ReadAllText(Path.Combine(mods, "old.dll")));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(mods, "keep.dll")));
        Assert.Equal("ADD", File.ReadAllText(Path.Combine(mods, "fresh.dll")));
        Assert.Contains("old.dll", result.Updated);
        Assert.Contains("fresh.dll", result.Added);
        Assert.Contains(result.Skipped, s => s.Name == "keep.dll");
        var backups = Directory.GetFiles(Path.Combine(ctx.DataDir, "replaced"), "old.dll", SearchOption.AllDirectories);
        Assert.True(backups.Length == 1 && File.ReadAllText(backups[0]) == "OLD");
        try { Directory.Delete(root, true); } catch { }
    }
}
