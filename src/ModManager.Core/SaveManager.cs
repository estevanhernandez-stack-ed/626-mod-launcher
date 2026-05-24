using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>One save snapshot: its zip path, label, when it was taken, and size.</summary>
public sealed record SaveSnapshot(string Path, string FileName, string Label, DateTime TakenUtc, long SizeBytes);

/// <summary>
/// Built-in game-save snapshots. Backup zips the save folder to a timestamped archive kept
/// outside the save folder; restore swaps the save contents back, snapshotting the current
/// state first so a restore is itself undoable (operating law #3). Mirrors the launcher's
/// reversible-by-default stance. Pure System.IO — tested headless against temp dirs.
/// </summary>
public static partial class SaveManager
{
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    [GeneratedRegex(@"^(\d{8}-\d{6})(?:__(.*))?$")]
    private static partial Regex NameRe();

    public static SaveSnapshot Backup(string saveDir, string snapshotsDir, string? label = null)
    {
        if (!Directory.Exists(saveDir))
            throw new DirectoryNotFoundException($"Save folder not found: {saveDir}");

        Directory.CreateDirectory(snapshotsDir);
        var takenUtc = DateTime.UtcNow;
        var stamp = takenUtc.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var safe = SanitizeLabel(label);
        var fileName = safe.Length > 0 ? $"{stamp}__{safe}.zip" : $"{stamp}.zip";
        var path = System.IO.Path.Combine(snapshotsDir, fileName);

        // A clashing name (same second) would throw; make it unique.
        var n = 1;
        while (File.Exists(path))
            path = System.IO.Path.Combine(snapshotsDir, (safe.Length > 0 ? $"{stamp}__{safe}-{n}" : $"{stamp}-{n}") + ".zip");

        ZipFile.CreateFromDirectory(saveDir, path);
        return new SaveSnapshot(path, System.IO.Path.GetFileName(path), safe, takenUtc, new FileInfo(path).Length);
    }

    public static void Restore(string snapshotZip, string saveDir, string snapshotsDir)
    {
        if (!File.Exists(snapshotZip))
            throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        // Safety: snapshot the current save state before we overwrite it.
        if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
            Backup(saveDir, snapshotsDir, "before-restore");

        Directory.CreateDirectory(saveDir);
        foreach (var f in Directory.GetFiles(saveDir)) File.Delete(f);
        foreach (var d in Directory.GetDirectories(saveDir)) Directory.Delete(d, recursive: true);

        ZipFile.ExtractToDirectory(snapshotZip, saveDir, overwriteFiles: true);
    }

    public static IReadOnlyList<SaveSnapshot> ListSnapshots(string snapshotsDir)
    {
        if (!Directory.Exists(snapshotsDir)) return Array.Empty<SaveSnapshot>();
        var outList = new List<SaveSnapshot>();
        foreach (var path in Directory.GetFiles(snapshotsDir, "*.zip"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var m = NameRe().Match(name);
            DateTime taken;
            string label;
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out taken))
                label = m.Groups[2].Success ? m.Groups[2].Value : "";
            else
            {
                taken = File.GetLastWriteTimeUtc(path);
                label = name;
            }
            outList.Add(new SaveSnapshot(path, System.IO.Path.GetFileName(path), label, taken, new FileInfo(path).Length));
        }
        return outList.OrderByDescending(s => s.TakenUtc).ThenByDescending(s => s.FileName, StringComparer.Ordinal).ToList();
    }

    public static void Delete(string snapshotZip)
    {
        if (File.Exists(snapshotZip)) File.Delete(snapshotZip);
    }

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var cleaned = Regex.Replace(label.Trim(), @"[^A-Za-z0-9 _-]", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
