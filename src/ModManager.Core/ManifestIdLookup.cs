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
    // One snapshot per generation: both store maps and the entries themselves, so a caller that resolves
    // a store id and then reads the entry it names never mixes two feeds.
    private sealed record Snapshot(
        IReadOnlyDictionary<string, string> Steam,
        IReadOnlyDictionary<string, string> Ea,
        IReadOnlyDictionary<string, GameManifestEntry> ById);

    private static Snapshot? _snap;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map => Maps().Steam;

    private static Snapshot Maps()
    {
        lock (_gate)
        {
            var gen = EffectiveManifest.Generation;
            if (_snap is null || _mapGen != gen)
            {
                _snap = Build();
                _mapGen = gen;
            }
            return _snap;
        }
    }

    private static Snapshot Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ea = new Dictionary<string, string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Stores.SteamAppId is { } appId)
                map.TryAdd(appId, g.Id); // first-entry-wins on a duplicate app id — pinned by a test above
            if (g.Stores.EaContentId is { } contentId)
                ea.TryAdd(contentId, g.Id);
            if (!string.IsNullOrEmpty(g.Id))
                byId.TryAdd(g.Id, g);
        }
        return new Snapshot(map, ea, byId);
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
        // One snapshot of both maps, so the Steam and EA answers come from the same feed generation.
        var (steam, ea, _) = Maps();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(game.Id)) ids.Add(game.Id);
        if (!string.IsNullOrWhiteSpace(game.SteamAppId) && steam.TryGetValue(game.SteamAppId, out var bySteam)) ids.Add(bySteam);
        if (!string.IsNullOrWhiteSpace(game.EaContentId) && ea.TryGetValue(game.EaContentId, out var byEa)) ids.Add(byEa);
        return ids;
    }

    /// <summary>
    /// The one manifest entry that describes this registration, or null. This is the join that lets a
    /// manifest correction (file extensions, grouping, mod path) reach a game the user already added.
    ///
    /// <para><b>Store identity first.</b> The entry claiming the registration's Steam app id, else the
    /// one claiming its EA content id, else the entry with its own id. A store id names exactly one
    /// game; the own id is only as good as however it was made. A second store copy is <c>&lt;id&gt;-2</c>
    /// and an older registration is a slug of its display name, which can collide with a different
    /// game's manifest id ("doom" for Doom Eternal).</para>
    ///
    /// <para><b>A contradiction is no match.</b> When the own id names an entry that claims a different
    /// id on a store the registration also carries, the two are different games, and null is the
    /// honest answer. A correction from the wrong game would point the scan, and intake, at a folder
    /// this game does not use.</para>
    /// </summary>
    public static GameManifestEntry? EntryFor(GameEntry? game)
    {
        if (game is null) return null;
        var snap = Maps();

        if (!string.IsNullOrWhiteSpace(game.SteamAppId)
            && snap.Steam.TryGetValue(game.SteamAppId, out var bySteam)
            && snap.ById.TryGetValue(bySteam, out var steamEntry))
            return steamEntry;
        if (!string.IsNullOrWhiteSpace(game.EaContentId)
            && snap.Ea.TryGetValue(game.EaContentId, out var byEa)
            && snap.ById.TryGetValue(byEa, out var eaEntry))
            return eaEntry;

        if (string.IsNullOrEmpty(game.Id) || !snap.ById.TryGetValue(game.Id, out var own)) return null;
        if (Contradicts(game.SteamAppId, own.Stores.SteamAppId)) return null;
        if (Contradicts(game.EaContentId, own.Stores.EaContentId)) return null;
        return own;
    }

    private static bool Contradicts(string? registered, string? claimed)
        => !string.IsNullOrWhiteSpace(registered) && !string.IsNullOrWhiteSpace(claimed)
           && !string.Equals(registered, claimed, StringComparison.Ordinal);

    /// <summary>The cached variant: which manifest entry (from <see cref="EffectiveManifest.Current"/>)
    /// claims this Steam app id, or null. Same answer as the two-argument overload called with the
    /// current effective manifest, generation-cached so a loop of callers doesn't rebuild the map per
    /// iteration.</summary>
    public static string? BySteamAppId(string? steamAppId)
        => !string.IsNullOrWhiteSpace(steamAppId) && Map.TryGetValue(steamAppId, out var id) ? id : null;
}
