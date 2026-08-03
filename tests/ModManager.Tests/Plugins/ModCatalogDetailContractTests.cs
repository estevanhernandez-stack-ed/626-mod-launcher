using System.Reflection;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

// Phase 2 detail + actions contract. Same ABI discipline as Phase 1: SourceSearchHit's 7-arg
// constructor is frozen — the shipped nexus-v0.13.0 plugin calls it — so Uid/GameId grow the record
// via init-only properties only. The pre-existing interfaces (IModSource, IModTextSearch, IModCatalog,
// IAuthorizedSend, IModCatalogBrowse) are untouched; new capability = new interface.
public class ModCatalogDetailContractTests
{
    [Fact]
    public void SourceSearchHit_positional_constructor_is_still_unchanged()
    {
        // The shipped 0.13.0 plugin calls this exact 7-arg ctor. Adding a positional parameter would
        // break it at load time (MissingMethodException), so growth happens via init-only properties.
        var ctor = typeof(SourceSearchHit).GetConstructor(new[]
        {
            typeof(string), typeof(int), typeof(string), typeof(string),
            typeof(string), typeof(int?), typeof(string),
        });
        Assert.NotNull(ctor);
    }

    [Fact]
    public void SourceSearchHit_exposes_the_phase2_uid_and_gameId_init_only_properties()
    {
        var uid = typeof(SourceSearchHit).GetProperty("Uid");
        Assert.NotNull(uid);
        Assert.Equal(typeof(string), uid!.PropertyType);

        var gameId = typeof(SourceSearchHit).GetProperty("GameId");
        Assert.NotNull(gameId);
        Assert.Equal(typeof(int?), gameId!.PropertyType);
    }

    [Fact]
    public void Old_style_hit_construction_still_compiles_and_leaves_uid_and_gameId_null()
    {
        // Exactly what an older (pre-0.14.0) plugin does — it never sets Uid/GameId.
        var hit = new SourceSearchHit("palworld", 1, "Mod", "Author", "Summary", 10, "https://x/1");
        Assert.Null(hit.Uid);
        Assert.Null(hit.GameId);
    }

    [Fact]
    public void IModCatalogDetail_has_the_expected_shape()
    {
        var m = typeof(IModCatalogDetail).GetMethod("GetModDetailAsync");
        Assert.NotNull(m);
        Assert.Equal(typeof(Task<CatalogDetail?>), m!.ReturnType);
        var ps = m.GetParameters();
        Assert.Equal(2, ps.Length);
        Assert.Equal(typeof(int), ps[0].ParameterType);
        Assert.Equal(typeof(int), ps[1].ParameterType);
    }

    [Fact]
    public void IModCatalogActions_has_the_expected_shape()
    {
        var endorse = typeof(IModCatalogActions).GetMethod("SetEndorsedAsync");
        Assert.NotNull(endorse);
        Assert.Equal(typeof(Task<bool>), endorse!.ReturnType);
        var endorseParams = endorse.GetParameters();
        Assert.Equal(2, endorseParams.Length);
        Assert.Equal(typeof(string), endorseParams[0].ParameterType);
        Assert.Equal(typeof(bool), endorseParams[1].ParameterType);

        var track = typeof(IModCatalogActions).GetMethod("SetTrackedAsync");
        Assert.NotNull(track);
        Assert.Equal(typeof(Task<bool>), track!.ReturnType);
        var trackParams = track.GetParameters();
        Assert.Equal(2, trackParams.Length);
        Assert.Equal(typeof(string), trackParams[0].ParameterType);
        Assert.Equal(typeof(bool), trackParams[1].ParameterType);
    }

    [Fact]
    public void CatalogRequirement_has_nullable_modId_for_external_requirements()
    {
        // Live data: an external requirement returns modId "0", externalRequirement true, and an
        // off-site Url — there is no Nexus mod page to key off of, so ModId must be nullable.
        var req = new CatalogRequirement("Some External Tool", null, "https://example.com/tool", "Notes", true);
        Assert.Null(req.ModId);
        Assert.True(req.External);
        Assert.Equal("https://example.com/tool", req.Url);
    }

    [Fact]
    public void CatalogDetail_carries_uid_and_raw_description_and_requirements()
    {
        var requirements = new[]
        {
            new CatalogRequirement("Internal Req", 123, null, "notes", false),
            new CatalogRequirement("External Req", null, "https://example.com", null, true),
        };
        var detail = new CatalogDetail(
            577, "26040386720381", "Test Mod", "Author", "Uploader", "1.0",
            "Summary", "[b]raw[/b] <br /> description", "https://img", "Category",
            10, 20, System.DateTimeOffset.UtcNow, "https://nexus/mod/577",
            true, false, null, requirements);

        Assert.Equal(577, detail.ModId);
        Assert.Equal("26040386720381", detail.Uid);
        Assert.Equal("[b]raw[/b] <br /> description", detail.DescriptionRaw);
        Assert.Equal(2, detail.Requirements.Count);
    }

    [Fact]
    public void Existing_catalog_and_search_and_browse_interfaces_are_untouched()
    {
        // ABI: old plugins implement these; their signatures are frozen.
        var catalog = typeof(IModCatalog).GetMethod("SearchCatalogAsync");
        Assert.NotNull(catalog);
        Assert.Equal(2, catalog!.GetParameters().Length);

        var search = typeof(IModTextSearch).GetMethod("SearchAsync");
        Assert.NotNull(search);
        Assert.Equal(2, search!.GetParameters().Length);

        var browse = typeof(IModCatalogBrowse).GetMethod("BrowseCatalogAsync");
        Assert.NotNull(browse);
        Assert.Single(browse!.GetParameters());
    }
}
