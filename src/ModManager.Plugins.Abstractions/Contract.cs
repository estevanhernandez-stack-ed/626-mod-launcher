namespace ModManager.Plugins.Abstractions;

/// <summary>The entry type a plugin assembly exports. The host instantiates it and calls Register.</summary>
public interface IModManagerPlugin
{
    string Id { get; }            // stable, e.g. "nexus"
    string DisplayName { get; }   // "Nexus Mods"
    void Register(IPluginHostServices host);
}

/// <summary>What the host offers a plugin: register contributions, read the on-machine credential, shared HttpClient.
/// The plugin NEVER stores or exfiltrates the credential — it receives it per call from the host-owned store.</summary>
public interface IPluginHostServices
{
    void AddModSource(IModSource source);
    [System.Obsolete("The host owns credentials. Use IAuthorizedSend.SendAuthorizedAsync; the host returns null here under OAuth.")]
    string? GetCredential(string key);                 // host-owned, on-machine per-user key store
    System.Net.Http.HttpClient HttpClient { get; }
    /// <summary>The launcher version, for any ToS / telemetry-identity header a source must send
    /// (e.g. Nexus's <c>Application-Version</c>). A plain string — Abstractions stays BCL-pure.</summary>
    string AppVersion { get; }
}

/// <summary>A mod-source site (Nexus, CurseForge, ...). Speaks DTOs only — never Core types — so a plugin
/// references just this slim assembly. Generalizes INexusClient.</summary>
public interface IModSource
{
    string Id { get; }
    bool RequiresApiKey { get; }
    Task<SourceIdentifyResult?> IdentifyByHashAsync(string gameDomain, string md5);
    Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef);
    Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion);
    Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed);
    /// <summary>Bulk current-user endorse state across all games (one call). Read-only sync.</summary>
    Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync();
    /// <summary>Recently-updated mods for a game in a fixed window ("1d"/"1w"/"1m").</summary>
    Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period);
}

public sealed record SourceModRef(string SourceId, string GameDomain, int ModId, string Version);
// Available + Endorsed are nullable: "the source didn't report this" must be expressible so a per-mod
// metadata fetch never clobbers persisted state. Endorse state is owned by the bulk endorsements sweep
// (a different endpoint) — a per-mod fetch returns Endorsed: null, never false.
public sealed record SourceModMetadata(
    int? Endorsements, long? Downloads, string? LatestVersion, bool? Available, bool? Endorsed,
    // B2a — identity/credit fields md5-identify produces (what Scanner needs to build a ModMeta):
    string? Title = null, string? Description = null, string? Author = null, string? AuthorUrl = null,
    string? ImageUrl = null, string? ModUrl = null, string? Category = null,
    bool? ContainsAdultContent = null, int? NexusFileId = null);

/// <summary>An identify hit: the mod ref + the full metadata, both from the single md5 call.</summary>
public sealed record SourceIdentifyResult(SourceModRef Ref, SourceModMetadata Metadata);

public sealed record EndorseResult(bool Ok, bool Refused, string? Message, bool? NowEndorsed);

/// <summary>One row of the user's bulk endorse state (mirrors Nexus /v1/user/endorsements.json).</summary>
public sealed record SourceEndorsement(int ModId, string DomainName, string Status);

/// <summary>One recently-updated mod in a game window (mirrors Nexus updated.json): unix-seconds file-update time.</summary>
public sealed record SourceUpdateEntry(int ModId, long LatestFileUpdate);

/// <summary>Thrown by a source when the service rate-limits (HTTP 429). Lets a bulk sweep stop and
/// report partial progress without the App referencing any provider-specific exception.</summary>
public sealed class SourceRateLimitException : Exception
{
    public SourceRateLimitException(string? message = null) : base(message ?? "Mod source rate limit reached.") { }
}

/// <summary>Optional text-search capability. A source that can search its catalog by name for a game
/// domain implements this ALONGSIDE IModSource; the host feature-detects with a type check, so
/// plugins built before this interface keep loading and working unchanged.</summary>
public interface IModTextSearch
{
    Task<IReadOnlyList<SourceSearchHit>> SearchAsync(string gameDomain, string query);
}

/// <summary>One text-search hit — enough for a review dialog row + a follow-up FetchMetadataAsync.</summary>
public record SourceSearchHit(
    string GameDomain, int ModId, string Name, string? Author,
    string? Summary, int? EndorsementCount, string? Url)
{
    /// <summary>Small mod thumbnail (Nexus <c>thumbnailUrl</c>), or null. Old plugins leave it null.</summary>
    public string? ThumbnailUrl { get; init; }
    /// <summary>Mod category name (Nexus <c>modCategory.name</c>), e.g. "Gameplay".</summary>
    public string? Category { get; init; }
    /// <summary>Author-published version string.</summary>
    public string? Version { get; init; }
    /// <summary>Total downloads.</summary>
    public int? DownloadCount { get; init; }
    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    // Per-user state. Null = unknown (disconnected, or an older plugin that never sets it) — the UI
    // shows a badge only when the value is explicitly true, so null/false both mean "no badge".
    public bool? ViewerDownloaded { get; init; }
    public bool? ViewerEndorsed { get; init; }
    public bool? ViewerUpdateAvailable { get; init; }
    public bool? ViewerTracked { get; init; }
}

/// <summary>Catalog sort views. Each maps to a live-verified <c>ModsSort</c> field; there is deliberately
/// no Trending — the schema has no trending sort.</summary>
public enum CatalogSort { MostEndorsed, MostDownloaded, RecentlyUpdated, RecentlyAdded }

/// <summary>A catalog browse request. A record envelope so later phases add options without changing
/// the interface signature. <paramref name="Text"/> null/blank = the default listing (no name filter).</summary>
public sealed record CatalogQuery(
    string GameDomain,
    string? Text = null,
    CatalogSort Sort = CatalogSort.MostEndorsed,
    string? Category = null,
    int Offset = 0,
    int Count = 20);

/// <summary>One category bucket with its mod count, from the browse response's facet data.</summary>
public sealed record CatalogCategory(string Name, int Count);

/// <summary>One page of catalog results. <paramref name="Categories"/> rides along on the same response
/// (facets), so the launcher needs no second round-trip to populate the category filter.</summary>
public sealed record CatalogPage(
    IReadOnlyList<SourceSearchHit> Hits,
    int TotalCount,
    IReadOnlyList<CatalogCategory> Categories)
{
    public static CatalogPage Empty { get; } =
        new(Array.Empty<SourceSearchHit>(), 0, Array.Empty<CatalogCategory>());
}

/// <summary>Optional capability: rich catalog browse (sort views, category filter, paging, per-user
/// state). Distinct from <see cref="IModCatalog"/>, which stays for back-compat — a host feature-detects
/// with <c>source is IModCatalogBrowse</c> and falls back to the simpler interface when absent.</summary>
public interface IModCatalogBrowse
{
    Task<CatalogPage> BrowseCatalogAsync(CatalogQuery query);
}

/// <summary>
/// Optional catalog-browse capability: search a game's mods for in-app discovery, with adult/mature
/// content EXCLUDED server-side (so the launcher never surfaces it and needs no age-gating). Distinct
/// from <see cref="IModTextSearch.SearchAsync"/>, which stays unfiltered for identifying the user's own
/// files. The host feature-detects with `source is IModCatalog`; plugins without it simply don't offer
/// the catalog.
/// </summary>
public interface IModCatalog
{
    Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query);
}

/// <summary>
/// Optional host capability: the host sends an authorized request on the plugin's behalf,
/// attaching credentials (OAuth bearer) server-side. The plugin builds an UNAUTHENTICATED
/// request and never receives a token. Plugins built before this interface keep loading;
/// the host feature-detects with `host is IAuthorizedSend`.
/// </summary>
public interface IAuthorizedSend
{
    Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, string credentialKey, CancellationToken ct = default);
}
