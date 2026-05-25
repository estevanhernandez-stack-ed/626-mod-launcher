using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

// Pure Nexus request-building + response-mapping (no HTTP). Nexus v1 has only
// mod-details-by-id and md5 file lookup — there is NO name-search endpoint.
public class NexusRequestsTests
{
    [Fact]
    public void ModRequest_builds_get_to_mods_id_with_apikey_header()
    {
        var r = NexusRequests.ModRequest("skyrimspecialedition", 266, new NexusOptions { ApiKey = "KEY" });
        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/266.json", r.Url);
        Assert.Equal("KEY", r.Headers["apikey"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
    }

    [Fact]
    public void ModRequest_omits_apikey_when_null()
    {
        var r = NexusRequests.ModRequest("skyrim", 5);
        Assert.False(r.Headers.ContainsKey("apikey"));
        Assert.Equal("application/json", r.Headers["Accept"]);
    }

    [Fact]
    public void ModRequest_honors_custom_baseurl()
    {
        var r = NexusRequests.ModRequest("skyrim", 7, new NexusOptions { BaseUrl = "https://proxy.example" });
        Assert.Equal("https://proxy.example/v1/games/skyrim/mods/7.json", r.Url);
    }

    [Fact]
    public void Md5Request_builds_get_to_md5_search()
    {
        var r = NexusRequests.Md5Request("skyrimspecialedition", "abc123", new NexusOptions { ApiKey = "KEY" });
        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/md5_search/abc123.json", r.Url);
        Assert.Equal("KEY", r.Headers["apikey"]);
        Assert.Equal("application/json", r.Headers["Accept"]);
    }

    [Fact]
    public void MapMod_maps_nexus_object_to_metadata_schema()
    {
        using var doc = JsonDocument.Parse("""
        {
            "name": "SkyUI",
            "summary": "Elegant, PC-friendly interface mod",
            "author": "SkyUI Team",
            "uploaded_by": "schlangster",
            "uploaded_users_profile_url": "https://www.nexusmods.com/users/123",
            "picture_url": "https://staticdelivery.nexusmods.com/mods/110/images/3863.jpg",
            "mod_id": 3863
        }
        """);
        var e = NexusRequests.MapMod("skyrimspecialedition", doc.RootElement);
        Assert.Equal("SkyUI", e.Title);
        Assert.Equal("Elegant, PC-friendly interface mod", e.Description);
        Assert.Equal("SkyUI Team", e.Author);
        Assert.Equal("https://www.nexusmods.com/users/123", e.AuthorUrl);
        Assert.Equal("https://staticdelivery.nexusmods.com/mods/110/images/3863.jpg", e.Image);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/3863", e.Url);
        Assert.Null(e.Source);
        Assert.Null(e.Donate);
        Assert.Null(e.Downloads);
    }

    [Fact]
    public void MapMod_falls_back_to_uploaded_by_when_author_missing()
    {
        using var doc = JsonDocument.Parse("""
        { "name": "X", "uploaded_by": "schlangster", "mod_id": 1 }
        """);
        var e = NexusRequests.MapMod("skyrim", doc.RootElement);
        Assert.Equal("schlangster", e.Author);
    }

    [Fact]
    public void MapMod_tolerates_missing_fields()
    {
        using var doc = JsonDocument.Parse("""{ "mod_id": 9 }""");
        var e = NexusRequests.MapMod("skyrim", doc.RootElement);
        Assert.Null(e.Title);
        Assert.Null(e.Description);
        Assert.Null(e.Author);
        Assert.Null(e.AuthorUrl);
        Assert.Null(e.Image);
        Assert.Equal("https://www.nexusmods.com/skyrim/mods/9", e.Url);
    }

    [Fact]
    public void MapModResponse_maps_root_object()
    {
        using var doc = JsonDocument.Parse("""{ "name": "Root Mod", "mod_id": 42 }""");
        var e = NexusRequests.MapModResponse("skyrim", doc.RootElement);
        Assert.Equal("Root Mod", e!.Title);
        Assert.Equal("https://www.nexusmods.com/skyrim/mods/42", e.Url);
    }

    [Fact]
    public void MapModResponse_returns_null_for_non_object()
    {
        using var doc = JsonDocument.Parse("[]");
        Assert.Null(NexusRequests.MapModResponse("skyrim", doc.RootElement));
    }

    [Fact]
    public void MapMd5Response_parses_one_element_array()
    {
        using var doc = JsonDocument.Parse("""
        [
            {
                "mod": { "name": "Matched Mod", "mod_id": 777, "author": "auth" },
                "file_details": { "file_id": 5, "md5": "abc" }
            }
        ]
        """);
        var m = NexusRequests.MapMd5Response("skyrimspecialedition", doc.RootElement);
        Assert.NotNull(m);
        Assert.Equal(777, m!.ModId);
        Assert.Equal("Matched Mod", m.Meta.Title);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/777", m.Meta.Url);
    }

    [Fact]
    public void MapMd5Response_returns_null_for_empty_array()
    {
        using var doc = JsonDocument.Parse("[]");
        Assert.Null(NexusRequests.MapMd5Response("skyrim", doc.RootElement));
    }

    [Fact]
    public void MapMd5Response_returns_null_for_wrong_shape()
    {
        using var doc = JsonDocument.Parse("""{ "mod": { "mod_id": 1 } }""");
        Assert.Null(NexusRequests.MapMd5Response("skyrim", doc.RootElement));
    }
}
