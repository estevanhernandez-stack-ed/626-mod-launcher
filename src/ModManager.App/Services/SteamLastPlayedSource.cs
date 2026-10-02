using ModManager.Core;
using ModManager.Core.Recency;

namespace ModManager.App.Services;

/// <summary>
/// Recency source backed by Steam's own appmanifest <c>LastPlayed</c> stamp. Falls in behind
/// <see cref="OwnLaunchLastPlayedSource"/> in the ladder — Steam only wins when 626 has never
/// launched the game itself (e.g. the user played it before installing 626, or launched it
/// directly from Steam instead of through the launcher). Steam's local data carries no playtime
/// figure, so <see cref="LastPlayed.Playtime"/> is always null here.
/// </summary>
public sealed class SteamLastPlayedSource : ILastPlayedSource
{
    private readonly Lazy<IReadOnlyList<InstalledGame>> _scan;

    public SteamLastPlayedSource(IStoreLibrary steam)
    {
        // Cache the scan per instance/load — ForGame is called once per game in the library,
        // and re-reading every appmanifest on disk per call would be wasteful.
        _scan = new Lazy<IReadOnlyList<InstalledGame>>(steam.InstalledGames);
    }

    public string Name => "steam";

    public LastPlayed? ForGame(GameRecencyKey key)
    {
        if (string.IsNullOrEmpty(key.SteamAppId)) return null;

        var match = _scan.Value.FirstOrDefault(g =>
            string.Equals(g.AppId, key.SteamAppId, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;
        // One reading of the store's timestamp for both kinds of library row (0 = never played; a value
        // past what a date can hold is unknown, not a crash).
        return ModManager.Core.Library.LibraryList.FromUnixSeconds(match.LastPlayed) is { } utc
            ? new LastPlayed(utc, null, Name)
            : null;
    }
}
