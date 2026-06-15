using System.Net;
using System.Text;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests.Nexus;

// updated.json wiring — the bulk "recently updated by game" call. One call per game
// returns [{ mod_id, latest_file_update, latest_mod_activity }] (unix timestamps) so the
// auto-check can narrow its per-id refresh to only the mods that actually changed.
public class NexusUpdatedTests
{
    private sealed record Call(string Url, string Method, string? ApiKey, string? AppName);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<Call> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var apiKey = request.Headers.TryGetValues("apikey", out var v) ? string.Join(",", v) : null;
            var appName = request.Headers.TryGetValues("Application-Name", out var a) ? string.Join(",", a) : null;
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey, appName));
            var (status, json) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static NexusClient Client(StubHandler h, NexusOptions opts) => new(new HttpClient(h), opts);

    [Fact]
    public void UpdatedRequest_builds_get_with_period_query_and_headers()
    {
        var r = NexusRequests.UpdatedRequest("eldenring", "1w", new NexusOptions { ApiKey = "K", AppVersion = "0.3.0" });

        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/updated.json?period=1w", r.Url);
        Assert.Equal("K", r.Headers["apikey"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
        Assert.Equal("626-mod-launcher", r.Headers["Application-Name"]);
        Assert.Equal("0.3.0", r.Headers["Application-Version"]);
    }

    [Fact]
    public void UpdatedRequest_honors_custom_baseurl()
    {
        var r = NexusRequests.UpdatedRequest("skyrim", "1d", new NexusOptions { BaseUrl = "https://proxy.example" });
        Assert.Equal("https://proxy.example/v1/games/skyrim/mods/updated.json?period=1d", r.Url);
    }

    [Fact]
    public void MapUpdatedResponse_maps_array_to_entries()
    {
        using var doc = JsonDocument.Parse("""
        [
            { "mod_id": 123, "latest_file_update": 1700000000, "latest_mod_activity": 1700000001 },
            { "mod_id": 456, "latest_file_update": 1700000100, "latest_mod_activity": 1700000200 }
        ]
        """);

        var entries = NexusRequests.MapUpdatedResponse(doc.RootElement);

        Assert.Equal(2, entries.Count);
        Assert.Equal(123, entries[0].ModId);
        Assert.Equal(1700000000L, entries[0].LatestFileUpdate);
        Assert.Equal(1700000001L, entries[0].LatestModActivity);
        Assert.Equal(456, entries[1].ModId);
    }

    [Fact]
    public void MapUpdatedResponse_empty_array_is_empty_list()
    {
        using var doc = JsonDocument.Parse("[]");
        var entries = NexusRequests.MapUpdatedResponse(doc.RootElement);
        Assert.Empty(entries);
    }

    [Fact]
    public void MapUpdatedResponse_skips_entries_missing_mod_id()
    {
        using var doc = JsonDocument.Parse("""
        [
            { "latest_file_update": 1700000000 },
            { "mod_id": 789, "latest_file_update": 1700000000, "latest_mod_activity": 1700000001 }
        ]
        """);

        var entries = NexusRequests.MapUpdatedResponse(doc.RootElement);

        Assert.Single(entries);
        Assert.Equal(789, entries[0].ModId);
    }

    [Fact]
    public void MapUpdatedResponse_non_array_is_empty_list()
    {
        using var doc = JsonDocument.Parse("""{ "mod_id": 1 }""");
        var entries = NexusRequests.MapUpdatedResponse(doc.RootElement);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_calls_updated_endpoint_and_maps()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """[ { "mod_id": 123, "latest_file_update": 1700000000, "latest_mod_activity": 1700000001 } ]"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var entries = await client.GetRecentlyUpdatedAsync("eldenring", "1w");

        Assert.Equal("https://api.nexusmods.com/v1/games/eldenring/mods/updated.json?period=1w", h.Calls[0].Url);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Single(entries);
        Assert.Equal(123, entries[0].ModId);
    }

    [Fact]
    public async Task GetRecentlyUpdatedAsync_is_rate_limit_aware()
    {
        var h = new StubHandler(_ => ((HttpStatusCode)429, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await Assert.ThrowsAsync<NexusRateLimitException>(() => client.GetRecentlyUpdatedAsync("eldenring", "1w"));
    }
}
