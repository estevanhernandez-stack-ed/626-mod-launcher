using ModManager.Core;

namespace ModManager.Tests;

public class UePakModLocationTests
{
    [Fact]
    public void Loaderless_gives_paks_root_on_Content_Paks()
    {
        var loc = ModLocations.UePakModLocation("Witchfire", loaderPresent: false);
        Assert.Equal("paks-root", loc.Form);
        Assert.Equal(System.IO.Path.Combine("Witchfire", "Content", "Paks"), loc.Path);
    }

    [Fact]
    public void Loader_present_gives_the_mods_install_target()
    {
        var loc = ModLocations.UePakModLocation("R5", loaderPresent: true);
        Assert.Null(loc.Form);
        Assert.Equal(System.IO.Path.Combine("R5", "Content", "Paks", "~mods"), loc.Path);
    }
}
