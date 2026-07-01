namespace ModManager.Core;

/// <summary>Public Steam CDN URLs for library art — no auth, the same public assets the Steam client
/// and community tools use. We fetch the portrait grid image when a game's art isn't cached locally
/// (Steam only caches a 32px icon for many installed games until you view them in its library grid).
/// Pure URL building; the fetch + on-disk cache is the App adapter's job.</summary>
public static class SteamCdn
{
    /// <summary>The public portrait (2:3, 600x900) library cover on the legacy CDN path. Serves most
    /// older-catalog games; brand-new games (whose art lives under the hashed store_item_assets path)
    /// 404 here — fall back to <see cref="GetItemsUrl"/> + <see cref="StoreItemAssetUrl"/>.</summary>
    public static string PortraitCoverUrl(string steamAppId)
        => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";

    /// <summary>Public store-API endpoint (no key — the store frontend uses it) that returns an app's
    /// asset URL format + hashed asset filenames, incl. the portrait <c>library_capsule</c>. Used when
    /// the legacy portrait path 404s (new games under the hashed store_item_assets layout).</summary>
    public static string GetItemsUrl(string steamAppId)
    {
        var json = "{\"ids\":[{\"appid\":" + steamAppId + "}],\"context\":{\"language\":\"english\","
                 + "\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
        return "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json="
             + Uri.EscapeDataString(json);
    }

    /// <summary>Assemble a hashed store_item_assets URL from the API's <c>asset_url_format</c> (e.g.
    /// <c>steam/apps/3280350/${FILENAME}?t=…</c>) and an asset filename (e.g.
    /// <c>&lt;hash&gt;/library_capsule.jpg</c>).</summary>
    public static string StoreItemAssetUrl(string assetUrlFormat, string filename)
        => "https://shared.akamai.steamstatic.com/store_item_assets/"
         + assetUrlFormat.Replace("${FILENAME}", filename);
}
