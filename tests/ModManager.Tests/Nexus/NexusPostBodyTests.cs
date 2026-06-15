using System.Net;
using System.Text;
using ModManager.Core;

namespace ModManager.Tests.Nexus;

// The Nexus client was GET-only. The first write path (endorse) needs SendAsync to attach
// a JSON body when the request carries one — mirroring CurseForgeClient. A null body (GET)
// must still send no content, and the rate-limit / 429 path is left intact.
public class NexusPostBodyTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public CapturingHandler(HttpStatusCode status = HttpStatusCode.OK, string json = "{}")
        {
            _status = status;
            _json = json;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static NexusClient Client(CapturingHandler h) => new(new HttpClient(h), new NexusOptions { ApiKey = "K" });

    [Fact]
    public async Task SendAsync_post_with_body_sends_json_content()
    {
        var h = new CapturingHandler();
        var client = Client(h);
        var req = new ApiRequest("https://example.test/v1/thing.json", "POST",
            new Dictionary<string, string> { ["Accept"] = "application/json" },
            Body: """{"Version":"1.0"}""");

        await client.SendAsync(req);

        Assert.Equal(HttpMethod.Post, h.LastRequest!.Method);
        Assert.NotNull(h.LastRequest.Content);
        Assert.Equal("""{"Version":"1.0"}""", h.LastBody);
        Assert.Equal("application/json", h.LastRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SendAsync_get_with_null_body_sends_no_content()
    {
        var h = new CapturingHandler();
        var client = Client(h);
        var req = new ApiRequest("https://example.test/v1/thing.json", "GET",
            new Dictionary<string, string> { ["Accept"] = "application/json" });

        await client.SendAsync(req);

        Assert.Equal(HttpMethod.Get, h.LastRequest!.Method);
        Assert.Null(h.LastRequest.Content);
    }

    [Fact]
    public async Task SendAsync_skips_copying_content_type_header_onto_message()
    {
        // A Content-Type in the request headers must not be copied onto msg.Headers — that throws
        // (it's a content header, not a message header). StringContent sets it instead.
        var h = new CapturingHandler();
        var client = Client(h);
        var req = new ApiRequest("https://example.test/v1/thing.json", "POST",
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body: """{"Version":"1.0"}""");

        // Must not throw.
        await client.SendAsync(req);

        Assert.Equal("application/json", h.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }
}
