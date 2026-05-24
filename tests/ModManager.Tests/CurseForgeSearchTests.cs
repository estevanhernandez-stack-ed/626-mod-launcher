using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// Ports curseforge-search.test.js — search request building + result parsing (pure).
public class CurseForgeSearchTests
{
    [Fact]
    public void SearchRequest_builds_get_with_gameid_and_searchfilter()
    {
        var r = CurseForgeRequests.SearchRequest(99078, "Black Market Shipyard", new CurseForgeOptions { ApiKey = "K" });
        Assert.Equal("GET", r.Method);
        Assert.StartsWith("https://api.curseforge.com/v1/mods/search?", r.Url);
        Assert.Contains("gameId=99078", r.Url);
        Assert.Contains("searchFilter=Black+Market+Shipyard", r.Url);
        Assert.Equal("K", r.Headers["x-api-key"]);
    }

    [Fact]
    public void SearchRequest_honors_proxy_baseurl_no_key()
    {
        var r = CurseForgeRequests.SearchRequest(1, "x", new CurseForgeOptions { BaseUrl = "https://proxy.example" });
        Assert.StartsWith("https://proxy.example/v1/mods/search?", r.Url);
        Assert.False(r.Headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public void ParseSearchResults_returns_the_data_array()
    {
        using var withData = JsonDocument.Parse("""{"data":[{"id":1},{"id":2}]}""");
        Assert.Equal(new[] { 1, 2 }, CurseForgeRequests.ParseSearchResults(withData.RootElement).Select(m => m.Id!.Value).ToArray());

        using var empty = JsonDocument.Parse("{}");
        Assert.Empty(CurseForgeRequests.ParseSearchResults(empty.RootElement));
    }
}
