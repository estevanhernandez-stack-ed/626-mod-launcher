using System.Text.Json;
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

    // --- Nexus MapMod category resolution ---

    [Fact]
    public void Nexus_MapMod_resolves_category_id_to_name_when_categories_provided()
    {
        var json = """{"mod_id":1,"name":"Cool Mod","category_id":7}""";
        using var doc = JsonDocument.Parse(json);
        var categories = new Dictionary<int, string> { [7] = "Gameplay", [8] = "Ships" };
        var meta = NexusRequests.MapMod("windrose", doc.RootElement, categories);
        Assert.Equal("Gameplay", meta.Category);
    }

    [Fact]
    public void Nexus_MapMod_leaves_category_null_when_categories_absent()
    {
        var json = """{"mod_id":1,"name":"Cool Mod","category_id":7}""";
        using var doc = JsonDocument.Parse(json);
        var meta = NexusRequests.MapMod("windrose", doc.RootElement, categories: null);
        Assert.Null(meta.Category);
    }

    [Fact]
    public void Nexus_MapMod_leaves_category_null_for_unknown_id()
    {
        var json = """{"mod_id":1,"name":"Cool Mod","category_id":99}""";
        using var doc = JsonDocument.Parse(json);
        var categories = new Dictionary<int, string> { [7] = "Gameplay", [8] = "Ships" };
        var meta = NexusRequests.MapMod("windrose", doc.RootElement, categories);
        Assert.Null(meta.Category);
    }

    [Fact]
    public void Nexus_MapCategories_reads_the_games_categories_array()
    {
        var json = """{"categories":[{"category_id":1,"name":"Gameplay"},{"category_id":2,"name":"UI"}]}""";
        using var doc = JsonDocument.Parse(json);
        var dict = NexusRequests.MapCategories(doc.RootElement);
        Assert.Equal(2, dict.Count);
        Assert.Equal("Gameplay", dict[1]);
        Assert.Equal("UI", dict[2]);
    }

    [Fact]
    public void Nexus_MapCategories_tolerant_of_malformed_entries()
    {
        // missing name, missing id — only the well-formed entry survives
        var json = """{"categories":[{"category_id":1,"name":"Gameplay"},{"category_id":2},{"name":"NoId"}]}""";
        using var doc = JsonDocument.Parse(json);
        var dict = NexusRequests.MapCategories(doc.RootElement);
        Assert.Single(dict);
        Assert.Equal("Gameplay", dict[1]);
    }
}
