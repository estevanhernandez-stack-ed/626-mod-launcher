using System.Text.Json;

namespace ModManager.Core;

/// <summary>Options for Nexus request building. BaseUrl points at a proxy in prod (no key).</summary>
public sealed class NexusOptions
{
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
}

/// <summary>An md5_search hit: the Nexus mod id + the mapped metadata.</summary>
public sealed record NexusMd5Match(int? ModId, ModMeta Meta);

/// <summary>
/// Pure Nexus request-building + response-mapping. The real v1 API exposes only
/// mod-details-by-id and md5 file lookup — there is NO name-search endpoint, so none is built.
/// Maps a Nexus mod object to our metadata schema; tolerant of missing fields.
/// Operating law #2: the key is never embedded — it rides on the request via opts at runtime.
/// </summary>
public static class NexusRequests
{
    public const string Base = "https://api.nexusmods.com";

    private static Dictionary<string, string> Headers(string? apiKey)
    {
        var h = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (!string.IsNullOrEmpty(apiKey)) h["apikey"] = apiKey; // omitted in proxy mode
        return h;
    }

    private static string? Z(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? Z(el.GetString()) : null;

    private static int? Int(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    public static ApiRequest ModRequest(string domain, int modId, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/{modId}.json", "GET", Headers(opts.ApiKey));
    }

    public static ApiRequest Md5Request(string domain, string md5, NexusOptions? opts = null)
    {
        opts ??= new NexusOptions();
        var baseUrl = opts.BaseUrl ?? Base;
        return new ApiRequest($"{baseUrl}/v1/games/{domain}/mods/md5_search/{md5}.json", "GET", Headers(opts.ApiKey));
    }

    /// <summary>Nexus mod object -> our metadata entry. Url is constructed from domain + mod_id.</summary>
    public static ModMeta MapMod(string domain, JsonElement modObject)
    {
        var modId = Int(modObject, "mod_id");
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
            Downloads = null,
        };
    }

    /// <summary>The GetMod response root IS the mod object. null if not an object.</summary>
    public static ModMeta? MapModResponse(string domain, JsonElement root)
        => root.ValueKind == JsonValueKind.Object ? MapMod(domain, root) : null;

    /// <summary>
    /// md5_search returns an array of { mod, file_details }. Take the first element, map its
    /// mod object, return the match. Empty array / wrong shape -> null.
    /// </summary>
    public static NexusMd5Match? MapMd5Response(string domain, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
                return null;
            var meta = MapMod(domain, mod);
            return new NexusMd5Match(Int(mod, "mod_id"), meta);
        }
        return null;
    }
}
