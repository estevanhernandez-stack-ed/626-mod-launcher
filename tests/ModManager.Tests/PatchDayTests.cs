using ModManager.Core;

namespace ModManager.Tests;

public class PatchDayTests
{
    private static readonly DateTime ExeUtc = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LoaderUtc = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static StaleLoader Stale(string name = "REFramework", DateTime? utc = null)
        => new(name, @"C:\Games\X\dinput8.dll", utc ?? LoaderUtc, "https://example.test/ref");

    // A report with one stale loader: the game exe is newer than the loader file by months.
    private static StaleLoaderReport StaleReport(params StaleLoader[] loaders)
        => new(@"C:\Games\X\game.exe", ExeUtc, loaders.Length == 0 ? new[] { Stale() } : loaders);

    // The loader file is no longer older than the exe, so Find returns an exe and no loaders.
    private static StaleLoaderReport FreshReport() => new(@"C:\Games\X\game.exe", ExeUtc, Array.Empty<StaleLoader>());

    private static PatchDayRecord Rec(
        string build = "200", string? restore = "rp-1", PatchDayChoice choice = PatchDayChoice.None, bool snoozed = false)
        => new() { BuildId = build, DetectedUtc = Now, RestorePointId = restore, Choice = choice, SnoozedUntilChange = snoozed };

    private static PatchDayInput Input(
        string? baseline = "100", string? live = "200", PatchDayRecord? record = null, StaleLoaderReport? loaders = null,
        IReadOnlyList<LoaderRelease>? releases = null, LaunchMode mode = LaunchMode.Modded)
        => new(baseline, live, record, loaders ?? StaleReport(), releases ?? Array.Empty<LoaderRelease>(), mode);

    private static LoaderRelease Release(string loader = "REFramework", DateTime? published = null)
        => new(loader, "v1.5.9.2", published ?? new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc), "https://example.test/ref/releases/v1.5.9.2");

    // ----- no record: detection -----

    [Fact]
    public void First_sighting_with_no_baseline_is_quiet()
        => Assert.Equal(PatchDayState.Quiet, PatchDay.Evaluate(Input(baseline: null)).State);

    [Fact]
    public void Same_build_is_quiet_even_with_a_stale_loader()
        => Assert.Equal(PatchDayState.Quiet, PatchDay.Evaluate(Input(baseline: "200", live: "200")).State);

    [Fact]
    public void No_live_build_is_quiet()
        => Assert.Equal(PatchDayState.Quiet, PatchDay.Evaluate(Input(live: null)).State);

    [Fact]
    public void Build_changed_with_a_loader_newer_than_the_exe_is_quiet() // the existing UPDATED banner owns this case
        => Assert.Equal(PatchDayState.Quiet, PatchDay.Evaluate(Input(loaders: FreshReport())).State);

    [Fact]
    public void Build_changed_with_a_stale_version_locked_loader_is_patched()
    {
        var v = PatchDay.Evaluate(Input());

        Assert.Equal(PatchDayState.Patched, v.State);
        Assert.False(v.Hidden);
        Assert.Null(v.Record); // nothing is written until the caller runs Detect
        Assert.True(v.NeedsDetect);
        Assert.Equal("REFramework", v.Loader!.Name);
        Assert.Equal(ExeUtc, v.GameExeUtc); // the chip's "updated on" date
        Assert.Equal(PatchDayChoice.None, v.Choice);
    }

    // ----- with a record -----

    [Fact]
    public void A_null_restore_point_still_yields_patched()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(restore: null) with { RestorePointError = "disk full" }));

        Assert.Equal(PatchDayState.Patched, v.State);
        Assert.Equal("disk full", v.Record!.RestorePointError);
    }

    [Fact]
    public void Modded_choice_stays_patched_and_exposes_the_choice()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(choice: PatchDayChoice.Modded)));

        Assert.Equal(PatchDayState.Patched, v.State);
        Assert.Equal(PatchDayChoice.Modded, v.Choice);
    }

    [Fact]
    public void Vanilla_choice_in_vanilla_mode_is_playing_vanilla()
        => Assert.Equal(PatchDayState.PlayingVanilla,
            PatchDay.Evaluate(Input(record: Rec(choice: PatchDayChoice.Vanilla), mode: LaunchMode.Vanilla)).State);

    [Fact]
    public void Vanilla_choice_with_the_mods_back_in_place_is_patched_again() // mode is what is true on disk
        => Assert.Equal(PatchDayState.Patched,
            PatchDay.Evaluate(Input(record: Rec(choice: PatchDayChoice.Vanilla), mode: LaunchMode.Modded)).State);

    [Fact]
    public void Vanilla_mode_without_a_vanilla_choice_is_not_playing_vanilla()
        => Assert.Equal(PatchDayState.Patched,
            PatchDay.Evaluate(Input(record: Rec(choice: PatchDayChoice.None), mode: LaunchMode.Vanilla)).State);

    [Fact]
    public void A_release_newer_than_the_loader_file_is_update_available()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(), releases: new[] { Release() }));

        Assert.Equal(PatchDayState.UpdateAvailable, v.State);
        Assert.Equal("v1.5.9.2", v.Release!.Version);
        Assert.Equal("REFramework", v.Loader!.Name);
    }

    [Fact]
    public void A_release_older_than_the_loader_file_is_not_news()
    {
        var old = Release(published: LoaderUtc.AddDays(-30));

        Assert.Equal(PatchDayState.Patched, PatchDay.Evaluate(Input(record: Rec(), releases: new[] { old })).State);
    }

    [Fact]
    public void Release_matching_is_by_loader_name_ignoring_case()
    {
        Assert.Equal(PatchDayState.UpdateAvailable,
            PatchDay.Evaluate(Input(record: Rec(), releases: new[] { Release("refRAMEWORK") })).State);
        Assert.Equal(PatchDayState.Patched,
            PatchDay.Evaluate(Input(record: Rec(), releases: new[] { Release("UE4SS") })).State);
    }

    [Fact]
    public void Update_available_wins_over_playing_vanilla() // the actionable news; its chip also offers Bring back
    {
        var v = PatchDay.Evaluate(Input(
            record: Rec(choice: PatchDayChoice.Vanilla), mode: LaunchMode.Vanilla, releases: new[] { Release() }));

        Assert.Equal(PatchDayState.UpdateAvailable, v.State);
    }

    [Fact]
    public void The_newest_release_for_the_loader_is_the_one_offered()
    {
        var older = Release(published: new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc)) with { Version = "v1" };
        var newer = Release(published: new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)) with { Version = "v2" };

        var v = PatchDay.Evaluate(Input(record: Rec(), releases: new[] { older, newer }));

        Assert.Equal("v2", v.Release!.Version);
    }

    [Fact]
    public void Only_the_stale_loader_with_a_release_drives_update_available()
    {
        var report = StaleReport(Stale("UE4SS"), Stale("REFramework"));

        var v = PatchDay.Evaluate(Input(record: Rec(), loaders: report, releases: new[] { Release("REFramework") }));

        Assert.Equal(PatchDayState.UpdateAvailable, v.State);
        Assert.Equal("REFramework", v.Loader!.Name);
    }

    [Fact]
    public void A_loader_file_newer_than_the_exe_is_loader_updated()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(choice: PatchDayChoice.Vanilla), loaders: FreshReport()));

        Assert.Equal(PatchDayState.LoaderUpdated, v.State);
        Assert.NotNull(v.Record); // nothing clears on its own
    }

    [Fact]
    public void Loader_updated_is_not_claimed_when_the_loader_is_set_aside_for_vanilla()
    {
        // Playing vanilla steps the loader's files aside, so a live scan finds none. That is "no information",
        // not "the loader is newer than the game".
        var v = PatchDay.Evaluate(Input(
            record: Rec(choice: PatchDayChoice.Vanilla), loaders: FreshReport(), mode: LaunchMode.Vanilla));

        Assert.Equal(PatchDayState.PlayingVanilla, v.State);
    }

    [Fact]
    public void No_executable_found_is_no_information_not_loader_updated()
        => Assert.Equal(PatchDayState.Patched,
            PatchDay.Evaluate(Input(record: Rec(), loaders: StaleLoaderReport.None)).State);

    // ----- snooze -----

    [Fact]
    public void Snooze_hides_patched_and_playing_vanilla_on_the_same_build()
    {
        Assert.True(PatchDay.Evaluate(Input(record: Rec(snoozed: true))).Hidden);
        Assert.True(PatchDay.Evaluate(Input(
            record: Rec(choice: PatchDayChoice.Vanilla, snoozed: true), mode: LaunchMode.Vanilla)).Hidden);
    }

    [Fact]
    public void Snooze_does_not_hide_when_nothing_is_snoozed()
        => Assert.False(PatchDay.Evaluate(Input(record: Rec())).Hidden);

    [Fact]
    public void Snooze_ends_when_an_update_arrives()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(snoozed: true), releases: new[] { Release() }));

        Assert.Equal(PatchDayState.UpdateAvailable, v.State);
        Assert.False(v.Hidden);
    }

    [Fact]
    public void Snooze_ends_when_the_loader_file_changes()
    {
        var v = PatchDay.Evaluate(Input(record: Rec(snoozed: true), loaders: FreshReport()));

        Assert.Equal(PatchDayState.LoaderUpdated, v.State);
        Assert.False(v.Hidden);
    }

    [Fact]
    public void Snooze_ends_when_the_build_moves_again()
    {
        // Steam patched again: live no longer matches the recorded build, so the snooze no longer applies
        // even before the caller has run Detect.
        var v = PatchDay.Evaluate(Input(live: "300", record: Rec(build: "200", snoozed: true)));

        Assert.Equal(PatchDayState.Patched, v.State);
        Assert.False(v.Hidden);
        Assert.True(v.NeedsDetect);
    }

    [Fact]
    public void Detect_is_not_needed_once_the_record_holds_the_live_build()
        => Assert.False(PatchDay.Evaluate(Input(record: Rec(build: "200"))).NeedsDetect);

    // ----- transitions -----

    [Fact]
    public void Detect_creates_a_record_for_the_new_build()
    {
        var r = PatchDay.Detect(null, "200", Now);

        Assert.Equal("200", r.BuildId);
        Assert.Equal(Now, r.DetectedUtc);
        Assert.Null(r.RestorePointId);
        Assert.Equal(PatchDayChoice.None, r.Choice);
        Assert.False(r.SnoozedUntilChange);
    }

    [Fact]
    public void Detect_on_the_same_build_changes_nothing()
    {
        var existing = Rec(build: "200", snoozed: true);

        Assert.Same(existing, PatchDay.Detect(existing, "200", Now.AddDays(1)));
    }

    [Fact]
    public void Two_patches_in_a_row_keep_the_first_restore_point()
    {
        var first = PatchDay.Detect(null, "200", Now) with { RestorePointId = "rp-before-any-patch" };

        var second = PatchDay.Detect(first, "300", Now.AddDays(3));

        Assert.Equal("300", second.BuildId);
        Assert.Equal(Now.AddDays(3), second.DetectedUtc);
        Assert.Equal("rp-before-any-patch", second.RestorePointId);
    }

    [Fact]
    public void Two_patches_in_a_row_keep_the_first_restore_point_error()
    {
        var first = PatchDay.Detect(null, "200", Now) with { RestorePointError = "disk full" };

        Assert.Equal("disk full", PatchDay.Detect(first, "300", Now).RestorePointError);
    }

    [Fact]
    public void A_new_build_clears_the_snooze()
        => Assert.False(PatchDay.Detect(Rec(snoozed: true), "300", Now).SnoozedUntilChange);

    [Fact]
    public void A_new_build_reopens_a_modded_choice_but_keeps_a_vanilla_one()
    {
        // Modded was an informed choice about the old build. Vanilla is a fact on disk (mods are set aside)
        // until the player brings them back, and nothing returns on its own.
        Assert.Equal(PatchDayChoice.None, PatchDay.Detect(Rec(choice: PatchDayChoice.Modded), "300", Now).Choice);
        Assert.Equal(PatchDayChoice.Vanilla, PatchDay.Detect(Rec(choice: PatchDayChoice.Vanilla), "300", Now).Choice);
    }

    [Fact]
    public void Choose_and_Snooze_set_only_their_own_field()
    {
        var chosen = PatchDay.Choose(Rec(), PatchDayChoice.Vanilla);
        Assert.Equal(PatchDayChoice.Vanilla, chosen.Choice);
        Assert.False(chosen.SnoozedUntilChange);

        var snoozed = PatchDay.Snooze(Rec(choice: PatchDayChoice.Modded));
        Assert.True(snoozed.SnoozedUntilChange);
        Assert.Equal(PatchDayChoice.Modded, snoozed.Choice);
    }

    [Fact]
    public void WithRestorePoint_records_the_id_and_drops_a_stale_error()
    {
        var r = PatchDay.WithRestorePoint(Rec(restore: null) with { RestorePointError = "disk full" }, "rp-9");

        Assert.Equal("rp-9", r.RestorePointId);
        Assert.Null(r.RestorePointError);
    }

    [Fact]
    public void WithRestorePointError_records_the_reason_but_never_over_a_saved_snapshot()
    {
        Assert.Equal("disk full", PatchDay.WithRestorePointError(Rec(restore: null), "disk full").RestorePointError);

        var kept = Rec(restore: "rp-1");
        Assert.Same(kept, PatchDay.WithRestorePointError(kept, "disk full"));
    }

    [Fact]
    public void NeedsRestorePoint_until_one_is_saved()
    {
        Assert.True(PatchDay.NeedsRestorePoint(Rec(restore: null)));
        Assert.False(PatchDay.NeedsRestorePoint(Rec(restore: "rp-1")));
    }

    [Fact]
    public void BringBack_moves_the_baseline_to_the_recorded_build_and_clears_the_record()
    {
        var outcome = PatchDay.BringBack(Rec(build: "300"));

        Assert.Equal("300", outcome.BaselineBuildId);
        Assert.Null(outcome.Record);
    }
}
