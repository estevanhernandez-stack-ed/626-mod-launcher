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
    /// <summary>Data at both folders. Not a guess to make for the user: say where each is, once.</summary>
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

    // The rule's options (camelCase), read case-insensitively as RegistryStore does, so a record written
    // by hand or by an older build still reads.
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

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
            (true, true) => MoveRecovery.NeedsYou,
            // Never moved, or put back; or nothing anywhere (a move of an empty data dir), which the
            // registration's folder serves as well as any: nothing to lose, nothing to say.
            _ => MoveRecovery.NothingToDo,
        };
    }

    /// <summary>
    /// Whether <paramref name="path"/> holds anything, answering "yes" whenever it cannot be SURE the
    /// answer is no. Recovery finishes a save only on positive evidence that the source is empty: a
    /// folder that is merely unreachable (an offline drive, a dropped share, a denied ACL, all of which
    /// make <see cref="Directory.Exists"/> say false rather than throw) must never read as empty, or a
    /// half-copied target would be adopted as the game's data.
    /// </summary>
    public static bool HasData(string path) => HasData(path, Directory.Exists, p => Directory.EnumerateFileSystemEntries(p).Any());

    internal static bool HasData(string path, Func<string, bool> dirExists, Func<string, bool> nonEmpty)
    {
        try
        {
            if (dirExists(path)) return nonEmpty(path);
            // Absent only counts as absent when its parent is there to be absent from.
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return parent is null || !dirExists(parent);
        }
        catch { return true; }
    }

    /// <summary>The line the player sees for a record that needs them.</summary>
    public static string NeedsYouMessage(DataDirMoveRecord r, string gameName)
        => $"A change to {gameName}'s folders was interrupted, and its launcher data is in both {r.From} and {r.To}. "
           + $"The game still uses {r.From}. Nothing was moved or deleted; if {r.To} is the copy you want, "
           + "open the game's setup and point it there.";

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
