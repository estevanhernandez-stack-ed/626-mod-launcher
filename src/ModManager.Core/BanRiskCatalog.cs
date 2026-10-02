using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Ban-risk catalog resolved by Steam app id, manifest id and EA app content id. A registration is
/// matched by its own id and also by the manifest entry its EA content id names, because a second store
/// copy of a game is registered as <c>&lt;id&gt;-2</c> (see <see cref="Effective"/>). A facade over
/// <see cref="EffectiveManifest"/> (twin of <see cref="NexusDomains"/>). Resolving live — not off a
/// persisted GameEntry field — means a feed update that raises a game's risk protects players who
/// already added it, with no migration. An unflagged or unknown id resolves to
/// <see cref="GameBanRisk.None"/>.
/// </summary>
public static class BanRiskCatalog
{
    private sealed record Maps(IReadOnlyDictionary<string, GameBanRisk> ByAppId,
                               IReadOnlyDictionary<string, GameBanRisk> ById,
                               IReadOnlyDictionary<string, string> IdByEaContentId);

    private static Maps? _maps;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    // The launcher's own write features ship with their protection. Keyed by manifest id, Steam app id
    // AND EA content id (FloorByEaContentId below), so a Steam copy registered under an older id and an
    // EA copy registered as "<id>-2" both still hit. Only games the launcher itself
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

    // The same floor by EA app content id, so it holds for an EA install whatever its registration id
    // is: a second store copy is renamed "<id>-2", and with no feed the embedded snapshot has no entry
    // to map the content id to. Content ids as read from both installs' installerdata.xml (EA football
    // grand plan, K3). Whether a title keeps one content id across patches, regions and editions is
    // still open there (K19). It matters less than it looks: EA discovery registers an install only
    // when its content id matches a manifest entry, so a registration carries a content id the feed
    // named, and the feed's own risk for that entry applies through IdByEaContentId. If K19 turns up a
    // second id per title, add it here: the floor is code by design.
    private static readonly IReadOnlyDictionary<string, GameBanRisk> FloorByEaContentId =
        new Dictionary<string, GameBanRisk>(StringComparer.Ordinal)
        {
            ["16425899"] = GameBanRisk.High, // EA Sports College Football 27
            ["16425895"] = GameBanRisk.High, // Madden NFL 27
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
        var idByEa = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            // Every entry with an EA content id, flagged or not: the floor is keyed by manifest id, so
            // an unflagged feed entry can still name a floor game.
            if (g.Stores.EaContentId is { Length: > 0 } contentId && !string.IsNullOrEmpty(g.Id))
                idByEa.TryAdd(contentId, g.Id);

            var level = BanRiskRules.Parse(g.BanRisk);
            if (level == GameBanRisk.None) continue;
            if (g.Stores.SteamAppId is { } appId) byAppId[appId] = level;
            if (!string.IsNullOrEmpty(g.Id)) byId[g.Id] = level;
        }
        return new Maps(byAppId, byId, idByEa);
    }

    /// <summary>The ban-risk level for a Steam app id, or None when unflagged / unknown / id is null.
    /// Callers outside Core use <see cref="Effective"/>; a source-scan test enforces it.</summary>
    public static GameBanRisk ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Current.ByAppId.TryGetValue(steamAppId, out var r) ? r : GameBanRisk.None;

    /// <summary>
    /// The ban risk the launcher acts on for this game: the highest of what the feed says by Steam app
    /// id, what it says by manifest id, and the compiled floor (by manifest id, Steam app id and EA
    /// content id). Highest wins, so no single source can lower another.
    ///
    /// <para><b>Manifest id means the registration's own id AND the entry its EA content id names.</b>
    /// A second store copy of a game the user already has is registered as <c>&lt;id&gt;-2</c>, so the
    /// EA copy of a dual-store ban-risk game used to read None: no warning, no prompt, and an agent free
    /// to enable mods. It still carries its EA content id, and that names the game. A Steam id needs no
    /// such step: <c>ByAppId</c> and the Steam floor already answer for the entry that claims it.
    /// Nothing here trims a suffix: a registration with no store identity matches only by its own id.</para>
    ///
    /// <para><b>One snapshot answers the whole call.</b> The EA mapping lives in the same generation-cached
    /// maps as the risks, so a feed update landing mid-call can never pair one generation's id
    /// resolution with another's risk table and let a flagged game through.</para>
    /// </summary>
    public static GameBanRisk Effective(GameEntry game)
    {
        // One snapshot of Current for the whole call, so a concurrent feed update (generation bump)
        // between the Steam-id read and the id read can never mix answers from two generations.
        var maps = Current;
        var level = !string.IsNullOrEmpty(game.SteamAppId) && maps.ByAppId.TryGetValue(game.SteamAppId, out var byApp)
            ? byApp
            : GameBanRisk.None;
        level = BanRiskRules.Max(level, ByManifestId(maps, game.Id));
        if (!string.IsNullOrEmpty(game.EaContentId) && maps.IdByEaContentId.TryGetValue(game.EaContentId, out var eaId))
            level = BanRiskRules.Max(level, ByManifestId(maps, eaId));
        if (!string.IsNullOrEmpty(game.SteamAppId) && FloorByAppId.TryGetValue(game.SteamAppId, out var floorApp))
            level = BanRiskRules.Max(level, floorApp);
        if (!string.IsNullOrEmpty(game.EaContentId) && FloorByEaContentId.TryGetValue(game.EaContentId, out var floorEa))
            level = BanRiskRules.Max(level, floorEa);
        return level;
    }

    // The feed's risk and the floor for one manifest id, from the given snapshot.
    private static GameBanRisk ByManifestId(Maps maps, string? id)
    {
        if (string.IsNullOrEmpty(id)) return GameBanRisk.None;
        var level = maps.ById.TryGetValue(id, out var byId) ? byId : GameBanRisk.None;
        return FloorById.TryGetValue(id, out var floor) ? BanRiskRules.Max(level, floor) : level;
    }
}
