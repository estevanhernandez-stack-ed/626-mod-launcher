using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>One save snapshot: its zip path, label, when it was taken, and size.</summary>
public sealed record SaveSnapshot(string Path, string FileName, string Label, DateTime TakenUtc, long SizeBytes);

/// <summary>A recognized save file: its name, extension, and human label (Vanilla / Seamless Co-op).</summary>
public sealed record SaveFile(string Name, string Extension, string TypeLabel);

/// <summary>
/// Built-in game-save snapshots. Backup zips the save folder to a timestamped archive kept
/// outside the save folder; restore swaps the save contents back, snapshotting the current
/// state first so a restore is itself undoable (operating law #3). Mirrors the launcher's
/// reversible-by-default stance. Pure System.IO — tested headless against temp dirs.
/// </summary>
public static partial class SaveManager
{
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    /// <summary>Known FromSoft save extensions and their plain-English type. Seamless Co-op invents .co2.</summary>
    public static IReadOnlyDictionary<string, string> SaveTypes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".sl2"] = "Vanilla",
        [".co2"] = "Seamless Co-op",
    };

    /// <summary>Recognized save files in the folder, labeled by type (ignores .bak and unknown files).</summary>
    public static IReadOnlyList<SaveFile> ListSaveFiles(string saveDir)
    {
        if (!Directory.Exists(saveDir)) return Array.Empty<SaveFile>();
        return Directory.GetFiles(saveDir)
            .Where(f => SaveTypes.ContainsKey(System.IO.Path.GetExtension(f)))
            .Select(f => new SaveFile(System.IO.Path.GetFileName(f), System.IO.Path.GetExtension(f), SaveTypes[System.IO.Path.GetExtension(f)]))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Clone a save to another type — copy <paramref name="sourceFileName"/> to the same base name with
    /// <paramref name="targetExt"/> (e.g. ER0000.sl2 → ER0000.co2). The source is never touched; an
    /// existing target is never overwritten unless <paramref name="overwrite"/> is set (the caller
    /// snapshots first). Returns the new file name.
    /// </summary>
    public static string CloneToType(string saveDir, string sourceFileName, string targetExt, bool overwrite = false)
    {
        var src = System.IO.Path.Combine(saveDir, sourceFileName);
        if (!File.Exists(src)) throw new FileNotFoundException($"Save not found: {sourceFileName}");
        if (!targetExt.StartsWith('.')) targetExt = "." + targetExt;

        var destName = System.IO.Path.GetFileNameWithoutExtension(sourceFileName) + targetExt;
        var dest = System.IO.Path.Combine(saveDir, destName);
        if (File.Exists(dest) && !overwrite)
        {
            var label = SaveTypes.TryGetValue(targetExt, out var l) ? l : targetExt;
            throw new IOException($"A {label} save already exists ({destName}). Snapshot it first if you want to replace it.");
        }
        File.Copy(src, dest, overwrite);
        return destName;
    }

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
