using ModManager.Core;

namespace ModManager.Tests;

// Ports variant-core.test.js — trailing multiplier tokens (_2x, _6h, stacked _6h_2x)
// strip to a family base; mods sharing a base are options of one logical mod.
public class VariantTests
{
    private sealed record VMod(string Name, bool Enabled = false);

    [Fact]
    public void ParseVariant_single_multiplier()
    {
        var v = Variant.ParseVariant("MoreStamina_5x");
        Assert.Equal("MoreStamina", v.Base);
        Assert.Equal("5x", v.Tag);
    }

    [Fact]
    public void ParseVariant_stacked_tokens()
    {
        var v = Variant.ParseVariant("MoreMineralResources_6h_2x");
        Assert.Equal("MoreMineralResources", v.Base);
        Assert.Equal("6h_2x", v.Tag);
    }

    [Fact]
    public void ParseVariant_hours_token()
    {
        var v = Variant.ParseVariant("Buff_6h");
        Assert.Equal("Buff", v.Base);
        Assert.Equal("6h", v.Tag);
    }

    [Fact]
    public void ParseVariant_no_variant()
    {
        var v = Variant.ParseVariant("FasterShips");
        Assert.Equal("FasterShips", v.Base);
        Assert.Null(v.Tag);
    }

    [Fact]
    public void ParseVariant_version_suffix_is_not_a_variant()
    {
        var v = Variant.ParseVariant("windroseNoFogofWarv2");
        Assert.Equal("windroseNoFogofWarv2", v.Base);
        Assert.Null(v.Tag);
    }

    [Fact]
    public void ParseVariant_hyphen_tail_is_not_a_variant()
    {
        var v = Variant.ParseVariant("zXz_Heechee_ReputationSell-balanced");
        Assert.Equal("zXz_Heechee_ReputationSell-balanced", v.Base);
        Assert.Null(v.Tag);
    }

    [Fact]
    public void GroupFamilies_single_variant_bases_stay_separate_order_preserved()
    {
        var fams = Variant.GroupFamilies(
            new[] { new VMod("ExtendedBonfireRadius_3x"), new VMod("FasterShips"), new VMod("LootPickupRange_2x") },
            m => m.Name);

        Assert.Equal(3, fams.Count);
        Assert.Equal("ExtendedBonfireRadius", fams[0].Base);
        Assert.Equal("3x", fams[0].Members[0].Tag);
        Assert.Null(fams[1].Members[0].Tag);
    }

    [Fact]
    public void GroupFamilies_multiple_variants_collapse_into_one_family()
    {
        var fams = Variant.GroupFamilies(
            new[] { new VMod("MoreStamina_2x"), new VMod("MoreStamina_5x", true), new VMod("MoreStamina_10x") },
            m => m.Name);

        Assert.Single(fams);
        Assert.Equal("MoreStamina", fams[0].Base);
        Assert.Equal(3, fams[0].Members.Count);
        Assert.Equal(new[] { "2x", "5x", "10x" }, fams[0].Members.Select(m => m.Tag).ToArray());
    }

    [Fact]
    public void GroupFamilies_distinct_bases_same_multiplier_do_not_merge()
    {
        var fams = Variant.GroupFamilies(
            new[] { new VMod("MoreMineralResources_6h_2x"), new VMod("MoreMineralResourcesOther_6h_2x") },
            m => m.Name);

        Assert.Equal(2, fams.Count);
    }
}
