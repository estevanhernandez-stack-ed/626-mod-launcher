using System.Globalization;
using System.Text.Json;

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
        => AtomicJson.WriteJsonAtomic(Path.Combine(batchDir, ManifestFile), entries);

    private const string ManifestFile = "__626replaced.json";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Every file recorded in any batch manifest under <paramref name="replacedRoot"/>. A missing
    /// root or a torn manifest yields fewer entries, never an exception: one unreadable batch must not hide
    /// the rest. Read-only.</summary>
    public static IReadOnlyList<ReplacedEntry> ReadAll(string replacedRoot)
    {
        var found = new List<ReplacedEntry>();
        if (string.IsNullOrEmpty(replacedRoot) || !Directory.Exists(replacedRoot)) return found;
        IEnumerable<string> manifests;
        try { manifests = Directory.EnumerateFiles(replacedRoot, ManifestFile, SearchOption.AllDirectories).ToList(); }
        catch { return found; }
        foreach (var mf in manifests)
        {
            try { found.AddRange(JsonSerializer.Deserialize<List<ReplacedEntry>>(File.ReadAllText(mf), ReadOptions) ?? new List<ReplacedEntry>()); }
            catch { /* a torn record: nothing known from it */ }
        }
        return found;
    }
}
