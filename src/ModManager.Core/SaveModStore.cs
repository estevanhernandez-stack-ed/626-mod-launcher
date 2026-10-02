using System.Text.Json;

namespace ModManager.Core;

/// <summary>One installed save/world mod: its world GUID, friendly name, the source zip kept for
/// reset, and when it was installed. Before E1's seventh slice <see cref="SourceZip"/> named the download
/// rather than the kept copy; <see cref="SaveModStore.KeptZip"/> reads both.</summary>
public sealed record SaveModEntry(string Guid, string Name, string SourceZip, DateTime InstalledUtc);

/// <summary>
/// The installed-save-mods registry: GUID -&gt; friendly name + the kept source zip. A small JSON
/// file (<c>save-mods.json</c>) under the data dir, written atomically (camelCase, Electron-shared).
/// Tolerant load — a missing or corrupt file reads as empty, never throws (it must never wipe the
/// user's view of their installed worlds on a partial-write or a hand-edit). Pure System.IO.
/// </summary>
public static class SaveModStore
{
    private const string FileName = "save-mods.json";

    // Tolerant read: case-insensitive so a hand-edited file still loads.
    private static readonly JsonSerializerOptions ReadJson = new() { PropertyNameCaseInsensitive = true };

    private static string PathFor(string dataDir) => System.IO.Path.Combine(dataDir, FileName);

    public static IReadOnlyList<SaveModEntry> Load(string dataDir)
    {
        var path = PathFor(dataDir);
        if (!File.Exists(path)) return Array.Empty<SaveModEntry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<SaveModEntry>>(File.ReadAllText(path), ReadJson);
            return list?.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Guid)).ToList()
                   ?? (IReadOnlyList<SaveModEntry>)Array.Empty<SaveModEntry>();
        }
        catch
        {
            return Array.Empty<SaveModEntry>(); // missing/corrupt -> empty, never throw
        }
    }

    public static void Upsert(string dataDir, SaveModEntry entry)
    {
        Directory.CreateDirectory(dataDir);
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Guid, entry.Guid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list.Add(entry);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }

    /// <summary>The zip a world resets from, or null when none is left. In order: the recorded <c>SourceZip</c> (the
    /// kept copy, for installs since E1's seventh slice); the kept copy's place under THIS data folder, so a moved
    /// data folder still finds it; and the copy an older build kept in the data folder under the zip's file name.
    /// Each must still hold this world: an older build kept every world's zip under its bare file name, so a later
    /// world's zip of the same name could have replaced it.</summary>
    public static string? KeptZip(string dataDir, SaveModEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceZip)) return null;
        var candidates = new List<string> { entry.SourceZip };
        try { candidates.Add(SaveModInstaller.KeptZipPath(dataDir, entry.Guid, entry.SourceZip)); }
        catch (InvalidOperationException) { /* a hand-edited id that isn't a GUID has no kept folder */ }
        candidates.Add(System.IO.Path.Combine(dataDir, System.IO.Path.GetFileName(entry.SourceZip)));
        return candidates.FirstOrDefault(c => File.Exists(c) && SaveModInstaller.ZipHoldsWorld(c, entry.Guid));
    }

    /// <summary>Stop listing a removed world and delete the zip kept for resetting it (its own folder under
    /// <c>save-mods</c>). Nothing can reset it once it is unlisted, so the copy would only pile up. An older build's
    /// copy in the data folder root is left alone: another world's zip may share its name.</summary>
    public static void Forget(string dataDir, string guid)
    {
        Remove(dataDir, guid);
        string keptDir;
        try { keptDir = System.IO.Path.GetDirectoryName(SaveModInstaller.KeptZipPath(dataDir, guid, "x.zip"))!; }
        catch (InvalidOperationException) { return; }
        LinkSafeDelete.DeleteTree(keptDir);
    }

    public static void Remove(string dataDir, string guid)
    {
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }
}
