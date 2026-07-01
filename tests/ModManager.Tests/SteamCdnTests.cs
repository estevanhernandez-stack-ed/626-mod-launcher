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

    [Fact]
    public void GetItemsUrl_targets_the_store_browse_api_with_the_appid()
    {
        var url = SteamCdn.GetItemsUrl("3280350");
        Assert.StartsWith("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=", url);
        // the appid is inside the (url-encoded) input_json, with assets requested
        Assert.Contains("3280350", Uri.UnescapeDataString(url));
        Assert.Contains("include_assets", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void StoreItemAssetUrl_substitutes_the_filename_into_the_hashed_path()
    {
        Assert.Equal(
            "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3280350/abc123/library_capsule.jpg?t=1",
            SteamCdn.StoreItemAssetUrl("steam/apps/3280350/${FILENAME}?t=1", "abc123/library_capsule.jpg"));
    }
}
