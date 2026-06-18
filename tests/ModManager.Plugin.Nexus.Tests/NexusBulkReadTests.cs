using System.Net;
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus.Tests;

// Bulk read paths over the plugin:
//   - GetUserEndorsementsAsync -> GET /v1/user/endorsements.json (the whole library's hearts, one call)
//   - GetRecentlyUpdatedAsync  -> GET /v1/games/{domain}/mods/updated.json?period={period}
// Relocated from the deleted Core NexusUserEndorsementsTests (its UserEndorsementsRequest /
// MapUserEndorsements / GetUserEndorsementsAsync sections — the ApplyEndorsements section STAYS in Core,
// it tests NexusRefresh) + NexusUpdatedTests — reworked over NexusModSource.
//
// RATE-LIMIT CONTRACT: on the bulk READ path a 429 throws SourceRateLimitException (so a sweep can stop
// and report partial progress) — this is the Abstractions exception, not Core's deleted
// NexusRateLimitException. Any other non-2xx yields an empty list (best-effort sync never throws).
public class NexusBulkReadTests
{
    // --- user endorsements ---

    [Fact]
    public async Task GetUserEndorsementsAsync_calls_endpoint_and_maps_entries()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"mod_id":42,"domain_name":"eldenring","status":"Endorsed"},{"mod_id":7,"domain_name":"skyrimspecialedition","status":"Abstained"}]""");
        var entries = await h.Source().GetUserEndorsementsAsync();

        Assert.Equal("https://api.nexusmods.com/v1/user/endorsements.json", h.Calls[0].Url);
        Assert.Equal("GET", h.Calls[0].Method);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Equal("0.3.0", h.Calls[0].AppVersion);

        Assert.Equal(2, entries.Count);
        Assert.Equal(42, entries[0].ModId);
        Assert.Equal("eldenring", entries[0].DomainName);
        Assert.Equal("Endorsed", entries[0].Status);
        Assert.Equal(7, entries[1].ModId);
        Assert.Equal("skyrimspecialedition", entries[1].DomainName);
        Assert.Equal("Abstained", entries[1].Status);
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_empty_array_is_empty_list()
    {
        var h = new StubHandler(HttpStatusCode.OK, "[]");
        Assert.Empty(await h.Source().GetUserEndorsementsAsync());
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_non_array_body_is_empty_list()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 1 }""");
        Assert.Empty(await h.Source().GetUserEndorsementsAsync());
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_skips_entries_missing_required_fields()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"domain_name":"eldenring","status":"Endorsed"},{"mod_id":42,"status":"Endorsed"},{"mod_id":42,"domain_name":"eldenring"},"not an object",{"mod_id":99,"domain_name":"eldenring","status":"Endorsed"}]""");
        var entries = await h.Source().GetUserEndorsementsAsync();

        Assert.Single(entries);
        Assert.Equal(99, entries[0].ModId);
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_429_throws_source_rate_limit_exception()
    {
        var h = new StubHandler((HttpStatusCode)429, "{}");
        await Assert.ThrowsAsync<SourceRateLimitException>(() => h.Source().GetUserEndorsementsAsync());
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_other_non_ok_is_empty_list_never_throws()
    {
        var h = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        Assert.Empty(await h.Source().GetUserEndorsementsAsync());
    }

    // --- recently updated ---

    [Fact]
    public async Task GetRecentlyUpdatedAsync_calls_updated_endpoint_with_period_and_maps()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"mod_id":123,"latest_file_update":1700000000,"latest_mod_activity":1700000001},{"mod_id":456,"latest_file_update":1700000100,"latest_mod_activity":1700000200}]""");
        var entries = await h.Source().GetRecentlyUpdatedAsync("eldenring", "1w");

        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/updated.json?period=1w", h.Calls[0].Url);
        Assert.Equal("GET", h.Calls[0].Method);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);

        Assert.Equal(2, entries.Count);
        Assert.Equal(123, entries[0].ModId);
        Assert.Equal(1700000000L, entries[0].LatestFileUpdate);
        Assert.Equal(456, entries[1].ModId);
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_empty_array_is_empty_list()
    {
        var h = new StubHandler(HttpStatusCode.OK, "[]");
        Assert.Empty(await h.Source().GetRecentlyUpdatedAsync("skyrim", "1d"));
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_skips_entries_missing_mod_id()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """[{"latest_file_update":1700000000},{"mod_id":789,"latest_file_update":1700000000,"latest_mod_activity":1700000001}]""");
        var entries = await h.Source().GetRecentlyUpdatedAsync("eldenring", "1w");

        Assert.Single(entries);
        Assert.Equal(789, entries[0].ModId);
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_non_array_body_is_empty_list()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 1 }""");
        Assert.Empty(await h.Source().GetRecentlyUpdatedAsync("skyrim", "1w"));
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_429_throws_source_rate_limit_exception()
    {
        var h = new StubHandler((HttpStatusCode)429, "{}");
        await Assert.ThrowsAsync<SourceRateLimitException>(() => h.Source().GetRecentlyUpdatedAsync("eldenring", "1w"));
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_other_non_ok_is_empty_list_never_throws()
    {
        var h = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        Assert.Empty(await h.Source().GetRecentlyUpdatedAsync("eldenring", "1w"));
    }
}
