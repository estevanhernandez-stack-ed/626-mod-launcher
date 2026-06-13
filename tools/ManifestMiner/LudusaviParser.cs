using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ManifestMiner;

/// <summary>Parses the Ludusavi manifest YAML (a map of gameName -> entry) into LudusaviGame facts.
/// Only the fields we mine are read; unknown fields are ignored.</summary>
public static class LudusaviParser
{
    // Loose DTOs matching only what we need.
    private sealed class Entry
    {
        public Dictionary<string, FileMeta>? Files { get; set; }
        public Dictionary<string, object>? InstallDir { get; set; }
        public SteamMeta? Steam { get; set; }
    }
    private sealed class FileMeta { public List<string>? Tags { get; set; } }
    private sealed class SteamMeta { public long? Id { get; set; } }

    public static IReadOnlyList<LudusaviGame> Parse(string yaml)
    {
        // Ludusavi keys are camelCase (files, installDir, steam, id, tags); map them to our
        // PascalCase DTO properties. Without this the lowercase YAML keys never bind and every
        // field reads back null.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var root = deserializer.Deserialize<Dictionary<string, Entry>>(yaml) ?? new();

        var games = new List<LudusaviGame>();
        foreach (var (name, entry) in root)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var savePaths = entry.Files?.Keys.ToList() ?? new List<string>();
            var installDirs = entry.InstallDir?.Keys.ToList() ?? new List<string>();
            games.Add(new LudusaviGame(name)
            {
                SteamAppId = entry.Steam?.Id?.ToString(),
                InstallDirs = installDirs,
                SavePaths = savePaths,
            });
        }
        return games;
    }
}
