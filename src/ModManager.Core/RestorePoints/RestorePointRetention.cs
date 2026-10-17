using System.Globalization;

namespace ModManager.Core.RestorePoints;

/// <summary>What the retention rule needs to know about one restore-point folder, read from its manifest.</summary>
public sealed record RestorePointFacts(
    string FolderName, bool Sealed, int SchemaVersion, string? Kind, string? CreatedUtc, IReadOnlyList<string> GameIds);

/// <summary>
/// Which <c>beforeRestore</c> undo snapshots a game no longer needs. Pure: facts in, folder names out, so
/// the whole rule is testable without a disk. Nothing here deletes anything.
///
/// <para><b>The rule.</b> A point is a candidate only if ALL of these hold: its manifest is sealed and
/// not from a newer build; its kind is exactly <c>beforeRestore</c>; it records exactly one game, and that
/// game is the one asked about; and its folder name is exactly <c>{yyyyMMdd-HHmmss}-{gameId}</c> with the
/// manifest's <c>createdUtc</c> the same string (how a capture names and records one). The time in that name
/// is the captured time (UTC): it is the only time the manifest carries. A point that fails any test is
/// invisible to the rule: not ranked, not selected. Candidates are ranked newest first; the newest
/// <c>keep</c> are kept; of the rest, any whose captured time is at least <c>minAge</c> before
/// <paramref name="nowUtc"/> and whose name is not protected is selected. A stamp in the future counts as young.
/// Protected points still take their place in the ranking.</para>
/// </summary>
public static class RestorePointRetention
{
    /// <summary>How many undo snapshots a game keeps.</summary>
    public const int KeepBeforeRestore = 3;

    /// <summary>An undo snapshot younger than this is never selected, whatever its rank.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    private const string StampFormat = "yyyyMMdd-HHmmss";

    /// <param name="points">Every restore point on disk, as read from its manifest.</param>
    /// <param name="gameId">The game whose undo snapshots are being tidied.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="protectedNames">Folder names never to select (compared ignoring case, as the file system does).</param>
    /// <returns>Folder names, oldest first. The caller deletes; this only chooses.</returns>
    public static IReadOnlyList<string> BeforeRestoreToPrune(
        IReadOnlyList<RestorePointFacts> points, string gameId, DateTime nowUtc,
        IReadOnlyCollection<string>? protectedNames = null, int keep = KeepBeforeRestore, TimeSpan? minAge = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        var age = minAge ?? MinAge;

        var ranked = new List<(string Name, DateTime Captured)>();
        foreach (var p in points)
            if (CapturedUtcOfUndoSnapshot(p, gameId) is { } captured)
                ranked.Add((p.FolderName, captured));
        ranked.Sort((a, b) =>
        {
            var c = b.Captured.CompareTo(a.Captured);   // newest first
            return c != 0 ? c : string.CompareOrdinal(b.Name, a.Name);
        });

        var doomed = new List<string>();
        for (var i = keep; i < ranked.Count; i++)
        {
            var (name, captured) = ranked[i];
            if (nowUtc - captured < age) continue;
            if (protectedNames is not null && protectedNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) continue;
            doomed.Add(name);
        }
        doomed.Reverse();
        return doomed;
    }

    // The captured time when `p` is provably a sealed undo snapshot of `gameId`; otherwise null.
    private static DateTime? CapturedUtcOfUndoSnapshot(RestorePointFacts p, string gameId)
    {
        if (!p.Sealed || p.SchemaVersion > RestorePoint.SchemaVersion) return null;
        if (!string.Equals(p.Kind, RestorePointKinds.BeforeRestore, StringComparison.Ordinal)) return null;
        if (p.GameIds is not { Count: 1 } || !string.Equals(p.GameIds[0], gameId, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrEmpty(p.FolderName) || p.CreatedUtc is null || p.CreatedUtc != p.FolderName) return null;
        if (p.FolderName.Length <= StampFormat.Length) return null;
        var stamp = p.FolderName[..StampFormat.Length];
        if (p.FolderName != $"{stamp}-{p.GameIds[0]}") return null;
        return DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }
}
