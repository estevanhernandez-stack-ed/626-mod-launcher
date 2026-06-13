using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID -> engine map. The reliable signal for games whose install folder carries
/// no detectable signature — notably FromSoftware's proprietary engine (Elden Ring et al.), where
/// only the app id tells you it's a Mod Engine 2 game. Checked before folder heuristics.
///
/// Facade over <see cref="EffectiveManifest"/>: reads only entries tagged with the
/// "known-engines" provenance source, so its membership is exactly what it always was even though
/// the manifest is a union of three legacy arrays. With no remote set, the effective manifest equals
/// the embedded snapshot, so behavior is unchanged. Every value is a real key in
/// <see cref="EnginePresets.Presets"/> (guarded by tests).
/// </summary>
public static class KnownEngines
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
            if (g.Provenance.Sources.Contains(ManifestSources.KnownEngines)
                && g.Stores.SteamAppId is { } appId
                && g.Engine is { } engine)
            {
                map[appId] = engine;
            }
        }
        return map;
    }

    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var e) ? e : null;

    public static IEnumerable<string> AllMappedEngines => Map.Values.Distinct();
}
