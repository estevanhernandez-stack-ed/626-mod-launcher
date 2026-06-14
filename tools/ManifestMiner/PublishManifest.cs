using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Shapes the mined manifest for PUBLISHING: tags each entry with the functional facade tag
/// its fields earn (so the launcher's KnownEngines/NexusDomains/PopularGames actually consume feed
/// entries), and drops entries that earn none (the skeletal backbone provides nothing the launcher
/// reads). Mining-source tags (ludusavi/mo2/curated) are preserved for attribution.</summary>
public static class PublishManifest
{
    public static GameManifest ForPublish(GameManifest manifest)
    {
        var kept = new List<GameManifestEntry>();
        foreach (var g in manifest.Games)
        {
            var tags = new List<string>(g.Provenance.Sources);
            void Add(string t) { if (!tags.Contains(t)) tags.Add(t); }

            if (g.Engine is not null) Add(ManifestSources.KnownEngines);
            if (g.NexusDomain is not null) Add(ManifestSources.NexusDomains);
            if (g.Engine is not null && g.ModPath is not null && g.Featured is not null) Add(ManifestSources.PopularGames);

            var earned = tags.Contains(ManifestSources.KnownEngines)
                         || tags.Contains(ManifestSources.NexusDomains)
                         || tags.Contains(ManifestSources.PopularGames);
            if (!earned) continue; // skeletal — nothing the facades read

            kept.Add(g with { Provenance = g.Provenance with { Sources = tags } });
        }
        return manifest with { Games = kept };
    }
}
