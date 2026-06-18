using ModManager.Core;

namespace ModManager.Tests;

public class CategoryTests
{
    [Fact]
    public void MergeMetadata_threads_category_from_meta_onto_mod()
    {
        var mod = new Mod { Name = "X", Base = "X" };
        var map = new Dictionary<string, ModMeta> { ["X"] = new ModMeta { Title = "X", Category = "Gameplay" } };
        var merged = Metadata.MergeMetadata(new[] { mod }, map).First();
        Assert.Equal("Gameplay", merged.Category);
    }

    [Fact]
    public void MergeMetadata_leaves_category_null_when_metadata_silent()
    {
        var merged = Metadata.MergeMetadata(new[] { new Mod { Name = "X", Base = "X" } }, null).First();
        Assert.Null(merged.Category);
    }

    [Fact]
    public void CurseForge_MapMod_captures_first_category_name_when_present()
    {
        var mod = new CfMod
        {
            Id = 1,
            Name = "Test Mod",
            Categories = new List<CfCategory>
            {
                new CfCategory { Name = "Gameplay" },
                new CfCategory { Name = "UI" },
            },
        };
        var meta = CurseForgeRequests.MapMod(mod);
        Assert.Equal("Gameplay", meta.Category);
    }

    [Fact]
    public void CurseForge_MapMod_category_null_when_categories_absent()
    {
        var mod = new CfMod { Id = 2, Name = "No Category Mod" };
        var meta = CurseForgeRequests.MapMod(mod);
        Assert.Null(meta.Category);
    }

    [Fact]
    public void CurseForge_MapMod_category_null_when_categories_empty()
    {
        var mod = new CfMod { Id = 3, Name = "Empty Categories Mod", Categories = new List<CfCategory>() };
        var meta = CurseForgeRequests.MapMod(mod);
        Assert.Null(meta.Category);
    }

    // Nexus MapMod / MapCategories category-resolution tests moved to the plugin test project
    // alongside the relocated Nexus client impl (the Core NexusRequests mapper was deleted).
}
