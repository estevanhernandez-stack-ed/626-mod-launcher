using System.Net;
using System.Text;
using ModManager.Core;

namespace ModManager.Tests.Nexus;

// Rate-limit hardening on the Nexus client: parse x-rl-* headers, surface a typed
// 429, and always attach the Nexus-ToS Application-Name / Application-Version headers.
public class NexusRateLimitTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string, (string, string)[])> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string, (string, string)[])> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, json, headers) = _responder(request);
            var res = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            foreach (var (k, v) in headers)
                res.Headers.TryAddWithoutValidation(k, v);
            return Task.FromResult(res);
        }
    }

    private static NexusClient Client(StubHandler h, NexusOptions opts) => new(new HttpClient(h), opts);

    [Fact]
    public void Parse_reads_rl_headers_into_record()
    {
        var headers = new List<KeyValuePair<string, IEnumerable<string>>>
        {
            new("x-rl-daily-remaining", new[] { "19999" }),
            new("x-rl-daily-limit", new[] { "20000" }),
            new("x-rl-hourly-remaining", new[] { "498" }),
            new("x-rl-hourly-limit", new[] { "500" }),
        };

        var rl = NexusRateLimit.Parse(headers);

        Assert.Equal(19999, rl.DailyRemaining);
        Assert.Equal(20000, rl.DailyLimit);
        Assert.Equal(498, rl.HourlyRemaining);
        Assert.Equal(500, rl.HourlyLimit);
    }

    [Fact]
    public void Parse_missing_headers_are_null()
    {
        var rl = NexusRateLimit.Parse(new List<KeyValuePair<string, IEnumerable<string>>>());

        Assert.Null(rl.DailyRemaining);
        Assert.Null(rl.DailyLimit);
        Assert.Null(rl.HourlyRemaining);
        Assert.Null(rl.HourlyLimit);
    }

    [Fact]
    public void Parse_tolerates_non_numeric_values()
    {
        var headers = new List<KeyValuePair<string, IEnumerable<string>>>
        {
            new("x-rl-daily-remaining", new[] { "n/a" }),
        };

        var rl = NexusRateLimit.Parse(headers);

        Assert.Null(rl.DailyRemaining);
    }

    [Fact]
    public async Task SendAsync_throws_typed_exception_on_429_carrying_parsed_limits()
    {
        var h = new StubHandler(_ => (
            (HttpStatusCode)429, "{}",
            new[] { ("x-rl-daily-remaining", "0"), ("x-rl-hourly-remaining", "0") }));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var ex = await Assert.ThrowsAsync<NexusRateLimitException>(() => client.GetModAsync("skyrim", 1));
        Assert.Equal(0, ex.RateLimit.DailyRemaining);
        Assert.Equal(0, ex.RateLimit.HourlyRemaining);
    }

    [Fact]
    public async Task SendAsync_records_last_rate_limit_on_success()
    {
        var h = new StubHandler(_ => (
            HttpStatusCode.OK, """{"name":"X","mod_id":9}""",
            new[] { ("x-rl-daily-remaining", "12345"), ("x-rl-hourly-remaining", "499") }));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await client.GetModAsync("skyrim", 9);

        Assert.NotNull(client.LastRateLimit);
        Assert.Equal(12345, client.LastRateLimit!.DailyRemaining);
        Assert.Equal(499, client.LastRateLimit.HourlyRemaining);
    }

    [Fact]
    public void Headers_always_include_tos_application_name_and_version()
    {
        var r = NexusRequests.ModRequest("skyrim", 5, new NexusOptions { ApiKey = "K", AppVersion = "0.3.0" });

        Assert.Equal("626-mod-launcher", r.Headers["Application-Name"]);
        Assert.Equal("0.3.0", r.Headers["Application-Version"]);
    }

    [Fact]
    public void Headers_default_app_version_when_unset()
    {
        var r = NexusRequests.ModRequest("skyrim", 5);

        Assert.Equal("626-mod-launcher", r.Headers["Application-Name"]);
        Assert.True(r.Headers.ContainsKey("Application-Version"));
        Assert.False(string.IsNullOrEmpty(r.Headers["Application-Version"]));
    }
}
