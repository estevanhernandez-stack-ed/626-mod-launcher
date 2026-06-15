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
}

/// <summary>A mod-source site (Nexus, CurseForge, ...). Speaks DTOs only — never Core types — so a plugin
/// references just this slim assembly. Generalizes INexusClient.</summary>
public interface IModSource
{
    string Id { get; }
    bool RequiresApiKey { get; }
    Task<SourceModRef?> IdentifyByHashAsync(string gameDomain, string md5);
    Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef);
    Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion);
    Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed);
}

public sealed record SourceModRef(string SourceId, string GameDomain, int ModId, string Version);
// Available + Endorsed are nullable: "the source didn't report this" must be expressible so a per-mod
// metadata fetch never clobbers persisted state. Endorse state is owned by the bulk endorsements sweep
// (a different endpoint) — a per-mod fetch returns Endorsed: null, never false.
public sealed record SourceModMetadata(int? Endorsements, long? Downloads, string? LatestVersion, bool? Available, bool? Endorsed);
public sealed record EndorseResult(bool Ok, bool Refused, string? Message, bool? NowEndorsed);
