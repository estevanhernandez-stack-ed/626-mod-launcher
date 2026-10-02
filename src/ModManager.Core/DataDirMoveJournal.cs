using System.Text.Json;

namespace ModManager.Core;

/// <summary>One data-folder move in flight: written before the move, removed once the registration
/// that points at its target is saved (A6). <paramref name="Proposed"/> is the whole entry the user
/// confirmed, so an interrupted save can be finished exactly as it was meant.</summary>
public sealed record DataDirMoveRecord(string GameId, string From, string To, GameEntry Proposed, DateTime StartedUtc);

/// <summary>What to do with a record left behind by a launcher that died mid-save.</summary>
public enum MoveRecovery
{
    /// <summary>The save landed, or the move never happened (or was put back): forget the record.</summary>
    NothingToDo,
    /// <summary>The data is at the target and only there, and the registration still points at the
    /// source: finish the save the user confirmed.</summary>
    FinishSave,
    /// <summary>Data at both, or at neither. Not a guess to make for the user: say where each is.</summary>
    NeedsYou,
}

/// <summary>
/// A breadcrumb for the registration repair's data-folder move (A6).
///
/// <para><b>The window.</b> The move runs before the registry write, so a launcher that dies between
/// the two leaves the data at the new folder and the registration at the old, with no record a move
/// happened: the game's disabled mods look gone. The registry lock (<c>RegistryStore.Update</c>) closes
/// the writer-clobber half; process death needs a record that outlives the process.</para>
///
/// <para><b>Where.</b> In the launcher's data root, beside games.json, not inside the move's target:
/// <c>DataDirMove</c> refuses a target that already holds anything, so a marker there would block the
/// very move it records. camelCase on disk, written atomically.</para>
/// </summary>
public static class DataDirMoveJournal
{
    private const string Folder = "pending-moves";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static string PathFor(string dataRoot, string gameId)
        => Path.Combine(dataRoot, Folder, Uri.EscapeDataString(gameId) + ".json");

    public static void Write(string dataRoot, DataDirMoveRecord record)
    {
        Directory.CreateDirectory(Path.Combine(dataRoot, Folder));
        AtomicJson.WriteJsonAtomic(PathFor(dataRoot, record.GameId), record);
    }

    public static void Clear(string dataRoot, string gameId)
    {
        try { File.Delete(PathFor(dataRoot, gameId)); } catch { /* a stale record is re-assessed, never acted on blindly */ }
    }

    /// <summary>Every record left on disk. An unreadable one is skipped, never thrown on: this runs at startup.</summary>
    public static IReadOnlyList<DataDirMoveRecord> Pending(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, Folder);
        if (!Directory.Exists(dir)) return Array.Empty<DataDirMoveRecord>();
        var records = new List<DataDirMoveRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<DataDirMoveRecord>(File.ReadAllText(file), ReadOpts) is { } r
                    && !string.IsNullOrEmpty(r.GameId) && r.Proposed is not null)
                    records.Add(r);
            }
            catch { /* skipped */ }
        }
        return records;
    }

    /// <summary>
    /// What a leftover record means now. Pure: <paramref name="hasData"/> answers "does this folder
    /// exist with something in it", and <paramref name="registered"/> is the game as the registry has
    /// it (null when it has since been removed).
    /// </summary>
    public static MoveRecovery Assess(DataDirMoveRecord record, GameEntry? registered, Func<string, bool> hasData)
    {
        if (registered is null) return MoveRecovery.NothingToDo;
        var current = Scanner.DataDirForGame(registered);
        if (Same(current, record.To)) return MoveRecovery.NothingToDo;         // the save landed
        if (!Same(current, record.From)) return MoveRecovery.NothingToDo;      // changed again since: not this move's to finish

        var atFrom = hasData(record.From);
        var atTo = hasData(record.To);
        return (atFrom, atTo) switch
        {
            (false, true) => MoveRecovery.FinishSave,
            (true, false) => MoveRecovery.NothingToDo,   // never moved, or put back
            _ => MoveRecovery.NeedsYou,
        };
    }

    /// <summary>The line the player sees for a record that needs them.</summary>
    public static string NeedsYouMessage(DataDirMoveRecord r, string gameName)
        => $"A change to {gameName}'s folders was interrupted. Its launcher data may be in {r.From} or in {r.To}. "
           + "Nothing was moved or deleted; open the game's setup to point it at the right one.";

    /// <summary>The line the player sees when an interrupted save was finished.</summary>
    public static string FinishedMessage(DataDirMoveRecord r, string gameName)
        => $"Finished an interrupted change to {gameName}: its launcher data is in {r.To}.";

    private static bool Same(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
