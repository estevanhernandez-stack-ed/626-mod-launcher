using ModManager.Core;

namespace ModManager.Tests;

// The data dir holds the ONLY copy of real user files — disabled mods, held framework proxies,
// archived Vortex takeovers, installed tool binaries. Moving it is the single most dangerous thing
// an edit can trigger, so it follows validate-then-extract: Plan cannot write, Execute cannot decide.
public class DataDirMovePlanTests
{
    private static string Src(params string[] names)
    {
        var d = TestSupport.TempDir("ddm-src-");
        foreach (var n in names) TestSupport.Write(Path.Combine(d, n), n);
        return d;
    }

    [Fact]
    public void A_plan_reports_the_real_file_count_and_size()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");

        var plan = DataDirMove.Plan(from, to);

        Assert.Equal(3, plan.FileCount);
        Assert.True(plan.TotalBytes > 0);
        Assert.True(plan.CanProceed);
    }

    // Never merge two data dirs — the same stance the legacy MigrateDataDir already takes. A merge
    // would interleave two games' disabled mods with no way to tell them apart afterwards.
    [Fact]
    public void A_non_empty_target_is_refused()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");
        TestSupport.Write(Path.Combine(to, "occupied.txt"), "x");

        var plan = DataDirMove.Plan(from, to);

        Assert.False(plan.CanProceed);
        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void An_empty_target_directory_is_not_a_refusal()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");   // exists, but empty

        Assert.True(DataDirMove.Plan(from, to).CanProceed);
    }

    [Fact]
    public void A_missing_source_is_nothing_to_do_rather_than_an_error()
    {
        var from = Path.Combine(TestSupport.TempDir("ddm-"), "never-existed");
        var to = Path.Combine(TestSupport.TempDir("ddm-"), "moved");

        var plan = DataDirMove.Plan(from, to);

        Assert.Equal(DataDirMoveKind.Nothing, plan.Kind);
        Assert.True(plan.CanProceed);
        Assert.Equal(0, plan.FileCount);
    }

    [Fact]
    public void Moving_a_folder_onto_itself_is_nothing_to_do()
    {
        var from = Src("a.txt");

        Assert.Equal(DataDirMoveKind.Nothing, DataDirMove.Plan(from, from).Kind);
    }

    // Same volume with no target gets an atomic rename: instant, and there is no window in which the
    // data exists in neither place. That is strictly safer than copy-then-delete, so it is preferred.
    [Fact]
    public void Same_volume_with_an_absent_target_plans_a_rename()
    {
        var from = Src("a.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(DataDirMoveKind.Rename, DataDirMove.Plan(from, to).Kind);
    }

    // Plan is inspection only. If planning could write, a user clicking Cancel would already have
    // changed their install.
    [Fact]
    public void Planning_writes_nothing()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var before = Directory.GetFileSystemEntries(from, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        DataDirMove.Plan(from, to);

        Assert.Equal(before, Directory.GetFileSystemEntries(from, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
        Assert.False(Directory.Exists(to));
    }
}

// Execute is the only thing here that writes. The ordering IS the safety: the source is never
// deleted until the target is verified in place, so any mid-flight failure leaves the user exactly
// where they started.
public class DataDirMoveExecuteTests
{
    private static string Src(params string[] names)
    {
        var d = TestSupport.TempDir("ddm-src-");
        foreach (var n in names) TestSupport.Write(Path.Combine(d, n), "content-of-" + n);
        return d;
    }

    [Fact]
    public void A_rename_moves_every_file_and_leaves_no_source()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(from));
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(to, "a.txt")));
        Assert.Equal("content-of-sub/b.txt", File.ReadAllText(Path.Combine(to, "sub", "b.txt")));
    }

    // The cross-volume path is exercised by constructing the plan directly, so the suite does not
    // need two volumes to run. The behaviour under test is the copy-verify-swap-delete sequence,
    // which is identical wherever the two paths happen to live.
    [Fact]
    public void A_copy_move_reproduces_the_whole_tree_and_removes_the_source()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var planned = DataDirMove.Plan(from, to);
        var forced = planned with { Kind = DataDirMoveKind.CopyVerifyDelete };

        var result = DataDirMove.Execute(forced);

        Assert.True(result.Moved);
        Assert.True(result.SourceRemoved);
        Assert.False(Directory.Exists(from));
        Assert.Equal("content-of-sub/deep/c.txt", File.ReadAllText(Path.Combine(to, "sub", "deep", "c.txt")));
        Assert.Equal(3, Directory.GetFiles(to, "*", SearchOption.AllDirectories).Length);
    }

    // THE reversibility test, and the reason Execute is shaped the way it is. A file held open with
    // no sharing is a real failure mode (a running game, an antivirus scan), not a contrived one.
    [Fact]
    public void A_failure_mid_copy_leaves_the_source_intact_and_the_target_absent()
    {
        var from = Src("a.txt", "locked.txt", "c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        DataDirMoveResult result;
        using (File.Open(Path.Combine(from, "locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = DataDirMove.Execute(forced);
        }

        Assert.False(result.Moved);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(to));                                   // no half-built target
        Assert.Equal(3, Directory.GetFiles(from, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(from, "a.txt")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(to)!, "*.moving-*"));   // staging cleaned
    }

    [Fact]
    public void A_refused_plan_is_never_executed()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");
        TestSupport.Write(Path.Combine(to, "occupied.txt"), "x");

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.False(result.Moved);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(Path.Combine(from, "a.txt")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(to, "occupied.txt")));
    }

    [Fact]
    public void A_nothing_to_do_plan_succeeds_without_writing()
    {
        var from = Path.Combine(TestSupport.TempDir("ddm-"), "never-existed");
        var to = Path.Combine(TestSupport.TempDir("ddm-"), "moved");

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(to));
    }
}
