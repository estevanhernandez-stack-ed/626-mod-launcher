using ModManager.Core;

namespace ModManager.Tests;

// VariantGroups.Group — collapse mods that are variants of one logical mod into families.
// Family key: identified mod page (ModUrl) first, else the _Nx variant base. One member = singleton.
public class VariantGroupTests
{
    [Fact]
    public void Same_mod_url_collapses_into_one_family()
    {
        var mods = new[]
        {
            new Mod { Name = "aaUltraFastShips", DisplayName = "Ultra Fast Ships", ModUrl = "https://www.nexusmods.com/windrose/mods/285" },
            new Mod { Name = "aaUltraFastShipsB", ModUrl = "https://www.nexusmods.com/windrose/mods/285" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Single(fams);
        Assert.True(fams[0].IsMulti);
        Assert.Equal(2, fams[0].Members.Count);
        Assert.Equal("Ultra Fast Ships", fams[0].Title);
    }

    [Fact]
    public void Mod_url_grouping_is_case_and_trailing_slash_insensitive()
    {
        var mods = new[]
        {
            new Mod { Name = "ShipsA", ModUrl = "https://www.nexusmods.com/windrose/mods/285" },
            new Mod { Name = "ShipsB", ModUrl = "HTTPS://WWW.NexusMods.com/windrose/mods/285/" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Single(fams);
        Assert.True(fams[0].IsMulti);
        Assert.Equal(2, fams[0].Members.Count);
    }

    [Fact]
    public void Shared_nx_base_collapses_into_one_family()
    {
        var mods = new[]
        {
            new Mod { Name = "LootPickupRange_2x" },
            new Mod { Name = "LootPickupRange_3x" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Single(fams);
        Assert.Equal("base:lootpickuprange", fams[0].Key);
        Assert.True(fams[0].IsMulti);
        Assert.Equal(2, fams[0].Members.Count);
    }

    [Fact]
    public void Genuinely_different_mods_are_two_singleton_families()
    {
        var mods = new[]
        {
            new Mod { Name = "FasterShips" },
            new Mod { Name = "BetterLoot" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Equal(2, fams.Count);
        Assert.False(fams[0].IsMulti);
        Assert.False(fams[1].IsMulti);
    }

    [Fact]
    public void Different_mod_urls_do_not_merge_even_with_same_title()
    {
        var mods = new[]
        {
            new Mod { Name = "ModA", DisplayName = "Same Title", ModUrl = "https://www.nexusmods.com/windrose/mods/285" },
            new Mod { Name = "ModB", DisplayName = "Same Title", ModUrl = "https://www.nexusmods.com/windrose/mods/999" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Equal(2, fams.Count);
        Assert.False(fams[0].IsMulti);
        Assert.False(fams[1].IsMulti);
    }

    [Fact]
    public void Order_is_preserved_for_families_and_members()
    {
        var mods = new[]
        {
            new Mod { Name = "ZebraMod" },
            new Mod { Name = "MoreStamina_2x" },
            new Mod { Name = "MoreStamina_5x" },
            new Mod { Name = "AppleMod" },
        };

        var fams = VariantGroups.Group(mods);

        Assert.Equal(3, fams.Count);
        // Families in first-appearance order.
        Assert.Equal("base:zebramod", fams[0].Key);
        Assert.Equal("base:morestamina", fams[1].Key);
        Assert.Equal("base:applemod", fams[2].Key);
        // Members in input order within the family.
        Assert.Equal(new[] { "MoreStamina_2x", "MoreStamina_5x" }, fams[1].Members.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Title_falls_back_to_name_when_display_name_empty()
    {
        var mods = new[] { new Mod { Name = "FasterShips", DisplayName = "" } };

        var fams = VariantGroups.Group(mods);

        Assert.Single(fams);
        Assert.Equal("FasterShips", fams[0].Title);
    }

    [Fact]
    public void Empty_input_yields_empty_list()
    {
        var fams = VariantGroups.Group(Array.Empty<Mod>());
        Assert.Empty(fams);
    }
}
