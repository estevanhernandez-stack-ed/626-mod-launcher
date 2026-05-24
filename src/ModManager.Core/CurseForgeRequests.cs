using System.Net;
using System.Text.Json;

namespace ModManager.Core;

/// <summary>A built HTTP request descriptor (no IO). The client layers the actual call on top.</summary>
public sealed record ApiRequest(string Url, string Method, IReadOnlyDictionary<string, string> Headers, string? Body = null);

/// <summary>An exact fingerprint match: the CurseForge mod id + the matched file fingerprint.</summary>
public sealed record FingerprintMatch(int? ModId, long? Fingerprint);

/// <summary>CurseForge game object (subset) — used to resolve a game id from its name.</summary>
public sealed class CfGame
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
}

/// <summary>Options for CurseForge request building. BaseUrl points at the proxy in prod (no key).</summary>
public sealed class CurseForgeOptions
{
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public int? PageSize { get; init; }
    public int? Index { get; init; }
    public int? GameId { get; init; }
}

// CurseForge mod object shape (subset we consume). camelCase JSON binds case-insensitively.
public sealed class CfMod
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public long? DownloadCount { get; set; }
    public List<CfAuthor>? Authors { get; set; }
    public CfLogo? Logo { get; set; }
    public CfLinks? Links { get; set; }
}

public sealed class CfAuthor
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
}

public sealed class CfLogo
{
    public string? Url { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public sealed class CfLinks
{
    public string? WebsiteUrl { get; set; }
    public string? SourceUrl { get; set; }
}

/// <summary>
/// Pure CurseForge request-building + response-mapping. Maps a CurseForge mod object to our
/// metadata schema (title/description/author/url + honor fields). Mirrors curseforge-core.js.
/// </summary>
public static class CurseForgeRequests
{
    public const string Base = "https://api.curseforge.com";

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static Dictionary<string, string> Headers(string? apiKey, bool post)
    {
        var h = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (!string.IsNullOrEmpty(apiKey)) h["x-api-key"] = apiKey; // omitted in proxy mode
        if (post) h["Content-Type"] = "application/json";
        return h;
    }

    private static string? Z(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public static ApiRequest ModRequest(int modId, CurseForgeOptions? opts = null)
    {
        opts ??= new CurseForgeOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/mods/{modId}", "GET", Headers(opts.ApiKey, false));
    }

    public static ApiRequest ModsRequest(IEnumerable<int> modIds, CurseForgeOptions? opts = null)
    {
        opts ??= new CurseForgeOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        var body = JsonSerializer.Serialize(new { modIds = modIds.ToArray() });
        return new ApiRequest($"{baseUrl}/v1/mods", "POST", Headers(opts.ApiKey, true), body);
    }

    public static ApiRequest FingerprintsRequest(IEnumerable<long> fingerprints, CurseForgeOptions? opts = null)
    {
        opts ??= new CurseForgeOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        var path = opts.GameId.HasValue ? $"/v1/fingerprints/{opts.GameId.Value}" : "/v1/fingerprints";
        var body = JsonSerializer.Serialize(new { fingerprints = fingerprints.ToArray() });
        return new ApiRequest($"{baseUrl}{path}", "POST", Headers(opts.ApiKey, true), body);
    }

    /// <summary>CurseForge mod object -> our metadata entry. Tolerant of missing nested fields.</summary>
    public static ModMeta MapMod(CfMod? mod)
    {
        mod ??= new CfMod();
        var author = mod.Authors is { Count: > 0 } ? mod.Authors[0] : null;
        var links = mod.Links;
        var logo = mod.Logo;
        return new ModMeta
        {
            Title = Z(mod.Name),
            Description = Z(mod.Summary),
            Author = author is null ? null : Z(author.Name),
            AuthorUrl = author is null ? null : Z(author.Url),
            Url = Z(links?.WebsiteUrl),
            Image = logo is null ? null : (Z(logo.ThumbnailUrl) ?? Z(logo.Url)),
            Downloads = mod.DownloadCount,
            Source = Z(links?.SourceUrl),
            CurseforgeId = mod.Id,
        };
    }

    public static IReadOnlyList<ModMeta> MapModsResponse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModMeta>();
        return data.EnumerateArray()
            .Select(el => MapMod(el.Deserialize<CfMod>(CaseInsensitive)))
            .ToList();
    }

    public static ModMeta? MapModResponse(JsonElement json)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? MapMod(data.Deserialize<CfMod>(CaseInsensitive))
            : null;

    /// <summary>Search a game's mods by name. Hits are full mod objects (MapMod-able directly).</summary>
    public static ApiRequest SearchRequest(int gameId, string query, CurseForgeOptions? opts = null)
    {
        opts ??= new CurseForgeOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        var pageSize = opts.PageSize ?? 10;
        var qs = $"gameId={gameId}&searchFilter={WebUtility.UrlEncode(query)}&pageSize={pageSize}";
        return new ApiRequest($"{baseUrl}/v1/mods/search?{qs}", "GET", Headers(opts.ApiKey, false));
    }

    public static IReadOnlyList<CfMod> ParseSearchResults(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CfMod>();
        return data.EnumerateArray()
            .Select(el => el.Deserialize<CfMod>(CaseInsensitive) ?? new CfMod())
            .ToList();
    }

    public static IReadOnlyList<CfGame> ParseGames(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CfGame>();
        return data.EnumerateArray()
            .Select(el => el.Deserialize<CfGame>(CaseInsensitive) ?? new CfGame())
            .ToList();
    }

    /// <summary>Games list (paged) — used to resolve a game's CurseForge id from its name.</summary>
    public static ApiRequest GamesRequest(CurseForgeOptions? opts = null)
    {
        opts ??= new CurseForgeOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        var pageSize = opts.PageSize ?? 50;
        var index = opts.Index ?? 0;
        return new ApiRequest($"{baseUrl}/v1/games?pageSize={pageSize}&index={index}", "GET", Headers(opts.ApiKey, false));
    }

    private static string NormGame(string? s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static int? FindGameId(IEnumerable<CfGame> games, string? gameName)
    {
        var want = NormGame(gameName);
        if (want.Length == 0) return null;
        foreach (var g in games)
        {
            if (NormGame(g.Name) == want || NormGame(g.Slug) == want) return g.Id;
        }
        return null;
    }

    public static IReadOnlyList<FingerprintMatch> ParseFingerprintMatches(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("exactMatches", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<FingerprintMatch>();

        var outList = new List<FingerprintMatch>();
        foreach (var m in arr.EnumerateArray())
        {
            int? modId = m.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : null;
            long? fp = null;
            if (m.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.Object
                && f.TryGetProperty("fileFingerprint", out var fpEl) && fpEl.ValueKind == JsonValueKind.Number)
                fp = fpEl.GetInt64();
            outList.Add(new FingerprintMatch(modId, fp));
        }
        return outList;
    }
}
