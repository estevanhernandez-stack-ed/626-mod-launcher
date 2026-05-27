using System.Text.Json;

namespace ModManager.Core.Catalog;

/// <summary>
/// Per-game user overrides for direct-inject mod config-file paths. Persisted at
/// <c>&lt;gameData&gt;/direct-inject/config-overrides.json</c>. Empty/missing/unreadable file
/// is treated as "no overrides" — Load never throws. Atomic temp+rename on Save.
///
/// Key structure: <c>OverridesByModId[modId][relativeConfigPath] = absoluteOverridePath</c>.
/// Lets the user point at a Seamless settings.ini that isn't where the catalog default
/// expects it (different folder / different drive entirely).
/// </summary>
public sealed record DirectInjectConfigOverrides(
    IReadOnlyDictionary<string, Dictionary<string, string>> OverridesByModId)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static DirectInjectConfigOverrides Empty { get; } =
        new(new Dictionary<string, Dictionary<string, string>>());

    public static DirectInjectConfigOverrides Load(string gameDataDir)
    {
        var path = Path.Combine(gameDataDir, "direct-inject", "config-overrides.json");
        if (!File.Exists(path)) return Empty;
        try
        {
            var doc = JsonSerializer.Deserialize<DirectInjectConfigOverrides>(File.ReadAllText(path), Json);
            return doc ?? Empty;
        }
        catch { return Empty; }
    }

    public static void Save(string gameDataDir, DirectInjectConfigOverrides overrides)
    {
        var dir = Path.Combine(gameDataDir, "direct-inject");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config-overrides.json");
        var json = JsonSerializer.Serialize(overrides, Json);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
