using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: overlay MO2 facts onto the Ludusavi backbone, keyed by Steam id. Fills modPath
/// (from a non-empty GameDataPath), engine (only when the path is unambiguous), and nexusDomain
/// (from GameNexusName when present). Adds "mo2" to provenance for every matched entry. Unmatched
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
            var engine = entry.Engine ?? EngineFromModPath.Infer(m.DataPath);
            var nexus = entry.NexusDomain ?? (string.IsNullOrWhiteSpace(m.NexusName) ? null : m.NexusName);
            var sources = entry.Provenance.Sources.Contains("mo2")
                ? entry.Provenance.Sources
                : entry.Provenance.Sources.Append("mo2").ToList();

            return entry with
            {
                ModPath = modPath,
                Engine = engine,
                NexusDomain = nexus,
                Provenance = entry.Provenance with { Sources = sources },
            };
        }).ToList();

        return backbone with { Games = games };
    }
}
