using ModManager.Core.Manifest;

namespace ModManager.Core.Loaders;

/// <summary>A mod loader with a DISTINCT launcher exe the launcher can detect in the game's play
/// folder and surface as a one-click "Launch via X" button. <see cref="BanSafe"/> marks loaders whose
/// modding path avoids the game's anti-cheat (Mod Engine 2 loads mods without touching the EAC surface;
/// Seamless Co-op runs its own multiplayer). Metadata + a Get-it-here URL only — the binary is never
/// bundled.</summary>
public sealed record KnownLoader(
    string LoaderId,
    string DisplayName,
    string Engine,
    string? SteamAppId,
    IReadOnlyList<string> LauncherExeNames,
    string GetUrl,
    string Author,
    bool BanSafe,
    bool EditsSaves = false,
    IReadOnlyList<string>? GameIds = null)
{
    /// <summary>Pinned to particular games, by Steam id or manifest id. Unpinned means engine-wide.</summary>
    public bool IsPinned => SteamAppId is not null || GameIds is { Count: > 0 };
}

/// <summary>
/// The loaders this binary knows: the manifest's <c>loaders</c> list, embedded snapshot overlaid with
/// the signed feed.
///
/// <para>These used to be compiled in, which meant a game newly flagged as ban-risk got the warning the
/// day the feed said so but no safe loader to point at until a release added one. They now live in the
/// embedded <c>games-manifest.json</c> (the offline baseline, unchanged from the compiled list) and the
/// feed can add a loader or correct one. See
/// <c>docs/superpowers/specs/2026-10-01-safe-loaders-in-the-feed-design.md</c>.</para>
///
/// <para>Every entry here has already passed <see cref="ManifestValidator"/>, so the identity fields are
/// present and every exe name is a bare <c>*.exe</c> filename. The projection is cached by
/// <see cref="EffectiveManifest.Generation"/>, so a feed applied at startup is picked up on the next
/// read.</para>
/// </summary>
public static class KnownLoaderCatalog
{
    // The same lock-and-generation cache every other manifest facade uses (KnownEngines, NexusDomains,
    // BanRiskCatalog), so there is one reviewed pattern rather than a second, lock-free one.
    private static IReadOnlyList<KnownLoader>? _catalog;
    private static int _catalogGen = -1;
    private static readonly object _gate = new();

    public static IReadOnlyList<KnownLoader> Catalog
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_catalog is null || _catalogGen != gen)
                {
                    _catalog = EffectiveManifest.Current.Loaders.Select(ToKnownLoader).ToList();
                    _catalogGen = gen;
                }
                return _catalog;
            }
        }
    }

    // The validator guarantees Id, DisplayName, Engine, a non-empty exe list and an https GetUrl; the
    // fallbacks below exist only to satisfy the compiler, never to paper over a missing field.
    private static KnownLoader ToKnownLoader(LoaderManifestEntry l) => new(
        LoaderId: l.Id,
        DisplayName: l.DisplayName ?? l.Id,
        Engine: l.Engine ?? "",
        SteamAppId: l.SteamAppId,
        LauncherExeNames: l.LauncherExeNames ?? Array.Empty<string>(),
        GetUrl: l.GetUrl ?? "",
        Author: l.Author ?? "",
        BanSafe: l.BanSafe == true,
        EditsSaves: l.EditsSaves == true,
        GameIds: l.GameIds);
}
