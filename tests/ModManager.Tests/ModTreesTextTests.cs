using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// B4 stage two, the row: what a mod's row says about its files in the game's other mod folders. Pure
/// text from three lists, so the wording is pinned here and the App only binds it.
/// </summary>
public class ModTreesTextTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void Nothing_anywhere_says_nothing()
    {
        var t = ModTreesText.For(None, None, None);

        Assert.False(t.Visible);
        Assert.Equal("", t.Line);
        Assert.Equal("", t.Tooltip);
    }

    [Fact]
    public void A_live_mod_whose_trees_all_move_says_626_turns_them_on_and_off()
    {
        var t = ModTreesText.For(new[] { "r6/scripts", "r6/tweaks" }, None, None);

        Assert.True(t.Visible);
        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 turns these on and off with the mod.", t.Tooltip);
    }

    [Fact]
    public void A_live_mod_with_a_tree_held_back_names_it_in_the_tooltip()
    {
        var t = ModTreesText.For(new[] { "r6/scripts" }, new[] { "red4ext/plugins" }, None);

        Assert.Equal("Also has files in r6/scripts, red4ext/plugins", t.Line);
        Assert.Equal("626 turns these on and off with the mod. Files in red4ext/plugins stay where they are: "
            + "626 can't tell they belong only to this mod.", t.Tooltip);
    }

    [Fact]
    public void A_live_mod_with_every_tree_held_back_never_claims_anything_moves()
    {
        var t = ModTreesText.For(None, new[] { "r6/scripts", "r6/tweaks" }, None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("Files in r6/scripts, r6/tweaks stay where they are: 626 can't tell they belong only to "
            + "this mod.", t.Tooltip);
        Assert.DoesNotContain("turns these on and off", t.Tooltip);
    }

    [Fact]
    public void A_tree_both_moving_and_held_back_is_listed_once_and_named_as_partly_staying()
    {
        // One entry of r6/scripts moves and another is held back (it holds a declared tree, say).
        var t = ModTreesText.For(new[] { "r6/scripts", "r6/tweaks" }, new[] { "r6/scripts" }, None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 turns these on and off with the mod. Some files in r6/scripts stay where they are: "
            + "626 can't tell they belong only to this mod.", t.Tooltip);
    }

    [Fact]
    public void A_turned_off_mod_names_the_trees_it_holds()
    {
        var t = ModTreesText.For(None, None, new[] { "r6/scripts", "r6/tweaks" });

        Assert.True(t.Visible);
        Assert.Equal("Also turned off in r6/scripts, r6/tweaks", t.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back.", t.Tooltip);
    }

    [Fact]
    public void A_turned_off_mod_with_a_tree_left_live_says_that_tree_stayed()
    {
        var t = ModTreesText.For(None, new[] { "red4ext/plugins" }, new[] { "r6/scripts" });

        Assert.Equal("Also turned off in r6/scripts", t.Line);
        Assert.Equal("626 turned these off with the mod. Turning it on puts them back. Files in red4ext/plugins "
            + "stay where they are: 626 can't tell they belong only to this mod.", t.Tooltip);
    }

    [Fact]
    public void Repeated_trees_are_listed_once_in_first_seen_order()
    {
        var t = ModTreesText.For(new[] { "r6/scripts", "R6/Scripts", "r6/tweaks" }, None, None);

        Assert.Equal("Also has files in r6/scripts, r6/tweaks", t.Line);
    }

    [Fact]
    public void The_leftover_warning_is_one_sentence_plus_the_path()
    {
        var s = ModTreesText.LeftoverStatus("CoolMod", @"C:\data\disabled-trees\CoolMod");

        Assert.Equal(@"CoolMod is on, but some of its files are still held in C:\data\disabled-trees\CoolMod. "
            + "626 couldn't tell where they go.", s);
    }

    [Fact]
    public void New_copy_carries_no_em_dash()
    {
        var all = new[]
        {
            ModTreesText.For(new[] { "a" }, new[] { "a", "b" }, None).Tooltip,
            ModTreesText.For(None, new[] { "b" }, new[] { "a" }).Tooltip,
            ModTreesText.LeftoverStatus("M", "p"),
        };
        Assert.All(all, s => Assert.DoesNotContain("\u2014", s));
    }
}
