using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 2 / A10. The updates view rendered "installed -> latest" for every row regardless of why the
/// row existed, so a real install showed about twenty consecutive "unknown -> 1.2.1", plus "1.1 -> 1"
/// and "1.0.1 -> 1.0.0" — two apparent DOWNGRADES the launcher was inviting the user to act on.
///
/// <para>The decision was never wrong: Nexus's per-user flag outranks a version compare because Nexus
/// knows which FILE was downloaded and we often do not. The row just drew a comparison behind a
/// decision that was not a comparison.</para>
/// </summary>
public class PendingUpdateTruthTests
{
    private static PendingUpdate Flagged(string key, string? installed = null, string? latest = null)
        => new("g", "Game", key, key, installed, latest, null, null, PendingReason.NexusFlag);

    private static PendingUpdate Compared(string key, string installed, string latest)
        => new("g", "Game", key, key, installed, latest, null, null, PendingReason.VersionDiffers);

    [Fact]
    public void A_flag_pending_row_carries_its_reason_so_the_view_can_refuse_to_draw_a_pair()
    {
        Assert.Equal(PendingReason.NexusFlag, Flagged("a", latest: "1.2.1").Reason);
        Assert.Equal(PendingReason.VersionDiffers, Compared("b", "1.2.0", "1.3.1").Reason);
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0", "1")]
    [InlineData("v1.2", "1.2.0")]
    public void Versions_that_differ_only_in_trailing_zeros_are_the_same_release(string a, string b)
        => Assert.True(Compared("k", a, b).VersionsLookEquivalent);

    [Theory]
    [InlineData("1.1", "1")]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("2.0", "1.0")]
    public void Genuinely_different_versions_are_not_called_equivalent(string a, string b)
        => Assert.False(Compared("k", a, b).VersionsLookEquivalent);

    [Fact]
    public void An_unknown_side_is_never_equivalent_to_anything()
    {
        // Absence is not agreement. "unknown" matching "unknown" would silently collapse the very
        // rows this entry is about.
        Assert.False(Flagged("k", null, "1.2.1").VersionsLookEquivalent);
        Assert.False(Flagged("k", "1.2.1", null).VersionsLookEquivalent);
        Assert.False(Flagged("k").VersionsLookEquivalent);
    }

    [Fact]
    public void No_ordering_is_offered_because_mod_versions_are_not_reliably_semver()
    {
        // Deliberate absence, asserted so nobody "helpfully" adds a comparator later. Guessing which
        // side is newer produces a confidently wrong DIRECTION, which is the fault being fixed.
        Assert.DoesNotContain(typeof(PendingUpdate).GetMethods().Select(m => m.Name),
            n => n is "CompareVersions" or "IsNewer" or "IsDowngrade");
    }
}

/// <summary>
/// Wave 2 / A11. "Faster Ships" appeared four times, every row identical, because Nexus tracks
/// downloads per FILE and that mod publishes four. The header read 115 updates for an install with a
/// fraction of that many mods actually behind.
/// </summary>
public class PendingUpdateGroupingTests
{
    // The real four, verbatim from Windrose. All one mod page: Nexus id 285.
    private static readonly string[] FasterShips =
        { "FasterShips10", "FasterShips10_B", "aaUltraFastShips", "aaUltraFastShipsB" };

    private static PendingUpdate Ship(string key)
        => new("windrose", "Windrose", key, "Faster Ships", "1", "1.0", 285, "windrose");

    private static PendingUpdate Loose(string key, string name, int? id = null)
        => new("windrose", "Windrose", key, name, null, "2.0", id, "windrose", PendingReason.NexusFlag);

    [Fact]
    public void Four_files_of_one_Nexus_mod_become_one_row()
    {
        var groups = PendingUpdateGroups.Group(FasterShips.Select(Ship).ToList());

        var only = Assert.Single(groups);
        Assert.Equal(4, only.Members.Count);
        Assert.Equal("Faster Ships", only.ModName);
        Assert.Equal(FasterShips, only.MemberKeys);
    }

    [Fact]
    public void The_count_a_person_sees_follows_the_rows_not_the_files()
    {
        // The badge half of the same overstatement.
        var summary = new GameUpdateSummary("windrose", "Windrose", true, FasterShips.Select(Ship).ToList());

        Assert.Equal(4, summary.Pending.Count);   // files, unchanged
        Assert.Equal(1, summary.Count);           // mods, which is what gets shown
    }

    [Fact]
    public void Distinct_mods_never_merge()
    {
        var groups = PendingUpdateGroups.Group(new[]
        {
            Loose("alpha", "Alpha", 11), Loose("beta", "Beta", 12), Loose("gamma", "Gamma", 13),
        });

        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void The_same_id_on_two_different_games_is_two_different_mods()
    {
        // Nexus ids are scoped per domain. Merging across them would collapse unrelated mods.
        var groups = PendingUpdateGroups.Group(new[]
        {
            new PendingUpdate("a", "A", "one", "One", null, "2", 285, "windrose"),
            new PendingUpdate("b", "B", "two", "Two", null, "2", 285, "eldenring"),
        });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Mods_with_no_id_fall_back_to_the_variant_family_the_mod_list_already_groups_on()
    {
        var groups = PendingUpdateGroups.Group(new[]
        {
            Loose("MoreStamina_2x", "More Stamina 2x"),
            Loose("MoreStamina_5x", "More Stamina 5x"),
            Loose("Unrelated", "Unrelated"),
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Members.Count);
    }

    [Fact]
    public void A_file_with_an_id_is_never_merged_with_one_without_on_a_name_coincidence()
    {
        // An id is evidence; a name is a guess. Keeping the keyspaces apart is what stops the guess
        // from overriding the evidence.
        var groups = PendingUpdateGroups.Group(new[] { Ship("FasterShips10"), Loose("FasterShips10_B", "Faster Ships") });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void The_row_speaks_for_whichever_member_can_actually_justify_a_pair()
    {
        // Three files pending on Nexus's flag and one with a real comparison. The row should show the
        // comparison rather than the first member's silence.
        var groups = PendingUpdateGroups.Group(new[]
        {
            Loose("a", "Thing", 90),
            new PendingUpdate("g", "G", "b", "Thing", "1.2.0", "1.3.1", 90, "windrose"),
        });

        var only = Assert.Single(groups);
        Assert.Equal("1.2.0", only.Primary.InstalledVersion);
        Assert.Equal("1.3.1", only.Primary.LatestVersion);
    }

    [Fact]
    public void A_group_of_only_flag_pending_files_still_has_a_primary()
    {
        var only = Assert.Single(PendingUpdateGroups.Group(new[] { Loose("a", "Thing", 90), Loose("b", "Thing", 90) }));

        Assert.Equal("a", only.Primary.ModKey);
        Assert.True(only.IsMulti);
    }

    [Fact]
    public void Nothing_is_dropped_by_the_collapse()
    {
        // Grouping is a display decision. A file that vanishes here is an update the user never sees.
        var all = FasterShips.Select(Ship).Concat(new[] { Loose("solo", "Solo", 7) }).ToList();

        var groups = PendingUpdateGroups.Group(all);

        Assert.Equal(all.Count, groups.Sum(g => g.Members.Count));
    }

    [Fact]
    public void An_empty_list_groups_to_nothing_rather_than_throwing()
    {
        Assert.Empty(PendingUpdateGroups.Group(Array.Empty<PendingUpdate>()));
        Assert.Empty(PendingUpdateGroups.Group(null!));
    }

    [Fact]
    public void The_row_is_named_for_the_mod_rather_than_one_of_its_option_files()
    {
        var only = Assert.Single(PendingUpdateGroups.Group(new[]
        {
            Loose("a", "Faster Ships - 10x Ultra", 285),
            Loose("b", "Faster Ships", 285),
            Loose("c", "Faster Ships - 10x", 285),
        }));

        Assert.Equal("Faster Ships", only.ModName);
    }
}
