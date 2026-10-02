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
            rows.Add(new GameLibraryRow(g.Id, g.GameName, StoreOf(g), cover(g), recency,
                ms.ModCount, ms.EnabledCount, ms.ActiveProfile, tier(g), banRisk(g), loaders(g), g.NexusGameDomain));
        }
        return rows
            .OrderByDescending(r => r.Recency.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Which store a registered game came from. <see cref="GameEntry.StoreSource"/> when it was
    /// recorded; otherwise the store ids the entry carries say it (B6 review: nothing sets StoreSource
    /// today, so managed rows had no store while the unmanaged rows beside them did, and a store filter
    /// dropped every managed game).</summary>
    public static string? StoreOf(GameEntry g)
        => !string.IsNullOrWhiteSpace(g.StoreSource) ? g.StoreSource
            : !string.IsNullOrWhiteSpace(g.SteamAppId) ? "steam"
            : !string.IsNullOrWhiteSpace(g.EaContentId) ? "ea"
            : null;

    /// <summary>A store's name as the home shows it, for both kinds of row.</summary>
    public static string StoreDisplayName(string? store) => store?.ToLowerInvariant() switch
    {
        null or "" => "",
        "steam" => "Steam",
        "ea" => "EA",
        _ => char.ToUpperInvariant(store![0]) + store[1..],
    };
}
