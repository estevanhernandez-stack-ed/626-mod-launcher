using System.Text.Json;

namespace ModManager.Core.ConfigMods;

/// <summary>One config file a config mod touched: whether it existed pre-install (false -> undo
/// deletes it), the exact snapshot taken before our write, and the mod's raw ini payload for that
/// file (kept so re-enable can re-merge against the file's CURRENT content).</summary>
public sealed record ConfigFileRecord(string FileName, bool ExistedAtInstall, string? SnapshotPath, string Payload);

/// <summary>One installed config-tweak mod (Engine.ini &amp; friends), with per-file records.</summary>
public sealed record ConfigModEntry(
    string Id, string Name, IReadOnlyList<ConfigFileRecord> Files, DateTime InstalledUtc, bool Enabled);

/// <summary>
/// The installed-config-mods registry: a small JSON file (<c>config-mods.json</c>) under the game's
/// data dir, written atomically (camelCase). Tolerant load — missing/corrupt reads as empty, never
/// throws (it must never wipe the user's view of their installed config mods on a partial write or
/// a hand-edit). Modeled on <see cref="ModManager.Core.SaveModStore"/>.
/// </summary>
public static class ConfigModStore
{
    public const string FileName = "config-mods.json";

    // Tolerant read: case-insensitive so a hand-edited file still loads.
    private static readonly JsonSerializerOptions ReadJson = new() { PropertyNameCaseInsensitive = true };

    private static string PathFor(string dataDir) => System.IO.Path.Combine(dataDir, FileName);

    public static IReadOnlyList<ConfigModEntry> Load(string dataDir)
    {
        var path = PathFor(dataDir);
        if (!File.Exists(path)) return Array.Empty<ConfigModEntry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ConfigModEntry>>(File.ReadAllText(path), ReadJson);
            return list?.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id)).ToList()
                   ?? (IReadOnlyList<ConfigModEntry>)Array.Empty<ConfigModEntry>();
        }
        catch
        {
            return Array.Empty<ConfigModEntry>(); // missing/corrupt -> empty, never throw
        }
    }

    public static void Upsert(string dataDir, ConfigModEntry entry)
    {
        Directory.CreateDirectory(dataDir);
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list.Add(entry);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }

    public static void Remove(string dataDir, string id)
    {
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }
}
