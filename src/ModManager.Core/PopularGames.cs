using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// One curated quick-pick game. Picking it in the Add Game wizard pre-fills the engine, mod
/// folder, and Steam App ID. <see cref="Engine"/> is an <see cref="EnginePresets.Presets"/> key.
/// <see cref="SteamAppId"/> is null for a game not sold on Steam.
/// <see cref="FileExtensions"/> is an optional override for games whose engine preset's default
/// extensions don't match (e.g. Cyberpunk's "custom" engine ships .pak, but its mods are .archive).
/// </summary>
public sealed record PopularGame(
    string Id,
    string Name,
    string Engine,
    string ModPath,
    string? SteamAppId)
{
    public IReadOnlyList<string>? FileExtensions { get; init; }
}

/// <summary>
/// Curated catalog of moddable games for the Add Game wizard's quick-pick. Facade over
/// <see cref="EmbeddedGameManifest"/>: projects every entry with an engine and a mod path, ordered
/// by their <see cref="GameManifestEntry.Featured"/> rank. The list order is intentional and
/// asserted by tests.
/// </summary>
public static class PopularGames
{
    private static IReadOnlyList<PopularGame>? _all;
    private static int _allGen = -1;
    private static readonly object _gate = new();

    public static IReadOnlyList<PopularGame> All
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_all is null || _allGen != gen)
                {
                    _all = Build();
                    _allGen = gen;
                }
                return _all;
            }
        }
    }

    private static IReadOnlyList<PopularGame> Build()
        // Every entry the projection can actually represent, not just the legacy-tagged 18. The tag
        // reproduced a hand-written array, so a newly curated game stayed invisible in the one surface
        // built for finding curated games — and for a game sold outside Steam, where no detection
        // exists, that was the whole of the user's experience of it.
        //
        // Engine and ModPath are the real gate, and not for tidiness: they are non-nullable on
        // PopularGame, so an entry missing either could only be projected by inventing a value.
        // Inventing a mod path is how files land somewhere a loader never looks.
        => EffectiveManifest.Current.Games
            .Where(g => g.Engine is not null && g.ModPath is not null)
            .OrderBy(g => g.Featured ?? int.MaxValue)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new PopularGame(g.Id, g.Name, g.Engine!, g.ModPath!, g.Stores.SteamAppId)
            {
                FileExtensions = g.FileExtensions,
            })
            .ToList();

    /// <summary>Look up a game by id; null when unknown (or the id is null/empty).</summary>
    public static PopularGame? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(g => g.Id == id);
}
