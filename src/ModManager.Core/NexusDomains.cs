namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID → Nexus Mods game-domain slug map. Nexus keys games by a URL slug
/// (nexusmods.com/&lt;slug&gt;), not a numeric id — and md5 metadata identify needs that slug to
/// query. The add paths (Steam auto-add, popular-game quick-pick, manual) carry a Steam app id but
/// not a Nexus domain; <see cref="EnginePresets.BuildGameEntry"/> resolves the domain from this map
/// when the input didn't supply one explicitly, so a Steam-added game still gets metadata.
///
/// Parallel to <see cref="KnownEngines"/> (app-id → engine). Add an entry when you know a game's
/// Nexus slug; an unmapped app id simply leaves the domain unset (metadata identify no-ops cleanly).
/// </summary>
public static class NexusDomains
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["3041230"] = "windrose",            // Windrose (ue-pak / UE4SS)
        ["3156770"] = "witchfire",           // Witchfire (ue-pak / loader-less content paks)
        ["1245620"] = "eldenring",           // Elden Ring
        ["489830"] = "skyrimspecialedition", // Skyrim Special Edition
        ["377160"] = "fallout4",             // Fallout 4
        ["1716740"] = "starfield",           // Starfield
        ["413150"] = "stardewvalley",        // Stardew Valley
        ["892970"] = "valheim",              // Valheim
        ["1966720"] = "lethalcompany",       // Lethal Company
        ["990080"] = "hogwartslegacy",       // Hogwarts Legacy
        ["1623730"] = "palworld",            // Palworld
        ["1091500"] = "cyberpunk2077",       // Cyberpunk 2077
    };

    /// <summary>The Nexus domain slug for a Steam app id, or null when unmapped / id is null/empty.</summary>
    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var d) ? d : null;

    /// <summary>
    /// The effective Nexus domain for a game: its stored <see cref="GameEntry.NexusGameDomain"/> if
    /// set, else resolved from the Steam app id. Read-time fallback so games registered BEFORE the
    /// domain was set on add (e.g. anything added via Steam auto-add pre-fix) still resolve a domain
    /// for md5 metadata identify, with no games.json migration.
    /// </summary>
    public static string? Effective(GameEntry game)
        => !string.IsNullOrWhiteSpace(game.NexusGameDomain) ? game.NexusGameDomain : ByAppId(game.SteamAppId);
}
