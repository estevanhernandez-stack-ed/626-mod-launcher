using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

[Collection("EffectiveManifest")] // serialize: these mutate the shared remote
public class BanRiskCatalogTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void ByAppId_resolves_a_flagged_game_and_defaults_None()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "risky", Name = "Risky", Stores = new StoreIds { SteamAppId = "111" }, BanRisk = "high" },
                new GameManifestEntry { Id = "safe", Name = "Safe", Stores = new StoreIds { SteamAppId = "222" } },
            },
        });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.ByAppId("111"));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("222"));  // present, no flag
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("999"));  // absent
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId(null));
    }
}

[CollectionDefinition("EffectiveManifest")]
public class EffectiveManifestCollection { }
