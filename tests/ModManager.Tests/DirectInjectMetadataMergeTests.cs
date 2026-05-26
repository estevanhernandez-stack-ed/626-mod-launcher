using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectMetadataMergeTests
{
    // Row shape produced by DirectInjectService.Row for catalog-recognized mods:
    //   Name = "Seamless Co-op", Base = "Seamless Co-op", Description = "Detected: ..."
    // The "Detected: ..." filler description must be replaced by the Nexus description when
    // metadata.json has an entry for the catalog name.
    [Fact]
    public void Catalog_named_row_picks_up_metadata_by_name_when_base_equals_name()
    {
        var rows = new List<Mod>
        {
            new() {
                Name = "Seamless Co-op",
                Base = "Seamless Co-op",
                Description = "Detected: seamlesscoop",
                Location = "direct-inject",
                Enabled = true,
            },
        };
        var meta = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["Seamless Co-op"] = new ModMeta
            {
                Title = "Seamless Co-op (Elden Ring)",
                Author = "Yui",
                AuthorUrl = "https://www.nexusmods.com/users/49594931",
                Url = "https://www.nexusmods.com/eldenring/mods/510",
                Image = "https://staticdelivery.nexusmods.com/mods/4333/images/510/test.png",
                Description = "Overhaul to the co-operative aspect of Elden Ring's multiplayer",
            },
        };

        var merged = Metadata.MergeMetadata(rows, meta);

        var row = Assert.Single(merged);
        Assert.Equal("Seamless Co-op (Elden Ring)", row.BaseTitle);
        Assert.Equal("Seamless Co-op (Elden Ring)", row.DisplayName);
        Assert.Equal("Yui", row.Author);
        Assert.Equal("https://www.nexusmods.com/users/49594931", row.AuthorUrl);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/510", row.ModUrl);
        Assert.Equal("https://staticdelivery.nexusmods.com/mods/4333/images/510/test.png", row.Image);
        Assert.Equal("Overhaul to the co-operative aspect of Elden Ring's multiplayer", row.Description);
        Assert.True(row.HasMeta);
    }

    // The same row, but without a matching metadata entry, must keep its bare display state —
    // no crash, no misattribution. Prettify falls back to the catalog name as-is (already pretty).
    [Fact]
    public void Catalog_named_row_without_metadata_keeps_bare_display()
    {
        var rows = new List<Mod>
        {
            new() { Name = "Seamless Co-op", Base = "Seamless Co-op", Description = "Detected: seamlesscoop" },
        };
        var merged = Metadata.MergeMetadata(rows, new Dictionary<string, ModMeta>());

        var row = Assert.Single(merged);
        Assert.Equal("Seamless Co Op", row.BaseTitle);   // Prettify treats "-" as a word break
        Assert.Null(row.Author);
        Assert.Null(row.Image);
        Assert.False(row.HasMeta);
    }
}
