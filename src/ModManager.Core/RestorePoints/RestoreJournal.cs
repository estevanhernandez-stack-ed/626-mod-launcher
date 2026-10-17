using System.Globalization;
using System.Text.Json;

namespace ModManager.Core.RestorePoints;

/// <summary>A Kind restore that was writing over a live game when the launcher stopped. <c>UndoSnapshot</c>
/// is the <c>beforeRestore</c> restore point taken just before the first write: restoring it goes back to
/// where the player was.</summary>
public sealed record InterruptedRestore(string RestorePoint, string UndoSnapshot, string GameId, string GameName, DateTime StartedUtc);

/// <summary>
/// A breadcrumb for the overlay half of a Kind restore (the Patch Day snapshot, or a <c>beforeRestore</c>
/// one). The overlay writes files one at a time, each whole-old or whole-new, so a launcher that dies partway
/// leaves a game with some files from the snapshot and some from before, and nothing to say so. The record
/// outlives the process, so the next start can say it, and name the snapshot that goes back.
///
/// <para><b>When.</b> Written after the plan and the <c>beforeRestore</c> snapshot succeed and before the
/// overlay's first write; cleared when the restore finishes. A refusal before that point never writes one.
/// A failure partway through leaves it, on purpose.</para>
///
/// <para><b>Where.</b> <c>pending-restore.json</c> in the launcher's data root, beside games.json. One at a
/// time is enough: restores run behind the orchestrator's single-writer gate. camelCase, atomic write.</para>
///
/// <para><b>What it never does.</b> Nothing here restores, deletes or rewrites a game file. Acknowledging
/// removes the record and only the record.</para>
/// </summary>
public static class RestoreJournal
{
    public const string FileName = "pending-restore.json";

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, FileName);

    /// <summary>Throws if the record can't be written; the caller then refuses the restore, before any write.</summary>
    public static void Write(string dataRoot, InterruptedRestore record)
        => AtomicJson.WriteJsonAtomic(PathFor(dataRoot), record);

    public static void Clear(string dataRoot)
    {
        try { if (File.Exists(PathFor(dataRoot))) File.Delete(PathFor(dataRoot)); }
        catch { /* a stale record is shown once and acknowledged; it is never acted on */ }
    }

    /// <summary>The record on disk, or null when there is none or it can't be trusted (unreadable, not an
    /// object, no snapshot to point at). This runs at startup: it never throws.</summary>
    public static InterruptedRestore? Read(string dataRoot)
    {
        try
        {
            var path = PathFor(dataRoot);
            if (!File.Exists(path)) return null;
            var r = JsonSerializer.Deserialize<InterruptedRestore>(File.ReadAllText(path), ReadOpts);
            return r is not null && !string.IsNullOrWhiteSpace(r.UndoSnapshot) && !string.IsNullOrWhiteSpace(r.GameName) ? r : null;
        }
        catch { return null; }
    }

    /// <summary>What the player is told at the next start. Says what happened, what state the game may be
    /// in, and the one thing to do; nothing has been put right on their behalf.</summary>
    public static string Message(InterruptedRestore r)
    {
        var when = r.StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return $"626 was closed in the middle of restoring {r.GameName}'s snapshot from {when}. "
             + "Some files may be from the snapshot and some from before. "
             + $"To undo it, restore \"{r.UndoSnapshot}\" in Settings, under Restore points.";
    }

    /// <summary>The second paragraph: what the button does and doesn't do.</summary>
    public const string AcknowledgeNote =
        "Choose Got it to stop this reminder. Only the reminder goes: the snapshot stays where it is, "
        + "and nothing is restored for you.";
}
