using ModManager.Core;

namespace ModManager.Tests;

// Ports metadata-core.test.js + metadata-honor.test.js — prettify a filename into a title
// and merge per-game metadata (curated wins), surfacing the honor-the-builders fields.
public class MetadataTests
{
    [Fact]
    public void Prettify_splits_camelcase_underscores_and_number_runs()
    {
        Assert.Equal("Extended Bonfire Radius", Metadata.Prettify("ExtendedBonfireRadius"));
        Assert.Equal("More Stamina", Metadata.Prettify("MoreStamina"));
        Assert.Equal("Mr G 3ba Fast Craft", Metadata.Prettify("MrG3ba_FastCraft"));
    }

    [Fact]
    public void MergeMetadata_no_meta_prettified_base_plus_variant_suffix()
    {
        var outList = Metadata.MergeMetadata(
            new[] { new Mod { Name = "MoreStamina_5x", Base = "MoreStamina", Variant = "5x" } },
            new Dictionary<string, ModMeta>());
        Assert.Equal("More Stamina (5x)", outList[0].DisplayName);
        Assert.False(outList[0].HasMeta);
        Assert.Null(outList[0].Description);
    }

    [Fact]
    public void MergeMetadata_meta_by_base_wins_keeps_real_title_and_description()
    {
        var meta = new Dictionary<string, ModMeta>
        {
            ["MoreStamina"] = new() { Title = "More Stamina", Description = "Increases max stamina.", Author = "someone", Url = "https://nexus/123" },
        };
        var outList = Metadata.MergeMetadata(
            new[] { new Mod { Name = "MoreStamina_5x", Base = "MoreStamina", Variant = "5x" } }, meta);
        Assert.Equal("More Stamina (5x)", outList[0].DisplayName);
        Assert.Equal("Increases max stamina.", outList[0].Description);
        Assert.Equal("https://nexus/123", outList[0].ModUrl);
        Assert.True(outList[0].HasMeta);
    }

    [Fact]
    public void MergeMetadata_preserves_existing_fields()
    {
        var outList = Metadata.MergeMetadata(
            new[] { new Mod { Name = "X", Base = "X", Variant = null, Class = "sp", Enabled = true } },
            new Dictionary<string, ModMeta>());
        Assert.Equal("sp", outList[0].Class);
        Assert.True(outList[0].Enabled);
        Assert.Equal("X", outList[0].DisplayName);
    }

    [Fact]
    public void MergeMetadata_surfaces_honor_fields()
    {
        var map = new Dictionary<string, ModMeta>
        {
            ["jei"] = new()
            {
                Title = "JEI", Description = "d", Author = "mezz",
                AuthorUrl = "https://www.curseforge.com/members/mezz",
                Url = "https://www.curseforge.com/minecraft/mc-mods/jei",
                Source = "https://github.com/mezz/JustEnoughItems",
                Donate = "https://buymeacoffee.com/mezz",
                Image = "https://media.forgecdn.net/x.png",
                Downloads = 999,
            },
        };
        var m = Metadata.MergeMetadata(new[] { new Mod { Name = "jei", Base = "jei" } }, map)[0];
        Assert.Equal("mezz", m.Author);
        Assert.Equal("https://www.curseforge.com/members/mezz", m.AuthorUrl);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/jei", m.ModUrl);
        Assert.Equal("https://github.com/mezz/JustEnoughItems", m.Source);
        Assert.Equal("https://buymeacoffee.com/mezz", m.Donate);
        Assert.Equal("https://media.forgecdn.net/x.png", m.Image);
        Assert.Equal(999, m.Downloads);
    }

    [Fact]
    public void MergeMetadata_leaves_honor_fields_null_with_no_entry()
    {
        var m = Metadata.MergeMetadata(new[] { new Mod { Name = "x", Base = "x" } }, new Dictionary<string, ModMeta>())[0];
        Assert.Null(m.AuthorUrl);
        Assert.Null(m.Source);
        Assert.Null(m.Donate);
        Assert.Null(m.Image);
        Assert.Null(m.Downloads);
    }
}
