using System.Net.Http;
using System.Text;
using System.Text.Json;
using ManifestMiner;
using ModManager.Core;
using ModManager.Core.Manifest;

const string LudusaviUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

var fileArg = GetArg(args, "--file");
string yaml;
if (fileArg is not null)
{
    yaml = File.ReadAllText(fileArg);
}
else
{
    using var http = new HttpClient();
    yaml = await http.GetStringAsync(LudusaviUrl);
}

var parsed = LudusaviParser.Parse(yaml);
var candidates = LudusaviNormalize.ToCandidates(parsed);

// Validate through the real Core gate (skips unknown engines — here engines are null, which is allowed;
// rejects unsafe modPath — none here). Proves the output is schema-correct.
var manifest = new GameManifest
{
    SchemaVersion = 1,
    Games = candidates,
};
var validated = ManifestValidator.Validate(manifest, EnginePresets.Presets.Keys.ToHashSet());

var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
Directory.CreateDirectory(outDir);

File.WriteAllText(
    Path.Combine(outDir, "ludusavi-candidates.json"),
    JsonSerializer.Serialize(validated.Manifest, ManifestJson.Options));

var sb = new StringBuilder();
sb.AppendLine("# Ludusavi mined candidates — draft");
sb.AppendLine();
sb.AppendLine($"- parsed entries: {parsed.Count}");
sb.AppendLine($"- with Steam id (kept): {candidates.Count}");
sb.AppendLine($"- dropped (no Steam id): {parsed.Count - candidates.Count}");
sb.AppendLine($"- emitted after validation: {validated.Manifest.Games.Count}");
sb.AppendLine($"- rejected by validator: {validated.RejectedEntries.Count}");
sb.AppendLine();
sb.AppendLine("All entries are skeletal (name + steamAppId + saveDirHint; engine/modPath null) —");
sb.AppendLine("engine + mod-path enrichment comes from the Vortex/MO2 slice. NOT for shipping as-is.");
sb.AppendLine();
sb.AppendLine("## Sample (first 20)");
foreach (var g in validated.Manifest.Games.Take(20))
    sb.AppendLine($"- {g.Id} — {g.Name} (steam {g.Stores.SteamAppId})");
File.WriteAllText(Path.Combine(outDir, "ludusavi-summary.md"), sb.ToString());

Console.WriteLine($"Wrote {validated.Manifest.Games.Count} candidates to {Path.GetFullPath(outDir)}");

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
