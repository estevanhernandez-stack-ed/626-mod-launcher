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

    // --- CloneWithAuthAsync: the host stamps a COPY so a plugin-supplied request can't read the token back ---

    [Fact]
    public async Task CloneWithAuth_does_not_mutate_the_caller_request()
    {
        // A plugin-supplied request arrives with NO auth.
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");

        var clone = await NexusAuthHeaders.CloneWithAuthAsync(original, "tok123", "626-mod-launcher", "0.11.0");

        // The whole point: the caller's request is never stamped, so the plugin can't read the token off it.
        Assert.Null(original.Headers.Authorization);
        // The clone carries the bearer + ToS identity.
        Assert.Equal("Bearer", clone.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", clone.Headers.Authorization!.Parameter);
        Assert.True(clone.Headers.Contains("Application-Name"));
    }

    [Fact]
    public async Task CloneWithAuth_preserves_content()
    {
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v1/x.json")
        {
            Content = new StringContent("{\"v\":1}"),
        };

        var clone = await NexusAuthHeaders.CloneWithAuthAsync(original, "tok", "626-mod-launcher", "0.11.0");

        Assert.NotNull(clone.Content);
        Assert.Equal("{\"v\":1}", await clone.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task CloneWithAuth_preserves_non_content_headers()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");
        original.Headers.TryAddWithoutValidation("Accept", "application/json");

        var clone = await NexusAuthHeaders.CloneWithAuthAsync(original, "tok", "626-mod-launcher", "0.11.0");

        Assert.Contains("application/json", string.Join(",", clone.Headers.GetValues("Accept")));
    }

    [Fact]
    public async Task CloneWithAuth_null_bearer_stamps_tos_only()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");

        var clone = await NexusAuthHeaders.CloneWithAuthAsync(original, null, "626-mod-launcher", "0.11.0");

        Assert.Null(clone.Headers.Authorization);          // no bearer -> no Authorization on the clone
        Assert.True(clone.Headers.Contains("Application-Name")); // ToS identity still present
        Assert.Null(original.Headers.Authorization);        // original untouched
    }
}
