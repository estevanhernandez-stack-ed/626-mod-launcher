using System.Net;
using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// The HTTP client over the pure Nexus builders. Transport is injectable (a stub
// HttpMessageHandler) so this is testable without a real key. 404 on md5_search is
// "not found" (normal) and returns null; other failures throw.
public class NexusClientTests
{
    private sealed record Call(string Url, string Method, string? ApiKey);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<Call> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var apiKey = request.Headers.TryGetValues("apikey", out var v) ? string.Join(",", v) : null;
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey));
            var (status, json) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static NexusClient Client(StubHandler h, NexusOptions opts) => new(new HttpClient(h), opts);

    [Fact]
    public async Task GetModAsync_maps_a_200_mod_details_body()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """{"name":"SkyUI","summary":"s","author":"auth","mod_id":3863}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var e = await client.GetModAsync("skyrimspecialedition", 3863);

        // Calls[0] = game-info (category prefetch); Calls[1] = the actual mod request
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/3863.json", h.Calls[1].Url);
        Assert.Equal("K", h.Calls[1].ApiKey);
        Assert.Equal("SkyUI", e!.Title);
        Assert.Equal("auth", e.Author);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/3863", e.Url);
    }

    [Fact]
    public async Task GetModAsync_throws_on_non_ok()
    {
        var h = new StubHandler(_ => (HttpStatusCode.Forbidden, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetModAsync("skyrim", 1));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task GetByMd5Async_maps_a_200_array_body()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """[{"mod":{"name":"Matched","mod_id":777,"author":"auth"},"file_details":{"file_id":5}}]"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var m = await client.GetByMd5Async("skyrimspecialedition", "abc123");

        // Calls[0] = game-info (category prefetch); Calls[1] = the actual md5-search request
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/md5_search/abc123.json", h.Calls[1].Url);
        Assert.Equal("K", h.Calls[1].ApiKey);
        Assert.NotNull(m);
        Assert.Equal(777, m!.ModId);
        Assert.Equal("Matched", m.Meta.Title);
    }

    [Fact]
    public async Task GetByMd5Async_returns_null_on_404()
    {
        var h = new StubHandler(_ => (HttpStatusCode.NotFound, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var m = await client.GetByMd5Async("skyrim", "deadbeef");

        Assert.Null(m);
    }

    [Fact]
    public async Task GetByMd5Async_throws_on_other_non_ok()
    {
        var h = new StubHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetByMd5Async("skyrim", "abc"));
    }

    [Fact]
    public async Task Custom_baseurl_no_key_omits_apikey_header()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK, """{"name":"X","mod_id":9}"""));
        var client = Client(h, new NexusOptions { BaseUrl = "https://proxy.example" });

        await client.GetModAsync("skyrim", 9);

        // Calls[0] = game-info (category prefetch); Calls[1] = the actual mod request
        Assert.Equal("https://proxy.example/v1/games/skyrim/mods/9.json", h.Calls[1].Url);
        Assert.Null(h.Calls[1].ApiKey);
    }

    [Fact]
    public async Task ValidateAsync_maps_a_200_body_to_user()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """{"name":"SomeUser","is_premium":true}"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var u = await client.ValidateAsync();

        Assert.Equal("https://api.nexusmods.com/v1/users/validate.json", h.Calls[0].Url);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.NotNull(u);
        Assert.Equal("SomeUser", u!.Name);
        Assert.True(u.IsPremium);
    }

    [Fact]
    public async Task ValidateAsync_returns_null_on_401()
    {
        var h = new StubHandler(_ => (HttpStatusCode.Unauthorized, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "BAD" });

        var u = await client.ValidateAsync();

        Assert.Null(u);
    }

    [Fact]
    public async Task ValidateAsync_throws_on_other_non_ok()
    {
        var h = new StubHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ValidateAsync());
    }
}
