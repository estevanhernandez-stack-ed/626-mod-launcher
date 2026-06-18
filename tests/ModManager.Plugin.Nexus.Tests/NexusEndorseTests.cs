using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// Endorse/abstain write path over the plugin: POST /v1/games/{domain}/mods/{id}/{endorse|abstain}.json
// with body {"Version": installedVersion}. Relocated from the deleted Core NexusEndorseTests +
// NexusPostBodyTests — reworked over NexusModSource.SetEndorsedAsync.
//
// BEHAVIORAL DIFFERENCE FROM THE DELETED CLIENT (the B1-review-requested change): the old NexusClient
// THREW NexusRateLimitException on a 429 endorse. The plugin's write path NEVER throws — a 429 (and
// every other non-2xx precondition refusal, and even a network failure) degrades to
// Refused = true with a friendly message. These tests pin the new contract.
public class NexusEndorseTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "eldenring", ModId: 42, Version: "1.0");

    // --- request shape: method / url / body / ToS headers ---

    [Fact]
    public async Task SetEndorsedAsync_endorse_posts_version_body_to_endorse_segment_with_headers()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{"message":"Endorsed","status":"Endorsed"}""");
        var src = h.Source();

        var result = await src.SetEndorsedAsync(Ref, endorsed: true);

        Assert.Equal("POST", h.Calls[0].Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/endorse.json", h.Calls[0].Url);
        Assert.Equal("""{"Version":"1.0"}""", h.Calls[0].Body);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("application/json", h.Calls[0].Accept);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Equal("0.3.0", h.Calls[0].AppVersion);

        Assert.True(result.Ok);
        Assert.False(result.Refused);
        Assert.True(result.NowEndorsed);
    }

    [Fact]
    public async Task SetEndorsedAsync_abstain_uses_abstain_segment()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{"message":"Abstained","status":"Abstained"}""");
        var src = h.Source();

        var result = await src.SetEndorsedAsync(Ref, endorsed: false);

        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/abstain.json", h.Calls[0].Url);
        Assert.True(result.Ok);
        // status != "Endorsed" -> NowEndorsed is false (the heart is now empty).
        Assert.False(result.NowEndorsed);
    }

    // --- refusals: every non-2xx degrades to a friendly Refused, never a throw ---

    [Fact]
    public async Task SetEndorsedAsync_not_downloaded_maps_to_friendly_message_refused()
    {
        var h = new StubHandler(HttpStatusCode.BadRequest, """{"message":"NOT_DOWNLOADED_MOD"}""");
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.False(result.Ok);
        Assert.True(result.Refused);
        Assert.Equal("You need to download this mod before you can endorse it.", result.Message);
        Assert.Null(result.NowEndorsed);
    }

    [Fact]
    public async Task SetEndorsedAsync_too_soon_maps_to_friendly_wait_message_refused()
    {
        var h = new StubHandler(HttpStatusCode.BadRequest, """{"message":"TOO_SOON_AFTER_DOWNLOAD"}""");
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.True(result.Refused);
        Assert.NotNull(result.Message);
        Assert.Contains("wait", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetEndorsedAsync_unknown_code_surfaces_raw_message_refused_no_throw()
    {
        var h = new StubHandler(HttpStatusCode.BadRequest, """{"message":"SOME_NEW_CODE_WE_DONT_KNOW"}""");
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.True(result.Refused);
        Assert.Equal("SOME_NEW_CODE_WE_DONT_KNOW", result.Message);
    }

    [Fact]
    public async Task SetEndorsedAsync_4xx_with_no_parseable_body_still_refuses_no_throw()
    {
        var h = new StubHandler(HttpStatusCode.BadRequest, "not json at all");
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.True(result.Refused);
        Assert.NotNull(result.Message);
        Assert.Contains("400", result.Message!);
    }

    // --- the B1-review-requested 429 change: degrade, do NOT throw ---

    [Fact]
    public async Task SetEndorsedAsync_429_degrades_to_friendly_refusal_does_not_throw()
    {
        var h = new StubHandler((HttpStatusCode)429, "{}");

        // Old NexusClient threw NexusRateLimitException here; the plugin must NOT throw.
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.False(result.Ok);
        Assert.True(result.Refused);
        Assert.NotNull(result.Message);
        Assert.Contains("rate-limiting", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.NowEndorsed);
    }

    [Fact]
    public async Task SetEndorsedAsync_429_with_api_message_prefers_the_api_wording()
    {
        // When the 429 body carries a "message", the API's own human wording surfaces (passthrough).
        var h = new StubHandler((HttpStatusCode)429, """{"message":"slow down please"}""");
        var result = await h.Source().SetEndorsedAsync(Ref, endorsed: true);

        Assert.True(result.Refused);
        Assert.Equal("slow down please", result.Message);
    }

    // --- network failure: degrade, do NOT throw ---

    [Fact]
    public async Task SetEndorsedAsync_network_failure_degrades_to_refusal_no_throw()
    {
        var src = new ModManager.Plugin.Nexus.NexusModSource(
            new HttpClient(new ThrowingHandler()), () => "K", "0.3.0");

        var result = await src.SetEndorsedAsync(Ref, endorsed: true);

        Assert.False(result.Ok);
        Assert.True(result.Refused);
        Assert.NotNull(result.Message);
        Assert.Contains("reach Nexus", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("offline");
    }
}
