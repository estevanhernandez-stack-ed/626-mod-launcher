using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// Ports curseforge-core.test.js + the fingerprintsRequest cases from fingerprint-core.test.js
// — pure request-building + response-mapping (no HTTP).
public class CurseForgeRequestsTests
{
    [Fact]
    public void ModRequest_builds_get_to_mods_id_with_api_key()
    {
        var r = CurseForgeRequests.ModRequest(12345, new CurseForgeOptions { ApiKey = "KEY" });
        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.curseforge.com/v1/mods/12345", r.Url);
        Assert.Equal("KEY", r.Headers["x-api-key"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
    }

    [Fact]
    public void ModRequest_honors_proxy_baseurl_and_omits_key()
    {
        var r = CurseForgeRequests.ModRequest(7, new CurseForgeOptions { BaseUrl = "https://proxy.626labs.dev" });
        Assert.Equal("https://proxy.626labs.dev/v1/mods/7", r.Url);
        Assert.False(r.Headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public void ModsRequest_posts_id_list_as_json()
    {
        var r = CurseForgeRequests.ModsRequest(new[] { 1, 2, 3 }, new CurseForgeOptions { ApiKey = "K" });
        Assert.Equal("POST", r.Method);
        Assert.Equal("https://api.curseforge.com/v1/mods", r.Url);
        Assert.Equal("application/json", r.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(r.Body!);
        Assert.Equal(new[] { 1, 2, 3 }, doc.RootElement.GetProperty("modIds").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public void MapMod_maps_curseforge_object_to_metadata_schema()
    {
        var mod = new CfMod
        {
            Id = 238222, Name = "Just Enough Items", Summary = "View items and recipes",
            DownloadCount = 123456789,
            Authors = new() { new CfAuthor { Id = 1, Name = "mezz", Url = "https://www.curseforge.com/members/mezz" } },
            Logo = new CfLogo { Url = "https://logo", ThumbnailUrl = "https://thumb" },
            Links = new CfLinks { WebsiteUrl = "https://www.curseforge.com/minecraft/mc-mods/jei", SourceUrl = "https://github.com/mezz/JustEnoughItems" },
        };
        var e = CurseForgeRequests.MapMod(mod);
        Assert.Equal("Just Enough Items", e.Title);
        Assert.Equal("View items and recipes", e.Description);
        Assert.Equal("mezz", e.Author);
        Assert.Equal("https://www.curseforge.com/members/mezz", e.AuthorUrl);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/jei", e.Url);
        Assert.Equal("https://thumb", e.Image);
        Assert.Equal(123456789L, e.Downloads);
        Assert.Equal("https://github.com/mezz/JustEnoughItems", e.Source);
        Assert.Equal(238222, e.CurseforgeId);
    }

    [Fact]
    public void MapMod_handles_missing_logo_links_authors()
    {
        var e = CurseForgeRequests.MapMod(new CfMod { Id = 9, Name = "Bare", Summary = "" });
        Assert.Equal("Bare", e.Title);
        Assert.Null(e.Author);
        Assert.Null(e.AuthorUrl);
        Assert.Null(e.Url);
        Assert.Null(e.Image);
        Assert.Null(e.Source);
        Assert.Equal(9, e.CurseforgeId);
    }

    [Fact]
    public void MapModsResponse_maps_data_array()
    {
        var json = JsonDocument.Parse("""{"data":[{"id":1,"name":"A","summary":""},{"id":2,"name":"B","summary":""}]}""");
        var titles = CurseForgeRequests.MapModsResponse(json.RootElement).Select(e => e.Title).ToArray();
        Assert.Equal(new[] { "A", "B" }, titles);
    }

    // --- fingerprintsRequest (from fingerprint-core.test.js) ---

    [Fact]
    public void FingerprintsRequest_posts_to_v1_fingerprints()
    {
        var r = CurseForgeRequests.FingerprintsRequest(new[] { 111L, 222L }, new CurseForgeOptions { ApiKey = "K" });
        Assert.Equal("POST", r.Method);
        Assert.Equal("https://api.curseforge.com/v1/fingerprints", r.Url);
        Assert.Equal("K", r.Headers["x-api-key"]);
        using var doc = JsonDocument.Parse(r.Body!);
        Assert.Equal(new[] { 111L, 222L }, doc.RootElement.GetProperty("fingerprints").EnumerateArray().Select(e => e.GetInt64()).ToArray());
    }

    [Fact]
    public void FingerprintsRequest_honors_proxy_baseurl_no_key()
    {
        var r = CurseForgeRequests.FingerprintsRequest(new[] { 1L }, new CurseForgeOptions { BaseUrl = "https://proxy.example" });
        Assert.Equal("https://proxy.example/v1/fingerprints", r.Url);
        Assert.False(r.Headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public void FingerprintsRequest_scopes_to_gameid()
    {
        var r = CurseForgeRequests.FingerprintsRequest(new[] { 1L }, new CurseForgeOptions { GameId = 432 });
        Assert.Equal("https://api.curseforge.com/v1/fingerprints/432", r.Url);
    }
}
