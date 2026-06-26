using ModManager.Core;

namespace ModManager.Tests;

public class GameRequestUrlTests
{
    [Fact]
    public void Build_targets_the_feed_repo_template_and_prefills_name_and_title()
    {
        var url = GameRequestUrl.Build("Baldur's Gate 3", "1086940", null, null);
        Assert.StartsWith("https://github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new?", url);
        Assert.Contains("template=game-request.yml", url);
        // name + title escaped (apostrophe + spaces)
        Assert.Contains("name=Baldur%27s%20Gate%203", url);
        Assert.Contains("title=%5Bgame%5D%20Baldur%27s%20Gate%203", url);
        Assert.Contains("steam-app-id=1086940", url);
        Assert.True(SafeUrl.IsHttpUrl(url));
    }

    [Fact]
    public void Build_defaults_engine_to_Not_sure_when_unknown_and_omits_blank_fields()
    {
        var url = GameRequestUrl.Build("Some Game", null, null, null);
        Assert.Contains("engine=Not%20sure", url);     // required field — always set
        Assert.DoesNotContain("steam-app-id=", url);    // omitted when blank
        Assert.DoesNotContain("notes=", url);           // omitted when blank
    }

    [Theory]
    // .NET 10 Uri.EscapeDataString encodes '(' as %28 and ')' as %29.
    [InlineData("fromsoft", "fromsoft%20%28Souls%20%2F%20Mod%20Engine%29")]
    [InlineData("ue-pak", "ue-pak%20%28Unreal%20.pak%29")]
    [InlineData("bethesda", "bethesda%20%28Creation%20Engine%20%E2%80%94%20esp%2Fesl%2Fbsa%29")]
    [InlineData("minecraft", "minecraft%20%28jar%20mods%29")]
    [InlineData("custom", "custom%20%2F%20other")]
    [InlineData("totally-unknown", "Not%20sure")]
    public void Build_maps_engine_key_to_the_exact_dropdown_option(string key, string expectedEncoded)
    {
        var url = GameRequestUrl.Build("G", null, key, null);
        Assert.Contains($"engine={expectedEncoded}", url);
    }

    [Fact]
    public void Build_includes_notes_when_present()
    {
        var url = GameRequestUrl.Build("G", null, null, "Mod path: Mods");
        Assert.Contains("notes=Mod%20path%3A%20Mods", url);
    }
}
