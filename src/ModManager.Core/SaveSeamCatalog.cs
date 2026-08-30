using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// The curated world/character seam for a Steam app id — a facade over <see cref="EffectiveManifest"/>
/// (twin of <see cref="SaveLayoutCatalog"/> and <see cref="BanRiskCatalog"/>).
///
/// <para><b>Empty is the answer for most games, and it is not a failure.</b> A game may have no seam
/// because nobody has curated one yet, or because it has no world half at all — Cyberpunk and Elden
/// Ring are your character inside somebody else's world. Both cases resolve to empty here, and the
/// caller does the same thing with them: it does not offer to share a world.</para>
///
/// <para>The UI never asks the user to distinguish those two. A control that exists only to explain
/// why it cannot work is worse than no control.</para>
/// </summary>
public static class SaveSeamCatalog
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Map
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

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Build()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            var paths = g.SavePlayerPaths;
            if (paths is null || paths.Count == 0) continue;
            if (g.Stores.SteamAppId is { Length: > 0 } appId) map[appId] = paths;
        }
        return map;
    }

    /// <summary>The curated seam, or empty when there is none to use.</summary>
    public static IReadOnlyList<string> ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId!, out var p)
            ? p
            : Array.Empty<string>();

    /// <summary>Whether a world from this game can be shared without its player. The one question the
    /// panel asks before deciding whether the control exists at all.</summary>
    public static bool CanShare(string? steamAppId) => ByAppId(steamAppId).Count > 0;
}
