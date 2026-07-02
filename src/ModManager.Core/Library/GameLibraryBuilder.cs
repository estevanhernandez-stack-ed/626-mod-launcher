namespace ModManager.Core.Library;
using ModManager.Core;
using ModManager.Core.Recency;

/// <summary>
/// Builds the Game Library home rows: merges recency across sources, rolls up mod state + tier +
/// ban risk + detected loaders per game, and orders rows most-recently-played first (nulls last,
/// then by name). Pure Core — every lookup is an injected delegate so this stays testable headless.
/// </summary>
public static class GameLibraryBuilder
{
    public static IReadOnlyList<GameLibraryRow> Build(
        IReadOnlyList<GameEntry> games, IReadOnlyList<ILastPlayedSource> sources,
        Func<GameEntry, GameModState> modState, Func<GameEntry, EngineTier> tier,
        Func<GameEntry, string?> banRisk, Func<GameEntry, IReadOnlyList<string>> loaders,
        Func<GameEntry, string?> cover)
    {
        var rows = new List<GameLibraryRow>(games.Count);
        foreach (var g in games)
        {
            var key = new GameRecencyKey(g.SteamAppId, g.GameRoot, g.LaunchExe, g.Id);
            var recency = RecencyLadder.Merge(key, sources);
            var ms = modState(g);
            rows.Add(new GameLibraryRow(g.Id, g.GameName, g.StoreSource, cover(g), recency,
                ms.ModCount, ms.EnabledCount, ms.ActiveProfile, tier(g), banRisk(g), loaders(g), g.NexusGameDomain));
        }
        return rows
            .OrderByDescending(r => r.Recency.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
