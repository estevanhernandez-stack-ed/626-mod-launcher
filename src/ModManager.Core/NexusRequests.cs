using System.Text.Json;

namespace ModManager.Core;

/// <summary>Options for Nexus request building. BaseUrl points at a proxy in prod (no key).</summary>
public sealed class NexusOptions
{
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }

    /// <summary>
    /// The launcher version reported via the Nexus-ToS <c>Application-Version</c> header.
    /// Passed in from the App (the entry-assembly version) so <see cref="NexusRequests"/> stays
    /// pure — when unset the request builder falls back to a fixed default.
    /// </summary>
    public string? AppVersion { get; init; }
}

/// <summary>An md5_search hit: the Nexus mod id + the mapped metadata.</summary>
public sealed record NexusMd5Match(int? ModId, ModMeta Meta);

/// <summary>The validate.json identity: the account name + premium flag.</summary>
public sealed record NexusUser(string? Name, bool IsPremium);

/// <summary>
/// Pure Nexus request-building + response-mapping. The real v1 API exposes only
/// mod-details-by-id and md5 file lookup — there is NO name-search endpoint, so none is built.
/// Maps a Nexus mod object to our metadata schema; tolerant of missing fields.
/// Operating law #2: the key is never embedded — it rides on the request via opts at runtime.
/// </summary>
public static class NexusRequests
{
    public const string Base = "https://api.nexusmods.com";

    /// <summary>The Nexus-ToS application identity. Sent on every request.</summary>
    public const string ApplicationName = "626-mod-launcher";

    /// <summary>Fallback <c>Application-Version</c> when <see cref="NexusOptions.AppVersion"/> is unset.</summary>
    public const string DefaultAppVersion = "0.0.0";

    private static Dictionary<string, string> Headers(NexusOptions opts)
    {
        var h = new Dictionary<string, string>
        {
            ["Accept"] = "application/json",
            // Nexus-ToS identity headers — always present, both key and proxy modes.
            ["Application-Name"] = ApplicationName,
            ["Application-Version"] = string.IsNullOrEmpty(opts.AppVersion) ? DefaultAppVersion : opts.AppVersion,
        };
        if (!string.IsNullOrEmpty(opts.ApiKey)) h["apikey"] = opts.ApiKey; // omitted in proxy mode
        return h;
    }

    private static string? Z(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? Z(el.GetString()) : null;

    private static int? Int(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    private static bool Bool(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static long? Long(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : null;

    private static bool? BoolN(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el)
            ? el.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => (bool?)null }
            : null;

    public static ApiRequest ModRequest(string domain, int modId, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/{modId}.json", "GET", Headers(opts));
    }

    public static ApiRequest Md5Request(string domain, string md5, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/md5_search/{md5}.json", "GET", Headers(opts));
    }

    /// <summary>Game info (including the categories array): GET /v1/games/{domain}.json.</summary>
    public static ApiRequest GameInfoRequest(string domain, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}.json", "GET", Headers(opts));
    }

    /// <summary>Verify the key + read the account identity: GET /v1/users/validate.json.</summary>
    public static ApiRequest ValidateRequest(NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/users/validate.json", "GET", Headers(opts));
    }

    /// <summary>
    /// Endorse/abstain a mod: POST /v1/games/{domain}/mods/{modId}/{endorse|abstain}.json with body
    /// <c>{"Version": version}</c>. The path segment is chosen from <paramref name="action"/>. The
    /// body is JSON-serialized (so the version is escaped); the usual auth + ToS headers ride along.
    /// </summary>
    public static ApiRequest EndorseRequest(string domain, int modId, string version, EndorseAction action, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        var segment = action == EndorseAction.Abstain ? "abstain" : "endorse";
        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["Version"] = version });
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/{modId}/{segment}.json", "POST", Headers(opts), body);
    }

    /// <summary>
    /// Bulk "recently updated by game": GET /v1/games/{domain}/mods/updated.json?period={period}.
    /// <paramref name="period"/> is one of Nexus's fixed windows — "1d", "1w", or "1m".
    /// </summary>
    public static ApiRequest UpdatedRequest(string domain, string period, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/updated.json?period={period}", "GET", Headers(opts));
    }

    /// <summary>
    /// Nexus mod object -> our metadata entry. Url is constructed from domain + mod_id.
    /// When <paramref name="categories"/> is supplied and the mod JSON contains a numeric
    /// <c>category_id</c>, the id is resolved to a name and written to <see cref="ModMeta.Category"/>.
    /// Unresolvable ids (id not in dict, dict null, or missing field) leave Category null.
    /// </summary>
    public static ModMeta MapMod(string domain, JsonElement modObject,
        IReadOnlyDictionary<int, string>? categories = null)
    {
        var modId = Int(modObject, "mod_id");
        var categoryId = Int(modObject, "category_id");
        string? category = null;
        if (categoryId.HasValue && categories != null)
            categories.TryGetValue(categoryId.Value, out category);

        return new ModMeta
        {
            Title = Str(modObject, "name"),
            Description = Str(modObject, "summary"),
            Author = Str(modObject, "author") ?? Str(modObject, "uploaded_by"),
            AuthorUrl = Str(modObject, "uploaded_users_profile_url"),
            Image = Str(modObject, "picture_url"),
            Url = modId.HasValue ? $"https://www.nexusmods.com/{domain}/mods/{modId.Value}" : null,
            Source = null,
            Donate = null,
            Downloads = Long(modObject, "mod_downloads"),
            EndorsementCount = Int(modObject, "endorsement_count"),
            Version = Str(modObject, "version"),
            Available = BoolN(modObject, "available"),
            ContainsAdultContent = BoolN(modObject, "contains_adult_content"),
            NexusModId = modId,
            Category = category,
        };
    }

    /// <summary>The GetMod response root IS the mod object. null if not an object.</summary>
    public static ModMeta? MapModResponse(string domain, JsonElement root,
        IReadOnlyDictionary<int, string>? categories = null)
        => root.ValueKind == JsonValueKind.Object ? MapMod(domain, root, categories) : null;

    /// <summary>
    /// Reads the <c>categories</c> array from a Nexus game-info JSON body
    /// (<c>GET /v1/games/{domain}.json</c>) and returns an id-to-name dict.
    /// Entries missing either <c>category_id</c> or <c>name</c> are silently skipped;
    /// malformed input never throws.
    /// </summary>
    public static IReadOnlyDictionary<int, string> MapCategories(JsonElement gameJson)
    {
        var dict = new Dictionary<int, string>();
        if (gameJson.ValueKind != JsonValueKind.Object) return dict;
        if (!gameJson.TryGetProperty("categories", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return dict;

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var id = Int(entry, "category_id");
            var name = Str(entry, "name");
            if (id.HasValue && name != null)
                dict[id.Value] = name;
        }
        return dict;
    }

    /// <summary>validate.json body -> account identity. Tolerant of missing fields; null if not an object.</summary>
    public static NexusUser? MapValidateResponse(JsonElement root)
        => root.ValueKind == JsonValueKind.Object ? new NexusUser(Str(root, "name"), Bool(root, "is_premium")) : null;

    /// <summary>
    /// updated.json body -> the list of recently-changed mods. The root is an array of
    /// { mod_id, latest_file_update, latest_mod_activity } (unix seconds). Entries missing
    /// <c>mod_id</c> are skipped; a non-array root yields an empty list. Never throws.
    /// </summary>
    public static IReadOnlyList<NexusUpdateEntry> MapUpdatedResponse(JsonElement root)
    {
        var list = new List<NexusUpdateEntry>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var modId = Int(el, "mod_id");
            if (!modId.HasValue) continue;
            list.Add(new NexusUpdateEntry(
                modId.Value,
                Long(el, "latest_file_update") ?? 0L,
                Long(el, "latest_mod_activity") ?? 0L));
        }
        return list;
    }

    /// <summary>
    /// md5_search returns an array of { mod, file_details }. Take the first element, map its
    /// mod object, return the match. Empty array / wrong shape -> null.
    /// </summary>
    public static NexusMd5Match? MapMd5Response(string domain, JsonElement root,
        IReadOnlyDictionary<int, string>? categories = null)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
                return null;
            var meta = MapMod(domain, mod, categories);
            if (el.TryGetProperty("file_details", out var fd) && fd.ValueKind == JsonValueKind.Object)
            {
                meta.NexusFileId = Int(fd, "file_id");
                var fileVersion = Str(fd, "version");
                if (fileVersion is not null) meta.Version = fileVersion;   // installed-file version beats mod-level
            }
            return new NexusMd5Match(Int(mod, "mod_id"), meta);
        }
        return null;
    }
}
