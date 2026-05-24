using ModManager.Core;

namespace ModManager.Tests;

// Curated Steam App ID -> engine map. The reliable way to know the engine for games whose
// folders carry no signature (FromSoftware's proprietary engine, etc.) — the app id is the tell.
public class KnownEnginesTests
{
    [Theory]
    [InlineData("1245620", "fromsoft")]  // Elden Ring
    [InlineData("374320", "fromsoft")]   // Dark Souls III
    [InlineData("489830", "bethesda")]   // Skyrim Special Edition
    [InlineData("1716740", "bethesda")]  // Starfield
    [InlineData("413150", "smapi")]      // Stardew Valley
    [InlineData("892970", "bepinex")]    // Valheim
    [InlineData("990080", "ue-pak")]     // Hogwarts Legacy
    public void ByAppId_maps_known_games(string appId, string engine)
        => Assert.Equal(engine, KnownEngines.ByAppId(appId));

    [Fact]
    public void Unknown_or_null_app_id_returns_null()
    {
        Assert.Null(KnownEngines.ByAppId("999999999"));
        Assert.Null(KnownEngines.ByAppId(null));
        Assert.Null(KnownEngines.ByAppId(""));
    }

    [Fact]
    public void Every_mapped_engine_is_a_real_preset()
    {
        foreach (var engine in KnownEngines.AllMappedEngines)
            Assert.True(EnginePresets.Presets.ContainsKey(engine), $"no preset for '{engine}'");
    }
}
