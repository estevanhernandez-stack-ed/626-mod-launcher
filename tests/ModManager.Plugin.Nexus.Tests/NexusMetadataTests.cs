using System.Net;

namespace ModManager.Plugin.Nexus.Tests;

// Per-mod metadata + update check over the plugin: GET /v1/games/{domain}/mods/{id}.json (the root IS
// the mod object). Relocated from the deleted Core NexusClientTests.GetModAsync* +
// NexusRequestsTests.MapMod*/MapModResponse* — reworked over NexusModSource.FetchMetadataAsync /
// IsUpdateAvailableAsync. The heart-wipe guard (Endorsed is ALWAYS null on these endpoints) is asserted
// here because the metadata mapper is the surface that would clobber a filled heart if it ever returned
// false.
public class NexusMetadataTests
{
    private static readonly ModManager.Plugins.Abstractions.SourceModRef Ref =
        new(SourceId: "nexus", GameDomain: "skyrimspecialedition", ModId: 3863, Version: "1.0");

    [Fact]
    public async Task FetchMetadataAsync_maps_a_200_mod_object()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """{"name":"SkyUI","summary":"Elegant, PC-friendly interface mod","author":"SkyUI Team","uploaded_users_profile_url":"https://www.nexusmods.com/users/123","picture_url":"https://staticdelivery.nexusmods.com/mods/110/images/3863.jpg","mod_id":3863}""");
        var src = h.Source();

        var meta = await src.FetchMetadataAsync(Ref);

        // The per-mod fetch is Calls[0]. (A cached per-domain game-info GET also fires for category
        // resolution, but with the single canned-body stub it lands after this and never displaces Calls[0].)
        Assert.Equal("https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/3863.json", h.Calls[0].Url);
        Assert.Equal("K", h.Calls[0].ApiKey);
        Assert.NotNull(meta);
        Assert.Equal("SkyUI", meta!.Title);
        Assert.Equal("Elegant, PC-friendly interface mod", meta.Description);
        Assert.Equal("SkyUI Team", meta.Author);
        Assert.Equal("https://www.nexusmods.com/users/123", meta.AuthorUrl);
        Assert.Equal("https://staticdelivery.nexusmods.com/mods/110/images/3863.jpg", meta.ImageUrl);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/3863", meta.ModUrl);
    }

    [Fact]
    public async Task FetchMetadataAsync_falls_back_to_uploaded_by_when_author_missing()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "X", "uploaded_by": "schlangster", "mod_id": 1 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.Equal("schlangster", meta!.Author);
    }

    [Fact]
    public async Task FetchMetadataAsync_reads_endorsements_downloads_version_available_adult()
    {
        var h = new StubHandler(HttpStatusCode.OK,
            """{"mod_id":510,"name":"X","summary":"s","author":"a","endorsement_count":1234,"mod_downloads":56789,"version":"2.3","available":false,"contains_adult_content":true}""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.Equal(1234, meta!.Endorsements);
        Assert.Equal(56789L, meta.Downloads);
        Assert.Equal("2.3", meta.LatestVersion);
        Assert.False(meta.Available);
        Assert.True(meta.ContainsAdultContent);
    }

    [Fact]
    public async Task FetchMetadataAsync_tolerates_missing_fields()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 9 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Null(meta!.Title);
        Assert.Null(meta.Description);
        Assert.Null(meta.Author);
        Assert.Null(meta.AuthorUrl);
        Assert.Null(meta.ImageUrl);
    }

    [Fact]
    public async Task FetchMetadataAsync_endorsed_is_always_null_the_heart_wipe_guard()
    {
        // Even if the API echoed an endorse flag, the per-mod endpoint is not the per-user-endorse
        // source of truth — the mapper must return Endorsed: null so a stats refresh never wipes a
        // filled heart. NEVER false.
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 7, "name": "X", "endorsement_count": 10 }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Null(meta!.Endorsed);
    }

    [Fact]
    public async Task FetchMetadataAsync_per_mod_endpoint_carries_no_file_id()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 42, "name": "Root Mod" }""");
        var meta = await h.Source().FetchMetadataAsync(Ref);

        Assert.NotNull(meta);
        Assert.Null(meta!.NexusFileId);
    }

    [Fact]
    public async Task FetchMetadataAsync_returns_null_for_non_object_body()
    {
        var h = new StubHandler(HttpStatusCode.OK, "[]");
        Assert.Null(await h.Source().FetchMetadataAsync(Ref));
    }

    [Fact]
    public async Task FetchMetadataAsync_returns_null_on_non_ok_never_throws()
    {
        var h = new StubHandler(HttpStatusCode.Forbidden, "{}");
        Assert.Null(await h.Source().FetchMetadataAsync(Ref));
    }

    [Fact]
    public async Task FetchMetadataAsync_omits_apikey_header_when_no_key()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "name": "X", "mod_id": 9 }""");
        await h.Source(apiKey: null).FetchMetadataAsync(Ref);

        Assert.Null(h.Calls[0].ApiKey);
    }

    // --- IsUpdateAvailableAsync: string-inequality vs installed (mirrors NexusRefresh, not semver) ---

    [Fact]
    public async Task IsUpdateAvailableAsync_true_when_latest_differs_from_installed()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 3863, "name": "X", "version": "2.0" }""");
        Assert.True(await h.Source().IsUpdateAvailableAsync(Ref, installedVersion: "1.0"));
    }

    [Fact]
    public async Task IsUpdateAvailableAsync_false_when_latest_equals_installed()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 3863, "name": "X", "version": "1.0" }""");
        Assert.False(await h.Source().IsUpdateAvailableAsync(Ref, installedVersion: "1.0"));
    }

    [Fact]
    public async Task IsUpdateAvailableAsync_false_when_source_reports_no_version()
    {
        var h = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 3863, "name": "X" }""");
        Assert.False(await h.Source().IsUpdateAvailableAsync(Ref, installedVersion: "1.0"));
    }

    [Fact]
    public async Task IsUpdateAvailableAsync_false_on_failed_fetch_never_throws()
    {
        var h = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        Assert.False(await h.Source().IsUpdateAvailableAsync(Ref, installedVersion: "1.0"));
    }
}
