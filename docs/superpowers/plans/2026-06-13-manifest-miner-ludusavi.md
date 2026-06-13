# Game Manifest — feed miner, slice 1: Ludusavi backbone

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the in-repo C# miner tool and prove its full pipeline — fetch → parse → normalize → validate → emit → diff — against the cleanest source (Ludusavi), producing a reviewable **draft candidates file**, not a shipped manifest.

**Architecture:** A console tool at `tools/ManifestMiner/` that references `ModManager.Core` (so it normalizes through the real `GameManifestEntry` records + `ManifestValidator` — schema-correct by construction). The Ludusavi source splits into a thin `LudusaviParser` (YAML → intermediate model, via YamlDotNet) and a pure `LudusaviNormalize` (model → `GameManifestEntry` candidates). The CLI fetches (or reads a local file), normalizes, validates, and writes `out/ludusavi-candidates.json` + a human-readable `out/ludusavi-summary.md` diff/coverage report.

**Tech Stack:** .NET 10, C#, YamlDotNet (new dependency — **tool-only**, never referenced by Core or App), xUnit. Dev/CI-only; excluded from the Velopack release build.

**Spec:** roadmap `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md` §8 (miner pipeline); go-live runbook `docs/manifest-feed-runbook.md`.

---

## Scope — read this first

**Ludusavi has no engine or mod-path data** (confirmed: entries are `gameName → {files (save/config paths), installDir, registry, steam: {id}}`). So this slice produces **skeletal candidates**: `id` (slug from name), `name`, `stores.steamAppId`, and `saveDirHint` (derived from save paths). **`engine` is null, `modPath` is null, `nexusDomain` is null.**

Consequences, by design:
- The output is a **draft for review** (`out/ludusavi-candidates.json`), **NOT merged into the embedded/shipped manifest.** Engine-less entries don't help the launcher's facades; merging thousands of them would bloat the manifest with unusable rows.
- This slice's value is: (1) the **pipeline works** end-to-end on real data; (2) **breadth** — the universe of games + Steam IDs + save hints, as a backbone for later enrichment.
- **Engine + mod-path enrichment is slice 2** (Vortex/MO2 sources), merged onto this backbone by Steam ID. Only then are entries launcher-usable.

**Out of scope here:** no Vortex/MO2 sources, no signing, no publish, no `626-game-manifest` repo, no merge into the shipped manifest, no App/Core change. The miner only reads upstream + writes draft files under `tools/ManifestMiner/out/` (gitignored).

## Licensing guardrails (from the signed-off decision)

Facts only. The miner extracts only factual data points (Steam IDs, names, save-path strings) into our own schema — never Ludusavi's prose or file structure wholesale. Per the runbook, each datum should ultimately be cross-verifiable against a primary source; for Ludusavi (itself MIT, derived from PCGamingWiki facts) this is satisfied by extracting bare facts. A NOTICE entry for Ludusavi lands when the feed is published (slice with the feed repo), not here.

---

## File Structure

- Create: `tools/ManifestMiner/ManifestMiner.csproj` — console (`Exe`), refs `ModManager.Core` + `YamlDotNet`.
- Create: `tools/ManifestMiner/LudusaviGame.cs` — intermediate record (parsed shape).
- Create: `tools/ManifestMiner/LudusaviParser.cs` — YAML text → `IReadOnlyList<LudusaviGame>` (YamlDotNet).
- Create: `tools/ManifestMiner/LudusaviNormalize.cs` — pure: `LudusaviGame` list → `GameManifestEntry` candidates.
- Create: `tools/ManifestMiner/Program.cs` — CLI: fetch/read → parse → normalize → validate → emit JSON + summary.
- Create: `tools/ManifestMiner/.gitignore` — ignore `out/`.
- Modify: `.gitignore` (repo root) — ignore `tools/ManifestMiner/out/` (belt + suspenders).
- Create: `tests/ModManager.Tests/Miner/LudusaviNormalizeTests.cs` — pure normalize (no YAML needed).
- Create: `tests/ModManager.Tests/Miner/LudusaviParserTests.cs` — small YAML fixture → model.
- Modify: `tests/ModManager.Tests/ModManager.Tests.csproj` — add a ProjectReference to `tools/ManifestMiner` so the miner tests run in the existing CI command.

**Test command (never bare root):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
**Run the miner (manual):** `dotnet run --project tools/ManifestMiner -- --source ludusavi` (writes to `tools/ManifestMiner/out/`).

---

### Task 1: Miner project skeleton + intermediate model

**Files:**
- Create: `tools/ManifestMiner/ManifestMiner.csproj`
- Create: `tools/ManifestMiner/LudusaviGame.cs`
- Modify: `tests/ModManager.Tests/ModManager.Tests.csproj`

- [ ] **Step 1: Create the project**

`tools/ManifestMiner/ManifestMiner.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Tool-only: never part of the shipped app or the Velopack release build. -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="16.2.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ModManager.Core\ModManager.Core.csproj" />
  </ItemGroup>
</Project>
```

(Pin the latest stable YamlDotNet; `16.2.1` is a known-good baseline — bump if the restore reports a newer stable.)

- [ ] **Step 2: Intermediate model**

`tools/ManifestMiner/LudusaviGame.cs`:

```csharp
namespace ManifestMiner;

/// <summary>The facts we extract from one Ludusavi manifest entry. Ludusavi is save-data oriented:
/// it gives us the game name, its Steam app id, install-dir name(s), and save/config path strings —
/// no engine and no mod folder (those come from Vortex/MO2 in a later slice).</summary>
public sealed record LudusaviGame(string Name)
{
    public string? SteamAppId { get; init; }
    public IReadOnlyList<string> InstallDirs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SavePaths { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 3: Wire miner tests into the existing test project**

In `tests/ModManager.Tests/ModManager.Tests.csproj`, add (alongside the existing references):

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\tools\ManifestMiner\ManifestMiner.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Build to verify the wiring**

Run: `dotnet build tools/ManifestMiner/ManifestMiner.csproj`
Expected: Build succeeded (restores YamlDotNet, references Core). Then `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"` → still PASS (Core does not reference the miner; the dependency is tests→miner→Core, one-way).

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/ManifestMiner.csproj tools/ManifestMiner/LudusaviGame.cs tests/ModManager.Tests/ModManager.Tests.csproj
git commit -m "feat(miner): ManifestMiner tool skeleton + Ludusavi intermediate model"
```

---

### Task 2: Pure normalize — LudusaviGame → GameManifestEntry candidates

**Files:**
- Create: `tools/ManifestMiner/LudusaviNormalize.cs`
- Test: `tests/ModManager.Tests/Miner/LudusaviNormalizeTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/LudusaviNormalizeTests.cs`:

```csharp
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class LudusaviNormalizeTests
{
    [Fact]
    public void Maps_name_and_steam_id_into_a_candidate()
    {
        var games = new[]
        {
            new LudusaviGame("Elden Ring") { SteamAppId = "1245620", SavePaths = new[] { "<home>/EldenRing" } },
        };

        var entries = LudusaviNormalize.ToCandidates(games);

        var e = Assert.Single(entries);
        Assert.Equal("elden-ring", e.Id);            // slug from name
        Assert.Equal("Elden Ring", e.Name);
        Assert.Equal("1245620", e.Stores.SteamAppId);
        Assert.Null(e.Engine);                        // Ludusavi has no engine
        Assert.Null(e.ModPath);                       // nor mod path
        Assert.Contains("ludusavi", e.Provenance.Sources);
    }

    [Fact]
    public void Skips_entries_without_a_steam_id()
    {
        // No Steam id -> we can't key/verify it; drop from candidates (Steam is our only probe today).
        var games = new[] { new LudusaviGame("Some GOG-only Game") { SteamAppId = null } };
        Assert.Empty(LudusaviNormalize.ToCandidates(games));
    }

    [Fact]
    public void Slugs_collide_safely_by_appending_the_app_id()
    {
        var games = new[]
        {
            new LudusaviGame("Game") { SteamAppId = "1" },
            new LudusaviGame("Game!") { SteamAppId = "2" }, // slugifies to the same base "game"
        };

        var ids = LudusaviNormalize.ToCandidates(games).Select(e => e.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count()); // unique ids, no collision
    }

    [Fact]
    public void Derives_a_save_dir_hint_when_present()
    {
        var games = new[] { new LudusaviGame("X") { SteamAppId = "9", SavePaths = new[] { "<home>/X/Saves" } } };
        var e = Assert.Single(LudusaviNormalize.ToCandidates(games));
        Assert.False(string.IsNullOrEmpty(e.SaveDirHint));
    }
}
```

(If `GameManifestEntry` has no `SaveDirHint` field yet, this slice adds it to the Core record — it is in the §3 schema but may not have been needed by earlier slices. If adding it: it is an optional `string?`, camelCase, covered by the existing manifest round-trip test pattern. Confirm before implementing; if absent, add it in this task with a one-line round-trip assertion.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LudusaviNormalizeTests"`
Expected: FAIL — `LudusaviNormalize` does not exist.

- [ ] **Step 3: Implement the pure normalize**

`tools/ManifestMiner/LudusaviNormalize.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: Ludusavi facts -> GameManifestEntry candidates. Steam-id-keyed (our only probe
/// today); engine/modPath/nexusDomain stay null (Ludusavi carries none). Output is a draft for
/// review, not a shipped manifest.</summary>
public static class LudusaviNormalize
{
    public static IReadOnlyList<GameManifestEntry> ToCandidates(IReadOnlyList<LudusaviGame> games)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GameManifestEntry>();

        foreach (var g in games)
        {
            if (string.IsNullOrWhiteSpace(g.SteamAppId)) continue; // need a Steam id to key/verify

            var baseId = EnginePresets.Slugify(g.Name);
            var id = baseId;
            if (!seen.Add(id)) { id = $"{baseId}-{g.SteamAppId}"; seen.Add(id); }

            result.Add(new GameManifestEntry
            {
                Id = id,
                Name = g.Name,
                Stores = new StoreIds { SteamAppId = g.SteamAppId },
                SaveDirHint = g.SavePaths.Count > 0 ? g.SavePaths[0] : null,
                Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
            });
        }

        return result;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LudusaviNormalizeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/LudusaviNormalize.cs tests/ModManager.Tests/Miner/LudusaviNormalizeTests.cs
git commit -m "feat(miner): pure Ludusavi normalize -> GameManifestEntry candidates"
```

---

### Task 3: YAML parser — Ludusavi manifest text → model

**Files:**
- Create: `tools/ManifestMiner/LudusaviParser.cs`
- Test: `tests/ModManager.Tests/Miner/LudusaviParserTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ModManager.Tests/Miner/LudusaviParserTests.cs`:

```csharp
using ManifestMiner;

namespace ModManager.Tests.Miner;

public class LudusaviParserTests
{
    // A trimmed sample of the real Ludusavi manifest shape: top-level key = game name.
    private const string Yaml = """
    An Example Game:
      files:
        <home>/Example/saves:
          tags:
            - save
      installDir:
        ExampleGame: {}
      steam:
        id: 12345
    Another Game:
      files:
        <home>/Another:
          tags:
            - save
      steam:
        id: 67890
    No Steam Game:
      files:
        <home>/NoSteam: {}
    """;

    [Fact]
    public void Parses_name_steam_id_and_save_paths()
    {
        var games = LudusaviParser.Parse(Yaml);

        var example = games.Single(g => g.Name == "An Example Game");
        Assert.Equal("12345", example.SteamAppId);
        Assert.Contains("ExampleGame", example.InstallDirs);
        Assert.Contains(example.SavePaths, p => p.Contains("Example/saves"));

        var noSteam = games.Single(g => g.Name == "No Steam Game");
        Assert.Null(noSteam.SteamAppId); // absent steam block -> null (normalize will drop it)
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LudusaviParserTests"`
Expected: FAIL — `LudusaviParser` does not exist.

- [ ] **Step 3: Implement the parser**

`tools/ManifestMiner/LudusaviParser.cs` — deserialize the YAML into a loose dictionary shape, then project to `LudusaviGame`. (Ludusavi's manifest is a map of game-name → entry.)

```csharp
using YamlDotNet.Serialization;

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
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LudusaviParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/LudusaviParser.cs tests/ModManager.Tests/Miner/LudusaviParserTests.cs
git commit -m "feat(miner): Ludusavi YAML parser -> model"
```

---

### Task 4: CLI — fetch → normalize → validate → emit draft + summary

**Files:**
- Create: `tools/ManifestMiner/Program.cs`
- Create: `tools/ManifestMiner/.gitignore`
- Modify: repo-root `.gitignore`

- [ ] **Step 1: Add gitignores for the draft output**

`tools/ManifestMiner/.gitignore`:

```
out/
```

Append to the repo-root `.gitignore`:

```
# Manifest miner draft output (never committed; drafts for review only)
tools/ManifestMiner/out/
```

- [ ] **Step 2: Implement the CLI**

`tools/ManifestMiner/Program.cs` — fetch the Ludusavi manifest (or read a local `--file`), parse, normalize, validate through `ManifestValidator`, write `out/ludusavi-candidates.json` (camelCase via `ManifestJson.Options`) + `out/ludusavi-summary.md` (counts: total parsed, with-steam-id, dropped-no-steam, emitted; a sample of entries). No network in a unit test — this is the manual entry point.

```csharp
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
```

- [ ] **Step 3: Smoke-run against the offline fixture (no network in the verify step)**

Create a tiny local fixture and run the CLI against it (proves the wiring without depending on the network):

Run:
```bash
printf 'Test Game:\n  steam:\n    id: 42\n' > /tmp/ludu.yaml
dotnet run --project tools/ManifestMiner -- --file /tmp/ludu.yaml
```
Expected: prints "Wrote 1 candidates to …/out"; `tools/ManifestMiner/out/ludusavi-candidates.json` contains the `test-game` entry (camelCase `steamAppId`).

- [ ] **Step 4: Commit**

```bash
git add tools/ManifestMiner/Program.cs tools/ManifestMiner/.gitignore .gitignore
git commit -m "feat(miner): Ludusavi CLI — fetch, normalize, validate, emit draft + summary"
```

---

### Task 5: Full suite green + scope clean

**Files:** none (verification only).

- [ ] **Step 1: Full Core+miner suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all existing tests + the new `LudusaviNormalizeTests`/`LudusaviParserTests`. `CorePurityTests` green (Core never references the miner; the new dep chain is tests→miner→Core + YamlDotNet, all outside Core).

- [ ] **Step 2: Scope + shipping isolation**

Run: `git diff --name-only master..HEAD -- src/`
Expected: at most a single optional one-line addition to `GameManifest.cs` IF `SaveDirHint` had to be added (Task 2 Step 1 note); otherwise EMPTY. The miner lives entirely under `tools/`. Confirm `src/ModManager.App` is untouched and the Velopack build script (`scripts/build-velopack-release.ps1`) does not reference the miner (it builds `ModManager.App` only — the miner ships nowhere).

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(miner): Ludusavi slice — full suite green"
```

---

## Self-Review

**Spec coverage:** §8 pipeline (fetch → normalize → validate → emit + diff/summary) → Tasks 2–4. The "merge with overrides" + "sign" steps of §8 are explicitly later slices (overrides arrive with multi-source merge; signing with the feed repo). ✓

**Placeholder scan:** none. The `SaveDirHint` field is flagged as a conditional add with an explicit instruction (confirm-then-add), not a vague TODO. ✓

**Type consistency:** `LudusaviGame` (Task 1) consumed by `LudusaviNormalize` (Task 2) and produced by `LudusaviParser` (Task 3); `LudusaviNormalize.ToCandidates` + `LudusaviParser.Parse` signatures consistent across impl + tests. Normalize emits real `GameManifestEntry`/`StoreIds`/`ManifestProvenance` and is gated by the real `ManifestValidator`. ✓

**Honest scoping:** output is a draft (`out/`, gitignored), not a shipped manifest; entries are skeletal (no engine/modPath); enrichment is slice 2. Stated in the Scope section and the emitted summary, not hidden. ✓

**Judgment flagged:** Steam-id-keyed — entries with no Steam id are dropped (we have no other probe today). That's a deliberate narrowing for a draft; GOG/Epic-only games surface when those store probes exist. The miner is tool-only and excluded from the shipped app/release.
