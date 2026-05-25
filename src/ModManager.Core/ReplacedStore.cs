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

    /// <summary>One file preserved in a replaced-versions batch: where it came from + when.</summary>
    public sealed record ReplacedEntry(string OriginalPath, string RelPath, DateTime TakenUtc);

    /// <summary>Write the batch manifest (provenance for a future revert) atomically.</summary>
    public static void WriteManifest(string batchDir, IReadOnlyList<ReplacedEntry> entries)
        => AtomicJson.WriteJsonAtomic(Path.Combine(batchDir, "__626replaced.json"), entries);
}
