using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Live Steam-app-id -> ban-risk level map, a facade over <see cref="EffectiveManifest"/> (twin of
/// <see cref="NexusDomains"/>). Resolving live — not off a persisted GameEntry field — means a feed
/// update that raises a game's risk protects players who already added it, with no migration. An
/// unflagged or unknown app id resolves to <see cref="GameBanRisk.None"/>.
/// </summary>
public static class BanRiskCatalog
{
    private static IReadOnlyDictionary<string, GameBanRisk>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, GameBanRisk> Map
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

    private static IReadOnlyDictionary<string, GameBanRisk> Build()
    {
        var map = new Dictionary<string, GameBanRisk>();
        foreach (var g in EffectiveManifest.Current.Games)
        {
            var level = BanRiskRules.Parse(g.BanRisk);
            if (level != GameBanRisk.None && g.Stores.SteamAppId is { } appId)
                map[appId] = level;
        }
        return map;
    }

    /// <summary>The ban-risk level for a Steam app id, or None when unflagged / unknown / id is null.</summary>
    public static GameBanRisk ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var r) ? r : GameBanRisk.None;
}
