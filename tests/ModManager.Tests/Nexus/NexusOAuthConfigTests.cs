using System.Text.Json;
using ModManager.Core.Nexus;
using Xunit;

public class NexusOAuthConfigTests
{
    [Fact]
    public void Baked_has_endpoints_but_no_client_id()
    {
        Assert.False(NexusOAuthConfig.Baked.IsConfigured);
        Assert.False(string.IsNullOrWhiteSpace(NexusOAuthConfig.Baked.AuthorizeUrl));
        Assert.False(string.IsNullOrWhiteSpace(NexusOAuthConfig.Baked.TokenUrl));
    }

    [Fact]
    public void Overlay_takes_remote_client_id_but_keeps_baked_endpoints_when_remote_blank()
    {
        var remote = new NexusOAuthConfig("real-client-id", "", "", "");
        var eff = NexusOAuthConfig.Baked.Overlay(remote);
        Assert.Equal("real-client-id", eff.ClientId);
        Assert.True(eff.IsConfigured);
        Assert.Equal(NexusOAuthConfig.Baked.AuthorizeUrl, eff.AuthorizeUrl); // blank remote -> keep baked
    }

    [Fact]
    public void Overlay_null_remote_returns_baked()
    {
        Assert.Equal(NexusOAuthConfig.Baked, NexusOAuthConfig.Baked.Overlay(null));
    }

    [Fact]
    public void RoundTrips_as_camelCase()
    {
        var c = new NexusOAuthConfig("cid", "https://a", "https://t", "public");
        var json = JsonSerializer.Serialize(c, NexusOAuthConfig.JsonOpts);
        Assert.Contains("\"clientId\"", json);
        Assert.DoesNotContain("\"ClientId\"", json);
        var back = JsonSerializer.Deserialize<NexusOAuthConfig>(json, NexusOAuthConfig.JsonOpts)!;
        Assert.Equal(c, back);
    }
}
