using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>One save snapshot: its zip path, label, when it was taken, size, and whether the app made it (auto).</summary>
public sealed record SaveSnapshot(string Path, string FileName, string Label, DateTime TakenUtc, long SizeBytes, bool IsAuto);

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

    /// <summary>Recognized save files, labeled by the game's declared save types (profile-driven).</summary>
    public static IReadOnlyList<SaveFile> ListSaveFiles(string saveDir, IReadOnlyList<SaveType> saveTypes)
    {
        if (!Directory.Exists(saveDir)) return Array.Empty<SaveFile>();
        var byExt = saveTypes.ToDictionary(t => t.Extension, t => t.Label, StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(saveDir)
            .Where(f => byExt.ContainsKey(System.IO.Path.GetExtension(f)))
            .Select(f => new SaveFile(System.IO.Path.GetFileName(f), System.IO.Path.GetExtension(f), byExt[System.IO.Path.GetExtension(f)]))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One world: the folder name as it sits on disk, when it was last written, how big it
    /// is, and how many files are in it.</summary>
    public sealed record SaveWorld(string Name, DateTime LastWriteUtc, long Bytes, int FileCount);

    /// <summary>
    /// The worlds in a save folder - one per subdirectory that actually contains files.
    ///
    /// <para><see cref="ListSaveFiles"/> reads a single directory level, which is right for a game
    /// that keeps several formats of one save side by side and useless for a game that keeps a folder
    /// per world. Palworld is the latter: 74 .sav files, 72 of them a level down, and the only
    /// top-level one is GlobalPalStorage.sav - shared storage, not anybody's save. Listing that single
    /// file would have implied it WAS the save, which is worse than listing nothing.</para>
    ///
    /// <para>Size and date come from walking the folder, which also picks up the game's own
    /// <c>backup</c> subfolder. Deliberate: the number answers "what does this world cost on disk",
    /// and quietly excluding part of it would make the figure disagree with Explorer.</para>
    ///
    /// <para>A subdirectory holding no files at all is not a world - it is a leftover.</para>
    /// </summary>
    public static IReadOnlyList<SaveWorld> ListWorlds(string saveDir)
    {
        if (!Directory.Exists(saveDir)) return Array.Empty<SaveWorld>();

        var worlds = new List<SaveWorld>();
        foreach (var dir in Directory.GetDirectories(saveDir))
        {
            long bytes = 0;
            var count = 0;
            var last = DateTime.MinValue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(f);
                    bytes += info.Length;
                    count++;
                    if (info.LastWriteTimeUtc > last) last = info.LastWriteTimeUtc;
                }
            }
            catch
            {
                // An unreadable world is skipped rather than failing the list. The panel shows what it
                // can see, and a snapshot covers everything either way.
                continue;
            }

            if (count == 0) continue;
            worlds.Add(new SaveWorld(Path.GetFileName(dir), last, bytes, count));
        }

        // Most recently played first: with GUID folder names the date is the only thing telling two
        // worlds apart until they can be labelled.
        return worlds.OrderByDescending(w => w.LastWriteUtc).ToList();
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
            throw new IOException($"A {targetExt} save already exists ({destName}). Snapshot it first if you want to replace it.");
        File.Copy(src, dest, overwrite);
        return destName;
    }

    [GeneratedRegex(@"^(\d{8}-\d{6})(?:__(.*))?$")]
    private static partial Regex NameRe();

    // The app's own auto snapshots carry this reserved label prefix so retention can tell them from
    // the user's deliberate backups. Reserved: user labels can never start with it (see Backup).
    private const string AutoPrefix = "auto-";

    private static bool IsAutoLabel(string label) => label.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase);

    public static SaveSnapshot Backup(string saveDir, string snapshotsDir, string? label = null, bool auto = false)
    {
        if (!Directory.Exists(saveDir))
            throw new DirectoryNotFoundException($"Save folder not found: {saveDir}");

        Directory.CreateDirectory(snapshotsDir);
        var takenUtc = DateTime.UtcNow;
        var stamp = takenUtc.ToString(TimeFormat, CultureInfo.InvariantCulture);

        var safe = SanitizeLabel(label);
        // The auto- prefix is reserved for the app: strip it from a user's label so a user backup can
        // never be misclassified as auto (and pruned). Only auto:true mints an auto snapshot.
        while (safe.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase)) safe = safe[AutoPrefix.Length..];
        if (auto) safe = AutoPrefix + (safe.Length > 0 ? safe : "snapshot");

        var fileName = safe.Length > 0 ? $"{stamp}__{safe}.zip" : $"{stamp}.zip";
        var path = System.IO.Path.Combine(snapshotsDir, fileName);

        // A clashing name (same second) would throw; make it unique. n++ each try or this loops forever.
        var n = 1;
        while (File.Exists(path))
            path = System.IO.Path.Combine(snapshotsDir, (safe.Length > 0 ? $"{stamp}__{safe}-{n++}" : $"{stamp}-{n++}") + ".zip");

        ZipFile.CreateFromDirectory(saveDir, path);
        return new SaveSnapshot(path, System.IO.Path.GetFileName(path), safe, takenUtc, new FileInfo(path).Length, IsAutoLabel(safe));
    }

    public static void Restore(string snapshotZip, string saveDir, string snapshotsDir)
    {
        if (!File.Exists(snapshotZip))
            throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        // Safety: snapshot the current save state before we overwrite it (auto-tagged).
        if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
            Backup(saveDir, snapshotsDir, "before-restore", auto: true);

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
            outList.Add(new SaveSnapshot(path, System.IO.Path.GetFileName(path), label, taken, new FileInfo(path).Length, IsAutoLabel(label)));
        }
        return outList.OrderByDescending(s => s.TakenUtc).ThenByDescending(s => s.FileName, StringComparer.Ordinal).ToList();
    }

    public static void Delete(string snapshotZip)
    {
        if (File.Exists(snapshotZip)) File.Delete(snapshotZip);
    }

    /// <summary>Keep every user snapshot plus the newest <paramref name="keepLastAuto"/> auto snapshots;
    /// delete older autos only. A user (non-auto) snapshot is never deleted.</summary>
    public static void Prune(string snapshotsDir, int keepLastAuto)
    {
        if (keepLastAuto < 0) keepLastAuto = 0;
        var autos = ListSnapshots(snapshotsDir).Where(s => s.IsAuto).ToList(); // ListSnapshots is newest-first
        foreach (var old in autos.Skip(keepLastAuto)) Delete(old.Path);
    }

    /// <summary>The declared save types that actually appear inside a snapshot zip.</summary>
    public static IReadOnlyList<SaveType> TypesInSnapshot(string snapshotZip, IReadOnlyList<SaveType> saveTypes)
    {
        if (!File.Exists(snapshotZip)) return Array.Empty<SaveType>();
        using var zip = ZipFile.OpenRead(snapshotZip);
        var exts = new HashSet<string>(zip.Entries.Select(e => System.IO.Path.GetExtension(e.FullName)), StringComparer.OrdinalIgnoreCase);
        return saveTypes.Where(t => exts.Contains(t.Extension)).ToList();
    }

    /// <summary>Restore only files of <paramref name="extension"/> from a snapshot, leaving other save
    /// types in place. Snapshots current state first (auto) so it stays reversible.</summary>
    public static void RestoreType(string snapshotZip, string saveDir, string snapshotsDir, string extension)
    {
        if (!File.Exists(snapshotZip)) throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
            Backup(saveDir, snapshotsDir, "before-restore", auto: true);

        Directory.CreateDirectory(saveDir);
        using var zip = ZipFile.OpenRead(snapshotZip);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            if (!string.Equals(System.IO.Path.GetExtension(entry.FullName), extension, StringComparison.OrdinalIgnoreCase)) continue;
            var dest = System.IO.Path.Combine(saveDir, entry.FullName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var cleaned = Regex.Replace(label.Trim(), @"[^A-Za-z0-9 _-]", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
