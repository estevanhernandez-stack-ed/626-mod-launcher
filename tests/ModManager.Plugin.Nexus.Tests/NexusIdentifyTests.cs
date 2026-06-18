using System.Net;
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus.Tests;

// md5 file-identify over the plugin: GET /v1/games/{domain}/mods/md5_search/{md5}.json returns an
// array of { mod, file_details }; the plugin reads the first element into a SourceIdentifyResult
// (the ref + the full metadata, both from the one call). Relocated from the deleted Core
// NexusClientTests.GetByMd5Async* + NexusRequestsTests.MapMd5Response* — reworked over
// NexusModSource. NOTE: unlike the old NexusClient, the plugin does NO category-prefetch call, so
// the md5/mod request is Calls[0], not Calls[1].
public class NexusIdentifyTests
{
    [Fact]
    public async Task IdentifyByHashAsync_maps_a_200_array_body_to_ref_plus_metadata()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"mod":{"name":"Matched","mod_id":777,"author":"auth","summary":"s"},"file_details":{"file_id":5}}]""");
        var src = h.Source();

        var result = await src.IdentifyByHashAsync("skyrimspecialedition", "abc123");

        // The plugin's only HTTP call IS the md5-search request (no game-info prefetch).
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/md5_search/abc123.json", h.Calls[0].Url);
        Assert.Equal("GET", h.Calls[0].Method);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("application/json", h.Calls[0].Accept);

        Assert.NotNull(result);
        Assert.Equal("nexus", result!.Ref.SourceId);
        Assert.Equal("skyrimspecialedition", result.Ref.GameDomain);
        Assert.Equal(777, result.Ref.ModId);
        Assert.Equal("Matched", result.Metadata.Title);
        Assert.Equal("auth", result.Metadata.Author);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/777", result.Metadata.ModUrl);
        Assert.Equal(5, result.Metadata.NexusFileId);
    }

    [Fact]
    public async Task IdentifyByHashAsync_stamps_file_id_and_prefers_file_details_version()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"mod":{"mod_id":510,"name":"X","version":"2.3"},"file_details":{"file_id":99,"version":"2.3.1"}}]""");
        var src = h.Source();

        var result = await src.IdentifyByHashAsync("windrose", "deadbeef");

        Assert.NotNull(result);
        Assert.Equal(510, result!.Ref.ModId);
        // file_details.version is the INSTALLED-file version and wins over mod.version on the ref.
        Assert.Equal("2.3.1", result.Ref.Version);
        Assert.Equal(99, result.Metadata.NexusFileId);
        // The mod-level version is the reported upstream/latest version on the metadata.
        Assert.Equal("2.3", result.Metadata.LatestVersion);
    }

    [Fact]
    public async Task IdentifyByHashAsync_returns_null_on_404()
    {
        var h = new StubHandler(HttpStatusCode.NotFound, "{}");
        var src = h.Source();

        Assert.Null(await src.IdentifyByHashAsync("skyrim", "deadbeef"));
    }

    [Fact]
    public async Task IdentifyByHashAsync_returns_null_on_other_non_ok_never_throws()
    {
        // Identify is best-effort on the read path — a 500 returns null rather than throwing.
        var h = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        var src = h.Source();

        Assert.Null(await src.IdentifyByHashAsync("skyrim", "abc"));
    }

    [Fact]
    public async Task IdentifyByHashAsync_returns_null_for_empty_array()
    {
        var h = new StubHandler(HttpStatusCode.OK, "[]");
        var src = h.Source();

        Assert.Null(await src.IdentifyByHashAsync("skyrim", "abc"));
    }

    [Fact]
    public async Task IdentifyByHashAsync_returns_null_for_non_array_body()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod": { "mod_id": 1 } }""");
        var src = h.Source();

        Assert.Null(await src.IdentifyByHashAsync("skyrim", "abc"));
    }

    [Fact]
    public async Task IdentifyByHashAsync_omits_apikey_header_when_no_key()
    {
        var h = new StubHandler(HttpStatusCode.OK, """[{"mod":{"mod_id":9,"name":"X"}}]""");
        var src = h.Source(apiKey: null);

        await src.IdentifyByHashAsync("skyrim", "abc");

        Assert.Null(h.Calls[0].ApiKey);
        // ToS identity headers are always present, even without a key.
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
    }
}
