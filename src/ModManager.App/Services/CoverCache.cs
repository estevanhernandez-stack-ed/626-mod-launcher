using System.IO;
using System.Net.Http;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>Resolves a game's portrait cover, preferring on-disk sources and fetching Steam's public
/// CDN art once when nothing is cached locally. App-side (HTTP + file IO); the URL is a pure Core
/// helper (<see cref="SteamCdn"/>). Fetched covers cache under
/// %LOCALAPPDATA%\ModManagerBuilder\covers\{appId}.jpg so a game is fetched at most once. Offline /
/// 404 / no app id → null → the row shows its themed placeholder. Never throws.</summary>
public sealed class CoverCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IStoreLibrary _store;
    private readonly string _dir;

    public CoverCache(IStoreLibrary store)
    {
        _store = store;
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModManagerBuilder", "covers");
    }

    /// <summary>Immediate local cover — our fetched cache first, else Steam's local portrait art — or
    /// null. Synchronous (file-exists + directory read); safe to call while building rows.</summary>
    public string? LocalPortrait(string? steamAppId)
    {
        if (string.IsNullOrEmpty(steamAppId)) return null;
        var cached = Path.Combine(_dir, steamAppId + ".jpg");
        if (File.Exists(cached)) return cached;
        return _store.ResolveCoverArtPath(steamAppId, CoverShape.Portrait);
    }

    /// <summary>Local cover if present; else fetch Steam's public portrait once, cache it, and return the
    /// cached path. Two remote tiers: the legacy CDN path (most older games), then the store-API hashed
    /// path (new games like Death Stranding 2, whose art lives under store_item_assets). Null on
    /// offline / not-found / no app id. Never throws — degrades to the row's placeholder.</summary>
    public async Task<string?> FetchPortraitAsync(string? steamAppId)
    {
        if (string.IsNullOrEmpty(steamAppId)) return null;
        var local = LocalPortrait(steamAppId);
        if (local is not null) return local;

        // 1. legacy CDN path; 2. store-API hashed path when the legacy path has no art for this game.
        var bytes = await TryGetAsync(SteamCdn.PortraitCoverUrl(steamAppId));
        if (bytes is null)
        {
            var apiUrl = await StoreApiPortraitUrlAsync(steamAppId);
            if (apiUrl is not null) bytes = await TryGetAsync(apiUrl);
        }
        if (bytes is null) return null;

        try
        {
            Directory.CreateDirectory(_dir);
            var dest = Path.Combine(_dir, steamAppId + ".jpg");
            var tmp = dest + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, dest, overwrite: true); // atomic swap-in
            return dest;
        }
        catch { return null; }
    }

    // Fetch bytes, treating a too-small payload (404 error body) as a miss. Never throws.
    private static async Task<byte[]?> TryGetAsync(string url)
    {
        try { var b = await Http.GetByteArrayAsync(url); return b.Length >= 1024 ? b : null; }
        catch { return null; }
    }

    // Ask Steam's public store API for the portrait (library_capsule) URL — the path for new games whose
    // art isn't on the legacy CDN. No key; the store frontend uses this endpoint. Null on any shortfall.
    private static async Task<string?> StoreApiPortraitUrlAsync(string steamAppId)
    {
        try
        {
            var json = await Http.GetStringAsync(SteamCdn.GetItemsUrl(steamAppId));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var resp)) return null;
            if (!resp.TryGetProperty("store_items", out var items) || items.GetArrayLength() == 0) return null;
            if (!items[0].TryGetProperty("assets", out var assets)) return null;
            if (!assets.TryGetProperty("asset_url_format", out var fmtEl)) return null;
            if (!assets.TryGetProperty("library_capsule", out var capEl)) return null;
            var fmt = fmtEl.GetString();
            var cap = capEl.GetString();
            if (string.IsNullOrEmpty(fmt) || string.IsNullOrEmpty(cap)) return null;
            return SteamCdn.StoreItemAssetUrl(fmt, cap);
        }
        catch { return null; } // API shape change / offline — the legacy tier already tried, so placeholder
    }
}
