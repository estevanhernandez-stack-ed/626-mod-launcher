using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

// Task 7 remediation: Safe Clear succeeded silently — the dialog closed without telling the user a
// restore point was made or where to find it. SafeClearSummary.SuccessMessage is the pure formatter
// the dialog shows in a Success InfoBar before it closes. Wording is unit-tested here so the canonical
// "Settings -> Restore points" pointer can't drift. SafeClearResult is the positional record from
// OrchestratorContracts: (Ok, RefusedReason, RestorePointTimestamp, PerGameSheetPaths, Warnings).
public class SafeClearSummaryTests
{
    private static SafeClearResult Ok(string? stamp, params string[] warnings)
        => new(Ok: true, RefusedReason: null, RestorePointTimestamp: stamp,
               PerGameSheetPaths: System.Array.Empty<string>(), Warnings: warnings);

    [Fact]
    public void SuccessMessage_with_timestamp_names_the_restore_point_and_points_to_settings()
    {
        var msg = SafeClearSummary.SuccessMessage(Ok("20260530-143200"));

        Assert.Contains("2026-05-30 14:32", msg);                 // friendly, not the raw stamp
        Assert.DoesNotContain("20260530-143200", msg);            // raw stamp never shown
        Assert.Contains("Settings", msg);
        Assert.Contains("Restore points", msg);                   // canonical pointer
    }

    [Fact]
    public void SuccessMessage_without_restore_point_does_not_claim_one()
    {
        var msg = SafeClearSummary.SuccessMessage(Ok(stamp: null));

        Assert.Contains("Reset complete", msg);
        Assert.Contains("No restore point", msg);                 // honest: nothing was saved
        Assert.DoesNotContain("Saved a restore point", msg);
        Assert.DoesNotContain("Restore points.", msg);            // no pointer when there's nothing to find
    }

    [Fact]
    public void SuccessMessage_appends_warnings()
    {
        var msg = SafeClearSummary.SuccessMessage(Ok("20260530-143200", "Couldn't copy nexus.json."));

        Assert.Contains("Note:", msg);
        Assert.Contains("Couldn't copy nexus.json.", msg);
        Assert.Contains("Restore points", msg);                   // still confirms the restore point
    }

    [Fact]
    public void SuccessMessage_with_unparseable_timestamp_still_points_to_settings_without_the_raw_stamp()
    {
        var msg = SafeClearSummary.SuccessMessage(Ok("not-a-stamp"));

        Assert.Contains("Saved a restore point", msg);            // we know one exists (stamp present)
        Assert.Contains("Restore points", msg);
        Assert.DoesNotContain("not-a-stamp", msg);               // never surface a junk stamp
    }

    private static RestoreResult Restored(params string[] warnings)
        => new(Ok: true, RefusedReason: null, Conflicts: System.Array.Empty<RestoreConflict>(), Warnings: warnings);

    [Fact]
    public void RestoreNotes_is_null_when_everything_came_back()
        => Assert.Null(SafeClearSummary.RestoreNotes(Restored()));

    [Fact]
    public void RestoreNotes_says_the_setup_is_back_and_lists_every_note()
    {
        var notes = SafeClearSummary.RestoreNotes(Restored(
            "Pak Game: \"alpha\" is not back on: target folder now owned by another tool",
            "Tree Game: \"CoolMod\" is not back on: it didn't come back on"));

        Assert.NotNull(notes);
        Assert.Contains("restored", notes, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"alpha\" is not back on", notes);
        Assert.Contains("\"CoolMod\" is not back on", notes);
    }

    [Fact]
    public void RestoreNotes_is_null_for_a_refused_restore()
        => Assert.Null(SafeClearSummary.RestoreNotes(Restored("x") with { Ok = false }));
}
