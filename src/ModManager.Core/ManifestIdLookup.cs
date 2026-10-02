using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Naming which curated game a store identifier refers to.
///
/// <para>A registered game joins to its manifest entry by ID (<c>Scanner.GameContext</c>), and that id
/// used to come from slugifying whatever display name was in the wizard's box. When the two disagreed
/// — "Minecraft: Java Edition" against the <c>minecraft</c> entry — every curated fact about the game
/// was silently discarded, with nothing reported. This lets the add path state which game it is instead
/// of inferring it from a name somebody typed.</para>
///
/// <para>Returning null is a normal answer, not a failure: a game outside the manifest, a machine whose
/// feed never loaded, a game with no Steam id. The caller falls back to the name-derived id, which is
/// exactly today's behaviour.</para>
/// </summary>
public static class ManifestIdLookup
{
    public static string? BySteamAppId(GameManifest? manifest, string? steamAppId)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(steamAppId)) return null;
        return manifest.Games
            .FirstOrDefault(g => string.Equals(g.Stores.SteamAppId, steamAppId, StringComparison.Ordinal))
            ?.Id;
    }

    // Generation-cached mirror of the pure lookup above, for callers that resolve against the live
    // merged manifest rather than a caller-supplied one. Mirrors KnownModPaths' Map: a ~170-entry
    // dictionary rebuilt only when EffectiveManifest.Generation advances, instead of on every call.
    // AddGameDialog's constructor calls this once per installed Steam game (via SteamGameImport.Plan)
    // on the UI thread, alongside the already-cached KnownEngines.ByAppId / KnownModPaths.ByAppId.
    private static IReadOnlyDictionary<string, string>? _map;
    private static IReadOnlyDictionary<string, string>? _eaMap;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map => Maps().Steam;

    private static (IReadOnlyDictionary<string, string> Steam, IReadOnlyDictionary<string, string> Ea) Maps()
    {
        lock (_gate)
        {
            var gen = EffectiveManifest.Generation;
            if (_map is null || _eaMap is null || _mapGen != gen)
            {
                (_map, _eaMap) = Build();
                _mapGen = gen;
            }
            return (_map, _eaMap);
        }
    }

    private static (IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>) Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ea = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Stores.SteamAppId is { } appId)
                map.TryAdd(appId, g.Id); // first-entry-wins on a duplicate app id — pinned by a test above
            if (g.Stores.EaContentId is { } contentId)
                ea.TryAdd(contentId, g.Id);
        }
        return (map, ea);
    }

    /// <summary>Which manifest entry (from <see cref="EffectiveManifest.Current"/>) claims this EA app
    /// content id, or null. The EA counterpart of <see cref="BySteamAppId(string?)"/>.</summary>
    public static string? ByEaContentId(string? eaContentId)
        => !string.IsNullOrWhiteSpace(eaContentId) && Maps().Ea.TryGetValue(eaContentId, out var id) ? id : null;

    /// <summary>
    /// Every manifest id this registration can be said to be: its own id, the entry that claims its
    /// Steam app id, and the entry that claims its EA content id. Case-insensitive, like
    /// <c>BanRiskCatalog</c>'s id lookups.
    ///
    /// <para><b>Why not just <see cref="GameEntry.Id"/>.</b> A registration's id is the manifest id
    /// only when it was registered that way and nothing collided. A second store copy of a game the
    /// user already has is renamed <c>&lt;id&gt;-2</c> by <c>EnginePresets.UniqueId</c>; a game added
    /// before this lookup existed, or while the feed was unreachable, carries a slug of its display
    /// name. Both still carry the store identity, and the store identity names the game.</para>
    /// </summary>
    public static IReadOnlySet<string> IdsFor(GameEntry game)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(game.Id)) ids.Add(game.Id);
        if (BySteamAppId(game.SteamAppId) is { } bySteam) ids.Add(bySteam);
        if (ByEaContentId(game.EaContentId) is { } byEa) ids.Add(byEa);
        return ids;
    }

    /// <summary>The cached variant: which manifest entry (from <see cref="EffectiveManifest.Current"/>)
    /// claims this Steam app id, or null. Same answer as the two-argument overload called with the
    /// current effective manifest, generation-cached so a loop of callers doesn't rebuild the map per
    /// iteration.</summary>
    public static string? BySteamAppId(string? steamAppId)
        => !string.IsNullOrWhiteSpace(steamAppId) && Map.TryGetValue(steamAppId, out var id) ? id : null;
}
