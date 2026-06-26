using ModManager.Core.Loaders;

namespace ModManager.Tests.Loaders;

public class KnownLoaderCatalogTests
{
    [Fact]
    public void Catalog_has_modengine2_and_seamless_both_ban_safe_for_elden_ring()
    {
        var c = KnownLoaderCatalog.Catalog;
        var me2 = Assert.Single(c, x => x.LoaderId == "mod-engine-2");
        Assert.True(me2.BanSafe);
        Assert.Equal("fromsoft", me2.Engine);
        Assert.Contains("modengine2_launcher.exe", me2.LauncherExeNames);

        var sc = Assert.Single(c, x => x.LoaderId == "seamless-coop");
        Assert.True(sc.BanSafe);
        Assert.Contains("launch_elden_ring_seamlesscoop.exe", sc.LauncherExeNames);
    }

    [Fact]
    public void Every_loader_has_a_get_url_and_at_least_one_launcher_exe()
    {
        Assert.All(KnownLoaderCatalog.Catalog, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.GetUrl));
            Assert.NotEmpty(l.LauncherExeNames);
        });
    }
}
