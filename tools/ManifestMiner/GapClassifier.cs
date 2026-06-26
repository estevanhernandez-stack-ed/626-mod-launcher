using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>The curation backlog, classified. <c>EngineCurated</c> ship via known-engines/popular-games;
/// <c>NexusOnly</c> ship via nexus-domains (engine folder-detected at runtime) and are the engine-upgrade
/// candidates; <c>Skeletal</c> earn no publish tag (no engine, no nexusDomain) and need full curation.</summary>
public sealed record GapReport(
    IReadOnlyList<GameManifestEntry> EngineCurated,
    IReadOnlyList<GameManifestEntry> NexusOnly,
    IReadOnlyList<GameManifestEntry> Skeletal);

/// <summary>Pure classifier over the enriched draft — the deterministic curation backlog feeding the
/// growth pipeline. No I/O.</summary>
public static class GapClassifier
{
    public static GapReport Classify(IReadOnlyList<GameManifestEntry> games) => new(
        games.Where(g => !string.IsNullOrWhiteSpace(g.Engine)).ToList(),
        games.Where(g => string.IsNullOrWhiteSpace(g.Engine) && !string.IsNullOrWhiteSpace(g.NexusDomain)).ToList(),
        games.Where(g => string.IsNullOrWhiteSpace(g.Engine) && string.IsNullOrWhiteSpace(g.NexusDomain)).ToList());
}
