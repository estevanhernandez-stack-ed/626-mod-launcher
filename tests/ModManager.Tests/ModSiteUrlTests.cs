using ModManager.Core;

namespace ModManager.Tests;

public class ModSiteUrlTests
{
    [Theory]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510", "eldenring", "510")]
    [InlineData("https://nexusmods.com/skyrimspecialedition/mods/12345", "skyrimspecialedition", "12345")]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510?tab=description", "eldenring", "510")]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510/", "eldenring", "510")]
    public void Parse_Nexus_url(string url, string expectedDomain, string expectedModId)
    {
        var p = ModSiteUrl.Parse(url);
        Assert.NotNull(p);
        Assert.Equal(ModSiteProvider.Nexus, p!.Provider);
        Assert.Equal(expectedDomain, p.GameKey);
        Assert.Equal(expectedModId, p.ModRef);
    }

    [Theory]
    [InlineData("https://www.curseforge.com/eldenring/mods/seamless-coop", "eldenring", "seamless-coop")]
    [InlineData("https://curseforge.com/minecraft/mc-mods/jei", "minecraft", "jei")]
    [InlineData("https://www.curseforge.com/eldenring/mods/seamless-coop/files", "eldenring", "seamless-coop")]
    public void Parse_CurseForge_url(string url, string expectedGameSlug, string expectedModSlug)
    {
        var p = ModSiteUrl.Parse(url);
        Assert.NotNull(p);
        Assert.Equal(ModSiteProvider.CurseForge, p!.Provider);
        Assert.Equal(expectedGameSlug, p.GameKey);
        Assert.Equal(expectedModSlug, p.ModRef);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://example.com/something")]
    [InlineData("https://nexusmods.com/")]
    [InlineData("https://nexusmods.com/eldenring/")]
    [InlineData("https://nexusmods.com/eldenring/mods/")]
    [InlineData("https://nexusmods.com/eldenring/mods/notanumber")]
    public void Parse_returns_null_for_unrecognized_input(string url)
    {
        Assert.Null(ModSiteUrl.Parse(url));
    }
}
