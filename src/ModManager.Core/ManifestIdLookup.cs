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
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_map is null || _mapGen != gen)
                {
                    _map = Build();
                    _mapGen = gen;
                }
                return _map;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Stores.SteamAppId is { } appId)
                map.TryAdd(appId, g.Id); // first-entry-wins on a duplicate app id — pinned by a test above
        }
        return map;
    }

    /// <summary>The cached variant: which manifest entry (from <see cref="EffectiveManifest.Current"/>)
    /// claims this Steam app id, or null. Same answer as the two-argument overload called with the
    /// current effective manifest, generation-cached so a loop of callers doesn't rebuild the map per
    /// iteration.</summary>
    public static string? BySteamAppId(string? steamAppId)
        => !string.IsNullOrWhiteSpace(steamAppId) && Map.TryGetValue(steamAppId, out var id) ? id : null;
}
