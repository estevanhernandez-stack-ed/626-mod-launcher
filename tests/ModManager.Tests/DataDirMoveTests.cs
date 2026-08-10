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

    // The old private HasRoom compared raw bytes, so a move that exactly filled the disk was blessed
    // and left the user with zero free space. SpaceCheck adds the headroom, and the refusal has to
    // name the real numbers — "not enough space" with no figures gives the user nothing to act on.
    [Fact]
    public void A_space_refusal_names_what_the_move_needs_and_what_is_free()
    {
        var payload = 40L << 30;                                        // 40 GiB of disabled mods
        var space = SpaceCheck.Evaluate(@"D:\", payload, 2L << 30);     // only 2 GiB free
        Assert.False(space.Ok);

        var refusal = DataDirMove.SpaceRefusal(payload, space);

        Assert.NotNull(refusal);
        Assert.Contains(@"D:\", refusal);
        Assert.Contains(Mb(payload), refusal);                  // what you asked to move
        Assert.Contains(Mb(space.RequiredBytes), refusal);      // what it actually needs, headroom included
        Assert.Contains(Mb(2L << 30), refusal);                 // what is really there
        Assert.EndsWith(".", refusal);
    }

    // Headroom is the whole point of routing through SpaceCheck: a payload that fits byte-for-byte
    // still leaves the user with a full disk, and the old raw-byte check waved it through.
    [Fact]
    public void A_move_that_only_just_fits_is_refused_for_want_of_headroom()
    {
        var payload = 40L << 30;
        var space = SpaceCheck.Evaluate(@"D:\", payload, payload);      // fits exactly, nothing spare

        Assert.NotNull(DataDirMove.SpaceRefusal(payload, space));
    }

    // Unknowable free space is NOT a reason to refuse — the behaviour the old HasRoom catch had, kept
    // deliberately. SpaceCheck reports a network share or an unreadable volume as AvailableBytes = -1
    // and not-Ok; refusing on that would block every legitimate move to a NAS.
    [Fact]
    public void Unreadable_free_space_is_not_a_refusal()
    {
        var space = new SpaceCheck.Result(false, 40L << 30, -1, @"\\nas\share");

        Assert.Null(DataDirMove.SpaceRefusal(40L << 30, space));
    }

    [Fact]
    public void Ample_free_space_is_not_a_refusal()
    {
        var payload = 1L << 20;
        var space = SpaceCheck.Evaluate(@"D:\", payload, 500L << 30);

        Assert.Null(DataDirMove.SpaceRefusal(payload, space));
    }

    // Formatted here rather than asserting literals, so the assertions do not hinge on the machine's
    // group separator.
    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:N0} MB";

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
        // Counting proves "still there"; reading proves "still intact". This is THE reversibility test
        // for the most dangerous operation on the branch, so it reads every byte back.
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(from, "a.txt")));
        Assert.Equal("content-of-locked.txt", File.ReadAllText(Path.Combine(from, "locked.txt")));
        Assert.Equal("content-of-c.txt", File.ReadAllText(Path.Combine(from, "c.txt")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(to)!, "*.moving-*"));   // staging cleaned
    }

    // Moving a game's data dir is exactly when that game might be running, so a file in use is the
    // likeliest failure this path will ever see. The user needs "close the game", not the raw Win32
    // sentence with a full path in it — which tells them nothing they can act on.
    [Fact]
    public void A_file_in_use_asks_you_to_close_the_game_rather_than_reporting_a_raw_io_error()
    {
        var from = Src("a.txt", "locked.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        DataDirMoveResult result;
        using (File.Open(Path.Combine(from, "locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = DataDirMove.Execute(forced);
        }

        var error = result.Error ?? "";
        Assert.False(result.Moved);
        Assert.Contains("in use", error);
        Assert.Contains("Close the game", error);
        Assert.DoesNotContain("locked.txt", error);   // the raw IOException names the file; this is not it
        Assert.EndsWith(".", error);

        // The refusal changes the words, never the reversibility: staging gone, source untouched.
        Assert.False(Directory.Exists(to));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(to)!, "*.moving-*"));
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(from, "a.txt")));
        Assert.Equal("content-of-locked.txt", File.ReadAllText(Path.Combine(from, "locked.txt")));
    }

    // Plan blesses an existing EMPTY target (a non-empty one is refused outright), but an existing
    // target also forces the copy path — and Directory.Move onto a path that already exists throws on
    // Windows. Without the empty-shell removal in Execute, a move Plan reported as fine always failed.
    // A folder picker hands back an existing folder, so this is the ordinary case, not an edge one.
    [Fact]
    public void An_existing_empty_target_is_filled_rather_than_failing()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = TestSupport.TempDir("ddm-to-");   // exists, empty
        var plan = DataDirMove.Plan(from, to);

        Assert.True(plan.CanProceed);
        Assert.Equal(DataDirMoveKind.CopyVerifyDelete, plan.Kind);   // an existing target forces the copy path

        var result = DataDirMove.Execute(plan);

        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(from));
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(to, "a.txt")));
        Assert.Equal("content-of-sub/b.txt", File.ReadAllText(Path.Combine(to, "sub", "b.txt")));
        Assert.Equal(2, Directory.GetFiles(to, "*", SearchOption.AllDirectories).Length);
    }

    // Failing to delete the source is tidy-up failing, not the move failing. Reporting it as a failed
    // move would invite a caller to retry onto a now-populated target, which Plan correctly refuses —
    // so the user would be stuck. FileShare.Read is permissive enough for the copy to read the file
    // and restrictive enough that deleting it afterwards cannot succeed.
    [Fact]
    public void A_source_that_cannot_be_deleted_is_still_a_successful_move()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        DataDirMoveResult result;
        using (File.Open(Path.Combine(from, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = DataDirMove.Execute(forced);
        }

        Assert.True(result.Moved);
        Assert.False(result.SourceRemoved);   // a duplicate on disk, never a lost file
        Assert.Null(result.Error);
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(to, "a.txt")));
        Assert.Equal("content-of-sub/b.txt", File.ReadAllText(Path.Combine(to, "sub", "b.txt")));
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

    // A multi-gigabyte move behind a bare spinner is indistinguishable from a hang, and the one thing
    // the user must not do is kill the app mid-move. Progress<T> dispatches via SynchronizationContext
    // (captured context, or the thread pool otherwise) — always asynchronously — so the assertions
    // must wait for the callbacks to actually land, not just lock the list they land in.
    [Fact]
    public void A_copy_move_reports_progress_for_every_file()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };
        var seen = new List<(int Copied, int Total)>();

        var result = DataDirMove.Execute(forced, new Progress<(int, int)>(p => { lock (seen) seen.Add(p); }));

        Assert.True(result.Moved);
        Assert.True(SpinWait.SpinUntil(() => { lock (seen) return seen.Count == 3; }, TimeSpan.FromSeconds(5)),
                    "progress callbacks did not arrive within 5s");
        lock (seen) Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, seen);
    }

    // A rename is instantaneous; reporting a fake tick would only invite a progress bar that lies. A
    // bare Assert.Empty right after Execute returns would pass even against an implementation that DID
    // tick on the rename path, since Progress<T> dispatch is asynchronous — give it a drain window
    // first so the test can actually fail against the behaviour it exists to forbid.
    [Fact]
    public void A_rename_reports_no_progress()
    {
        var from = Src("a.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));
        var seen = new List<(int, int)>();

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to), new Progress<(int, int)>(p => { lock (seen) seen.Add(p); }));

        Assert.True(result.Moved);
        SpinWait.SpinUntil(() => { lock (seen) return seen.Count > 0; }, TimeSpan.FromSeconds(1));
        lock (seen) Assert.Empty(seen);
    }

    // The plan is taken before the dialog and before the confirm; the copy runs afterwards. If a file
    // lands in (or leaves) the data dir in between, a denominator taken from plan.FileCount reads
    // "4 of 3 files" — an absurd number on the one operation the user must not kill. The copy is
    // correct either way (Verify re-walks the source and rolls back on any mismatch); the words are
    // what this pins. A stale plan is forced here rather than raced for, so the test is deterministic.
    [Fact]
    public void Progress_counts_against_the_live_file_set_not_a_stale_plan()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var stale = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete, FileCount = 99 };
        var seen = new List<(int Copied, int Total)>();

        var result = DataDirMove.Execute(stale, new Progress<(int, int)>(p => { lock (seen) seen.Add(p); }));

        Assert.True(result.Moved);
        Assert.True(SpinWait.SpinUntil(() => { lock (seen) return seen.Count == 3; }, TimeSpan.FromSeconds(5)),
                    "progress callbacks did not arrive within 5s");
        lock (seen) Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, seen);
    }

    // The default keeps every existing call site and all current tests compiling unchanged.
    [Fact]
    public void A_null_progress_callback_changes_nothing()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        var result = DataDirMove.Execute(forced, progress: null);

        Assert.True(result.Moved);
        Assert.Equal(2, Directory.GetFiles(to, "*", SearchOption.AllDirectories).Length);
    }

    // CopyTreeReporting's own doc comment states the load-bearing case: Verify walks GetFiles only, so
    // a vanished empty directory is uncaught — and the source is deleted immediately afterwards. That
    // guarantee lived in a private helper covered by nothing; this pins it down.
    [Fact]
    public void An_empty_subdirectory_survives_a_copy_move()
    {
        var from = Src("a.txt");
        Directory.CreateDirectory(Path.Combine(from, "empty-sub"));
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        var result = DataDirMove.Execute(forced);

        Assert.True(result.Moved);
        Assert.True(Directory.Exists(Path.Combine(to, "empty-sub")));
    }
}
