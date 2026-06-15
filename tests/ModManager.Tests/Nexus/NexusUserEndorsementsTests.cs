using System.Net;
using System.Text;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests.Nexus;

// Bulk user-endorsements wiring — GET /v1/user/endorsements.json returns the current user's
// endorse status across all games as [{ mod_id, domain_name, status }]. One cheap call gives
// accurate per-mod state library-wide; ApplyEndorsements folds it back onto the active domain's
// metas so hearts reflect reality even for mods endorsed outside the launcher.
public class NexusUserEndorsementsTests
{
    private sealed record Call(string Url, string Method, string? ApiKey, string? AppName, string? AppVersion);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<Call> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var apiKey = request.Headers.TryGetValues("apikey", out var v) ? string.Join(",", v) : null;
            var appName = request.Headers.TryGetValues("Application-Name", out var a) ? string.Join(",", a) : null;
            var appVersion = request.Headers.TryGetValues("Application-Version", out var av) ? string.Join(",", av) : null;
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Method.Method, apiKey, appName, appVersion));
            var (status, json) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static NexusClient Client(StubHandler h, NexusOptions opts) => new(new HttpClient(h), opts);

    // (a) Request builder.

    [Fact]
    public void UserEndorsementsRequest_builds_get_with_headers()
    {
        var r = NexusRequests.UserEndorsementsRequest(new NexusOptions { ApiKey = "K", AppVersion = "0.3.0" });

        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.nexusmods.com/v1/user/endorsements.json", r.Url);
        Assert.Equal("K", r.Headers["apikey"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
        Assert.Equal("626-mod-launcher", r.Headers["Application-Name"]);
        Assert.Equal("0.3.0", r.Headers["Application-Version"]);
    }

    [Fact]
    public void UserEndorsementsRequest_honors_custom_baseurl()
    {
        var r = NexusRequests.UserEndorsementsRequest(new NexusOptions { BaseUrl = "https://proxy.example" });
        Assert.Equal("https://proxy.example/v1/user/endorsements.json", r.Url);
    }

    // (b) Mapper.

    [Fact]
    public void MapUserEndorsements_maps_array_to_entries()
    {
        using var doc = JsonDocument.Parse("""
        [
            { "mod_id": 42, "domain_name": "eldenring", "status": "Endorsed" },
            { "mod_id": 7,  "domain_name": "skyrimspecialedition", "status": "Abstained" }
        ]
        """);

        var entries = NexusRequests.MapUserEndorsements(doc.RootElement);

        Assert.Equal(2, entries.Count);
        Assert.Equal(42, entries[0].ModId);
        Assert.Equal("eldenring", entries[0].DomainName);
        Assert.Equal("Endorsed", entries[0].Status);
        Assert.Equal(7, entries[1].ModId);
        Assert.Equal("skyrimspecialedition", entries[1].DomainName);
        Assert.Equal("Abstained", entries[1].Status);
    }

    [Fact]
    public void MapUserEndorsements_empty_array_is_empty_list()
    {
        using var doc = JsonDocument.Parse("[]");
        var entries = NexusRequests.MapUserEndorsements(doc.RootElement);
        Assert.Empty(entries);
    }

    [Fact]
    public void MapUserEndorsements_non_array_is_empty_list()
    {
        using var doc = JsonDocument.Parse("""{ "mod_id": 1 }""");
        var entries = NexusRequests.MapUserEndorsements(doc.RootElement);
        Assert.Empty(entries);
    }

    [Fact]
    public void MapUserEndorsements_skips_entries_missing_required_fields()
    {
        using var doc = JsonDocument.Parse("""
        [
            { "domain_name": "eldenring", "status": "Endorsed" },
            { "mod_id": 42, "status": "Endorsed" },
            { "mod_id": 42, "domain_name": "eldenring" },
            "not an object",
            { "mod_id": 99, "domain_name": "eldenring", "status": "Endorsed" }
        ]
        """);

        var entries = NexusRequests.MapUserEndorsements(doc.RootElement);

        Assert.Single(entries);
        Assert.Equal(99, entries[0].ModId);
    }

    // (c) Client wiring.

    [Fact]
    public async Task GetUserEndorsementsAsync_calls_endpoint_and_maps()
    {
        var h = new StubHandler(_ => (HttpStatusCode.OK,
            """[ { "mod_id": 42, "domain_name": "eldenring", "status": "Endorsed" } ]"""));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        var entries = await client.GetUserEndorsementsAsync();

        Assert.Equal("https://api.nexusmods.com/v1/user/endorsements.json", h.Calls[0].Url);
        Assert.Equal("GET", h.Calls[0].Method);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Single(entries);
        Assert.Equal(42, entries[0].ModId);
        Assert.Equal("eldenring", entries[0].DomainName);
        Assert.Equal("Endorsed", entries[0].Status);
    }

    [Fact]
    public async Task GetUserEndorsementsAsync_is_rate_limit_aware()
    {
        var h = new StubHandler(_ => ((HttpStatusCode)429, "{}"));
        var client = Client(h, new NexusOptions { ApiKey = "K" });

        await Assert.ThrowsAsync<NexusRateLimitException>(() => client.GetUserEndorsementsAsync());
    }

    // (d) ApplyEndorsements — the pure status -> bool apply onto the active domain's metas.

    [Fact]
    public void ApplyEndorsements_sets_true_for_endorsed_match_in_domain()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A" },
        };
        var endorsements = new List<NexusEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_sets_false_for_non_endorsed_status_match()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A" },
            new() { NexusModId = 7, Title = "B" },
        };
        var endorsements = new List<NexusEndorsement>
        {
            new(42, "eldenring", "Abstained"),
            new(7, "eldenring", "Undecided"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.False(metas[0].Endorsed);
        Assert.False(metas[1].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_leaves_non_matching_metas_untouched()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "matched", Endorsed = null },
            new() { NexusModId = 99, Title = "no entry", Endorsed = null }, // not in the list
            new() { CurseforgeId = 5, Title = "no nexus id", Endorsed = null }, // unresolvable id
        };
        var endorsements = new List<NexusEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
        Assert.Null(metas[1].Endorsed);
        Assert.Null(metas[2].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_ignores_entries_from_a_different_domain()
    {
        var metas = new List<ModMeta>
        {
            new() { NexusModId = 42, Title = "A", Endorsed = null },
        };
        var endorsements = new List<NexusEndorsement>
        {
            // same mod id, but a different game domain — must NOT apply to eldenring metas.
            new(42, "skyrimspecialedition", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.Null(metas[0].Endorsed);
    }

    [Fact]
    public void ApplyEndorsements_resolves_id_from_url_when_nexus_mod_id_absent()
    {
        var metas = new List<ModMeta>
        {
            new() { Url = "https://www.nexusmods.com/eldenring/mods/42", Title = "A", Endorsed = null },
        };
        var endorsements = new List<NexusEndorsement>
        {
            new(42, "eldenring", "Endorsed"),
        };

        NexusRefresh.ApplyEndorsements(metas, endorsements, "eldenring");

        Assert.True(metas[0].Endorsed);
    }
}
