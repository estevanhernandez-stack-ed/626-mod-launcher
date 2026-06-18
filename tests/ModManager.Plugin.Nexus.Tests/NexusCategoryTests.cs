using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// Category resolution — relocated from the deleted Core CategoryTests Nexus section. The old Core
// NexusRequests.MapMod accepted a category-id -> name dictionary (prefetched from the game-info call)
// and resolved category_id to a Category name. The PLUGIN deliberately drops that: it makes no
// game-info prefetch and carries no category dictionary, so MapMod ALWAYS sets Category = null —
// category-name resolution stays a Core concern (SourceMetadataMapper / the category dictionary live
// in Core, fed by the manifest facade). These tests pin that documented behavior: a category_id in the
// response does NOT produce a Category off the plugin.
public class NexusCategoryTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "windrose", ModId: 1, Version: "1.0");

    [Fact]
    public async Task FetchMetadataAsync_leaves_category_null_even_with_category_id_present()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 1, "name": "Cool Mod", "category_id": 7 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        // The plugin has no category dictionary — category resolution is a Core concern.
        Assert.NotNull(meta);
        Assert.Null(meta!.Category);
    }

    [Fact]
    public async Task IdentifyByHashAsync_leaves_category_null_even_with_category_id_present()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{ "mod": { "mod_id": 1, "name": "Cool Mod", "category_id": 7 }, "file_details": { "file_id": 5 } }]""");
        var result = await h.Source().IdentifyByHashAsync("windrose", "abc");

        Assert.NotNull(result);
        Assert.Null(result!.Metadata.Category);
    }
}
