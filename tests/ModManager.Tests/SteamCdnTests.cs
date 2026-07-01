using ModManager.Core;

namespace ModManager.Tests;

public class SteamCdnTests
{
    [Fact]
    public void PortraitCoverUrl_builds_the_public_library_600x900_path()
    {
        Assert.Equal(
            "https://cdn.cloudflare.steamstatic.com/steam/apps/1091500/library_600x900.jpg",
            SteamCdn.PortraitCoverUrl("1091500"));
    }
}
