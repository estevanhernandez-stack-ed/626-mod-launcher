using System.Reflection;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

// Phase 1 browse contract. The ABI rules matter more than the shape: the shipped nexus-v0.12.1 plugin
// must still load on this host, which means SourceSearchHit's 7-arg constructor is frozen and the
// pre-existing interfaces are untouched. New capability = new interface.
public class ModCatalogBrowseContractTests
{
    [Fact]
    public void SourceSearchHit_positional_constructor_is_unchanged()
    {
        // The shipped 0.12.1 plugin calls this exact 7-arg ctor. Adding a positional parameter would
        // break it at load time (MissingMethodException), so growth happens via init-only properties.
        var ctor = typeof(SourceSearchHit).GetConstructor(new[]
        {
            typeof(string), typeof(int), typeof(string), typeof(string),
            typeof(string), typeof(int?), typeof(string),
        });
        Assert.NotNull(ctor);
    }

    [Fact]
    public void SourceSearchHit_exposes_the_phase1_init_only_properties()
    {
        foreach (var (name, type) in new (string, Type)[]
        {
            ("ThumbnailUrl", typeof(string)),
            ("Category", typeof(string)),
            ("Version", typeof(string)),
            ("DownloadCount", typeof(int?)),
            ("UpdatedAt", typeof(DateTimeOffset?)),
            ("ViewerDownloaded", typeof(bool?)),
            ("ViewerEndorsed", typeof(bool?)),
            ("ViewerUpdateAvailable", typeof(bool?)),
            ("ViewerTracked", typeof(bool?)),
        })
        {
            var p = typeof(SourceSearchHit).GetProperty(name);
            Assert.NotNull(p);
            Assert.Equal(type, p!.PropertyType);
        }
    }

    [Fact]
    public void Old_style_hit_construction_still_compiles_and_leaves_new_props_null()
    {
        // Exactly what an older plugin does.
        var hit = new SourceSearchHit("palworld", 1, "Mod", "Author", "Summary", 10, "https://x/1");
        Assert.Null(hit.ThumbnailUrl);
        Assert.Null(hit.ViewerUpdateAvailable);
    }

    [Fact]
    public void IModCatalogBrowse_has_the_expected_shape()
    {
        var m = typeof(IModCatalogBrowse).GetMethod("BrowseCatalogAsync");
        Assert.NotNull(m);
        Assert.Equal(typeof(Task<CatalogPage>), m!.ReturnType);
        var ps = m.GetParameters();
        Assert.Single(ps);
        Assert.Equal(typeof(CatalogQuery), ps[0].ParameterType);
    }

    [Fact]
    public void CatalogSort_covers_the_four_verified_views()
    {
        // No Trending: ModsSort has no trending field (live-verified 2026-08-02).
        Assert.Equal(
            new[] { "MostEndorsed", "MostDownloaded", "RecentlyUpdated", "RecentlyAdded" },
            Enum.GetNames(typeof(CatalogSort)));
    }

    [Fact]
    public void CatalogQuery_defaults_to_most_endorsed_first_page()
    {
        var q = new CatalogQuery("palworld");
        Assert.Null(q.Text);
        Assert.Equal(CatalogSort.MostEndorsed, q.Sort);
        Assert.Null(q.Category);
        Assert.Equal(0, q.Offset);
        Assert.Equal(20, q.Count);
    }

    [Fact]
    public void Existing_catalog_and_search_interfaces_are_untouched()
    {
        // ABI: old plugins implement these; their signatures are frozen.
        var catalog = typeof(IModCatalog).GetMethod("SearchCatalogAsync");
        Assert.NotNull(catalog);
        Assert.Equal(2, catalog!.GetParameters().Length);

        var search = typeof(IModTextSearch).GetMethod("SearchAsync");
        Assert.NotNull(search);
        Assert.Equal(2, search!.GetParameters().Length);
    }
}
