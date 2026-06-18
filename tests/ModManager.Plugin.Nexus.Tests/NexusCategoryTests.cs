using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// Category resolution — relocated from the deleted Core CategoryTests Nexus section. The old Core
// NexusRequests.MapMod accepted a category-id -> name dictionary (prefetched from the game-info call)
// and resolved category_id to a Category name. The lean PLUGIN drops that: it makes no game-info
// prefetch and carries no category dictionary, so MapMod ALWAYS sets Category = null. Nexus
// category-LABEL enrichment is therefore DROPPED in the FULL flavor — it worked pre-B1 via the old
// NexusClient's per-domain category cache, and nothing took it over (there is NO Core category
// resolver; SourceMetadataMapper just passes the null through). Accepted lean-plugin cost, tracked in
// docs/smoke-tests/pending.md. These tests pin that behavior: a category_id in the response does NOT
// produce a Category off the plugin.
public class NexusCategoryTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "windrose", ModId: 1, Version: "1.0");

    [Fact]
    public async Task FetchMetadataAsync_leaves_category_null_even_with_category_id_present()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 1, "name": "Cool Mod", "category_id": 7 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        // The plugin has no category dictionary — Nexus category-label enrichment is dropped (no Core resolver took over).
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
