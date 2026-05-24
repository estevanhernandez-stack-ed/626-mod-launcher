using ModManager.Core;

namespace ModManager.Tests;

// Pure candidate mod-folder generation per engine. The App checks which exist under the game
// root and points the game there, so already-installed / sideloaded mods are detected on add.
public class ModLocationsTests
{
    private static string P(params string[] parts) => System.IO.Path.Combine(parts);

    [Fact]
    public void Unreal_uses_the_project_folder_for_paks()
    {
        var c = ModLocations.Candidates("ue-pak", new[] { "R5" });
        Assert.Contains(P("R5", "Content", "Paks", "~mods"), c);
        Assert.Contains(P("Content", "Paks", "~mods"), c); // root-level fallback
    }

    [Fact]
    public void FromSoft_is_the_mod_folder()
        => Assert.Contains("mod", ModLocations.Candidates("fromsoft", null));

    [Fact]
    public void Bepinex_plugins()
        => Assert.Contains(P("BepInEx", "plugins"), ModLocations.Candidates("bepinex", null));

    [Fact]
    public void Bethesda_data()
        => Assert.Contains("Data", ModLocations.Candidates("bethesda", null));

    [Fact]
    public void Unknown_engine_has_no_candidates()
        => Assert.Empty(ModLocations.Candidates("custom", null));
}
