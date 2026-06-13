using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class EffectiveManifestStateTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private static GameManifest Remote(params GameManifestEntry[] games) => new() { Games = games };

    private static GameManifestEntry Entry(string id, string engine, string? appId = null)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            Stores = new StoreIds { SteamAppId = appId },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        };

    [Fact]
    public void Current_defaults_to_the_embedded_manifest()
    {
        EffectiveManifest.SetRemote(null);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }

    [Fact]
    public void SetRemote_makes_Current_reflect_the_merge()
    {
        EffectiveManifest.SetRemote(Remote(Entry("brand-new-game", "bethesda", "55555")));

        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "brand-new-game");
        // embedded games still present
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "elden-ring");
    }

    [Fact]
    public void SetRemote_bumps_the_generation()
    {
        var before = EffectiveManifest.Generation;
        EffectiveManifest.SetRemote(Remote(Entry("x", "bethesda")));
        var after = EffectiveManifest.Generation;
        Assert.True(after > before, $"generation did not advance: {before} -> {after}");
    }

    [Fact]
    public void SetRemote_null_reverts_to_embedded()
    {
        EffectiveManifest.SetRemote(Remote(Entry("temp", "bethesda")));
        EffectiveManifest.SetRemote(null);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
}
