using System.Net;
using System.Text;
using ModManager.Core;

namespace ModManager.Tests.Nexus;

// Endorse/abstain write path: POST /v1/games/{domain}/mods/{id}/{endorse|abstain}.json with
// body {"Version": installedVersion}. A 4xx precondition refusal degrades to a friendly
// status (never throws); a 429 still propagates as NexusRateLimitException.
public class NexusEndorseTests
{
    private sealed record Call(string Url, string Method, string? ApiKey, string? AppName, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<Call> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var apiKey = request.Headers.TryGetValues("apikey", out var v) ? string.Join(",", v) : null;
            var appName = request.Headers.TryGetValues("Application-Name", out var a) ? string.Join(",", a) : null;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey, appName, body));
            var (status, json) = _responder(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static NexusClient Client(StubHandler h, NexusOptions opts) => new(new HttpClient(h), opts);

    // (a) Request builder.

    [Fact]
    public void EndorseRequest_endorse_builds_post_with_version_body_and_headers()
    {
        var r = NexusRequests.EndorseRequest("eldenring", 42, "1.0", EndorseAction.Endorse,
            new NexusOptions { ApiKey = "K", AppVersion = "0.3.0" });

        Assert.Equal("POST", r.Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/endorse.json", r.Url);
        Assert.Equal("""{"Version":"1.0"}""", r.Body);
        Assert.Equal("K", r.Headers["apikey"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
        Assert.Equal("626-mod-launcher", r.Headers["Application-Name"]);
        Assert.Equal("0.3.0", r.Headers["Application-Version"]);
    }

    [Fact]
    public void EndorseRequest_abstain_uses_abstain_segment()
    {
        var r = NexusRequests.EndorseRequest("eldenring", 42, "1.0", EndorseAction.Abstain, new NexusOptions { ApiKey = "K" });

        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/abstain.json", r.Url);
    }

    [Fact]
    public void EndorseRequest_honors_custom_baseurl()
    {
        var r = NexusRequests.EndorseRequest("skyrim", 7, "2.0", EndorseAction.Endorse,
            new NexusOptions { BaseUrl = "https://proxy.example" });

        Assert.Equal("https://proxy.example/v1/games/skyrim/mods/7/endorse.json", r.Url);
    }

    // (b) Success path.

    [Fact]
    public async Task EndorseAsync_success_returns_status_not_refused()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK, """{"message":"Endorsed","status":"Endorsed"}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse);

        Assert.False(outcome.Refused);
        Assert.Equal("Endorsed", outcome.Status);
        Assert.Equal("POST", h.Calls[0].Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/endorse.json", h.Calls[0].Url);
        Assert.Equal("""{"Version":"1.0"}""", h.Calls[0].Body);
        Assert.Equal("K", h.Calls[0].ApiKey);
    }

    [Fact]
    public async Task EndorseAsync_abstain_success()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK, """{"message":"Abstained","status":"Abstained"}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Abstain);

        Assert.False(outcome.Refused);
        Assert.Equal("Abstained", outcome.Status);
        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/42/abstain.json", h.Calls[0].Url);
    }

    // (c) Refusals.

    [Fact]
    public async Task EndorseAsync_not_downloaded_maps_to_friendly_message_refused()
    {
        var h = new StubHandler(_ => (HttpStatusCode.BadRequest, """{"message":"NOT_DOWNLOADED_MOD"}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse);

        Assert.True(outcome.Refused);
        Assert.Equal("You need to download this mod before you can endorse it.", outcome.Message);
    }

    [Fact]
    public async Task EndorseAsync_too_soon_maps_to_friendly_wait_message_refused()
    {
        var h = new StubHandler(_ => (HttpStatusCode.BadRequest, """{"message":"TOO_SOON_AFTER_DOWNLOAD"}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse);

        Assert.True(outcome.Refused);
        Assert.NotNull(outcome.Message);
        Assert.Contains("wait", outcome.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndorseAsync_unknown_code_surfaces_raw_message_refused_no_throw()
    {
        var h = new StubHandler(_ => (HttpStatusCode.BadRequest, """{"message":"SOME_NEW_CODE_WE_DONT_KNOW"}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse);

        Assert.True(outcome.Refused);
        Assert.Equal("SOME_NEW_CODE_WE_DONT_KNOW", outcome.Message);
    }

    [Fact]
    public async Task EndorseAsync_4xx_with_no_parseable_body_still_refuses_no_throw()
    {
        var h = new StubHandler(_ => (HttpStatusCode.BadRequest, "not json at all"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var outcome = await client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse);

        Assert.True(outcome.Refused);
        Assert.NotNull(outcome.Message);
    }

    // (d) Rate limit.

    [Fact]
    public async Task EndorseAsync_429_propagates_rate_limit_exception()
    {
        var h = new StubHandler(_ => ((HttpStatusCode)429, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await Assert.ThrowsAsync<NexusRateLimitException>(
            () => client.EndorseAsync("eldenring", 42, "1.0", EndorseAction.Endorse));
    }

    // FriendlyRefusal map is pure + directly testable.

    [Fact]
    public void FriendlyRefusal_known_codes_mapped_unknown_passes_through()
    {
        Assert.Equal("You need to download this mod before you can endorse it.",
            NexusEndorse.FriendlyRefusal("NOT_DOWNLOADED_MOD"));
        Assert.Contains("wait", NexusEndorse.FriendlyRefusal("TOO_SOON_AFTER_DOWNLOAD"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WHATEVER", NexusEndorse.FriendlyRefusal("WHATEVER"));
    }
}
