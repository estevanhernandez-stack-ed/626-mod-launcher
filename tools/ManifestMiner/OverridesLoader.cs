using System.Text.Json;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Reads hand-curated override files (*.json) from a directory. Each file is one OverrideEntry
/// (camelCase, matching the manifest convention). A malformed file is skipped (not fatal) so one bad
/// file doesn't sink the whole run. Every other parseable file is admitted — even one with neither a
/// Steam id nor an explicit id, since <see cref="OverridesValidate.KeyOf"/> can still derive a slug
/// from its name. <see cref="OverridesValidate"/> is the only gate that refuses an entry with no
/// usable key; the loader no longer makes that call silently. README.json is ignored.</summary>
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
                if (entry is not null) result.Add(entry with { SourcePath = file });
            }
            catch (JsonException) { /* skip a malformed curated file */ }
        }
        return result;
    }
}
