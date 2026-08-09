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
