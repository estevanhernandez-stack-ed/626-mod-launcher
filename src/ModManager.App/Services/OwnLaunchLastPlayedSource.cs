using ModManager.Core;
using ModManager.Core.Recency;

namespace ModManager.App.Services;

/// <summary>
/// The highest-priority recency source: what 626 itself observed. Reads
/// <see cref="GameEntry.LastLaunchedUtc"/> (stamped by <see cref="LauncherService.StampLaunch"/>) for
/// the last-played timestamp, and sums the own-launch log's session durations for playtime. Returns
/// null when we have no stamp for the game — recency then falls through to the next source in the
/// ladder (e.g. Steam).
/// </summary>
public sealed class OwnLaunchLastPlayedSource(GameRegistry registry) : ILastPlayedSource
{
    public string Name => "626";

    public LastPlayed? ForGame(GameRecencyKey key)
    {
        var g = registry.Games.FirstOrDefault(x => x.Id == key.Id);
        if (g?.LastLaunchedUtc is null) return null;

        var playtime = SummedPlaytime(key.Id);
        return new LastPlayed(g.LastLaunchedUtc, playtime, Name);
    }

    private static TimeSpan? SummedPlaytime(string gameId)
    {
        var entries = LaunchLog.ForGame(gameId);
        TimeSpan? total = null;
        foreach (var e in entries)
        {
            if (e.EndedUtc is null) continue;
            var span = e.EndedUtc.Value - e.StartedUtc;
            if (span <= TimeSpan.Zero) continue;
            total = (total ?? TimeSpan.Zero) + span;
        }
        return total;
    }
}
