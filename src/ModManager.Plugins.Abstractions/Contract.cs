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
