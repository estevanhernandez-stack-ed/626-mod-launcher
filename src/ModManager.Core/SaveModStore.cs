using System.Text.Json;

namespace ModManager.Core;

/// <summary>One installed save/world mod: its world GUID, friendly name, the source zip kept for
/// reset, and when it was installed.</summary>
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

    public static void Remove(string dataDir, string guid)
    {
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }
}
