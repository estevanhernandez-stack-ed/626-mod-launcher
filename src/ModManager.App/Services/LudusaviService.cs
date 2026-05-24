using System.IO;
using System.Net.Http;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ModManager.App.Services;

/// <summary>
/// Authoritative save-location data from the community Ludusavi manifest (derived from
/// PCGamingWiki), keyed by Steam app id. Downloads the manifest once, parses it into a slim
/// appId -> save-path-template index cached as JSON, and serves lookups. All best-effort: any
/// failure yields no templates, so callers fall back to the heuristic guesser.
/// </summary>
public sealed class LudusaviService
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, List<string>>? _index;

    public LudusaviService(HttpClient http) => _http = http;

    private static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");
    private static string IndexPath => Path.Combine(DataDir, "ludusavi-index.json");

    /// <summary>Windows save-path templates for a Steam app id (empty if unknown / unavailable).</summary>
    public async Task<IReadOnlyList<string>> SaveTemplatesAsync(string steamAppId)
    {
        var index = await EnsureIndexAsync();
        return index.TryGetValue(steamAppId, out var templates) ? templates : Array.Empty<string>();
    }

    private async Task<Dictionary<string, List<string>>> EnsureIndexAsync()
    {
        if (_index is not null) return _index;
        await _gate.WaitAsync();
        try
        {
            if (_index is not null) return _index;
            try
            {
                if (File.Exists(IndexPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(IndexPath)).TotalDays < 14)
                {
                    _index = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await File.ReadAllTextAsync(IndexPath));
                    if (_index is not null) return _index;
                }
            }
            catch { /* rebuild below */ }

            try
            {
                var yaml = await _http.GetStringAsync(ManifestUrl);
                _index = await Task.Run(() => ParseToIndex(yaml));
                try { Directory.CreateDirectory(DataDir); await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(_index)); }
                catch { /* cache write is optional */ }
            }
            catch { _index = new Dictionary<string, List<string>>(); } // offline / parse error -> heuristics handle it

            return _index!;
        }
        finally { _gate.Release(); }
    }

    private static Dictionary<string, List<string>> ParseToIndex(string yaml)
    {
        var de = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var manifest = de.Deserialize<Dictionary<string, GameNode>>(yaml) ?? new Dictionary<string, GameNode>();

        var index = new Dictionary<string, List<string>>();
        foreach (var node in manifest.Values)
        {
            if (node?.Steam?.Id is not long appId || node.Files is null) continue;
            var templates = new List<string>();
            foreach (var (path, file) in node.Files)
            {
                if (file?.Tags is null || !file.Tags.Contains("save")) continue;
                var windows = file.When is null || file.When.Count == 0
                    || file.When.Any(w => string.IsNullOrEmpty(w.Os) || w.Os == "windows");
                if (windows) templates.Add(path);
            }
            if (templates.Count > 0) index[appId.ToString()] = templates;
        }
        return index;
    }

    private sealed class GameNode
    {
        public Dictionary<string, FileNode>? Files { get; set; }
        public SteamNode? Steam { get; set; }
    }

    private sealed class FileNode
    {
        public List<string>? Tags { get; set; }
        public List<WhenNode>? When { get; set; }
    }

    private sealed class SteamNode { public long? Id { get; set; } }
    private sealed class WhenNode { public string? Os { get; set; } public string? Store { get; set; } }
}
