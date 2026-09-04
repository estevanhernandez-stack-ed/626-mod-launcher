using System.Threading;

namespace ModManager.Core.Manifest;

/// <summary>
/// Produces the effective game manifest: the embedded snapshot overlaid with a verified remote
/// manifest (when one has been set). The facades read <see cref="Current"/>. <see cref="SetRemote"/>
/// is called once at startup by the App layer after it loads + verifies a cached remote manifest;
/// until then the remote is null and Current == the embedded manifest, so behavior is unchanged.
/// </summary>
public static class EffectiveManifest
{
    // Reference assignment is atomic; volatile gives cross-thread visibility. SetRemote is a
    // startup-time, single-writer operation in production; reads are lock-free.
    private static volatile GameManifest? _remote;
    private static int _generation;

    /// <summary>Monotonic counter; advances on every <see cref="SetRemote"/>. Consumers cache by it.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>The embedded manifest overlaid with the current remote (if any).</summary>
    public static GameManifest Current => Merge(EmbeddedGameManifest.Current, _remote);

    /// <summary>Set (or clear, with null) the verified remote manifest. Bumps <see cref="Generation"/>.</summary>
    public static void SetRemote(GameManifest? remote)
    {
        _remote = remote;
        Interlocked.Increment(ref _generation);
    }

    public static GameManifest Merge(GameManifest embedded, GameManifest? remote)
    {
        if (remote is null)
            return embedded;

        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in embedded.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }
        foreach (var g in remote.Games) // new ids appended in remote order; collisions field-merge (below)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = MergeEntry(byId[g.Id], g);
        }

        // A Steam app id names the GAME; an id names an ENTRY. Merging on the id alone let the feed
        // and the snapshot describe one game twice under two ids -- Skyrim Special Edition was
        // skyrim-se in the snapshot and the-elder-scrolls-v-skyrim-special-edition in the feed. The
        // quick-pick then offered it twice under two names, and whichever the user happened to pick
        // decided the id their install carried. Landing on the snapshot's stale alias meant every
        // later feed correction for that game -- ban risk, save layout, nexus domain -- attached to
        // an id they did not have, silently and permanently. So: where the feed names a game the
        // snapshot also names, the feed's id wins, and what only the snapshot knew folds into it.
        var remoteIdByApp = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in remote.Games)
            if (g.Stores.SteamAppId is { Length: > 0 } appId) remoteIdByApp.TryAdd(appId, g.Id);

        foreach (var g in embedded.Games)
        {
            if (g.Stores.SteamAppId is not { Length: > 0 } app) continue;      // nothing to collide on
            if (!remoteIdByApp.TryGetValue(app, out var winner)) continue;     // the feed never names it
            if (winner == g.Id || !byId.ContainsKey(g.Id)) continue;           // same entry, or already folded

            // Folded, not dropped. MergeEntry keeps the FIRST argument's id, so the remote id is
            // reasserted explicitly -- the whole point is that the feed's id is the surviving one.
            byId[winner] = MergeEntry(byId[g.Id], byId[winner]) with { Id = winner };
            byId.Remove(g.Id);
            order.Remove(g.Id);
        }

        return embedded with { Games = order.Select(id => byId[id]).ToList() };
    }

    /// <summary>
    /// Field-merge a remote entry onto the embedded one they share an id with. The feed UPDATES but
    /// never DOWNGRADES: a remote non-null value wins (so the feed can correct a built-in), but a
    /// remote null can never blank a field the built-in had. Provenance sources union (so functional
    /// facade tags from the curated baseline — e.g. <c>popular-games</c> — survive a feed collision),
    /// and a curated status is never downgraded to auto. Prevents an auto-mined feed entry from
    /// silently wiping a curated built-in out of a facade (the Stardew quick-pick regression).
    /// </summary>
    private static GameManifestEntry MergeEntry(GameManifestEntry embedded, GameManifestEntry remote)
    {
        // Union provenance sources, embedded order first, then any new from remote.
        var sources = new List<string>(embedded.Provenance.Sources);
        foreach (var s in remote.Provenance.Sources)
            if (!sources.Contains(s)) sources.Add(s);

        // Never downgrade trust: if either side is curated, the merged entry is curated.
        var status = embedded.Provenance.Status == "curated" || remote.Provenance.Status == "curated"
            ? "curated"
            : remote.Provenance.Status;

        return embedded with
        {
            Name = Prefer(remote.Name, embedded.Name),
            Engine = remote.Engine ?? embedded.Engine,
            Stores = MergeStores(embedded.Stores, remote.Stores),
            NexusDomain = remote.NexusDomain ?? embedded.NexusDomain,
            CurseforgeGameId = remote.CurseforgeGameId ?? embedded.CurseforgeGameId,
            ModPath = remote.ModPath ?? embedded.ModPath,
            SaveDirHint = remote.SaveDirHint ?? embedded.SaveDirHint,
            FileExtensions = remote.FileExtensions ?? embedded.FileExtensions,
            GroupingRule = remote.GroupingRule ?? embedded.GroupingRule,
            Featured = remote.Featured ?? embedded.Featured,
            BanRisk = BanRiskRules.MaxString(embedded.BanRisk, remote.BanRisk),
            // Added late, and forgotten here when they were - so for the three ids the feed and the
            // snapshot BOTH carry, the remote value was silently dropped. Those three were
            // elden-ring, dark-souls-iii and palworld: a game is in the embedded snapshot BECAUSE it
            // matters, so the bug fell exclusively on the games where a safe route matters most.
            // EveryOptionalFieldMerges pins this shape for whatever gets added next.
            SaveLayout = remote.SaveLayout ?? embedded.SaveLayout,
            SavePlayerPaths = remote.SavePlayerPaths ?? embedded.SavePlayerPaths,
            SafeRoute = remote.SafeRoute ?? embedded.SafeRoute,
            SafeRouteHint = remote.SafeRouteHint ?? embedded.SafeRouteHint,
            Provenance = embedded.Provenance with { Sources = sources, Status = status },
        };
    }

    private static StoreIds MergeStores(StoreIds embedded, StoreIds remote) => new()
    {
        SteamAppId = remote.SteamAppId ?? embedded.SteamAppId,
        GogId = remote.GogId ?? embedded.GogId,
        EpicAppName = remote.EpicAppName ?? embedded.EpicAppName,
        XboxStoreId = remote.XboxStoreId ?? embedded.XboxStoreId,
    };

    // Non-empty remote string wins; otherwise keep the embedded one (Name is non-nullable).
    private static string Prefer(string remote, string embedded)
        => string.IsNullOrEmpty(remote) ? embedded : remote;
}
