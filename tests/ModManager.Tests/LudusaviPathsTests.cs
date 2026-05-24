using ModManager.Core;

namespace ModManager.Tests;

// Pure resolver for Ludusavi save-path templates: replace <tokens> with real folders. The App
// indexes the manifest by Steam app id and supplies the tokens; this turns a template into a path.
public class LudusaviPathsTests
{
    [Fact]
    public void Resolves_known_tokens()
    {
        var tokens = new Dictionary<string, string>
        {
            ["winLocalAppData"] = @"C:\Users\mom\AppData\Local",
            ["base"] = @"C:\Games\Windrose",
        };
        Assert.Equal(@"C:\Users\mom\AppData\Local\R5\Saved\SaveProfiles",
            LudusaviPaths.Resolve("<winLocalAppData>/R5/Saved/SaveProfiles", tokens));
    }

    [Fact]
    public void Base_token_maps_to_the_install_dir()
    {
        var tokens = new Dictionary<string, string> { ["base"] = @"C:\Games\X" };
        Assert.Equal(@"C:\Games\X\Saves", LudusaviPaths.Resolve("<base>/Saves", tokens));
    }

    [Fact]
    public void Returns_null_when_a_token_cannot_be_resolved()
    {
        // e.g. <storeUserId> isn't something we resolve in v1 — skip rather than emit a broken path.
        Assert.Null(LudusaviPaths.Resolve("<winAppData>/<storeUserId>/saves",
            new Dictionary<string, string> { ["winAppData"] = @"C:\AppData" }));
    }
}
