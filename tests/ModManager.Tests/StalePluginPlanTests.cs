using ModManager.Core.Plugins;

namespace ModManager.Tests;

/// <summary>
/// Tidying up the Nexus plugin, now that Nexus is compiled into every build.
///
/// <para>The delivery split was never Microsoft's rule — it was Nexus's. Their integration could not
/// ship until they approved us as a partner, and downloading it as a signed plugin kept it off a
/// certified package meanwhile. The approval landed, so the download has no job left and the file it
/// wrote should not sit on someone's disk looking authoritative.</para>
///
/// <para>These are the decisions rather than the deleting. The App half is a few
/// <c>File.Delete</c> calls, and the ways to get that subtly wrong — taking a folder that still holds
/// somebody else's plugin, or a record that still names one — are all judgements, which is why they
/// live here.</para>
/// </summary>
public class StalePluginPlanTests
{
    private const string Record = StalePluginCleanupPlan.RecordFileName;

    private static IReadOnlyDictionary<string, string> Rec(params (string Id, string Version)[] entries)
        => entries.ToDictionary(e => e.Id, e => e.Version);

    [Fact]
    public void The_retired_plugin_and_its_record_both_go_when_nothing_else_is_installed()
    {
        // The file list is the one taken off a REAL machine, signature file included. The first cut of
        // this test invented the listing, left out nexus.dll.sig, and would have shipped a cleanup that
        // stranded the signature and kept the folder alive for nothing.
        var plan = StalePluginCleanupPlan.For("nexus", Rec(("nexus", "0.14.0")),
                                              new[] { "installed-plugins.json", "nexus.dll", "nexus.dll.sig" });

        Assert.Equal(new[] { "nexus.dll", "nexus.dll.sig", Record }, plan.FilesToDelete);
        Assert.Null(plan.RecordToWrite);          // nothing recorded any more, so the file goes too
        Assert.True(plan.RemoveDirectory);
    }

    [Fact]
    public void A_second_plugin_keeps_the_record_and_the_folder()
    {
        // The whole reason this rewrites the record rather than deleting it. Este kept the loader as
        // the lane for plugins that go to GitHub before, or instead of, the Store - so a folder with
        // somebody else's plugin in it is the case that has to survive.
        var plan = StalePluginCleanupPlan.For("nexus",
            Rec(("nexus", "0.14.0"), ("someone-elses", "1.2.0")),
            new[] { "nexus.dll", "someone-elses.dll", Record });

        Assert.Equal(new[] { "nexus.dll" }, plan.FilesToDelete);
        Assert.NotNull(plan.RecordToWrite);
        Assert.Equal(new[] { "someone-elses" }, plan.RecordToWrite!.Keys);
        Assert.False(plan.RemoveDirectory);
    }

    [Fact]
    public void A_file_nobody_recorded_is_left_alone_and_keeps_the_folder_alive()
    {
        // It is a folder on somebody's disk. Deleting a file we did not put there is not tidying up.
        var plan = StalePluginCleanupPlan.For("nexus", Rec(("nexus", "0.14.0")),
                                              new[] { "nexus.dll", Record, "notes.txt" });

        Assert.DoesNotContain("notes.txt", plan.FilesToDelete);
        Assert.False(plan.RemoveDirectory);
    }

    [Fact]
    public void Running_twice_is_a_no_op_the_second_time()
    {
        // It runs on every startup. The second run must find nothing to do rather than trying again.
        var plan = StalePluginCleanupPlan.For("nexus", Rec(), Array.Empty<string>());

        Assert.Empty(plan.FilesToDelete);
        Assert.Null(plan.RecordToWrite);
        Assert.False(plan.RemoveDirectory);       // an ALREADY-EMPTY folder is not ours to remove
    }

    [Fact]
    public void A_recorded_plugin_whose_file_is_already_gone_still_loses_its_record_entry()
    {
        // Half-tidied is the state a failed earlier run leaves behind, and the record is what the
        // loader actually reads - so a stale entry matters more than a stale file.
        var plan = StalePluginCleanupPlan.For("nexus", Rec(("nexus", "0.14.0")), new[] { Record });

        Assert.Equal(new[] { Record }, plan.FilesToDelete);
        Assert.Null(plan.RecordToWrite);
    }

    [Fact]
    public void A_dll_with_no_record_entry_is_still_removed()
    {
        // The opposite half-state: the file the feed wrote, with the record already gone.
        var plan = StalePluginCleanupPlan.For("nexus", Rec(), new[] { "nexus.dll", "nexus.dll.sig" });

        Assert.Equal(new[] { "nexus.dll", "nexus.dll.sig" }, plan.FilesToDelete);
        Assert.Null(plan.RecordToWrite);
        Assert.True(plan.RemoveDirectory);
    }

    [Fact]
    public void Ids_and_file_names_are_matched_case_insensitively()
    {
        var plan = StalePluginCleanupPlan.For("nexus", Rec(("Nexus", "0.14.0")),
                                              new[] { "Nexus.DLL", Record });

        Assert.Contains("nexus.dll", plan.FilesToDelete);
        Assert.Null(plan.RecordToWrite);
    }

    [Fact]
    public void Nothing_at_all_is_not_a_throw()
    {
        var plan = StalePluginCleanupPlan.For("nexus", null, null);

        Assert.Empty(plan.FilesToDelete);
        Assert.False(plan.RemoveDirectory);
    }
}
