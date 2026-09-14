namespace ModManager.Core;

/// <summary>
/// The rules every toggle lane follows for its per-mod holding folder, in one place so a lane cannot
/// quietly relax them. Two lanes had each written their own cleanup as a recursive delete, and both
/// destroyed a held copy the day a fresh install collided with it.
/// </summary>
internal static class HoldingFolder
{
    /// <summary>True when the holding folder already holds a turned-off copy: any file other than the
    /// lane's own record at its top level. A record with nothing behind it is stale and does not count,
    /// so a record whose best-effort delete failed cannot block the mod forever.</summary>
    public static bool HoldsFiles(string dir, string recordFileName)
    {
        if (!Directory.Exists(dir)) return false;
        var record = Path.GetFullPath(Path.Combine(dir, recordFileName));
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Any(p => !string.Equals(Path.GetFullPath(p), record, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Remove a folder only when no file remains anywhere under it. Empty subfolders left by a
    /// move go with it; a file never does.</summary>
    public static void RemoveIfNoFiles(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort — leaving an empty folder behind is harmless */ }
    }
}
