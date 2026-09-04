using System.Text.Json;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Reads hand-curated override files (*.json) from a directory. Each file is one OverrideEntry
/// (camelCase, matching the manifest convention). A malformed file, or one with neither a Steam id nor
/// a slug to key on, is skipped (not fatal) so one bad file doesn't sink the whole run; the count is
/// reported by the caller. README.json is ignored.</summary>
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

                // Keyed by Steam id OR by slug. An entry with neither cannot be addressed at all, so
                // it is still refused here - OverridesValidate reports it as a build problem.
                if (entry is not null
                    && (!string.IsNullOrWhiteSpace(entry.SteamAppId) || !string.IsNullOrWhiteSpace(entry.Id)))
                    result.Add(entry with { SourcePath = file });
            }
            catch (JsonException) { /* skip a malformed curated file; caller reports the count */ }
        }
        return result;
    }
}
