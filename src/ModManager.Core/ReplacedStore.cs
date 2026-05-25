using System.Globalization;

namespace ModManager.Core;

/// <summary>
/// Holds the prior version of a file replaced during a mod update, so a replace is always
/// reversible (the law: never overwrite-destroy). One timestamped batch folder per drop.
/// </summary>
public static class ReplacedStore
{
    /// <summary>A fresh timestamped backup folder under <paramref name="replacedRoot"/> for one update batch.</summary>
    public static string NewBatch(string replacedRoot)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var dir = Path.Combine(replacedRoot, stamp);
        var n = 1;
        while (Directory.Exists(dir)) dir = Path.Combine(replacedRoot, $"{stamp}-{n++}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Move the live file into the batch folder under its relative path; returns the backup path.
    /// Cross-volume safe (game and data dir may be on different drives).</summary>
    public static string Backup(string existingAbs, string relPath, string batchDir)
    {
        var dest = Path.Combine(batchDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try { File.Move(existingAbs, dest); }
        catch (IOException) { File.Copy(existingAbs, dest, overwrite: false); File.Delete(existingAbs); }
        return dest;
    }
}
