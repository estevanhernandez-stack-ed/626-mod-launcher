using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>The result of merging curated overrides onto a manifest: the merged manifest, plus which
/// overrides MATCHED an existing entry versus which ADDED a new one. A curator reading the miner's
/// summary line needs that split to catch slug drift — an override meant to update a mined game that
/// instead slugifies to a different id silently adds a near-duplicate row, and "added: &lt;a game
/// already mined&gt;" is the only thing that makes that visible.</summary>
public sealed record OverridesMergeResult(
    GameManifest Manifest,
    IReadOnlyList<string> MatchedIds,
    IReadOnlyList<string> AddedIds);

/// <summary>Pure: apply curated overrides onto the (backbone + enriched) manifest, keyed by Steam id
/// where there is one and by slug otherwise. Overrides WIN — any field the override specifies replaces
/// the mined value; unspecified fields are left intact. An override that matches nothing adds a new
/// entry. Matched/added entries gain the "curated" provenance source + status.</summary>
public static class OverridesMerge
{
    /// <summary>Back-compat surface for callers that only need the merged manifest.</summary>
    public static GameManifest Apply(GameManifest manifest, IReadOnlyList<OverrideEntry> overrides)
        => ApplyReporting(manifest, overrides).Manifest;

    public static OverridesMergeResult ApplyReporting(GameManifest manifest, IReadOnlyList<OverrideEntry> overrides)
    {
        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in manifest.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }

        // Index existing entries by Steam id for override matching.
        var idBySteam = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in manifest.Games)
            if (g.Stores.SteamAppId is { } s) idBySteam.TryAdd(s, g.Id);

        var matched = new List<string>();
        var added = new List<string>();

        foreach (var ov in overrides)
        {
            // Steam id first, so all 149 existing files keep matching exactly as they did.
            string? existingId = null;
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId))
            {
                // A Steam-keyed override that does NOT resolve falls through to the add path below,
                // which is what it did before slug-keying existed. It must never try the slug: its slug
                // can coincide with an unrelated game's id, and merging into that game would be a
                // silent, wrong overwrite - the exact regression this shape exists to avoid.
                idBySteam.TryGetValue(ov.SteamAppId!, out existingId);
            }
            else if (OverridesValidate.KeyOf(ov) is { Length: > 0 } slug && byId.ContainsKey(slug))
            {
                // The only key a game bought outside Steam has.
                existingId = slug;
            }

            if (existingId is not null)
            {
                byId[existingId] = ApplyTo(byId[existingId], ov);
                matched.Add(existingId);
                continue;
            }

            var id = OverridesValidate.KeyOf(ov);
            // A Steam-keyed override with neither an explicit id nor a name, that also fails to
            // resolve to an existing game, drops here silently. OverridesValidate's "no usable key"
            // rule does NOT catch this case - it only fires when the Steam id is ALSO blank, so a
            // present-but-unmatched Steam id slips past that gate.
            if (id.Length == 0) continue;
            if (byId.ContainsKey(id) && !string.IsNullOrWhiteSpace(ov.SteamAppId))
                id = $"{id}-{ov.SteamAppId}";          // slug taken by a different game
            byId[id] = NewFrom(id, ov);
            order.Add(id);
            added.Add(id);
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId)) idBySteam[ov.SteamAppId!] = id;
        }

        var merged = manifest with { Games = order.Select(id => byId[id]).ToList() };
        return new OverridesMergeResult(merged, matched, added);
    }

    private static GameManifestEntry ApplyTo(GameManifestEntry e, OverrideEntry ov) => e with
    {
        Name = ov.Name ?? e.Name,
        Engine = ov.Engine ?? e.Engine,
        ModPath = ov.ModPath ?? e.ModPath,
        NexusDomain = ov.NexusDomain ?? e.NexusDomain,
        Featured = ov.Featured ?? e.Featured,
        BanRisk = ov.BanRisk ?? e.BanRisk,
        SaveLayout = ov.SaveLayout ?? e.SaveLayout,
        SavePlayerPaths = ov.SavePlayerPaths ?? e.SavePlayerPaths,
        SafeRoute = ov.SafeRoute ?? e.SafeRoute,
        SafeRouteHint = ov.SafeRouteHint ?? e.SafeRouteHint,
        SaveDirHint = ov.SaveDirHint ?? e.SaveDirHint,
        FileExtensions = ov.FileExtensions ?? e.FileExtensions,
        Provenance = Curate(e.Provenance),
    };

    private static GameManifestEntry NewFrom(string id, OverrideEntry ov) => new()
    {
        Id = id,
        Name = ov.Name ?? id,
        Engine = ov.Engine,
        ModPath = ov.ModPath,
        NexusDomain = ov.NexusDomain,
        Featured = ov.Featured,
        BanRisk = ov.BanRisk,
        SaveLayout = ov.SaveLayout,
        SavePlayerPaths = ov.SavePlayerPaths,
        SafeRoute = ov.SafeRoute,
        SafeRouteHint = ov.SafeRouteHint,
        SaveDirHint = ov.SaveDirHint,
        FileExtensions = ov.FileExtensions,
        Stores = new StoreIds { SteamAppId = ov.SteamAppId },
        Provenance = new ManifestProvenance { Sources = new[] { "curated" }, Status = "curated" },
    };

    private static ManifestProvenance Curate(ManifestProvenance p)
    {
        var sources = p.Sources.Contains("curated") ? p.Sources : p.Sources.Append("curated").ToList();
        return p with { Sources = sources, Status = "curated" };
    }
}
