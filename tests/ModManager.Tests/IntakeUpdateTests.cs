using System.Linq;
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

    [Fact]
    public void DirectInject_Plan_splits_new_from_colliding_in_play_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-dip-" + Guid.NewGuid().ToString("N"));
        var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
        File.WriteAllText(Path.Combine(play, "ersc.dll"), "OLD");

        var zipPath = Path.Combine(root, "seamless.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            z.CreateEntry("ersc.dll").Open().Dispose();
            z.CreateEntry("launch_elden_ring_seamlesscoop.exe").Open().Dispose();
        }

        var plan = DirectInject.Plan(play, new[] { zipPath });
        Assert.Contains(plan.Collisions, c => c.RelPath.Equals("ersc.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.ToAdd, a => a.RelPath.EndsWith("seamlesscoop.exe", StringComparison.OrdinalIgnoreCase));
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void DirectInject_Execute_updates_whole_set_when_all_chosen_and_backs_up()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-die-" + Guid.NewGuid().ToString("N"));
        var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
        var backup = Path.Combine(root, "data", "replaced");
        File.WriteAllText(Path.Combine(play, "ersc.dll"), "OLD-DLL");
        File.WriteAllText(Path.Combine(play, "ersc_settings.ini"), "OLD-INI");

        var zipPath = Path.Combine(root, "seamless.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("ersc.dll").Open())) w.Write("NEW-DLL");
            using (var w = new StreamWriter(z.CreateEntry("ersc_settings.ini").Open())) w.Write("NEW-INI");
        }

        var plan = DirectInject.Plan(play, new[] { zipPath });
        var replaceAll = plan.Collisions.Select(c => c.RelPath).ToHashSet();
        var result = DirectInject.Execute(play, backup, plan, replaceAll);

        Assert.Equal("NEW-DLL", File.ReadAllText(Path.Combine(play, "ersc.dll")));
        Assert.Equal("NEW-INI", File.ReadAllText(Path.Combine(play, "ersc_settings.ini")));
        Assert.Equal(2, result.Updated.Count);
        Assert.True(Directory.GetFiles(backup, "*", SearchOption.AllDirectories).Length >= 2);
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void ExecuteIntake_rolls_back_when_replace_copy_fails_leaving_original()
    {
        // A collision whose incoming source is a bogus zip entry -> backup moves the old file out,
        // then the copy throws. The original must be restored (not left missing), counted skipped.
        var root = Path.Combine(Path.GetTempPath(), "mmb-rb-" + Guid.NewGuid().ToString("N"));
        var ctx = FlatCtx(root, "dll");
        var mods = ctx.Locations[0].Abs;
        File.WriteAllText(Path.Combine(mods, "old.dll"), "ORIGINAL");

        var bogus = new IntakeCollision("old.dll", "old.dll", Path.Combine(mods, "old.dll"),
            Path.Combine(root, "nope.zip") + "!ghost.dll"); // zip doesn't exist -> CopyPlanned throws
        var plan = new IntakePlan(Array.Empty<IntakeItem>(), new[] { bogus }, Array.Empty<SkippedItem>());

        var result = Scanner.ExecuteIntake(plan, new HashSet<string> { "old.dll" }, ctx);

        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(mods, "old.dll"))); // restored, not lost
        Assert.DoesNotContain("old.dll", result.Updated);
        Assert.Contains(result.Skipped, s => s.Name == "old.dll");
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void DirectInject_Execute_rolls_back_when_replace_copy_fails_leaving_original()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-dirb-" + Guid.NewGuid().ToString("N"));
        var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
        var backup = Path.Combine(root, "data", "replaced");
        File.WriteAllText(Path.Combine(play, "ersc.dll"), "ORIGINAL");

        var bogus = new IntakeCollision("ersc.dll", "ersc.dll", Path.Combine(play, "ersc.dll"),
            Path.Combine(root, "nope.zip") + "!ghost.dll");
        var plan = new IntakePlan(Array.Empty<IntakeItem>(), new[] { bogus }, Array.Empty<SkippedItem>());

        var result = DirectInject.Execute(play, backup, plan, new HashSet<string> { "ersc.dll" });

        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(play, "ersc.dll")));
        Assert.DoesNotContain("ersc.dll", result.Updated);
        Assert.Contains(result.Skipped, s => s.Name == "ersc.dll");
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void DirectInject_Execute_writes_backup_manifest_with_provenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmb-man-" + Guid.NewGuid().ToString("N"));
        var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
        var backup = Path.Combine(root, "data", "replaced");
        File.WriteAllText(Path.Combine(play, "ersc.dll"), "OLD");
        var zip = Path.Combine(root, "s.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("ersc.dll").Open())) w.Write("NEW");

        var plan = DirectInject.Plan(play, new[] { zip });
        DirectInject.Execute(play, backup, plan, plan.Collisions.Select(c => c.RelPath).ToHashSet());

        var manifest = Directory.GetFiles(backup, "__626replaced.json", SearchOption.AllDirectories);
        Assert.Single(manifest);
        var json = File.ReadAllText(manifest[0]);
        Assert.Contains("ersc.dll", json); // relPath/originalPath recorded
        try { Directory.Delete(root, true); } catch { }
    }
}
