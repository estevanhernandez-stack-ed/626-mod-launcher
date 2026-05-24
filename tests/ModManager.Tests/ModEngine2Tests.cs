using ModManager.Core;

namespace ModManager.Tests;

// Mod Engine 2 launches FromSoft games via `modengine2_launcher.exe -t <code> -c <config>`.
// The -t code is game-specific; we map it from the Steam App ID we already know.
public class ModEngine2Tests
{
    [Theory]
    [InlineData("1245620", "er")]    // Elden Ring
    [InlineData("374320", "ds3")]    // Dark Souls III
    [InlineData("814380", "sekiro")] // Sekiro
    [InlineData("1888160", "ac6")]   // Armored Core VI
    public void Maps_known_app_ids_to_target_codes(string appId, string code)
        => Assert.Equal(code, ModEngine2.TargetForAppId(appId));

    [Fact]
    public void Unknown_app_id_has_no_target()
    {
        Assert.Null(ModEngine2.TargetForAppId("999999"));
        Assert.Null(ModEngine2.TargetForAppId(null));
    }
}
