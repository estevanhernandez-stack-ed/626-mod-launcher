using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Ban-risk catalog resolved by both Steam app id and manifest id. A facade over
/// <see cref="EffectiveManifest"/> (twin of <see cref="NexusDomains"/>). Resolving live — not off a
/// persisted GameEntry field — means a feed update that raises a game's risk protects players who
/// already added it, with no migration. An unflagged or unknown id resolves to
/// <see cref="GameBanRisk.None"/>.
/// </summary>
public static class BanRiskCatalog
{
    private sealed record Maps(IReadOnlyDictionary<string, GameBanRisk> ByAppId,
                               IReadOnlyDictionary<string, GameBanRisk> ById);

    private static Maps? _maps;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    // The launcher's own write features ship with their protection. Keyed by manifest id AND Steam
    // app id, so a Steam copy registered under an older id still hits. Only games the launcher itself
    // writes files for belong here; every other game's risk stays data in the feed. Raise-only: a feed
    // can never lower these, and removing one is a code change and a release.
    private static readonly IReadOnlyDictionary<string, GameBanRisk> FloorById =
        new Dictionary<string, GameBanRisk>(StringComparer.OrdinalIgnoreCase)
        {
            ["ea-sports-college-football-27"] = GameBanRisk.High,
            ["madden-nfl-27"] = GameBanRisk.High,
        };

    private static readonly IReadOnlyDictionary<string, GameBanRisk> FloorByAppId =
        new Dictionary<string, GameBanRisk>(StringComparer.Ordinal)
        {
            ["4032350"] = GameBanRisk.High, // EA Sports College Football 27
            ["3940610"] = GameBanRisk.High, // Madden NFL 27
        };

    private static Maps Current
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_maps is null || _mapGen != gen)
                {
                    _maps = Build();
                    _mapGen = gen;
                }
                return _maps;
            }
        }
    }

    private static Maps Build()
    {
        var byAppId = new Dictionary<string, GameBanRisk>(StringComparer.Ordinal);
        var byId = new Dictionary<string, GameBanRisk>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            var level = BanRiskRules.Parse(g.BanRisk);
            if (level == GameBanRisk.None) continue;
            if (g.Stores.SteamAppId is { } appId) byAppId[appId] = level;
            if (!string.IsNullOrEmpty(g.Id)) byId[g.Id] = level;
        }
        return new Maps(byAppId, byId);
    }

    /// <summary>The ban-risk level for a Steam app id, or None when unflagged / unknown / id is null.
    /// Callers outside Core use <see cref="Effective"/>; a source-scan test enforces it.</summary>
    public static GameBanRisk ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Current.ByAppId.TryGetValue(steamAppId, out var r) ? r : GameBanRisk.None;

    /// <summary>The ban risk the launcher acts on for this game: the highest of what the feed says by
    /// Steam app id, what it says by manifest id, and the compiled floor. Highest wins, so no single
    /// source can lower another. A game with no Steam id (EA app, Xbox, a folder) still resolves.</summary>
    public static GameBanRisk Effective(GameEntry game)
    {
        var level = ByAppId(game.SteamAppId);
        if (!string.IsNullOrEmpty(game.Id))
        {
            if (Current.ById.TryGetValue(game.Id, out var byId)) level = BanRiskRules.Max(level, byId);
            if (FloorById.TryGetValue(game.Id, out var floorId)) level = BanRiskRules.Max(level, floorId);
        }
        if (!string.IsNullOrEmpty(game.SteamAppId) && FloorByAppId.TryGetValue(game.SteamAppId, out var floorApp))
            level = BanRiskRules.Max(level, floorApp);
        return level;
    }
}
