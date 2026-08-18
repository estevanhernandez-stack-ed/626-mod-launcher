using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID -> mod folder, straight off the manifest's per-game <c>modPath</c>.
///
/// <para>The engine preset knows where a TYPICAL game of that engine keeps its mods; it cannot know
/// where THIS one does. Unreal games prefix the project name (<c>Pal/Content/Paks/~mods</c>,
/// <c>Phoenix/Content/Paks/~mods</c>), RE Engine games use <c>reframework/autorun</c>, Cyberpunk uses
/// <c>archive/pc/mod</c> — none of which any preset can derive. <see cref="GameManifest.ModPath"/> is
/// documented as "override to the engine-default mod folder", and this is the facade that lets a
/// caller honour that instead of silently taking the preset.</para>
///
/// <para>Deliberately NOT provenance-filtered, unlike <see cref="KnownEngines"/>. That facade narrows
/// to one legacy array to keep its membership identical to what it always was; a mod path has no such
/// history. Any manifest entry that states one means it.</para>
/// </summary>
public static class KnownModPaths
{
    private static IReadOnlyDictionary<string, string>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_map is null || _mapGen != gen)
                {
                    _map = Build();
                    _mapGen = gen;
                }
                return _map;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Stores.SteamAppId is { } appId && !string.IsNullOrWhiteSpace(g.ModPath))
                map[appId] = g.ModPath!;
        }
        return map;
    }

    /// <summary>The curated mod folder for a Steam app id, or null when the manifest states none.</summary>
    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var p) ? p : null;

    /// <summary>The curated mod folder for a manifest game id, or null. Used by the refresh path,
    /// which keys on game id rather than app id because a registration always has one and may not
    /// have the other.</summary>
    public static string? ById(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return null;
        var g = EffectiveManifest.Current.Games.FirstOrDefault(x => x.Id == gameId);
        return string.IsNullOrWhiteSpace(g?.ModPath) ? null : g!.ModPath;
    }
}
