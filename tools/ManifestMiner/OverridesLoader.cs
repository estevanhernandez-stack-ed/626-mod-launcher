using System.Text.Json;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Reads hand-curated override files (*.json) from a directory. Each file is one OverrideEntry
/// (camelCase, matching the manifest convention). A malformed file is skipped (not fatal) so one typo
/// doesn't sink the whole run; the count is reported by the caller. README.json is ignored.</summary>
public static class OverridesLoader
{
    public static IReadOnlyList<OverrideEntry> Load(string overridesDir)
    {
        if (!Directory.Exists(overridesDir)) return Array.Empty<OverrideEntry>();

        var result = new List<OverrideEntry>();
        foreach (var file in Directory.GetFiles(overridesDir, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<OverrideEntry>(File.ReadAllText(file), ManifestJson.Options);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.SteamAppId))
                    result.Add(entry);
            }
            catch (JsonException) { /* skip a malformed curated file; caller reports the count */ }
        }
        return result;
    }
}
