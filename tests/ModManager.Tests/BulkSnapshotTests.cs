using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 6. Every file a bulk operation moves is reversible; the KNOWLEDGE is not. Press
/// <c>Disable all</c> on a 200-mod install and every file lands safely in the holding folder, while
/// the fact that 140 of them were on is gone — and <c>Enable all</c> is not the undo, because it turns
/// on all 200 including the sixty that were deliberately off.
///
/// <para>The launcher already had the fix and never connected it: a profile saves exactly that set.</para>
/// </summary>
public class BulkSnapshotTests
{
    private static readonly DateTime When = new(2026, 8, 18, 19, 42, 0, DateTimeKind.Local);

    [Fact]
    public void The_name_says_what_it_was_taken_before_and_when()
        => Assert.Equal("Before disable all 2026-08-18 19.42", BulkSnapshot.NameFor("disable all", When));

    [Fact]
    public void An_automatic_snapshot_is_distinguishable_from_one_a_person_named()
    {
        // A profiles list that mixes "Before disable all" with "My co-op setup" has to let the user
        // tell which is which, or the automatic ones become clutter nobody dares delete.
        Assert.True(BulkSnapshot.IsAutomatic(BulkSnapshot.NameFor("enable all", When)));
        Assert.False(BulkSnapshot.IsAutomatic("My co-op setup"));
        Assert.False(BulkSnapshot.IsAutomatic(""));
        Assert.False(BulkSnapshot.IsAutomatic(null));
    }

    [Fact]
    public void The_name_survives_an_operation_string_a_filename_could_not_hold()
    {
        var name = BulkSnapshot.NameFor("apply MP/SP <all>", When);

        foreach (var bad in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            Assert.DoesNotContain(bad, name);
    }

    [Fact]
    public void An_empty_operation_still_produces_a_usable_name()
    {
        // Never a profile called "Before  2026-08-18" with a hole in it.
        Assert.Equal("Before bulk change 2026-08-18 19.42", BulkSnapshot.NameFor("", When));
        Assert.Equal("Before bulk change 2026-08-18 19.42", BulkSnapshot.NameFor(null!, When));
    }

    [Fact]
    public void The_reassurance_names_the_profile_and_says_where_to_find_it()
    {
        // A snapshot the user cannot find is the same as no snapshot.
        var line = BulkSnapshot.Reassurance("Before disable all 2026-08-18 19.42");

        Assert.Contains("Before disable all", line);
        Assert.Contains("Profiles", line);
    }

    [Fact]
    public void The_time_comes_from_the_caller_so_this_stays_pure()
    {
        // Not DateTime.Now inside: a caller can hand it the same instant it stamps elsewhere, and the
        // test above can assert an exact string.
        var a = BulkSnapshot.NameFor("enable all", When);
        var b = BulkSnapshot.NameFor("enable all", When);

        Assert.Equal(a, b);
    }
}

/// <summary>
/// Wave 6. The MP/SP segments used to call <c>Scanner.ApplyMode</c>, which enables every mod matching
/// the mode and disables every mod that does not — a bulk file operation behind three segmented
/// buttons, and a cosmetic no-op on Mod Engine 2, direct-inject and loose-root games.
///
/// <para>They are a FILTER now. <see cref="Classification.ModeFilter"/> is the same pure rule the apply
/// used, reused rather than reimplemented, and these tests pin what it decides so the render list and
/// any future apply can never disagree about what "MP" means.</para>
/// </summary>
public class ModeFilterTests
{
    [Theory]
    [InlineData("mp", "mp", true)]
    [InlineData("mp", "both", true)]
    [InlineData("mp", "sp", false)]
    [InlineData("sp", "sp", true)]
    [InlineData("sp", "both", true)]
    [InlineData("sp", "mp", false)]
    public void A_mode_shows_its_own_class_and_the_ones_marked_for_both(string mode, string cls, bool shown)
        => Assert.Equal(shown, Classification.ModeFilter(mode, cls));

    [Theory]
    [InlineData("mp")]
    [InlineData("sp")]
    [InlineData("both")]
    [InlineData("")]
    public void ALL_shows_everything_including_a_class_nothing_recognises(string cls)
    {
        // "all" is the default and must never hide a row. A filter that silently drops an unclassified
        // mod would make it look uninstalled.
        Assert.True(Classification.ModeFilter("all", cls));
        Assert.True(Classification.ModeFilter("anything-else", cls));
    }

    [Fact]
    public void An_unclassified_mod_is_treated_as_both_by_the_caller_not_by_this_rule()
    {
        // The rule itself does not know about nulls; the call site passes "both" for an unset class.
        // Pinned so nobody "fixes" it by making the rule guess, which would put the default in two
        // places at once.
        Assert.False(Classification.ModeFilter("mp", ""));
        Assert.True(Classification.ModeFilter("mp", "both"));
    }
}
