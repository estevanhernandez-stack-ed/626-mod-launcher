using ManifestMiner;

namespace ModManager.Tests.Miner;

public class EngineFromModPathTests
{
    [Theory]
    [InlineData("Data", "bethesda")]
    [InlineData("data", "bethesda")]                 // case-insensitive
    [InlineData("BepInEx/plugins", "bepinex")]
    [InlineData("BepInEx\\plugins", "bepinex")]      // backslash normalized
    [InlineData("addons", "source")]
    [InlineData("mod", "fromsoft")]
    public void Maps_unambiguous_paths(string path, string engine)
        => Assert.Equal(engine, EngineFromModPath.Infer(path));

    [Theory]
    [InlineData("Mods")]    // smapi AND melonloader
    [InlineData("mods")]    // minecraft AND custom
    [InlineData("")]        // empty
    [InlineData(null)]
    [InlineData("weird/path")]
    public void Leaves_ambiguous_or_unknown_null(string? path)
        => Assert.Null(EngineFromModPath.Infer(path));
}
