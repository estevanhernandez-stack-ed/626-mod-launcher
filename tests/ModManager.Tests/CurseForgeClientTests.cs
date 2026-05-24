using System.Net;
using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// Ports curseforge-client.test.js — the HTTP client over the pure builders. The transport
// is injectable (a stub HttpMessageHandler) so this is testable without a real key.
public class CurseForgeClientTests
{
    private sealed record Call(string Url, string Method, string? ApiKey, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<Call> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var apiKey = request.Headers.TryGetValues("x-api-key", out var v) ? string.Join(",", v) : null;
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey, body));
            var (status, json) = _responder(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static CurseForgeClient Client(StubHandler h, CurseForgeOptions opts) => new(new HttpClient(h), opts);

    [Fact]
    public async Task GetMod_fetches_mods_id_with_key_and_returns_mapped_entry()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """{"data":{"id":5,"name":"Cool Mod","summary":"s","authors":[{"name":"auth"}],"links":{}}}"""));
        var client = Client(h, new CurseForgeOptions { ApiKey = "K" });

        var e = await client.GetModAsync(5);

        Assert.Equal("https://api.curseforge.com/v1/mods/5", h.Calls[0].Url);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("Cool Mod", e!.Title);
        Assert.Equal("auth", e.Author);
    }

    [Fact]
    public async Task GetMods_posts_id_list_and_returns_mapped_entries()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """{"data":[{"id":1,"name":"A","summary":""},{"id":2,"name":"B","summary":""}]}"""));
        var client = Client(h, new CurseForgeOptions { ApiKey = "K" });

        var arr = await client.GetModsAsync(new[] { 1, 2 });

        Assert.Equal("POST", h.Calls[0].Method);
        Assert.Contains("\"modIds\":[1,2]", h.Calls[0].Body);
        Assert.Equal(new[] { "A", "B" }, arr.Select(e => e.Title).ToArray());
    }

    [Fact]
    public async Task Proxy_mode_custom_baseurl_no_key_header()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK, """{"data":{"id":9,"name":"X","summary":""}}"""));
        var client = Client(h, new CurseForgeOptions { BaseUrl = "https://proxy.example" });

        await client.GetModAsync(9);

        Assert.Equal("https://proxy.example/v1/mods/9", h.Calls[0].Url);
        Assert.Null(h.Calls[0].ApiKey);
    }

    [Fact]
    public async Task A_non_ok_response_throws_with_the_status()
    {
        var h = new StubHandler(_ => (HttpStatusCode.Forbidden, "{}"));
        var client = Client(h, new CurseForgeOptions { ApiKey = "K" });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetModAsync(1));
        Assert.Contains("403", ex.Message);
    }
}
