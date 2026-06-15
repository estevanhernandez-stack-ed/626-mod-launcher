using System.Collections.Concurrent;
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
    Task<NexusUser?> ValidateAsync();

    /// <summary>
    /// The rate-limit snapshot from the most recent response (null until the first call).
    /// Lets a sweep read remaining budget and back off before it gets throttled.
    /// </summary>
    NexusRateLimit? LastRateLimit { get; }
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
    private const int TooManyRequests = 429;

    private readonly HttpClient _http;
    private readonly NexusOptions _opts;

    // Per-session, per-domain category cache. Populated lazily on first GetModAsync or
    // GetByMd5Async call for a domain; a failed fetch stores nothing so the next call retries.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>> _categoriesCache = new();

    /// <inheritdoc />
    public NexusRateLimit? LastRateLimit { get; private set; }

    public NexusClient(HttpClient http, NexusOptions? opts = null)
    {
        _http = http;
        _opts = opts ?? new NexusOptions();
    }

    /// <summary>
    /// Snapshot the <c>x-rl-*</c> headers off a response onto <see cref="LastRateLimit"/>, then
    /// throw <see cref="NexusRateLimitException"/> if the status is 429. Call right after the send,
    /// before any per-endpoint status branching, so every path stays rate-limit-aware.
    /// </summary>
    private void CaptureRateLimit(HttpResponseMessage res)
    {
        LastRateLimit = NexusRateLimit.Parse(res.Headers);
        if ((int)res.StatusCode == TooManyRequests)
            throw new NexusRateLimitException(LastRateLimit);
    }

    private async Task<JsonElement> SendAsync(ApiRequest req)
    {
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
            msg.Headers.TryAddWithoutValidation(k, v);

        using var res = await _http.SendAsync(msg);
        CaptureRateLimit(res);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Fetch and cache the id→name category map for <paramref name="domain"/>.
    /// Hits GET /v1/games/{domain}.json once per session; on any HTTP failure returns null
    /// without caching (the next call will retry). A null result is non-fatal — callers
    /// pass it straight to MapMod and Category is left null rather than failing the fetch.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, string>?> GetCategoriesAsync(string domain)
    {
        if (_categoriesCache.TryGetValue(domain, out var cached))
            return cached;

        try
        {
            var root = await SendAsync(NexusRequests.GameInfoRequest(domain, _opts));
            var dict = NexusRequests.MapCategories(root);
            _categoriesCache[domain] = dict;
            return dict;
        }
        catch
        {
            // Non-fatal: category resolution is best-effort. Don't cache failures so
            // the next request can retry (e.g. transient network error).
            return null;
        }
    }

    public async Task<ModMeta?> GetModAsync(string gameDomain, int modId)
    {
        var categories = await GetCategoriesAsync(gameDomain);
        var root = await SendAsync(NexusRequests.ModRequest(gameDomain, modId, _opts));
        return NexusRequests.MapModResponse(gameDomain, root, categories);
    }

    /// <summary>md5 file lookup. 404 means "not found" (normal) and returns null; other failures throw.</summary>
    public async Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5)
    {
        var categories = await GetCategoriesAsync(gameDomain);

        var req = NexusRequests.Md5Request(gameDomain, md5, _opts);
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
            msg.Headers.TryAddWithoutValidation(k, v);

        using var res = await _http.SendAsync(msg);
        CaptureRateLimit(res);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return null; // not found is normal for an unknown file hash
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return NexusRequests.MapMd5Response(gameDomain, doc.RootElement, categories);
    }

    /// <summary>Verify the key + read the account name. 401 means a bad/expired key and returns null; other failures throw.</summary>
    public async Task<NexusUser?> ValidateAsync()
    {
        var req = NexusRequests.ValidateRequest(_opts);
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
            msg.Headers.TryAddWithoutValidation(k, v);

        using var res = await _http.SendAsync(msg);
        CaptureRateLimit(res);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            return null; // bad / expired key
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return NexusRequests.MapValidateResponse(doc.RootElement);
    }
}
