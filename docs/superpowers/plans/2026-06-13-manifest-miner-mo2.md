# Game Manifest — feed miner, slice 2: MO2 enrichment

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Mod Organizer 2 `basic_games` as a second source and prove the **enrichment-merge mechanism**: parse MO2's ~75 game definitions, merge them onto the Ludusavi backbone by Steam id to fill `modPath` (and `engine` only where the mod path unambiguously implies one), and emit an enriched draft + coverage report.

**Architecture:** Extends `tools/ManifestMiner/`. A pure `Mo2GameParser` (Python class text → `Mo2Game` facts via targeted regex) and a pure `Mo2Enrich.Apply(backbone, mo2Games)` (merge by Steam id). The CLI gains a combined run: mine Ludusavi (backbone) → fetch + parse MO2 → enrich → emit `out/manifest-draft.json` + `out/enrichment-summary.md`. Pure parse/merge are unit-tested with fixtures; the network fetch is the CLI's job.

**Tech Stack:** .NET 10, C#, `System.Text.RegularExpressions`, `System.IO.Compression` (BCL, for the MO2 repo zipball), xUnit. Tool-only; never shipped.

**Spec:** roadmap §8 (multi-source merge with `overrides`); runbook `docs/manifest-feed-runbook.md`.

---

## Scope + honest expectations

- **MO2 has no engine field** (confirmed: `game_*.py` carry `GameName`, `GameSteamId = [..]` (list), `GameDataPath` (string, sometimes `""`), `GameNexusName`/`GameNexusId`, `GameBinary`, `GameSaveExtension`). So enrichment fills **`modPath`** from `GameDataPath`, and sets **`engine`** only where the path is in a small **unambiguous** reverse-map; otherwise engine stays null (the launcher folder-detects at runtime — manifest engine is an optimization, not a requirement).
- **Coverage is ~75 games** (the MO2 `basic_games` universe — the long tail; big Bethesda titles have dedicated MO2 plugins and aren't here). The win is the merge mechanism + those 75 mod paths, not breadth. Vortex (130+) reuses the same mechanism next.
- **Nexus numeric→slug deferred:** MO2 gives `GameNexusName` (a slug) for some games and only `GameNexusId` (numeric) for others. Fill `nexusDomain` from `GameNexusName` when present; **skip** numeric-only (conversion is a later slice).
- **Still draft-only:** output stays under `tools/ManifestMiner/out/` (gitignored), NOT merged into the shipped manifest. No signing, no publish, no Vortex (next slice), no App/Core behavior change.

## Engine reverse-map (unambiguous only)

From `EnginePresets` defaults, the mod paths that map to exactly ONE engine (case-insensitive, `/`-normalized):

| GameDataPath | engine |
|---|---|
| `Data` | bethesda |
| `BepInEx/plugins` | bepinex |
| `addons` | source |
| `mod` | fromsoft |

`Mods` (smapi **and** melonloader), `mods` (minecraft **and** custom), and `Content/Paks/~mods` (project-prefixed in practice) are **ambiguous or variable → leave engine null.** Wrong engine is worse than null (the launcher detects at runtime), so the map is deliberately conservative.

---

## File Structure

- Create: `tools/ManifestMiner/Mo2Game.cs` — parsed MO2 facts.
- Create: `tools/ManifestMiner/Mo2GameParser.cs` — Python class text → `Mo2Game` (regex).
- Create: `tools/ManifestMiner/EngineFromModPath.cs` — the unambiguous reverse-map.
- Create: `tools/ManifestMiner/Mo2Enrich.cs` — pure merge of `Mo2Game`s onto a `GameManifest` backbone by Steam id.
- Modify: `tools/ManifestMiner/Program.cs` — combined Ludusavi→MO2 run + enriched output.
- Create: `tests/ModManager.Tests/Miner/Mo2GameParserTests.cs`
- Create: `tests/ModManager.Tests/Miner/EngineFromModPathTests.cs`
- Create: `tests/ModManager.Tests/Miner/Mo2EnrichTests.cs`

**Test command:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
**Run (manual):** `dotnet run --project tools/ManifestMiner -- --with-mo2` (fetches both; writes `out/manifest-draft.json`).

---

### Task 1: Mo2Game model + parser

**Files:**
- Create: `tools/ManifestMiner/Mo2Game.cs`
- Create: `tools/ManifestMiner/Mo2GameParser.cs`
- Test: `tests/ModManager.Tests/Miner/Mo2GameParserTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/Mo2GameParserTests.cs`:

```csharp
using ManifestMiner;

namespace ModManager.Tests.Miner;

public class Mo2GameParserTests
{
    private const string ValheimPy = """
    from ..basic_game import BasicGame

    class ValheimGame(BasicGame):
        Name = "Valheim Support Plugin"
        GameName = "Valheim"
        GameShortName = "valheim"
        GameNexusId = 3667
        GameSteamId = [892970, 896660, 1223920]
        GameBinary = "valheim.exe"
        GameDataPath = ""
    """;

    private const string Witcher3Py = """
    class Witcher3Game(BasicGame):
        GameName = "The Witcher 3"
        GameNexusName = "witcher3"
        GameSteamId = [499450, 292030]
        GameDataPath = "Mods"
    """;

    [Fact]
    public void Parses_name_steam_id_list_and_data_path()
    {
        var g = Mo2GameParser.Parse(ValheimPy);
        Assert.NotNull(g);
        Assert.Equal("Valheim", g!.GameName);
        Assert.Equal(new[] { "892970", "896660", "1223920" }, g.SteamIds.ToArray());
        Assert.Equal("", g.DataPath);          // empty is preserved (means "no mod path")
        Assert.Null(g.NexusName);              // only GameNexusId (numeric) here -> no slug
    }

    [Fact]
    public void Parses_nexus_name_slug_and_multi_steam_ids()
    {
        var g = Mo2GameParser.Parse(Witcher3Py);
        Assert.NotNull(g);
        Assert.Equal("witcher3", g!.NexusName);
        Assert.Equal("Mods", g.DataPath);
        Assert.Contains("292030", g.SteamIds);
    }

    [Fact]
    public void Returns_null_when_no_steam_id_present()
    {
        var g = Mo2GameParser.Parse("class X(BasicGame):\n    GameName = \"X\"\n");
        Assert.Null(g); // without a Steam id we can't key it onto the backbone
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Mo2GameParserTests"`
Expected: FAIL — `Mo2GameParser` does not exist.

- [ ] **Step 3: Implement the model + parser**

`tools/ManifestMiner/Mo2Game.cs`:

```csharp
namespace ManifestMiner;

/// <summary>Facts mined from one MO2 basic_games game_*.py. No engine field exists in MO2;
/// the mod path is <see cref="DataPath"/> (sometimes empty). Steam ids are a list.</summary>
public sealed record Mo2Game(string GameName)
{
    public IReadOnlyList<string> SteamIds { get; init; } = Array.Empty<string>();
    public string? DataPath { get; init; }   // GameDataPath ("" = no separate mod dir; null = absent)
    public string? NexusName { get; init; }  // GameNexusName slug, when present (not the numeric id)
}
```

`tools/ManifestMiner/Mo2GameParser.cs` (targeted regex over the class attributes — MO2 files are one class per game with class-level assignments; we read only the fields we mine and skip files that lack a Steam id):

```csharp
using System.Text.RegularExpressions;

namespace ManifestMiner;

/// <summary>Extracts mined facts from a MO2 basic_games game_*.py via targeted regex. Not a Python
/// parser — it reads the common `Attr = value` class-attribute forms. Files that don't yield a Steam
/// id return null (can't key them onto the backbone).</summary>
public static partial class Mo2GameParser
{
    [GeneratedRegex(@"GameName\s*=\s*""([^""]+)""")] private static partial Regex NameRe();
    [GeneratedRegex(@"GameNexusName\s*=\s*""([^""]+)""")] private static partial Regex NexusRe();
    [GeneratedRegex(@"GameDataPath\s*=\s*(?:r)?""([^""]*)""")] private static partial Regex DataPathRe();
    // GameSteamId may be a single int or a list: GameSteamId = 892970  |  GameSteamId = [892970, 896660]
    [GeneratedRegex(@"GameSteamId\s*=\s*(\[[^\]]*\]|\d+)")] private static partial Regex SteamRe();
    [GeneratedRegex(@"\d+")] private static partial Regex DigitsRe();

    public static Mo2Game? Parse(string pythonText)
    {
        var nameM = NameRe().Match(pythonText);
        if (!nameM.Success) return null;

        var steamM = SteamRe().Match(pythonText);
        if (!steamM.Success) return null;
        var steamIds = DigitsRe().Matches(steamM.Groups[1].Value).Select(m => m.Value).ToList();
        if (steamIds.Count == 0) return null;

        var dataM = DataPathRe().Match(pythonText);
        var nexusM = NexusRe().Match(pythonText);

        return new Mo2Game(nameM.Groups[1].Value)
        {
            SteamIds = steamIds,
            DataPath = dataM.Success ? dataM.Groups[1].Value : null,
            NexusName = nexusM.Success ? nexusM.Groups[1].Value : null,
        };
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Mo2GameParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/Mo2Game.cs tools/ManifestMiner/Mo2GameParser.cs tests/ModManager.Tests/Miner/Mo2GameParserTests.cs
git commit -m "feat(miner): MO2 basic_games parser (Python class -> facts)"
```

---

### Task 2: Engine reverse-map (unambiguous mod path → engine)

**Files:**
- Create: `tools/ManifestMiner/EngineFromModPath.cs`
- Test: `tests/ModManager.Tests/Miner/EngineFromModPathTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/EngineFromModPathTests.cs`:

```csharp
using ManifestMiner;

namespace ModManager.Tests.Miner;

public class EngineFromModPathTests
{
    [Theory]
    [InlineData("Data", "bethesda")]
    [InlineData("data", "bethesda")]                 // case-insensitive
    [InlineData("BepInEx/plugins", "bepinex")]
    [InlineData("BepInEx\\plugins", "bepinex")]      // backslash normalized
    [InlineData("addons", "source")]
    [InlineData("mod", "fromsoft")]
    public void Maps_unambiguous_paths(string path, string engine)
        => Assert.Equal(engine, EngineFromModPath.Infer(path));

    [Theory]
    [InlineData("Mods")]    // smapi AND melonloader
    [InlineData("mods")]    // minecraft AND custom
    [InlineData("")]        // empty
    [InlineData(null)]
    [InlineData("weird/path")]
    public void Leaves_ambiguous_or_unknown_null(string? path)
        => Assert.Null(EngineFromModPath.Infer(path));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EngineFromModPathTests"`
Expected: FAIL — `EngineFromModPath` does not exist.

- [ ] **Step 3: Implement**

`tools/ManifestMiner/EngineFromModPath.cs`:

```csharp
namespace ManifestMiner;

/// <summary>Conservative reverse-map: a mod path -> the single engine it unambiguously implies, else
/// null. Wrong engine is worse than null (the launcher folder-detects at runtime), so anything that
/// maps to more than one engine ("Mods", "mods") or is unknown returns null.</summary>
public static class EngineFromModPath
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data"] = "bethesda",
            ["BepInEx/plugins"] = "bepinex",
            ["addons"] = "source",
            ["mod"] = "fromsoft",
        };

    public static string? Infer(string? modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath)) return null;
        var normalized = modPath.Replace('\\', '/').Trim();
        return Map.TryGetValue(normalized, out var engine) ? engine : null;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EngineFromModPathTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/EngineFromModPath.cs tests/ModManager.Tests/Miner/EngineFromModPathTests.cs
git commit -m "feat(miner): conservative mod-path -> engine reverse-map"
```

---

### Task 3: Mo2Enrich — merge MO2 facts onto the backbone by Steam id

**Files:**
- Create: `tools/ManifestMiner/Mo2Enrich.cs`
- Test: `tests/ModManager.Tests/Miner/Mo2EnrichTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/Mo2EnrichTests.cs`:

```csharp
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class Mo2EnrichTests
{
    private static GameManifest Backbone(params (string id, string steamId)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.id, Name = g.id, Stores = new StoreIds { SteamAppId = g.steamId },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        }).ToList(),
    };

    [Fact]
    public void Fills_modPath_and_engine_for_a_matched_game()
    {
        var backbone = Backbone(("skyrim-se", "489830"));
        var mo2 = new[] { new Mo2Game("Skyrim SE") { SteamIds = new[] { "489830" }, DataPath = "Data" } };

        var result = Mo2Enrich.Apply(backbone, mo2);

        var e = result.Games.Single(g => g.Id == "skyrim-se");
        Assert.Equal("Data", e.ModPath);
        Assert.Equal("bethesda", e.Engine);                  // unambiguous reverse-map
        Assert.Contains("mo2", e.Provenance.Sources);        // provenance records the enrichment
        Assert.Contains("ludusavi", e.Provenance.Sources);   // original source preserved
    }

    [Fact]
    public void Matches_on_any_steam_id_in_the_mo2_list()
    {
        var backbone = Backbone(("witcher-3", "292030"));
        var mo2 = new[] { new Mo2Game("Witcher 3") { SteamIds = new[] { "499450", "292030" }, DataPath = "Mods" } };

        var e = Mo2Enrich.Apply(backbone, mo2).Games.Single(g => g.Id == "witcher-3");
        Assert.Equal("Mods", e.ModPath);
        Assert.Null(e.Engine); // "Mods" is ambiguous -> engine stays null
    }

    [Fact]
    public void Leaves_unmatched_backbone_entries_untouched()
    {
        var backbone = Backbone(("a", "111"), ("b", "222"));
        var mo2 = new[] { new Mo2Game("A") { SteamIds = new[] { "111" }, DataPath = "Data" } };

        var result = Mo2Enrich.Apply(backbone, mo2);
        var b = result.Games.Single(g => g.Id == "b");
        Assert.Null(b.ModPath);
        Assert.DoesNotContain("mo2", b.Provenance.Sources);
    }

    [Fact]
    public void Does_not_set_modPath_when_data_path_is_empty()
    {
        var backbone = Backbone(("valheim", "892970"));
        var mo2 = new[] { new Mo2Game("Valheim") { SteamIds = new[] { "892970" }, DataPath = "" } };

        var e = Mo2Enrich.Apply(backbone, mo2).Games.Single(g => g.Id == "valheim");
        Assert.Null(e.ModPath);                          // empty DataPath -> no modPath
        Assert.Contains("mo2", e.Provenance.Sources);     // still records the match
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Mo2EnrichTests"`
Expected: FAIL — `Mo2Enrich` does not exist.

- [ ] **Step 3: Implement**

`tools/ManifestMiner/Mo2Enrich.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: overlay MO2 facts onto the Ludusavi backbone, keyed by Steam id. Fills modPath
/// (from a non-empty GameDataPath), engine (only when the path is unambiguous), and nexusDomain
/// (from GameNexusName when present). Adds "mo2" to provenance for every matched entry. Unmatched
/// entries are returned unchanged.</summary>
public static class Mo2Enrich
{
    public static GameManifest Apply(GameManifest backbone, IReadOnlyList<Mo2Game> mo2Games)
    {
        // Index MO2 facts by each Steam id they claim.
        var bySteam = new Dictionary<string, Mo2Game>(StringComparer.Ordinal);
        foreach (var g in mo2Games)
            foreach (var id in g.SteamIds)
                bySteam.TryAdd(id, g);

        var games = backbone.Games.Select(entry =>
        {
            var appId = entry.Stores.SteamAppId;
            if (appId is null || !bySteam.TryGetValue(appId, out var m))
                return entry;

            var modPath = string.IsNullOrEmpty(m.DataPath) ? entry.ModPath : m.DataPath;
            var engine = entry.Engine ?? EngineFromModPath.Infer(m.DataPath);
            var nexus = entry.NexusDomain ?? (string.IsNullOrWhiteSpace(m.NexusName) ? null : m.NexusName);
            var sources = entry.Provenance.Sources.Contains("mo2")
                ? entry.Provenance.Sources
                : entry.Provenance.Sources.Append("mo2").ToList();

            return entry with
            {
                ModPath = modPath,
                Engine = engine,
                NexusDomain = nexus,
                Provenance = entry.Provenance with { Sources = sources },
            };
        }).ToList();

        return backbone with { Games = games };
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Mo2EnrichTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/Mo2Enrich.cs tests/ModManager.Tests/Miner/Mo2EnrichTests.cs
git commit -m "feat(miner): merge MO2 facts onto the backbone by Steam id"
```

---

### Task 4: CLI — combined Ludusavi + MO2 run, enriched draft + coverage

**Files:**
- Modify: `tools/ManifestMiner/Program.cs`

- [ ] **Step 1: Add the MO2 fetch + combined flow**

Extend `Program.cs`: keep the existing Ludusavi flow; when `--with-mo2` is passed, after building the Ludusavi backbone, fetch the MO2 `basic_games` repo zipball (`https://codeload.github.com/ModOrganizer2/modorganizer-basic_games/zip/refs/heads/master`), read each `games/game_*.py` from the zip via `System.IO.Compression.ZipArchive`, parse with `Mo2GameParser`, enrich via `Mo2Enrich.Apply`, validate, and write `out/manifest-draft.json` + `out/enrichment-summary.md`. Support `--mo2-dir <path>` to read local `.py` files instead of fetching (offline-testable). Coverage report: backbone size, MO2 games parsed, matched-by-steam, modPath filled, engine inferred, nexusDomain filled.

```csharp
// ... after `var candidates = LudusaviNormalize.ToCandidates(parsed);` and building `manifest` ...

if (args.Contains("--with-mo2"))
{
    var mo2Texts = await LoadMo2Texts(GetArg(args, "--mo2-dir"));
    var mo2Games = mo2Texts.Select(Mo2GameParser.Parse).OfType<Mo2Game>().ToList();
    var enriched = Mo2Enrich.Apply(manifest, mo2Games);
    var validatedEnriched = ManifestValidator.Validate(enriched, EnginePresets.Presets.Keys.ToHashSet());

    File.WriteAllText(Path.Combine(outDir, "manifest-draft.json"),
        JsonSerializer.Serialize(validatedEnriched.Manifest, ManifestJson.Options));

    var matched = validatedEnriched.Manifest.Games.Count(g => g.Provenance.Sources.Contains("mo2"));
    var withMod = validatedEnriched.Manifest.Games.Count(g => g.ModPath is not null);
    var withEngine = validatedEnriched.Manifest.Games.Count(g => g.Engine is not null);
    var withNexus = validatedEnriched.Manifest.Games.Count(g => g.NexusDomain is not null);
    var es = new StringBuilder();
    es.AppendLine("# MO2 enrichment — draft");
    es.AppendLine();
    es.AppendLine($"- backbone games: {validatedEnriched.Manifest.Games.Count}");
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

// helper appended at the bottom:
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
```

(Adjust the existing `Program.cs` so `outDir`, `manifest`, and `GetArg` are in scope where this block runs; keep the Ludusavi-only path working when `--with-mo2` is absent.)

- [ ] **Step 2: Offline smoke-run**

Run:
```bash
mkdir -p /tmp/mo2 && printf 'class V(BasicGame):\n    GameName = "Valheim"\n    GameSteamId = [892970]\n    GameDataPath = ""\n' > /tmp/mo2/game_v.py
printf 'Valheim:\n  steam:\n    id: 892970\n' > /tmp/ludu.yaml
dotnet run --project tools/ManifestMiner -- --file /tmp/ludu.yaml --with-mo2 --mo2-dir /tmp/mo2
```
Expected: prints "MO2 enrichment: 1 matched, 0 modPaths (empty DataPath), 0 engines"; `out/manifest-draft.json` has the valheim entry with `mo2` in provenance.

- [ ] **Step 3: Commit**

```bash
git add tools/ManifestMiner/Program.cs
git commit -m "feat(miner): combined Ludusavi+MO2 run -> enriched draft + coverage"
```

---

### Task 5: Full suite + scope clean

**Files:** none (verification only).

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — existing + new `Mo2GameParserTests`/`EngineFromModPathTests`/`Mo2EnrichTests`. `CorePurityTests` green.

- [ ] **Step 2: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: EMPTY (this slice is entirely under `tools/` + `tests/`; no Core change — `SaveDirHint` already landed in slice 1). No `src/ModManager.App`. Miner output gitignored; not merged into the shipped manifest.

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(miner): MO2 enrichment slice — full suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §8 multi-source merge → Tasks 1–4 (MO2 as the second source, merged by Steam id). `overrides` + Vortex are later slices. ✓

**Placeholder scan:** none. ✓

**Type consistency:** `Mo2Game` (Task 1) consumed by `Mo2Enrich` (Task 3); `Mo2GameParser.Parse → Mo2Game?`, `EngineFromModPath.Infer(string?) → string?`, `Mo2Enrich.Apply(GameManifest, IReadOnlyList<Mo2Game>) → GameManifest` consistent across impl + tests. Enrich emits real `GameManifestEntry` via `with` and re-validates through `ManifestValidator`. ✓ (One path typo to avoid: the file is `tools/ManifestMiner/Mo2Enrich.cs`.)

**Honest scoping:** engine set only on the unambiguous reverse-map; `""` DataPath → no modPath; numeric-only Nexus skipped; ~75-game coverage; draft-only, not shipped. Stated in Scope + the emitted summary. ✓

**Judgment flagged:** the parser is regex-over-source, not a Python parser — it deliberately skips files whose `GameSteamId`/`GameName` don't match the common forms (e.g. computed values, deep inheritance). That's acceptable for a draft; the coverage report shows how many of the ~75 parsed. Robustness improves if/when a specific game is missed and someone cares.
