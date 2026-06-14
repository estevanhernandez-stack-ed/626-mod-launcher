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

// The most-enriched manifest so far. Starts at the Ludusavi backbone; each enrichment step
// (MO2, then overrides) replaces it so the final merge step always sees the latest data.
var current = validated.Manifest;

// --with-mo2: fetch (or read --mo2-dir) MO2 basic_games, parse, enrich the Ludusavi backbone by
// Steam id, re-validate, and emit out/manifest-draft.json + out/enrichment-summary.md. Draft-only —
// never merged into the shipped manifest.
if (args.Contains("--with-mo2"))
{
    var mo2Texts = await LoadMo2Texts(GetArg(args, "--mo2-dir"));
    var mo2Games = mo2Texts.Select(Mo2GameParser.Parse).OfType<Mo2Game>().ToList();
    var enriched = Mo2Enrich.Apply(current, mo2Games);
    var validatedEnriched = ManifestValidator.Validate(enriched, EnginePresets.Presets.Keys.ToHashSet());
    current = validatedEnriched.Manifest;

    File.WriteAllText(Path.Combine(outDir, "manifest-draft.json"),
        JsonSerializer.Serialize(current, ManifestJson.Options));

    var matched = current.Games.Count(g => g.Provenance.Sources.Contains("mo2"));
    var withMod = current.Games.Count(g => g.ModPath is not null);
    var withEngine = current.Games.Count(g => g.Engine is not null);
    var withNexus = current.Games.Count(g => g.NexusDomain is not null);
    var es = new StringBuilder();
    es.AppendLine("# MO2 enrichment — draft");
    es.AppendLine();
    es.AppendLine($"- backbone games: {current.Games.Count}");
    es.AppendLine($"- MO2 games parsed: {mo2Games.Count}");
    es.AppendLine($"- matched onto backbone (by Steam id): {matched}");
    es.AppendLine($"- with modPath: {withMod}");
    es.AppendLine($"- with engine (unambiguous infer): {withEngine}");
    es.AppendLine($"- with nexusDomain: {withNexus}");
    es.AppendLine();
    es.AppendLine("Still a draft (not the shipped manifest). Engine is set only where the mod path is");
    es.AppendLine("unambiguous; the launcher folder-detects the rest at runtime.");
    File.WriteAllText(Path.Combine(outDir, "enrichment-summary.md"), es.ToString());
    Console.WriteLine($"MO2 enrichment: {matched} matched, {withMod} modPaths, {withEngine} engines -> out/manifest-draft.json");
}

// --with-overrides: the FINAL merge step. Load hand-curated overrides (default
// tools/ManifestMiner/overrides/, or --overrides-dir <path>) and apply them onto the most-enriched
// manifest so far. Curated data wins unconditionally; an override whose Steam id isn't present adds a
// new entry. Re-validate through the real Core gate and re-emit out/manifest-draft.json. Draft-only.
if (args.Contains("--with-overrides"))
{
    var overridesDir = GetArg(args, "--overrides-dir")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "overrides");
    var overrides = OverridesLoader.Load(overridesDir);
    var curated = OverridesMerge.Apply(current, overrides);
    var validatedCurated = ManifestValidator.Validate(curated, EnginePresets.Presets.Keys.ToHashSet());
    current = validatedCurated.Manifest;

    File.WriteAllText(Path.Combine(outDir, "manifest-draft.json"),
        JsonSerializer.Serialize(current, ManifestJson.Options));

    var curatedCount = current.Games.Count(g => g.Provenance.Sources.Contains("curated"));
    var withEngine = current.Games.Count(g => g.Engine is not null);
    Console.WriteLine($"Overrides: {overrides.Count} loaded, {curatedCount} curated entries, {withEngine} total with engine -> out/manifest-draft.json");
}

// --sign: the publish step. Serialize the most-processed validated manifest in scope (`current`) ONCE,
// write out/games-manifest.json with those exact bytes, then sign THOSE SAME bytes with
// ManifestSigner.Sign (ECDSA P-256 / SHA-256, the launcher's verify format) and write the detached
// .sig. The private key comes only from the MANIFEST_SIGNING_KEY env var (CI secret) — if it's
// missing/empty, fail hard (non-zero exit, no .sig) so we never emit an unsigned-but-named artifact.
if (args.Contains("--sign"))
{
    // ForPublish tags each entry with the functional facade tag its fields earn (so the launcher's
    // KnownEngines/NexusDomains/PopularGames actually consume feed entries) and drops entries that earn
    // none. The unsigned manifest-draft.json above stays the full set for review; only the published
    // games-manifest.json is the tagged, filtered set.
    var finalManifest = PublishManifest.ForPublish(current);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(finalManifest, ManifestJson.Options);
    var manifestOut = Path.Combine(outDir, "games-manifest.json");
    File.WriteAllBytes(manifestOut, bytes);   // the published artifact

    var keyPem = Environment.GetEnvironmentVariable("MANIFEST_SIGNING_KEY");
    if (string.IsNullOrWhiteSpace(keyPem))
    {
        Console.Error.WriteLine("--sign requires MANIFEST_SIGNING_KEY (PKCS#8 PEM) in the environment.");
        Environment.Exit(1);
        return;
    }

    var sig = ManifestSigner.Sign(bytes, keyPem);          // signs the EXACT published bytes
    File.WriteAllBytes(manifestOut + ".sig", sig);
    Console.WriteLine($"Signed {finalManifest.Games.Count} useful games -> games-manifest.json (+ .sig, {sig.Length} bytes)");
}

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Load MO2 basic_games game_*.py texts: from a local dir (--mo2-dir, offline-testable) or by
// fetching the repo zipball over HTTPS and reading games/game_*.py out of the archive.
static async Task<IReadOnlyList<string>> LoadMo2Texts(string? localDir)
{
    if (localDir is not null)
        return Directory.GetFiles(localDir, "game_*.py").Select(File.ReadAllText).ToList();

    using var http = new HttpClient();
    var zipBytes = await http.GetByteArrayAsync(
        "https://codeload.github.com/ModOrganizer2/modorganizer-basic_games/zip/refs/heads/master");
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes));
    var texts = new List<string>();
    foreach (var entry in zip.Entries)
    {
        if (!entry.FullName.Contains("/games/") || !entry.Name.StartsWith("game_") || !entry.Name.EndsWith(".py"))
            continue;
        using var r = new StreamReader(entry.Open());
        texts.Add(r.ReadToEnd());
    }
    return texts;
}
