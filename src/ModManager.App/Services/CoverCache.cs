using System.IO;
using System.Net.Http;
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

    /// <summary>Local cover if present; else fetch Steam's public CDN portrait once, cache it, and return
    /// the cached path. Null on 404 / offline / no app id. Never throws — degrades to placeholder.</summary>
    public async Task<string?> FetchPortraitAsync(string? steamAppId)
    {
        if (string.IsNullOrEmpty(steamAppId)) return null;
        var local = LocalPortrait(steamAppId);
        if (local is not null) return local;
        try
        {
            var bytes = await Http.GetByteArrayAsync(SteamCdn.PortraitCoverUrl(steamAppId));
            if (bytes.Length < 1024) return null; // guard against tiny/error payloads
            Directory.CreateDirectory(_dir);
            var dest = Path.Combine(_dir, steamAppId + ".jpg");
            var tmp = dest + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, dest, overwrite: true); // atomic swap-in
            return dest;
        }
        catch { return null; } // 404 / offline / IO — the row keeps its placeholder
    }
}
