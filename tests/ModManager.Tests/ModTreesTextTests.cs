using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two, the row: what a mod's row says about its files in the game's other mod folders. Pure
/// text from the toggle's selection, so the wording is pinned here and the App only binds it. Every held
/// tree carries its reason, and the row says the true one.
/// </summary>
public class ModTreesTextTests
{
    private static readonly string[] None = Array.Empty<string>();
    private static readonly HeldTree[] NoHeld = Array.Empty<HeldTree>();

    private static HeldTree[] Held(HeldReason reason, params string[] trees)
        => trees.Select(t => new HeldTree(t, reason)).ToArray();

    private const string CantTell = "stay where they are: 626 can't tell they belong only to this mod.";

    [Fact]
    public void Nothing_anywhere_says_nothing()
    {
        var t = ModTreesText.For(None, NoHeld, None);

        Assert.False(t.Visible);
        Assert.Equal("", t.Line);
        Assert.Equal("", t.Tooltip);
    }

    [Fact]
    public void A_live_mod_whose_trees_all_move_says_626_turns_them_on_and_off()
    {
        var t = ModTreesText.For(new[] { "r6/scripts", "r6/tweaks" }, NoHeld, None);

        Assert.True(t.Visible);
        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 turns these on and off with the mod.", t.Tooltip);
    }

    [Fact]
    public void A_contested_tree_says_626_cant_tell()
    {
        var t = ModTreesText.For(new[] { "r6/scripts" }, Held(HeldReason.Contested, "red4ext/plugins"), None);

        Assert.Equal("Also has files in r6/scripts, red4ext/plugins", t.Line);
        Assert.Equal($"626 turns these on and off with the mod. Files in red4ext/plugins {CantTell}", t.Tooltip);
    }

    [Fact]
    public void A_protected_entry_says_626_cant_tell()
    {
        var t = ModTreesText.For(None, Held(HeldReason.Protected, "r6/scripts", "r6/tweaks"), None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal($"Files in r6/scripts, r6/tweaks {CantTell}", t.Tooltip);
        Assert.DoesNotContain("turns these on and off", t.Tooltip);
    }

    [Fact]
    public void An_owned_tree_says_another_tool_manages_it()
    {
        var t = ModTreesText.For(new[] { "r6/tweaks" }, Held(HeldReason.OwnedTree, "r6/scripts"), None);

        Assert.Equal("Also has files in r6/tweaks, r6/scripts", t.Line);
        Assert.Equal("626 turns these on and off with the mod. Files in r6/scripts stay where they are: "
            + "another tool manages that folder.", t.Tooltip);
    }

    [Fact]
    public void Two_owned_trees_say_those_folders()
    {
        var t = ModTreesText.For(None, Held(HeldReason.OwnedTree, "r6/scripts", "r6/tweaks"), None);

        Assert.Equal("Files in r6/scripts, r6/tweaks stay where they are: another tool manages those folders.",
            t.Tooltip);
    }

    [Fact]
    public void Each_reason_gets_its_own_sentence()
    {
        var held = Held(HeldReason.Contested, "r6/input").Concat(Held(HeldReason.OwnedTree, "r6/scripts")).ToArray();

        var t = ModTreesText.For(new[] { "r6/tweaks" }, held, None);

        Assert.Equal("626 turns these on and off with the mod. Files in r6/input " + CantTell
            + " Files in r6/scripts stay where they are: another tool manages that folder.", t.Tooltip);
    }

    [Fact]
    public void A_row_whose_files_never_move_says_so_and_never_claims_626_moves_them()
    {
        var t = ModTreesText.For(None, Held(HeldReason.RowNotMoved, "r6/scripts", "r6/tweaks"), None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 doesn't move this mod's files in these folders. They stay where they are, on or off.",
            t.Tooltip);
    }

    [Fact]
    public void A_tree_both_moving_and_held_back_is_listed_once_and_named_as_partly_staying()
    {
        var t = ModTreesText.For(new[] { "r6/scripts", "r6/tweaks" }, Held(HeldReason.Protected, "r6/scripts"), None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal($"626 turns these on and off with the mod. Some files in r6/scripts {CantTell}", t.Tooltip);
    }

    [Fact]
    public void A_turned_off_mod_names_the_trees_it_holds()
    {
        var t = ModTreesText.For(None, NoHeld, new[] { "r6/scripts", "r6/tweaks" });

        Assert.True(t.Visible);
        Assert.Equal("Also turned off in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back.", t.Tooltip);
    }

    [Fact]
    public void A_turned_off_mod_with_a_tree_held_back_says_why_it_stayed()
    {
        var t = ModTreesText.For(None, Held(HeldReason.Contested, "red4ext/plugins"), new[] { "r6/scripts" });

        Assert.Equal("Also turned off in r6/scripts", t.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back. Files in red4ext/plugins "
            + CantTell, t.Tooltip);
    }

    [Fact]
    public void A_turned_off_mod_whose_files_are_still_live_says_they_are_still_on()
    {
        var t = ModTreesText.For(None, NoHeld, None, stillOn: new[] { "r6/scripts", "r6/tweaks" });

        Assert.Equal("Files in r6/scripts, r6/tweaks are still on.", t.Line);
        Assert.Equal("These files didn't move when the mod was turned off. Turn it on and off again to move them.",
            t.Tooltip);
    }

    [Fact]
    public void Held_and_still_on_trees_share_one_line()
    {
        var t = ModTreesText.For(None, NoHeld, new[] { "r6/scripts" }, stillOn: new[] { "r6/tweaks" });

        Assert.Equal("Also turned off in r6/scripts. Files in r6/tweaks are still on.", t.Line);
        Assert.DoesNotContain("\n", t.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back. Files in r6/tweaks didn't "
            + "move when the mod was turned off. Turn it on and off again to move them.", t.Tooltip);
    }

    [Fact]
    public void Held_files_with_no_known_tree_name_the_folder()
    {
        var t = ModTreesText.For(None, NoHeld, None,
            heldUnknown: new TreeLeftover(@"C:\data\disabled-trees\CoolMod", Readable: true));

        Assert.True(t.Visible);
        Assert.Equal(@"Some files are held in C:\data\disabled-trees\CoolMod.", t.Line);
        Assert.Equal("626 can't tell which folders these came from.", t.Tooltip);
    }

    [Fact]
    public void An_unreadable_holding_folder_never_claims_files_are_there()
    {
        var t = ModTreesText.For(None, NoHeld, None,
            heldUnknown: new TreeLeftover(@"C:\data\disabled-trees\CoolMod", Readable: false));

        Assert.True(t.Visible);
        Assert.Equal(@"626 couldn't read C:\data\disabled-trees\CoolMod.", t.Line);
        Assert.Equal("626 couldn't check whether this mod's other files are held here.", t.Tooltip);
        Assert.DoesNotContain("are held in", t.Line);
    }

    [Fact]
    public void Repeated_trees_are_listed_once_in_first_seen_order()
    {
        var t = ModTreesText.For(new[] { "r6/scripts", "R6/Scripts", "r6/tweaks" }, NoHeld, None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
    }

    [Fact]
    public void The_leftover_warning_is_one_sentence_plus_the_path()
    {
        var s = ModTreesText.LeftoverStatus("CoolMod", new TreeLeftover(@"C:\data\disabled-trees\CoolMod", Readable: true));

        Assert.Equal(@"CoolMod is on, but some of its files are still held in C:\data\disabled-trees\CoolMod. "
            + "626 couldn't tell where they go.", s);
    }

    [Fact]
    public void An_unreadable_holding_folder_is_not_claimed_to_hold_files()
    {
        var s = ModTreesText.LeftoverStatus("CoolMod", new TreeLeftover(@"C:\data\disabled-trees\CoolMod", Readable: false));

        Assert.Equal(@"CoolMod is on, but 626 couldn't read C:\data\disabled-trees\CoolMod to check for leftover files.", s);
    }

    [Fact]
    public void New_copy_carries_no_em_dash()
    {
        var all = new[]
        {
            ModTreesText.For(new[] { "a" }, Held(HeldReason.Protected, "a", "b"), None).Tooltip,
            ModTreesText.For(None, Held(HeldReason.OwnedTree, "b"), new[] { "a" }, stillOn: new[] { "c" }).Tooltip,
            ModTreesText.For(None, Held(HeldReason.RowNotMoved, "b"), None).Tooltip,
            ModTreesText.LeftoverStatus("M", new TreeLeftover("p", true)),
            ModTreesText.LeftoverStatus("M", new TreeLeftover("p", false)),
        };
        Assert.All(all, s => Assert.DoesNotContain("\u2014", s));
    }
}
