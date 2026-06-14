using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: overlay MO2 facts onto the Ludusavi backbone, keyed by Steam id. Fills modPath
/// (from a non-empty GameDataPath) and nexusDomain (from GameNexusName when present). Does NOT set
/// engine — inferring engine from a mod path is unreliable (e.g. Dark Souls' MO2 data path is "Data"
/// but it's FromSoft, not Bethesda), so engine comes only from curated overrides; the launcher
/// folder-detects the rest at runtime. Adds "mo2" to provenance for every matched entry. Unmatched
/// entries are returned unchanged.</summary>
public static class Mo2Enrich
{
    public static GameManifest Apply(GameManifest backbone, IReadOnlyList<Mo2Game> mo2Games)
    {
        // Index MO2 facts by each Steam id they claim.
        var bySteam = new Dictionary<string, Mo2Game>(StringComparer.Ordinal);
        foreach (var g in mo2Games)
            foreach (var id in g.SteamIds)
                bySteam.TryAdd(id, g);

        var games = backbone.Games.Select(entry =>
        {
            var appId = entry.Stores.SteamAppId;
            if (appId is null || !bySteam.TryGetValue(appId, out var m))
                return entry;

            var modPath = string.IsNullOrEmpty(m.DataPath) ? entry.ModPath : m.DataPath;
            var nexus = entry.NexusDomain ?? (string.IsNullOrWhiteSpace(m.NexusName) ? null : m.NexusName);
            var sources = entry.Provenance.Sources.Contains("mo2")
                ? entry.Provenance.Sources
                : entry.Provenance.Sources.Append("mo2").ToList();

            return entry with
            {
                ModPath = modPath,
                NexusDomain = nexus,                              // engine intentionally untouched (see summary)
                Provenance = entry.Provenance with { Sources = sources },
            };
        }).ToList();

        return backbone with { Games = games };
    }
}
