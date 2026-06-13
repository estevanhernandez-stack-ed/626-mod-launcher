using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// One curated quick-pick game. Picking it in the Add Game wizard pre-fills the engine, mod
/// folder, and Steam App ID. <see cref="Engine"/> is an <see cref="EnginePresets.Presets"/> key.
/// <see cref="FileExtensions"/> is an optional override for games whose engine preset's default
/// extensions don't match (e.g. Cyberpunk's "custom" engine ships .pak, but its mods are .archive).
/// </summary>
public sealed record PopularGame(
    string Id,
    string Name,
    string Engine,
    string ModPath,
    string SteamAppId)
{
    public IReadOnlyList<string>? FileExtensions { get; init; }
}

/// <summary>
/// Curated catalog of popular moddable games for the Add Game wizard's quick-pick. Facade over
/// <see cref="EmbeddedGameManifest"/>: projects entries tagged with the "popular-games" provenance
/// source, ordered by their <see cref="GameManifestEntry.Featured"/> rank. The list order is
/// intentional and asserted by tests.
/// </summary>
public static class PopularGames
{
    public static IReadOnlyList<PopularGame> All { get; } = Build();

    private static IReadOnlyList<PopularGame> Build()
        => EmbeddedGameManifest.Current.Games
            .Where(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames))
            .OrderBy(g => g.Featured ?? int.MaxValue)
            .Select(g => new PopularGame(g.Id, g.Name, g.Engine!, g.ModPath!, g.Stores.SteamAppId!)
            {
                FileExtensions = g.FileExtensions,
            })
            .ToList();

    /// <summary>Look up a game by id; null when unknown (or the id is null/empty).</summary>
    public static PopularGame? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(g => g.Id == id);
}
