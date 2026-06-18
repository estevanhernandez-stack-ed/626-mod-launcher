using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus;

/// <summary>
/// The Nexus mod source as a host plugin: a LEAN re-implementation of just the four
/// <see cref="IModSource"/> operations (identify / metadata / update / endorse) over the Nexus v1
/// REST API, mapping responses onto the Abstractions DTOs. It references ONLY
/// <c>ModManager.Plugins.Abstractions</c> + the BCL — never <c>ModManager.Core</c>.
///
/// <para>Endpoints/headers reconciled from Core's <c>NexusClient</c> / <c>NexusRequests</c> /
/// <c>NexusEndorse</c> (the reference implementation that stays in Core for the scan-time identify
/// path until B2). This is a deliberate, temporary second Nexus code path.</para>
///
/// <para>Operating law #2: the API key is NEVER stored. It is read per call from the host-provided
/// getter (<c>host.GetCredential("nexus")</c>), attached to that one request, and discarded.</para>
/// </summary>
public sealed class NexusModSource : IModSource
{
    /// <summary>The Nexus v1 API base. Same host Core's <c>NexusRequests.Base</c> targets.</summary>
    private const string Base = "https://api.nexusmods.com";

    /// <summary>The Nexus-ToS application identity headers — sent on every request (mirrors Core).</summary>
    private const string ApplicationName = "626-mod-launcher";
    private const string DefaultAppVersion = "0.0.0";

    private const int TooManyRequests = 429;

    private readonly HttpClient _http;
    private readonly Func<string?> _getApiKey;
    private readonly string _appVersion;

    /// <param name="http">The shared host <see cref="HttpClient"/>.</param>
    /// <param name="getApiKey">Per-call key lookup against the host-owned on-machine store. Invoked on
    /// every request; the result is attached to that request only and never retained.</param>
    /// <param name="appVersion">The launcher version for the <c>Application-Version</c> ToS header
    /// (falls back to a fixed default when unset).</param>
    public NexusModSource(HttpClient http, Func<string?> getApiKey, string? appVersion = null)
    {
        _http = http;
        _getApiKey = getApiKey;
        _appVersion = string.IsNullOrEmpty(appVersion) ? DefaultAppVersion : appVersion;
    }

    public string Id => "nexus";

    /// <summary>Nexus identifies the user — every call carries the personal key.</summary>
    public bool RequiresApiKey => true;

    /// <summary>
    /// md5 file lookup: <c>GET /v1/games/{domain}/mods/md5_search/{md5}.json</c>. The body is an array
    /// of <c>{ mod, file_details }</c>; take the first, read <c>mod.mod_id</c> and the installed-file
    /// version (which beats the mod-level version). 404 / empty / wrong-shape → null (not-found is the
    /// normal path for an unknown hash).
    /// </summary>
    public async Task<SourceModRef?> IdentifyByHashAsync(string gameDomain, string md5)
    {
        var url = $"{Base}/v1/games/{gameDomain}/mods/md5_search/{md5}.json";
        using var res = await SendAsync(HttpMethod.Get, url);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) return null; // identify is best-effort — never throw on the read path

        using var doc = await ParseAsync(res);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return null;

        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object
                || !el.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
                return null;

            var modId = Int(mod, "mod_id");
            if (modId is null) return null;

            // The installed-file version wins over the mod-level version (matches Core's MapMd5Response).
            string? version = Str(mod, "version");
            if (el.TryGetProperty("file_details", out var fd) && fd.ValueKind == JsonValueKind.Object)
            {
                var fileVersion = Str(fd, "version");
                if (fileVersion is not null) version = fileVersion;
            }

            return new SourceModRef(SourceId: Id, GameDomain: gameDomain, ModId: modId.Value, Version: version ?? "");
        }
        return null;
    }

    /// <summary>
    /// Per-mod metadata: <c>GET /v1/games/{domain}/mods/{modId}.json</c> (the root IS the mod object).
    /// Maps live stats + the reported upstream version.
    ///
    /// <para><b>Endorsed is ALWAYS null.</b> The per-mod endpoint carries no per-user endorse state —
    /// that state is owned by the bulk endorsements sweep (a different endpoint). Returning
    /// <c>false</c> here would let a stats refresh wipe the user's filled heart, so this returns
    /// <c>Endorsed: null</c> and the mapper preserves the persisted value. This is the heart-wipe guard
    /// the contract is built around.</para>
    /// </summary>
    public async Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef)
    {
        var url = $"{Base}/v1/games/{modRef.GameDomain}/mods/{modRef.ModId}.json";
        using var res = await SendAsync(HttpMethod.Get, url);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = await ParseAsync(res);
        var mod = doc.RootElement;
        if (mod.ValueKind != JsonValueKind.Object) return null;

        return new SourceModMetadata(
            Endorsements: Int(mod, "endorsement_count"),
            Downloads: Long(mod, "mod_downloads"),
            LatestVersion: Str(mod, "version"),
            Available: BoolN(mod, "available"),
            Endorsed: null); // per-mod endpoint has no user-endorse state — NEVER false, never wipes the heart
    }

    /// <summary>
    /// "Is a newer version available?" — fetches the per-mod metadata and applies Core's exact
    /// comparison: an update exists when the source reports a version that differs from the installed
    /// one (<c>latest is not null &amp;&amp; latest != installed</c>; string inequality, not semver —
    /// mirrors <c>NexusRefresh</c>). A failed/empty fetch reports "no update" rather than throwing.
    /// </summary>
    public async Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion)
    {
        var meta = await FetchMetadataAsync(modRef);
        var latest = meta?.LatestVersion;
        return latest is not null && latest != installedVersion;
    }

    /// <summary>
    /// Endorse / abstain: <c>POST /v1/games/{domain}/mods/{modId}/{endorse|abstain}.json</c> with body
    /// <c>{"Version": version}</c>. A 2xx reads the new <c>status</c> and reports
    /// <c>NowEndorsed = status == "Endorsed"</c>. Any non-2xx (the precondition refusals, a 429, etc.)
    /// degrades to <c>Refused = true</c> with a friendly message — it NEVER throws on this write path.
    /// </summary>
    public async Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed)
    {
        var segment = endorsed ? "endorse" : "abstain";
        var url = $"{Base}/v1/games/{modRef.GameDomain}/mods/{modRef.ModId}/{segment}.json";
        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["Version"] = modRef.Version });

        HttpResponseMessage res;
        try
        {
            res = await SendAsync(HttpMethod.Post, url, body);
        }
        catch (HttpRequestException ex)
        {
            // Network-level failure (offline, DNS, TLS) — degrade, don't throw.
            return new EndorseResult(Ok: false, Refused: true, Message: $"Couldn't reach Nexus ({ex.Message}).", NowEndorsed: null);
        }

        try
        {
            var bodyText = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                var status = TryReadStringProperty(bodyText, "status");
                var nowEndorsed = string.Equals(status, "Endorsed", StringComparison.Ordinal);
                return new EndorseResult(Ok: true, Refused: false, Message: null, NowEndorsed: nowEndorsed);
            }

            // 429 and 4xx preconditions both degrade to a friendly refusal — never a throw.
            var rawMessage = TryReadStringProperty(bodyText, "message");
            var friendly = rawMessage is not null
                ? FriendlyRefusal(rawMessage)
                : (int)res.StatusCode == TooManyRequests
                    ? "Nexus is rate-limiting endorsements right now — try again in a bit."
                    : $"Nexus declined the endorsement ({(int)res.StatusCode}).";
            return new EndorseResult(Ok: false, Refused: true, Message: friendly, NowEndorsed: null);
        }
        finally
        {
            res.Dispose();
        }
    }

    /// <summary>
    /// Map a Nexus refusal code/message to user-facing text (mirrors Core's <c>NexusEndorse</c>). The
    /// two known precondition codes get friendly copy; any other value passes through verbatim so the
    /// API's own human wording surfaces.
    /// </summary>
    private static string FriendlyRefusal(string code) => code switch
    {
        "NOT_DOWNLOADED_MOD" => "You need to download this mod before you can endorse it.",
        "TOO_SOON_AFTER_DOWNLOAD" => "You'll need to wait a little while after downloading before you can endorse this mod.",
        _ => code,
    };

    /// <summary>
    /// Build + send one request with the Nexus-ToS headers and the per-call API key. The key is read
    /// fresh from the host getter on every call and attached to this request only (operating law #2 —
    /// never stored). When <paramref name="body"/> is set it rides as <c>application/json</c> content.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.TryAddWithoutValidation("Accept", "application/json");
        msg.Headers.TryAddWithoutValidation("Application-Name", ApplicationName);
        msg.Headers.TryAddWithoutValidation("Application-Version", _appVersion);

        var apiKey = _getApiKey();
        if (!string.IsNullOrEmpty(apiKey))
            msg.Headers.TryAddWithoutValidation("apikey", apiKey);

        if (body is not null)
            msg.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return await _http.SendAsync(msg);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    // --- tolerant JSON readers (mirror Core's NexusRequests helpers; missing/typed-wrong → null) ---

    private static string? Z(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? Z(el.GetString()) : null;

    private static int? Int(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    private static long? Long(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : null;

    private static bool? BoolN(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el)
            ? el.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => (bool?)null }
            : null;

    /// <summary>
    /// Best-effort read of a top-level string property from a JSON body. Returns null on any parse
    /// failure or when the property is absent / not a string — a malformed refusal body must never
    /// throw on the write path (mirrors Core's <c>NexusClient.TryReadStringProperty</c>).
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
