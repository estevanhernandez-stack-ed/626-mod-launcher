using ModManager.Core;

namespace ModManager.Tests;

// A6. The strings a registration save reports after moving a game's data dir are read by someone
// deciding whether to delete a folder that may hold the only complete copy of their disabled mods.
// Each test holds one of them to the facts it has to state — and to the one word it must never use.
public class RegistrationRepairTextTests
{
    private const string From = @"D:\SteamLibrary\_626mods\elden-ring";
    private const string To = @"E:\Games\_626mods\elden-ring";

    [Fact]
    public void A_failed_save_whose_move_could_not_be_undone_names_both_folders()
    {
        var text = RegistrationRepairText.SaveFailedAfterMove(From, To, sourceSurvived: false);

        // Where the data IS, and where the game EXPECTS it — the two facts a user needs to recover.
        Assert.Contains($"It is at {To}", text);
        Assert.Contains($"still expects it at {From}", text);
        Assert.Contains("could not be moved back", text);
    }

    // The old folder survived the forward move (a file held open at delete time), so the unchanged
    // registration still points at it. Saying the mods were orphaned would be false — and calling the
    // old folder the safe one would be too, because it may be partly deleted.
    [Fact]
    public void A_failed_save_whose_old_folder_survived_says_nothing_changed_and_which_copy_was_verified()
    {
        var text = RegistrationRepairText.SaveFailedAfterMove(From, To, sourceSurvived: true);

        Assert.Contains("nothing about this game changed", text);
        Assert.Contains($"still at {From}", text);
        Assert.Contains($"{To} is the one that was verified complete", text);
        Assert.Contains("compare the two before you remove either", text);
        Assert.DoesNotContain("could not be moved back", text);
    }

    [Fact]
    public void A_save_that_left_the_old_folder_behind_says_so_and_names_the_verified_one()
    {
        var text = RegistrationRepairText.SavedOldFolderRemains(From, To);

        Assert.StartsWith("Saved.", text);
        Assert.Contains($"{From} could not be removed and may be partly deleted", text);
        Assert.Contains($"reads its data from {To}, which was verified complete", text);
    }

    // A recursive delete stops partway on a lock, so a leftover can be a partial tree. Any wording
    // that calls one copy disposable can point the user at deleting the only complete one.
    [Fact]
    public void No_outcome_calls_a_leftover_copy_disposable()
    {
        var all = new[]
        {
            RegistrationRepairText.SaveFailedAfterMove(From, To, sourceSurvived: true),
            RegistrationRepairText.SaveFailedAfterMove(From, To, sourceSurvived: false),
            RegistrationRepairText.SavedOldFolderRemains(From, To),
            RegistrationRepairText.Clobbered(From, From, To, To, movedTo: To),
        };

        foreach (var text in all)
            foreach (var word in new[] { "spare", "safe to delete", "safe to remove", "backup copy" })
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_clobbered_save_that_moved_nothing_says_nothing_moved()
    {
        var text = RegistrationRepairText.Clobbered(@"C:\Old", null, @"C:\New", null, movedTo: null);

        Assert.Contains(@"reads as being at C:\Old with its launcher data at not set", text);
        Assert.Contains(@"you asked for C:\New and not set", text);
        Assert.Contains("Nothing was moved", text);
    }

    // The dangerous one: the data has already moved and the registration was reverted underneath it.
    // Carrying on would have the launcher look for this game's disabled mods where they no longer are.
    [Fact]
    public void A_clobbered_save_after_a_move_says_where_the_data_now_is()
    {
        var text = RegistrationRepairText.Clobbered(@"C:\Old", null, @"C:\New", null, movedTo: To);

        Assert.Contains($"already been moved to {To}", text);
        Assert.Contains("before using this game", text);
        Assert.DoesNotContain("Nothing was moved", text);
    }

    [Fact]
    public void A_clobber_that_removed_the_game_entirely_reads_as_not_set_rather_than_blank()
    {
        var text = RegistrationRepairText.Clobbered(null, null, @"C:\New", To, movedTo: null);

        Assert.Contains("reads as being at not set", text);
        Assert.DoesNotContain("at  ", text);
    }
}
