using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: Ludusavi facts -> GameManifestEntry candidates. Steam-id-keyed (our only probe
/// today); engine/modPath/nexusDomain stay null (Ludusavi carries none). Output is a draft for
/// review, not a shipped manifest.</summary>
public static class LudusaviNormalize
{
    public static IReadOnlyList<GameManifestEntry> ToCandidates(IReadOnlyList<LudusaviGame> games)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GameManifestEntry>();

        foreach (var g in games)
        {
            if (string.IsNullOrWhiteSpace(g.SteamAppId)) continue; // need a Steam id to key/verify

            var baseId = EnginePresets.Slugify(g.Name);
            var id = baseId;
            if (!seen.Add(id)) { id = $"{baseId}-{g.SteamAppId}"; seen.Add(id); }

            result.Add(new GameManifestEntry
            {
                Id = id,
                Name = g.Name,
                Stores = new StoreIds { SteamAppId = g.SteamAppId },
                SaveDirHint = g.SavePaths.Count > 0 ? g.SavePaths[0] : null,
                Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
            });
        }

        return result;
    }
}
