using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Persists per-mod MP-risk overrides at &lt;dataDir&gt;\mp-compat.json — a plain object mapping
/// modKey -&gt; the MpRisk NAME (string), e.g. { "coolmod": "Risky" }. Names, not numbers, so the
/// file is human-readable and a stray/legacy value just gets skipped instead of corrupting the map.
/// Tolerant by design: a missing or corrupt file, or any unparseable / Unknown entry, never throws.
/// Writes go through AtomicJson (temp-then-rename), so a crash mid-write can't strand a half file.
/// </summary>
public static class MpCompatStore
{
    private const string FileName = "mp-compat.json";

    /// <summary>
    /// Load the per-mod override map. Missing or corrupt file -&gt; empty map. Any entry whose value
    /// isn't a known MpRisk name, or is "Unknown", is skipped. Keys are modKeys.
    /// </summary>
    public static IReadOnlyDictionary<string, MpRisk> Load(string dataDir)
    {
        var raw = LoadRaw(dataDir);
        var map = new Dictionary<string, MpRisk>();
        foreach (var (key, value) in raw)
        {
            if (Enum.TryParse<MpRisk>(value, ignoreCase: true, out var risk) && risk != MpRisk.Unknown)
                map[key] = risk;
        }
        return map;
    }

    /// <summary>
    /// Set or clear one mod's override and persist atomically. A null OR Unknown value clears the
    /// override (Auto) by removing the key; any other value stores its enum name. Creates the dir if
    /// needed. Round-trips with <see cref="Load"/>.
    /// </summary>
    public static void SetOverride(string dataDir, string modKey, MpRisk? value)
    {
        var raw = LoadRaw(dataDir);

        if (value is null || value == MpRisk.Unknown)
            raw.Remove(modKey);
        else
            raw[modKey] = value.Value.ToString();

        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName), raw);
    }

    // Read the on-disk modKey -> name string map, tolerating everything: missing file, bad JSON,
    // a non-object payload. Any failure collapses to an empty (mutable) map.
    private static Dictionary<string, string> LoadRaw(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
