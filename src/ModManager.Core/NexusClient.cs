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
    /// Bulk "recently updated by game" — one call returns every mod that changed in the window.
    /// <paramref name="period"/> is one of Nexus's fixed windows ("1d" / "1w" / "1m").
    /// Rate-limit-aware: a 429 surfaces as <see cref="NexusRateLimitException"/>.
    /// </summary>
    Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period);

    /// <summary>
    /// Endorse or abstain a mod. POSTs the version-stamped body to the endorse/abstain endpoint.
    /// A 2xx returns the new status; a 4xx precondition refusal degrades to a friendly
    /// <see cref="EndorseOutcome"/> (<c>Refused = true</c>) without throwing; a 429 surfaces as
    /// <see cref="NexusRateLimitException"/>. Never auto-called — one user click per write.
    /// </summary>
    Task<EndorseOutcome> EndorseAsync(string domain, int modId, string version, EndorseAction action);

    /// <summary>
    /// Bulk read of the current user's endorse state across all games — one cheap call returns the
    /// whole library's <c>{ mod_id, domain_name, status }</c> rows. Read-only state sync (never a
    /// write); feeds <see cref="NexusRefresh.ApplyEndorsements"/> so hearts reflect reality even for
    /// mods endorsed outside the launcher. Rate-limit-aware: a 429 surfaces as
    /// <see cref="NexusRateLimitException"/>.
    /// </summary>
    Task<IReadOnlyList<NexusEndorsement>> GetUserEndorsementsAsync();

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

    /// <summary>
    /// Build + send a request, capturing rate limits, throwing on a non-2xx (429 as
    /// <see cref="NexusRateLimitException"/>). When <see cref="ApiRequest.Body"/> is set,
    /// attaches it as <c>application/json</c> content (the POST write path); GET requests with a
    /// null body send no content. Mirrors <c>CurseForgeClient.SendAsync</c>. Internal so the
    /// body plumbing is directly testable.
    /// </summary>
    internal async Task<JsonElement> SendAsync(ApiRequest req)
    {
        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
        {
            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue; // set on Content below
            msg.Headers.TryAddWithoutValidation(k, v);
        }
        if (req.Body is not null)
            msg.Content = new StringContent(req.Body, System.Text.Encoding.UTF8, "application/json");

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period)
    {
        var root = await SendAsync(NexusRequests.UpdatedRequest(gameDomain, period, _opts));
        return NexusRequests.MapUpdatedResponse(root);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NexusEndorsement>> GetUserEndorsementsAsync()
    {
        var root = await SendAsync(NexusRequests.UserEndorsementsRequest(_opts));
        return NexusRequests.MapUserEndorsements(root);
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

    /// <inheritdoc />
    public async Task<EndorseOutcome> EndorseAsync(string domain, int modId, string version, EndorseAction action)
    {
        var req = NexusRequests.EndorseRequest(domain, modId, version, action, _opts);

        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);
        foreach (var (k, v) in req.Headers)
        {
            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue; // set on Content below
            msg.Headers.TryAddWithoutValidation(k, v);
        }
        if (req.Body is not null)
            msg.Content = new StringContent(req.Body, System.Text.Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(msg);
        CaptureRateLimit(res); // 429 throws NexusRateLimitException; everything else falls through

        var bodyText = await res.Content.ReadAsStringAsync();

        if (res.IsSuccessStatusCode)
        {
            // 2xx: read the new status from { message, status }.
            var status = TryReadStringProperty(bodyText, "status");
            return new EndorseOutcome(Status: status, Message: null, Refused: false);
        }

        // Any other non-2xx (the precondition refusals, etc.): degrade to a friendly status line,
        // never throw. Surface the human message; map the two known codes to friendlier text.
        var rawMessage = TryReadStringProperty(bodyText, "message");
        var friendly = rawMessage is not null
            ? NexusEndorse.FriendlyRefusal(rawMessage)
            : $"Nexus declined the endorsement ({(int)res.StatusCode}).";
        return new EndorseOutcome(Status: null, Message: friendly, Refused: true);
    }

    /// <summary>
    /// Best-effort read of a top-level string property from a JSON body. Returns null on any
    /// parse failure or when the property is absent / not a string — a malformed refusal body
    /// must never throw on the write path.
    /// </summary>
    private static string? TryReadStringProperty(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — fall through to null.
        }
        return null;
    }
}
