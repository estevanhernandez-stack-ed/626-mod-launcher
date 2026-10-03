using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ModManager.Core.RestorePoints;

/// <summary>
/// Pure formatter for the message the Reset-launcher dialog shows after a successful Safe Clear.
/// Lives in Core so the wording — especially the canonical "Settings → Restore points." pointer —
/// is unit-tested and can't drift. Never touches the clock: the friendly stamp is derived from the
/// result's own <see cref="SafeClearResult.RestorePointTimestamp"/>. Mode-neutral on purpose — the
/// result carries no end-state, so the base line stays "Reset complete." rather than risk claiming
/// "vanilla" on a leave-mods-active clear.
/// </summary>
public static class SafeClearSummary
{
    /// <summary>Render the success confirmation for a completed clear.</summary>
    public static string SuccessMessage(SafeClearResult result)
    {
        var parts = new[] { "Reset complete.", RestoreLine(result.RestorePointTimestamp), WarningLine(result.Warnings) };
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>What to tell the user after a restore that went through but left something to look at
    /// (a mod the clear turned off that is not back on, a game missing from the restored registry). Null
    /// when there is nothing to say, or when the restore was refused (that has its own message).</summary>
    public static string? RestoreNotes(RestoreResult result)
    {
        if (!result.Ok) return null;
        var real = (result.Warnings ?? Array.Empty<string>()).Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        if (real.Count == 0) return null;
        return "Your setup is restored. A few things need a look:\n\n" + string.Join("\n", real.Select(w => "- " + w));
    }

    private static string RestoreLine(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return "No restore point was created.";

        return DateTime.TryParseExact(timestamp, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? $"Saved a restore point from {dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}. Find it in Settings → Restore points."
            : "Saved a restore point. Find it in Settings → Restore points."; // junk stamp: confirm it exists, never surface the raw value
    }

    private static string WarningLine(IReadOnlyList<string>? warnings)
    {
        var real = (warnings ?? Array.Empty<string>()).Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        return real.Count == 0 ? "" : "Note: " + string.Join(" ", real);
    }
}
