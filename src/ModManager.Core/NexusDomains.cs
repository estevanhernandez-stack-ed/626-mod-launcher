using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID → Nexus Mods game-domain slug map. Nexus keys games by a URL slug
/// (nexusmods.com/&lt;slug&gt;), not a numeric id — and md5 metadata identify needs that slug.
///
/// Facade over <see cref="EmbeddedGameManifest"/>: reads only entries tagged with the
/// "nexus-domains" provenance source, preserving its original membership (which includes games not
/// in <see cref="KnownEngines"/>, e.g. Windrose/Witchfire/Cyberpunk). An unmapped app id leaves the
/// domain unset (metadata identify no-ops cleanly).
/// </summary>
public static class NexusDomains
{
    private static readonly IReadOnlyDictionary<string, string> Map = Build();

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EmbeddedGameManifest.Current.Games)
        {
            if (g.Provenance.Sources.Contains(ManifestSources.NexusDomains)
                && g.Stores.SteamAppId is { } appId
                && g.NexusDomain is { } domain)
            {
                map[appId] = domain;
            }
        }
        return map;
    }

    /// <summary>The Nexus domain slug for a Steam app id, or null when unmapped / id is null/empty.</summary>
    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var d) ? d : null;

    /// <summary>
    /// The effective Nexus domain for a game: its stored <see cref="GameEntry.NexusGameDomain"/> if
    /// set, else resolved from the Steam app id. Read-time fallback so games registered BEFORE the
    /// domain was set on add still resolve a domain for md5 metadata identify, with no migration.
    /// </summary>
    public static string? Effective(GameEntry game)
        => !string.IsNullOrWhiteSpace(game.NexusGameDomain) ? game.NexusGameDomain : ByAppId(game.SteamAppId);
}
