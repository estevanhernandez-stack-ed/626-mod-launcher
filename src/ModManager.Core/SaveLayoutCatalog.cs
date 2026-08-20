using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Live Steam-app-id -> save layout map, a facade over <see cref="EffectiveManifest"/> (twin of
/// <see cref="BanRiskCatalog"/>).
///
/// <para><b>This replaces a single hardcoded app id.</b> The layout used to be
/// <c>steamAppId == "1623730" ? Worlds : TypedFiles</c> — one game recognised, and a claim of
/// <c>TypedFiles</c> asserted over 149 others nobody had ever checked, roughly 30 of which are
/// folder-per-save. A new one is now a data PR to the feed rather than an app release, exactly as it
/// already is for engines and mod paths.</para>
///
/// <para>Resolving live rather than off a persisted field means a feed correction reaches a game the
/// user already added, with no migration.</para>
///
/// <para><b>Unknown still resolves to <see cref="SaveLayout.TypedFiles"/>.</b> That is what every game
/// does today and the floor the panel already handles — whole-folder backup and restore. The
/// manifest's null is meaningful to a CURATOR (nobody looked) but must not become a third runtime
/// state the UI has to explain.</para>
/// </summary>
public static class SaveLayoutCatalog
{
    private static IReadOnlyDictionary<string, SaveLayout>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    /// <summary>Parse a manifest value. Anything unrecognised — including a word from a newer feed
    /// this binary has never heard of — is the default, never a throw.</summary>
    public static SaveLayout Parse(string? value)
        => string.Equals(value, "worlds", StringComparison.OrdinalIgnoreCase)
            ? SaveLayout.Worlds
            : SaveLayout.TypedFiles;

    private static IReadOnlyDictionary<string, SaveLayout> Map
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

    private static IReadOnlyDictionary<string, SaveLayout> Build()
    {
        var map = new Dictionary<string, SaveLayout>(StringComparer.Ordinal);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            // Only entries that actually declare something. An absent value is not a claim, and
            // storing the default for it would erase the one distinction the data still holds.
            if (string.IsNullOrWhiteSpace(g.SaveLayout)) continue;
            if (g.Stores.SteamAppId is { Length: > 0 } appId) map[appId] = Parse(g.SaveLayout);
        }
        return map;
    }

    /// <summary>The declared layout for a Steam app id, or <see cref="SaveLayout.TypedFiles"/> when
    /// the feed says nothing.</summary>
    public static SaveLayout ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId!, out var l)
            ? l
            : SaveLayout.TypedFiles;
}
