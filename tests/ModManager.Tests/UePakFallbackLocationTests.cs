using ModManager.Core;

namespace ModManager.Tests;

public class UePakFallbackLocationTests
{
    [Fact]
    public void Loaderless_Content_Paks_exists_gives_paks_root()
    {
        var loc = ModLocations.UePakFallbackLocation("Witchfire", contentPaksExists: true);
        Assert.Equal("paks-root", loc.Form);
        Assert.Equal(System.IO.Path.Combine("Witchfire", "Content", "Paks"), loc.Path);
    }

    [Fact]
    public void No_Content_Paks_yet_gives_the_mods_install_target()
    {
        var loc = ModLocations.UePakFallbackLocation("R5", contentPaksExists: false);
        Assert.Null(loc.Form);
        Assert.Equal(System.IO.Path.Combine("R5", "Content", "Paks", "~mods"), loc.Path);
    }
}
