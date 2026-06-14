using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: apply curated overrides onto the (backbone + enriched) manifest, keyed by Steam id.
/// Overrides WIN — any field the override specifies replaces the mined value; unspecified fields are
/// left intact. An override whose Steam id isn't present adds a new entry. Matched/added entries gain
/// the "curated" provenance source + status.</summary>
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
            if (string.IsNullOrWhiteSpace(ov.SteamAppId)) continue;

            if (idBySteam.TryGetValue(ov.SteamAppId, out var existingId))
            {
                byId[existingId] = ApplyTo(byId[existingId], ov);
            }
            else
            {
                var id = !string.IsNullOrWhiteSpace(ov.Id) ? ov.Id! : EnginePresets.Slugify(ov.Name);
                if (byId.ContainsKey(id)) id = $"{id}-{ov.SteamAppId}"; // avoid slug collision
                byId[id] = NewFrom(id, ov);
                order.Add(id);
                idBySteam[ov.SteamAppId] = id;
            }
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
