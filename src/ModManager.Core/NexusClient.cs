using System.Net;
using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Nexus metadata operations the launcher depends on — a second metadata + md5-file-ID
/// source alongside CurseForge. Injected so the metadata flows stay headless-testable.
/// </summary>
public interface INexusClient
{
    Task<ModMeta?> GetModAsync(string gameDomain, int modId);
    Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5);
}

/// <summary>
/// Nexus metadata client (I/O). Pure request-building + mapping live in NexusRequests.
/// The HttpClient is injected, so this is testable without a real key, and BaseUrl/ApiKey
/// are configurable for both key-transport modes:
///   - per-user key: ApiKey set, BaseUrl defaults to api.nexusmods.com
///   - thin proxy:   BaseUrl = proxy, no ApiKey (key held server-side)
/// Operating law #2: the key is never embedded here — it's passed in at runtime.
/// Note: Nexus games are keyed by domain name (e.g. "skyrimspecialedition"), not numeric id.
/// </summary>
public sealed class NexusClient : INexusClient
{
    private readonly HttpClient _http;
    private readonly NexusOptions _opts;

    public NexusClient(HttpClient http, NexusOptions? opts = null)
    {
        _http = http;
        _opts = opts ?? new NexusOptions();
    }

    private async Task<JsonElement> SendAsync(ApiRequest req)
    {
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
            msg.Headers.TryAddWithoutValidation(k, v);

        using var res = await _http.SendAsync(msg);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    public async Task<ModMeta?> GetModAsync(string gameDomain, int modId)
        => NexusRequests.MapModResponse(gameDomain, await SendAsync(NexusRequests.ModRequest(gameDomain, modId, _opts)));

    /// <summary>md5 file lookup. 404 means "not found" (normal) and returns null; other failures throw.</summary>
    public async Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5)
    {
        var req = NexusRequests.Md5Request(gameDomain, md5, _opts);
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
            msg.Headers.TryAddWithoutValidation(k, v);

        using var res = await _http.SendAsync(msg);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return null; // not found is normal for an unknown file hash
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return NexusRequests.MapMd5Response(gameDomain, doc.RootElement);
    }
}
