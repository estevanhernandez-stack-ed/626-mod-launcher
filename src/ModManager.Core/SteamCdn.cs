namespace ModManager.Core;

/// <summary>Public Steam CDN URLs for library art — no auth, the same public assets the Steam client
/// and community tools use. We fetch the portrait grid image when a game's art isn't cached locally
/// (Steam only caches a 32px icon for many installed games until you view them in its library grid).
/// Pure URL building; the fetch + on-disk cache is the App adapter's job.</summary>
public static class SteamCdn
{
    /// <summary>The public portrait (2:3, 600x900) library cover for a Steam app id.</summary>
    public static string PortraitCoverUrl(string steamAppId)
        => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";
}
