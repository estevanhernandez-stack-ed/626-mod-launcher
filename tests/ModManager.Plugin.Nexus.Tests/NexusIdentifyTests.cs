using System.Net;
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus.Tests;

// md5 file-identify over the plugin: GET /v1/games/{domain}/mods/md5_search/{md5}.json returns an
// array of { mod, file_details }; the plugin reads the first valid element into a SourceIdentifyResult
// (the ref + the full metadata, both from the md5 call). Relocated from the deleted Core
// NexusClientTests.GetByMd5Async* + NexusRequestsTests.MapMd5Response* — reworked over NexusModSource.
// NOTE: the md5_search request is Calls[0]; the category restore adds ONE cached per-domain game-info
// GET (/v1/games/{domain}.json) on the side, so when a category dict is served that lands at Calls[1].
// The tests in THIS file serve a single canned body to every URL, so Calls[0] is always the md5 call.
public class NexusIdentifyTests
{
    [Fact]
    public async Task IdentifyByHashAsync_maps_a_200_array_body_to_ref_plus_metadata()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"mod":{"name":"Matched","mod_id":777,"author":"auth","summary":"s"},"file_details":{"file_id":5}}]""");
        var src = h.Source();

        var result = await src.IdentifyByHashAsync("skyrimspecialedition", "abc123");

        // The md5-search request is Calls[0]. (A cached per-domain game-info GET also fires for category
        // resolution, but with the single canned-body stub it lands after this and never displaces Calls[0].)
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
    public async Task IdentifyByHashAsync_skips_a_malformed_first_element_and_takes_the_next_valid_one()
    {
        // FIX 8: per-element shape guards must SKIP (continue), not abort. A malformed FIRST entry used to
        // sink the whole identify; now the second, valid entry is returned.
        var h = new StubHandler(HttpStatusCode.OK,
            """[ { "junk": 1 }, { "mod": { "mod_id": 5, "name": "Real" }, "file_details": { "file_id": 9, "version": "2.0" } } ]""");
        var src = h.Source();

        var result = await src.IdentifyByHashAsync("skyrim", "abc");

        Assert.NotNull(result);
        Assert.Equal(5, result!.Ref.ModId);          // the SECOND (valid) element won, not null
        Assert.Equal("2.0", result.Ref.Version);
        Assert.Equal("Real", result.Metadata.Title);
        Assert.Equal(9, result.Metadata.NexusFileId);
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
