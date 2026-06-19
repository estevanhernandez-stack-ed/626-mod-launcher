using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// Category resolution — the plugin restores the per-domain category dictionary that Core's NexusClient
// used to provide (it was dropped in the lean B1 re-implementation). On identify/fetch the plugin makes
// one cached GET /v1/games/{domain}.json, maps category_id -> name, and writes ModMeta.Category. These
// tests pin that round-trip plus the best-effort fallbacks (id not in the dict, or a failed game-info
// fetch -> Category null, never a throw).
public class NexusCategoryTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "windrose", ModId: 1, Version: "1.0");

    private const string GameInfo =
        """{ "categories": [ { "category_id": 7, "name": "Gameplay" }, { "category_id": 9, "name": "Armor" } ] }""";

    // Per-URL responder: the game-info call (/v1/games/windrose.json) returns the category dict; every
    // other call (md5_search / per-mod) returns the supplied mod body.
    private static StubHandler Serve(string modBody, HttpStatusCode gameInfoStatus = HttpStatusCode.OK, string gameInfo = GameInfo)
        => new(req => req.RequestUri!.AbsolutePath.EndsWith("/games/windrose.json", StringComparison.Ordinal)
            ? (gameInfoStatus, gameInfo)
            : (HttpStatusCode.OK, modBody));

    [Fact]
    public async Task FetchMetadataAsync_resolves_category_id_to_a_name()
    {
        var h = Serve("""{ "mod_id": 1, "name": "Cool Mod", "category_id": 7 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Equal("Gameplay", meta!.Category);
    }

    [Fact]
    public async Task IdentifyByHashAsync_resolves_category_id_to_a_name()
    {
        var h = Serve("""[{ "mod": { "mod_id": 1, "name": "Cool Mod", "category_id": 9 }, "file_details": { "file_id": 5 } }]""");
        var result = await h.Source().IdentifyByHashAsync("windrose", "abc");

        Assert.NotNull(result);
        Assert.Equal("Armor", result!.Metadata.Category);
    }

    [Fact]
    public async Task An_unknown_category_id_leaves_category_null()
    {
        var h = Serve("""{ "mod_id": 1, "name": "Cool Mod", "category_id": 999 }"""); // 999 not in the dict
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Null(meta!.Category);
    }

    [Fact]
    public async Task A_failed_game_info_fetch_leaves_category_null_and_does_not_throw()
    {
        // game-info 500 -> no dict cached -> Category null; the metadata itself still maps cleanly.
        var h = Serve("""{ "mod_id": 1, "name": "Cool Mod", "category_id": 7 }""", gameInfoStatus: HttpStatusCode.InternalServerError);
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Equal("Cool Mod", meta!.Title); // metadata still mapped
        Assert.Null(meta.Category);            // category gracefully absent, no throw
    }
}
