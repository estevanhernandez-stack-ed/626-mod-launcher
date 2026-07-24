using System.Net.Http;
using ModManager.Core.Nexus;
using Xunit;

public class NexusAuthHeadersTests
{
    [Fact]
    public void Apply_stamps_bearer_and_tos_headers()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");
        NexusAuthHeaders.Apply(req, "tok123", "626-mod-launcher", "0.11.0");
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", req.Headers.Authorization!.Parameter);
        Assert.Contains("626-mod-launcher", string.Join(",", req.Headers.GetValues("Application-Name")));
        Assert.Contains("0.11.0", string.Join(",", req.Headers.GetValues("Application-Version")));
    }

    [Fact]
    public void Apply_without_token_still_stamps_tos_headers_no_authorization()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");
        NexusAuthHeaders.Apply(req, null, "626-mod-launcher", "0.11.0");
        Assert.Null(req.Headers.Authorization);
        Assert.True(req.Headers.Contains("Application-Name"));
    }
}
