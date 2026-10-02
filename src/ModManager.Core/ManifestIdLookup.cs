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
    // One snapshot per generation. The store maps hold the entries themselves, not their ids, so a store
    // match never goes back through an id lookup that could land on a different entry. ById is for the
    // own-id fallback only, case-insensitive like BanRiskCatalog's id lookups.
    private sealed record Snapshot(
        IReadOnlyDictionary<string, GameManifestEntry> Steam,
        IReadOnlyDictionary<string, GameManifestEntry> Ea,
        IReadOnlyDictionary<string, GameManifestEntry> ById);

    private static Snapshot? _snap;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

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
        var steam = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var ea = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Stores.SteamAppId is { } appId)
                steam.TryAdd(appId, g); // first-entry-wins on a duplicate app id — pinned by a test above
            if (g.Stores.EaContentId is { } contentId)
                ea.TryAdd(contentId, g);
            if (!string.IsNullOrEmpty(g.Id))
                byId.TryAdd(g.Id, g);
        }
        return new Snapshot(steam, ea, byId);
    }

    /// <summary>Which manifest entry (from <see cref="EffectiveManifest.Current"/>) claims this EA app
    /// content id, or null. The EA counterpart of <see cref="BySteamAppId(string?)"/>.</summary>
    public static string? ByEaContentId(string? eaContentId)
        => !string.IsNullOrWhiteSpace(eaContentId) && Maps().Ea.TryGetValue(eaContentId, out var e) ? e.Id : null;

    /// <summary>
    /// The manifest ids this registration is: the entry that claims its Steam app id and the entry that
    /// claims its EA content id, or, when neither store id names an entry, its own id. Case-insensitive,
    /// like <c>BanRiskCatalog</c>'s id lookups. Same precedence as <see cref="EntryFor"/>.
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
        var snap = Maps();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (StoreEntry(snap.Steam, game.SteamAppId) is { } bySteam) ids.Add(bySteam.Id);
        if (StoreEntry(snap.Ea, game.EaContentId) is { } byEa) ids.Add(byEa.Id);
        // The own id only when no store id names a game, the same precedence as EntryFor, so the loader
        // scan and the mod scan never disagree about which game this is.
        if (ids.Count == 0 && !string.IsNullOrEmpty(game.Id)) ids.Add(game.Id);
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
    /// <para><b>An unmatched store id is not a contradiction.</b> A registration whose store id the feed
    /// does not know still joins by its own id, as it always did. The feed correcting an entry's app id,
    /// or a user holding another edition's id, must not strip every correction from that game.</para>
    /// </summary>
    public static GameManifestEntry? EntryFor(GameEntry? game)
    {
        if (game is null) return null;
        var snap = Maps();
        return StoreEntry(snap.Steam, game.SteamAppId)
            ?? StoreEntry(snap.Ea, game.EaContentId)
            ?? (!string.IsNullOrEmpty(game.Id) && snap.ById.TryGetValue(game.Id, out var own) ? own : null);
    }

    /// <summary>The manifest entry claiming this Steam app id, or null. What <c>KnownModPaths</c> reads,
    /// so the add path and the scan path break a tie the same way.</summary>
    public static GameManifestEntry? EntryBySteamAppId(string? steamAppId) => StoreEntry(Maps().Steam, steamAppId);

    private static GameManifestEntry? StoreEntry(IReadOnlyDictionary<string, GameManifestEntry> map, string? storeId)
        => !string.IsNullOrWhiteSpace(storeId) && map.TryGetValue(storeId, out var e) ? e : null;

    /// <summary>The cached variant: which manifest entry (from <see cref="EffectiveManifest.Current"/>)
    /// claims this Steam app id, or null. Same answer as the two-argument overload called with the
    /// current effective manifest, generation-cached so a loop of callers doesn't rebuild the map per
    /// iteration.</summary>
    public static string? BySteamAppId(string? steamAppId)
        => EntryBySteamAppId(steamAppId)?.Id;
}
