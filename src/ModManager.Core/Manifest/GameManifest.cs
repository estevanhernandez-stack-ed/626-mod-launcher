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
    public string? BanRisk { get; init; }             // null | "low" | "medium" | "high" — anti-cheat/ban exposure for online play (descriptive only)
    // Ban-risk nuance (batch 4): does a DOCUMENTED safe modding route exist despite the risk?
    // "offline" | "private-server" | "official-mods" | "none" | "unclear" | null (unresearched).
    // Descriptive only, like BanRisk — the warn-and-ack mechanism stays compiled code.
    /// <summary>
    /// How this game arranges its saves: <c>"typedFiles"</c> (several formats of one save side by
    /// side, Elden Ring's .sl2/.co2/.err) or <c>"worlds"</c> (a folder per world or slot, Palworld,
    /// Cyberpunk, Stellaris). <b>Absent means nobody has checked</b> — not "flat".
    ///
    /// <para>Descriptive, like <see cref="BanRisk"/> and <see cref="GroupingRule"/>: it says what the
    /// folder looks like, never how to write to it. The reader stays compiled.</para>
    ///
    /// <para>A string rather than the <c>SaveLayout</c> enum on purpose. An unrecognised value from a
    /// newer feed must degrade to the default; an enum would throw during deserialization, and
    /// ManifestLoader catches JsonException by returning null — which drops the ENTIRE feed, all 150
    /// games, over one unknown word.</para>
    /// </summary>
    public string? SaveLayout { get; init; }

    /// <summary>
    /// Globs, relative to a save UNIT, naming the files that are the PLAYER rather than the place.
    /// Palworld: <c>["Players/**", "LocalData.sav"]</c>. Windrose:
    /// <c>["**/Accounts/**", "**/Players/**", "**/AccountDescription.json"]</c>.
    ///
    /// <para>A unit is one world folder when <see cref="SaveLayout"/> is <c>worlds</c>, and the whole
    /// save folder otherwise — which is exactly why those two examples look different.</para>
    ///
    /// <para><b>Absent means nobody has curated it</b>, never "this game has no character data".
    /// Descriptive, like the rest: it says where the line is, never how to cut it.</para>
    /// </summary>
    public IReadOnlyList<string>? SavePlayerPaths { get; init; }

    public string? SafeRoute { get; init; }
    public string? SafeRouteHint { get; init; }       // one user-facing sentence naming the route (or the absence of one)
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

    /// <summary>An entry earns this when it's curated purely for anti-cheat/ban-risk safety — no
    /// engine, no Nexus domain, nothing else the facades read — so that curation still survives the
    /// miner's publish filter instead of being dropped as skeletal.</summary>
    public const string BanRiskCuration = "ban-risk";
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
