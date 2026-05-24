using System.Text;
using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// CurseForge metadata client (I/O). Pure request-building + mapping live in
/// CurseForgeRequests. The HttpClient is injected, so this is testable without a real key,
/// and BaseUrl/ApiKey are configurable so it works in both key-transport modes:
///   - per-user key: ApiKey set, BaseUrl defaults to api.curseforge.com
///   - thin proxy:   BaseUrl = proxy, no ApiKey (key held server-side)
/// Operating law #2: the key is never embedded here — it's passed in at runtime.
/// Mirrors curseforge.js.
/// </summary>
public sealed class CurseForgeClient
{
    private readonly HttpClient _http;
    private readonly CurseForgeOptions _opts;

    public CurseForgeClient(HttpClient http, CurseForgeOptions? opts = null)
    {
        _http = http;
        _opts = opts ?? new CurseForgeOptions();
    }

    private async Task<JsonElement> SendAsync(ApiRequest req)
    {
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
        {
            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue; // set on Content below
            msg.Headers.TryAddWithoutValidation(k, v);
        }
        if (req.Body is not null)
            msg.Content = new StringContent(req.Body, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(msg);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"CurseForge request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    public async Task<ModMeta?> GetModAsync(int modId)
        => CurseForgeRequests.MapModResponse(await SendAsync(CurseForgeRequests.ModRequest(modId, _opts)));

    public async Task<IReadOnlyList<ModMeta>> GetModsAsync(IEnumerable<int> modIds)
        => CurseForgeRequests.MapModsResponse(await SendAsync(CurseForgeRequests.ModsRequest(modIds, _opts)));

    /// <summary>Raw search hits (full mod objects) for a game + query.</summary>
    public async Task<IReadOnlyList<CfMod>> SearchAsync(int gameId, string query)
        => CurseForgeRequests.ParseSearchResults(await SendAsync(CurseForgeRequests.SearchRequest(gameId, query, _opts)));

    /// <summary>Exact file identification for fingerprints CurseForge knows.</summary>
    public async Task<IReadOnlyList<FingerprintMatch>> GetFingerprintMatchesAsync(IEnumerable<long> fingerprints)
        => CurseForgeRequests.ParseFingerprintMatches(await SendAsync(CurseForgeRequests.FingerprintsRequest(fingerprints, _opts)));

    /// <summary>Resolve a game's CurseForge id from its name by paging the games list. null if not found.</summary>
    public async Task<int?> ResolveGameIdAsync(string gameName)
    {
        for (var index = 0; index < 1000; index += 50)
        {
            var json = await SendAsync(CurseForgeRequests.GamesRequest(
                new CurseForgeOptions { BaseUrl = _opts.BaseUrl, ApiKey = _opts.ApiKey, Index = index, PageSize = 50 }));
            var games = CurseForgeRequests.ParseGames(json);
            if (games.Count == 0) break;
            var id = CurseForgeRequests.FindGameId(games, gameName);
            if (id is not null) return id;
        }
        return null;
    }
}
