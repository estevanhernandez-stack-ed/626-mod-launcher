using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The saves panel gave the same answer to two different questions. Palworld's save folder is
/// correct, its saves are in it, and the panel said "Check the save folder above" — advice aimed at
/// the one thing that was not wrong.
///
/// <para>Only <c>fromsoft</c> declares save types, so every other game hit that sentence. Sending
/// someone to verify a correct setting is worse than silence: they change it, and the snapshots that
/// were covering everything start covering the wrong folder.</para>
/// </summary>
public class SaveListingEmptyStateTests
{
    [Fact]
    public void A_game_whose_format_is_not_itemized_is_told_it_is_the_apps_gap()
    {
        var m = SaveListingEmptyState.MessageFor(folderSet: true, declaresTypes: false);

        Assert.Contains("isn't itemized yet", m);
        // And never sent to re-check a setting that is already right.
        Assert.DoesNotContain("Check the folder", m);
    }

    [Fact]
    public void It_says_what_still_protects_them()
    {
        // The reassurance has to be specific to be worth anything: the snapshot is a whole-folder zip
        // and it RECURSES, which is the part a Palworld player needs to believe - their worlds live a
        // folder down from the one the panel is looking at.
        var m = SaveListingEmptyState.MessageFor(folderSet: true, declaresTypes: false);

        Assert.Contains("whole folder", m);
        Assert.Contains("subfolders", m);
    }

    [Fact]
    public void When_types_are_declared_and_none_are_found_the_folder_IS_the_thing_to_check()
    {
        var m = SaveListingEmptyState.MessageFor(folderSet: true, declaresTypes: true);

        Assert.Contains("Check the folder", m);
    }

    [Fact]
    public void No_folder_at_all_is_its_own_answer()
    {
        var m = SaveListingEmptyState.MessageFor(folderSet: false, declaresTypes: true);

        Assert.Contains("No save folder set", m);
        Assert.DoesNotContain("known types", m);
    }

    [Fact]
    public void Every_branch_says_something_and_ends_in_a_full_stop()
    {
        foreach (var folder in new[] { true, false })
        foreach (var types in new[] { true, false })
        {
            var m = SaveListingEmptyState.MessageFor(folder, types);
            Assert.False(string.IsNullOrWhiteSpace(m));
            Assert.EndsWith(".", m);
        }
    }
}
