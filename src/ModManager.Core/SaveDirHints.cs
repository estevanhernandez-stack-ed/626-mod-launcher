using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// The curated save-folder hint for a Steam app id — a facade over <see cref="EffectiveManifest"/>
/// (twin of <see cref="BanRiskCatalog"/> and <see cref="SaveLayoutCatalog"/>).
///
/// <para><b>This is what makes the hint load-bearing.</b> Until now nothing read
/// <c>saveDirHint</c> at all: detection went straight to Ludusavi's template list and took the first
/// path that existed on disk. That is usually right and sometimes precisely wrong — Stellaris resolves
/// to the game's CONFIG directory, because Ludusavi lists it first and it exists.</para>
///
/// <para>It matters more now than it did, because <see cref="SaveLayoutCatalog"/> describes the folder
/// the hint points at. A layout declared against one folder and applied to another would list
/// <c>.launcher-cache</c> and <c>logs</c> to a player as though they were saves.</para>
/// </summary>
public static class SaveDirHints
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
                if (_map is null || _mapGen != gen) { _map = Build(); _mapGen = gen; }
                return _map;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (string.IsNullOrWhiteSpace(g.SaveDirHint)) continue;
            if (g.Stores.SteamAppId is { Length: > 0 } appId) map[appId] = g.SaveDirHint!;
        }
        return map;
    }

    /// <summary>The curated hint, still holding its <c>&lt;winDocuments&gt;</c>-style placeholders, or
    /// null when the feed says nothing.</summary>
    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId!, out var h) ? h : null;
}
