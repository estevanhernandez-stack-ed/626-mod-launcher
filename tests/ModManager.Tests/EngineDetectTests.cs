using ModManager.Core;

namespace ModManager.Tests;

// Pure engine guessing from folder signatures. The App probes the filesystem; this decides.
public class EngineDetectTests
{
    [Fact]
    public void BepInEx_marker_wins()
        => Assert.Equal("bepinex", EngineDetect.GuessEngine(new EngineProbe { HasBepInEx = true }));

    [Fact]
    public void MelonLoader_marker()
        => Assert.Equal("melonloader", EngineDetect.GuessEngine(new EngineProbe { HasMelonLoader = true }));

    [Fact]
    public void Unreal_content_paks()
        => Assert.Equal("ue-pak", EngineDetect.GuessEngine(new EngineProbe { HasContentPaks = true }));

    [Fact]
    public void Bethesda_data_plugins()
        => Assert.Equal("bethesda", EngineDetect.GuessEngine(new EngineProbe { HasDataPlugins = true }));

    [Fact]
    public void Stardew_smapi()
        => Assert.Equal("smapi", EngineDetect.GuessEngine(new EngineProbe { HasStardew = true }));

    [Fact]
    public void Source_addons()
        => Assert.Equal("source", EngineDetect.GuessEngine(new EngineProbe { HasSourceAddons = true }));

    [Fact]
    public void Bare_unity_defaults_to_bepinex_route()
        => Assert.Equal("bepinex", EngineDetect.GuessEngine(new EngineProbe { HasUnityData = true }));

    [Fact]
    public void Unknown_returns_null_so_the_user_chooses()
        => Assert.Null(EngineDetect.GuessEngine(new EngineProbe()));

    [Fact]
    public void Specific_loader_beats_generic_unity()
        => Assert.Equal("bepinex", EngineDetect.GuessEngine(new EngineProbe { HasBepInEx = true, HasUnityData = true }));
}
