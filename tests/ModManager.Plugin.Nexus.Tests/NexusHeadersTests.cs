using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// The Nexus-ToS identity headers the plugin attaches to EVERY request, on every endpoint. Relocated
// from the deleted Core NexusRateLimitTests header assertions (the x-rl-* response-header parsing /
// LastRateLimit surface is NOT reimplemented in the plugin — its rate-limit signal is purely the 429
// -> SourceRateLimitException on the bulk reads, covered in NexusBulkReadTests). Application-Name is a
// fixed const; Application-Version comes from the host-provided app version, falling back to a fixed
// default when unset.
public class NexusHeadersTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "skyrim", ModId: 5, Version: "1.0");

    [Fact]
    public async Task Every_request_carries_the_tos_application_name_and_version()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 5, "name": "X" }""");
        await h.Source(appVersion: "0.3.0").FetchMetadataAsync(Ref);

        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.Equal("0.3.0", h.Calls[0].AppVersion);
        Assert.Equal("application/json", h.Calls[0].Accept);
    }

    [Fact]
    public async Task App_version_falls_back_to_default_when_unset()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 5, "name": "X" }""");
        await h.Source(appVersion: null).FetchMetadataAsync(Ref);

        Assert.Equal("626-mod-launcher", h.Calls[0].AppName);
        Assert.False(string.IsNullOrEmpty(h.Calls[0].AppVersion));
        // The plugin's documented fallback literal.
        Assert.Equal("0.0.0", h.Calls[0].AppVersion);
    }

    [Fact]
    public void Plugin_source_id_is_stable_nexus()
    {
        var src = new StubHandler(HttpStatusCode.OK, "{}").Source();
        Assert.Equal("nexus", src.Id);
        Assert.True(src.RequiresApiKey);
    }
}
