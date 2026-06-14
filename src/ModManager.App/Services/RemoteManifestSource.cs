using System.IO;
using System.Net.Http;
using System.Reflection;
using ModManager.Core.Manifest;

namespace ModManager.App.Services;

/// <summary>
/// Drives the remote game-definition feed. At startup, <see cref="ApplyCachedAtStartup"/> applies the
/// last-fetched manifest from the on-disk cache (verified against the pinned key in Core) BEFORE the
/// UI / facades read — so a slow or offline network never blocks launch. In the background,
/// <see cref="RefreshAsync"/> re-fetches the feed (debounced 24h) into the cache for the NEXT launch.
/// Both are gated on the "auto-update definitions" setting. Ships dark: <see cref="FeedUrl"/> is empty
/// until the feed repo exists, so nothing is fetched. Auto-update is comfort, not load-bearing —
/// every failure is swallowed; the embedded manifest is always the floor.
/// </summary>
public sealed class RemoteManifestSource
{
    // The published 626-game-manifest feed (signed, verified against the pinned key in Core).
    // The .sig URL is derived as <FeedUrl>.sig. Empty => RefreshAsync no-ops (ships dark).
    private const string FeedUrl = "https://raw.githubusercontent.com/estevanhernandez-stack-ed/626-game-manifest/main/games-manifest.json";

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");
    private static readonly string StampPath = Path.Combine(CacheDir, "last-manifest-fetch.txt");

    private static Version AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private readonly HttpClient _http;

    public RemoteManifestSource(HttpClient http) => _http = http;

    /// <summary>Apply the cached feed (if the setting is on and a cache exists). Static so
    /// <c>Program.Main</c> can call it before the WinUI app / DI host spin up — i.e. before any
    /// facade reads. Never throws.</summary>
    public static void ApplyCachedAtStartup()
    {
        try
        {
            if (!new AppSettingsService().AutoUpdateDefinitions) return;
            RemoteManifestCache.ApplyCached(CacheDir, AppVersion);
        }
        catch { /* never block launch on the cache */ }
    }

    /// <summary>Background refresh for the NEXT launch: fetch the feed + signature into the cache,
    /// debounced once per 24h. No-op when the setting is off or the feed URL is empty (dark).</summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(FeedUrl)) return;          // ships dark until go-live
        if (!new AppSettingsService().AutoUpdateDefinitions) return;
        if (!ShouldFetch()) return;

        try
        {
            var manifestBytes = await _http.GetByteArrayAsync(FeedUrl).ConfigureAwait(false);
            var sigBytes = await _http.GetByteArrayAsync(FeedUrl + ".sig").ConfigureAwait(false);
            RemoteManifestCache.WriteCache(CacheDir, manifestBytes, sigBytes);
        }
        catch
        {
            // Swallow — the cached/embedded manifest is the floor.
        }
        finally
        {
            StampNow();
        }
    }

    private static bool ShouldFetch()
    {
        try
        {
            if (!File.Exists(StampPath)) return true;
            var last = File.ReadAllText(StampPath).Trim();
            if (!DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                return true;
            return DateTime.UtcNow - t >= DebounceWindow;
        }
        catch { return true; }
    }

    private static void StampNow()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O"));
        }
        catch { /* best-effort */ }
    }
}
