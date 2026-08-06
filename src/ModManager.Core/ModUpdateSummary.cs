using System.Text.Json;

namespace ModManager.Core;

/// <summary>One mod with a newer version available on Nexus than what's installed.</summary>
public sealed record PendingUpdate(string GameId, string GameName, string ModKey, string ModName,
    string? InstalledVersion, string LatestVersion, int? NexusModId, string? NexusDomain);

/// <summary>What we know about one game's updates, from its already-persisted metadata.json alone.
/// <see cref="Checked"/> = false means the game has never had a Nexus refresh — that is NOT the same
/// as "up to date" (which is Checked = true, Count = 0), and callers must render it as "unknown"
/// (no badge, distinct empty state), never as "0 updates". Conflating the two states is the one bug
/// this type exists to make impossible.</summary>
public sealed record GameUpdateSummary(string GameId, string GameName, bool Checked,
    IReadOnlyList<PendingUpdate> Pending)
{
    public int Count => Pending.Count;
}

/// <summary>
/// Pure reader: "what needs updating" aggregated from per-game metadata.json files that a prior,
/// user-initiated Nexus refresh already wrote (<see cref="Mod.NexusLatestVersion"/> via
/// <c>Scanner.SaveMetadata</c> / <c>WriteManyMeta</c>). Deliberately does NOT call Nexus and does
/// NOT re-scan the game folder — those are the existing, explicit user actions (open the game, hit
/// Refresh). This type only reports what's already on disk, so the Library badge and the cross-game
/// Updates view can answer "what needs updating" without any new network traffic or file-system walk.
///
/// The pending rule mirrors <see cref="Mod.UpdateAvailable"/> exactly (latest present and different
/// from installed), with one addition: a blank (whitespace-only) <c>NexusLatestVersion</c> is treated
/// the same as null — never pending, and never enough on its own to flip <see cref="GameUpdateSummary.Checked"/>
/// to true. That keeps a stray empty string from ever being misread as "this game was checked."
/// </summary>
public static class ModUpdateSummary
{
    // Matches Scanner's private metadata JsonSerializerOptions field-for-field (case-insensitive
    // reads, camelCase on write) so this reader deserializes the exact same on-disk shape. Scanner's
    // options object isn't public, and this task must not modify Scanner.cs to expose it — the values
    // below are the source of truth to keep in sync with Scanner.cs if that ever changes.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Reads one game's metadata.json (if any) and reports its pending updates. Never
    /// throws: a missing file, an empty file, malformed JSON, or an unreadable directory all yield
    /// <c>Checked = false</c> with an empty list — the same "unknown" state as "never refreshed."</summary>
    public static GameUpdateSummary ForGame(GameEntry game)
    {
        var gameId = game.Id ?? "";
        var gameName = game.GameName ?? "";

        Dictionary<string, ModMeta>? meta;
        try
        {
            var path = Path.Combine(Scanner.DataDirForGame(game), "metadata.json");
            if (!File.Exists(path)) return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());
            meta = JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            // Malformed JSON, unreadable file/directory, etc. — "unknown", not a crash.
            return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());
        }

        if (meta is null || meta.Count == 0)
            return new GameUpdateSummary(gameId, gameName, false, Array.Empty<PendingUpdate>());

        var checkedAny = false;
        var pending = new List<PendingUpdate>();
        foreach (var (key, m) in meta)
        {
            var latest = m.NexusLatestVersion;
            if (string.IsNullOrWhiteSpace(latest)) continue; // never polled (or blank) — doesn't count as checked
            checkedAny = true;
            if (latest == m.Version) continue; // up to date
            // Unknown installed version: we polled this row (so it stays CHECKED above) but we cannot
            // say it is behind. Mirrors Mod.UpdateAvailable — the chip and this badge read the same
            // persisted fields and must never disagree. A name-search identify leaves Version null by
            // design, so without this the badge counts every identified mod as pending.
            if (string.IsNullOrWhiteSpace(m.Version)) continue;

            var modName = string.IsNullOrWhiteSpace(m.Title) ? key : m.Title;
            pending.Add(new PendingUpdate(gameId, gameName, key, modName, m.Version, latest,
                m.NexusModId, game.NexusGameDomain));
        }

        return new GameUpdateSummary(gameId, gameName, checkedAny, pending);
    }

    /// <summary>Runs <see cref="ForGame"/> across every registered game, preserving each game's own
    /// identity and Checked state — one game's "never refreshed" never contaminates another's count.</summary>
    public static IReadOnlyList<GameUpdateSummary> ForGames(IEnumerable<GameEntry> games)
        => games.Select(ForGame).ToList();
}
