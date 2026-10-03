namespace ModManager.Core.RestorePoints;

/// <summary>
/// The <c>safe-clear.lock</c> breadcrumb a Safe Clear leaves while it runs. Startup reads it to offer recovery
/// (<see cref="RestorePointOrchestrator.DetectInterruptedClear"/>). For a point sealed by a NEWER 626 this
/// build can neither restore nor discard it, so without a way to acknowledge the reminder it came back on every
/// launch (code review on #385).
/// </summary>
public static class SafeClearLock
{
    public const string FileName = "safe-clear.lock";

    /// <summary>
    /// Stop reminding about the interrupted reset <paramref name="timestamp"/>: removes the lock file and
    /// NOTHING else. The restore point stays exactly where it is, for the newer 626 that made it. A lock that
    /// names a different reset is left alone. Returns true when the lock was removed.
    /// </summary>
    public static bool Acknowledge(string dataRoot, string timestamp)
    {
        var path = Path.Combine(dataRoot, FileName);
        try
        {
            if (!File.Exists(path)) return false;
            if (!string.Equals(File.ReadAllText(path).Trim(), timestamp, StringComparison.Ordinal)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }
}
