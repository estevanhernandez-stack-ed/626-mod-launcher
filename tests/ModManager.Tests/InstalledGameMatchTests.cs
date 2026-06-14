using ModManager.Core;

namespace ModManager.Tests;

public class InstalledGameMatchTests
{
    private static readonly IReadOnlyList<InstalledGame> Games = new[]
    {
        new InstalledGame("steam", "1091500", "Cyberpunk 2077", @"D:\Steam\steamapps\common\Cyberpunk 2077"),
        new InstalledGame("steam", "489830", "Skyrim SE", @"D:\Steam\steamapps\common\Skyrim Special Edition"),
    };

    [Fact]
    public void ByAppId_returns_the_match()
    {
        var g = InstalledGameMatch.ByAppId(Games, "1091500");
        Assert.Equal(@"D:\Steam\steamapps\common\Cyberpunk 2077", g!.InstallDir);
    }

    [Fact]
    public void ByAppId_returns_null_for_unknown()
        => Assert.Null(InstalledGameMatch.ByAppId(Games, "999999"));

    [Fact]
    public void ByAppId_returns_null_for_empty_appid()
        => Assert.Null(InstalledGameMatch.ByAppId(Games, ""));
}
