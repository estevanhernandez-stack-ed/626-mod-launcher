using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>Per-store identifiers for one game. Only SteamAppId is populated/probed in Phase 0;
/// the rest exist so GOG/Epic/Game Pass slot in later without a schema migration.</summary>
public sealed record StoreIds
{
    public string? SteamAppId { get; init; }
    public string? GogId { get; init; }
    public string? EpicAppName { get; init; }
    public string? XboxStoreId { get; init; }
}

/// <summary>Which legacy arrays / mining sources contributed this entry, and its curation status.
/// In Phase 0 the sources are the legacy-array tags in <see cref="ManifestSources"/>; the facades
/// filter on them to reproduce each array's exact original membership.</summary>
public sealed record ManifestProvenance
{
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public string Status { get; init; } = "curated";
}

/// <summary>One game's identity + mod-layout overrides. Descriptive data only — it never describes
/// how to enable/disable a mod (that stays compiled, per the operating laws). ModPath is the one
/// trust-sensitive field; <see cref="ManifestValidator"/> gates it.</summary>
public sealed record GameManifestEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Engine { get; init; }              // null when the engine isn't known (nexus-only entries)
    public StoreIds Stores { get; init; } = new();
    public string? NexusDomain { get; init; }
    public int? CurseforgeGameId { get; init; }
    public string? ModPath { get; init; }             // override to the engine-default mod folder
    public string? SaveDirHint { get; init; }          // descriptive save-location hint (e.g. mined from Ludusavi save paths)
    public IReadOnlyList<string>? FileExtensions { get; init; }
    public string? GroupingRule { get; init; }
    public int? Featured { get; init; }               // quick-pick rank; null = not in the quick-pick list
    public ManifestProvenance Provenance { get; init; } = new();
}

/// <summary>The on-disk / embedded manifest: a schema version plus the game list.</summary>
public sealed record GameManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string? GeneratedUtc { get; init; }
    public string? MinBinaryVersion { get; init; }
    public IReadOnlyList<GameManifestEntry> Games { get; init; } = Array.Empty<GameManifestEntry>();
}

/// <summary>Provenance source tags. Phase 0 uses the legacy-array names so the facades can
/// reproduce each array's original membership exactly. The miner adds its own tags in Phase 1.</summary>
public static class ManifestSources
{
    public const string KnownEngines = "known-engines";
    public const string NexusDomains = "nexus-domains";
    public const string PopularGames = "popular-games";
}

/// <summary>Serializer options for the manifest: camelCase on disk (project rule), indented,
/// case-insensitive read. Mirrors <see cref="AtomicJson"/>'s policy.</summary>
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
