using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: apply curated overrides onto the (backbone + enriched) manifest, keyed by Steam id
/// where there is one and by slug otherwise. Overrides WIN — any field the override specifies replaces
/// the mined value; unspecified fields are left intact. An override that matches nothing adds a new
/// entry. Matched/added entries gain the "curated" provenance source + status.</summary>
public static class OverridesMerge
{
    public static GameManifest Apply(GameManifest manifest, IReadOnlyList<OverrideEntry> overrides)
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

        foreach (var ov in overrides)
        {
            // Steam id first, so all 149 existing files keep matching exactly as they did. Slug second,
            // which is the only key a game bought outside Steam has.
            string? existingId = null;
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId) && idBySteam.TryGetValue(ov.SteamAppId!, out var bySteam))
                existingId = bySteam;
            else if (OverridesValidate.KeyOf(ov) is { Length: > 0 } slug && byId.ContainsKey(slug))
                existingId = slug;

            if (existingId is not null)
            {
                byId[existingId] = ApplyTo(byId[existingId], ov);
                continue;
            }

            var id = OverridesValidate.KeyOf(ov);
            if (id.Length == 0) continue;              // unaddressable; OverridesValidate reports it
            if (byId.ContainsKey(id) && !string.IsNullOrWhiteSpace(ov.SteamAppId))
                id = $"{id}-{ov.SteamAppId}";          // slug taken by a different game
            byId[id] = NewFrom(id, ov);
            order.Add(id);
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId)) idBySteam[ov.SteamAppId!] = id;
        }

        return manifest with { Games = order.Select(id => byId[id]).ToList() };
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
