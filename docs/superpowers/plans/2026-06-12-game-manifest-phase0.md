# Game Manifest — Phase 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the three hardcoded game-identity arrays (`KnownEngines`, `NexusDomains`, `PopularGames`) into one embedded `GameManifest`, with those classes becoming thin facades over it — changing zero observable behavior.

**Architecture:** A new pure-Core `Manifest/` module defines the manifest records, a validator, and an `EmbeddedGameManifest` that reads a baked-in `games-manifest.json`. The three legacy classes keep their exact public signatures but read their data from the manifest, filtered by per-entry **provenance tags** that reproduce each array's original membership. The existing `KnownEnginesTests`, `PopularGamesTests`, and `NexusGameDomainTests` pass **unchanged** — they are the parity proof. This is the seam the multi-game roadmap (spec: `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md`) rests on; Phase 0 draws it and proves it inert.

**Tech Stack:** .NET 10, C#, System.Text.Json (camelCase on disk — project rule), xUnit. No new package references. No network, no disk writes (read-only embedded resource).

---

## Scope

**In scope (Phase 0):** the manifest schema, validator, embedded snapshot generated from today's data, and the three facades. Pure refactor.

**Explicitly out of scope (later phases, separate plans):** remote fetch, signature verification, the `626-game-manifest` repo + miner, the settings toggle, GOG/Epic probing, the agentic-profile tail, and any change to the install-affecting catalogs (`KnownDirectInjectMod`, `KnownFramework`, `ToolCatalog` stay compiled C#). Do not build these here.

## The membership trap (read before Task 4)

The three arrays have **different, overlapping** memberships. A naive union breaks parity:

- `KnownEngines.Map` has exactly **12** app-ids → engine. It is checked *before* folder heuristics in `SteamGameImport`, so adding an app-id here changes auto-import classification.
- `NexusDomains.Map` has **12** app-ids → Nexus slug — including Windrose (`3041230`), Witchfire (`3156770`), Cyberpunk (`1091500`) which are **not** in `KnownEngines`.
- `PopularGames.All` has **10** games — including RimWorld (`294100`) which is in **neither** map, and carries engine `smapi`.

If the union let `KnownEngines.ByAppId` return an engine for every entry with that app-id, it would start classifying RimWorld, Witchfire, and Cyberpunk — a behavior change Phase 0 forbids. **Solution:** each manifest entry records `provenance.sources` listing which legacy arrays it came from (`"known-engines"`, `"nexus-domains"`, `"popular-games"`), and each facade filters on its own tag. The engine value is shared (verified consistent for the 9 games in both `KnownEngines` and `PopularGames`); only the *membership* differs, and provenance carries it.

The 16-game union (id · engine · steamAppId · nexusDomain · featured · provenance):

| id | engine | steamAppId | nexusDomain | featured | provenance |
|---|---|---|---|---|---|
| elden-ring | fromsoft | 1245620 | eldenring | – | known-engines, nexus-domains |
| dark-souls-iii | fromsoft | 374320 | – | – | known-engines |
| sekiro | fromsoft | 814380 | – | – | known-engines |
| armored-core-vi | fromsoft | 1888160 | – | – | known-engines |
| skyrim-se | bethesda | 489830 | skyrimspecialedition | 1 | known-engines, nexus-domains, popular-games |
| fallout-4 | bethesda | 377160 | fallout4 | 2 | known-engines, nexus-domains, popular-games |
| starfield | bethesda | 1716740 | starfield | 3 | known-engines, nexus-domains, popular-games |
| stardew-valley | smapi | 413150 | stardewvalley | 4 | known-engines, nexus-domains, popular-games |
| rimworld | smapi | 294100 | – | 5 | popular-games |
| valheim | bepinex | 892970 | valheim | 6 | known-engines, nexus-domains, popular-games |
| lethal-company | bepinex | 1966720 | lethalcompany | 7 | known-engines, nexus-domains, popular-games |
| palworld | ue-pak | 1623730 | palworld | 8 | known-engines, nexus-domains, popular-games |
| hogwarts-legacy | ue-pak | 990080 | hogwartslegacy | 9 | known-engines, nexus-domains, popular-games |
| cyberpunk-2077 | custom | 1091500 | cyberpunk2077 | 10 | nexus-domains, popular-games |
| windrose | *(null)* | 3041230 | windrose | – | nexus-domains |
| witchfire | *(null)* | 3156770 | witchfire | – | nexus-domains |

Note Windrose/Witchfire carry **null engine** — we do not invent `ue-pak` from a code comment. Popular-games entries also carry `modPath` (and Cyberpunk `fileExtensions`) to reproduce the exact `PopularGame` records.

---

## File Structure

- Create: `src/ModManager.Core/Manifest/GameManifest.cs` — the records (`StoreIds`, `ManifestProvenance`, `GameManifestEntry`, `GameManifest`), `ManifestJson` options, `ManifestSources` tag constants.
- Create: `src/ModManager.Core/Manifest/ManifestValidator.cs` — pure validation (skip unknown-engine, reject unsafe `modPath`).
- Create: `src/ModManager.Core/Manifest/EmbeddedGameManifest.cs` — reads + caches the embedded snapshot.
- Create: `src/ModManager.Core/Manifest/games-manifest.json` — the 16-game snapshot (embedded resource).
- Modify: `src/ModManager.Core/ModManager.Core.csproj` — mark the JSON as `EmbeddedResource`.
- Modify: `src/ModManager.Core/KnownEngines.cs` — facade over manifest.
- Modify: `src/ModManager.Core/NexusDomains.cs` — facade over manifest.
- Modify: `src/ModManager.Core/PopularGames.cs` — facade over manifest.
- Create: `tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs`
- Create: `tests/ModManager.Tests/Manifest/ManifestValidatorTests.cs`
- Create: `tests/ModManager.Tests/Manifest/EmbeddedGameManifestTests.cs`
- Create: `tests/ModManager.Tests/Manifest/ManifestInvariantsTests.cs`
- Create: `tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs`
- Unchanged (parity proof, must stay green): `tests/ModManager.Tests/{KnownEnginesTests,PopularGamesTests,NexusGameDomainTests,EnginePresetsTests}.cs`

**Test command (never bare root — the WinUI project hangs the build):**
`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

To run one test class: append ` --filter "FullyQualifiedName~ClassName"`.

---

### Task 1: Manifest records, JSON options, and source tags

**Files:**
- Create: `src/ModManager.Core/Manifest/GameManifest.cs`
- Test: `tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs`

- [ ] **Step 1: Write the failing camelCase round-trip test**

Create `tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs`:

```csharp
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// camelCase-on-disk rule: the launcher's JSON shares shape with the Electron predecessor.
// The string-contains assertion is what protects the convention — STJ reads case-insensitively,
// so a round-trip alone would pass even if keys serialized as PascalCase.
public class GameManifestJsonTests
{
    [Fact]
    public void Manifest_round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            SchemaVersion = 1,
            MinBinaryVersion = "0.6.0",
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "elden-ring",
                    Name = "Elden Ring",
                    Engine = "fromsoft",
                    Stores = new StoreIds { SteamAppId = "1245620" },
                    NexusDomain = "eldenring",
                    Provenance = new ManifestProvenance
                    {
                        Sources = new[] { ManifestSources.KnownEngines, ManifestSources.NexusDomains },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"steamAppId\"", json);
        Assert.Contains("\"nexusDomain\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"SteamAppId\"", json);

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options);
        Assert.NotNull(back);
        Assert.Equal("elden-ring", back!.Games[0].Id);
        Assert.Equal("1245620", back.Games[0].Stores.SteamAppId);
        Assert.Equal("fromsoft", back.Games[0].Engine);
        Assert.Contains(ManifestSources.NexusDomains, back.Games[0].Provenance.Sources);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameManifestJsonTests"`
Expected: FAIL — `ModManager.Core.Manifest` namespace / types do not exist (compile error).

- [ ] **Step 3: Create the records, options, and source tags**

Create `src/ModManager.Core/Manifest/GameManifest.cs` (the Core csproj has `ImplicitUsings` on, so `System`/`System.Collections.Generic`/`System.Linq` are implicit):

```csharp
using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>Per-store identifiers for one game. Only SteamAppId is populated/probed in Phase 0;
/// the rest exist so GOG/Epic/Game Pass slot in later without a schema migration.</summary>
public sealed record StoreIds
{
    public string? SteamAppId { get; init; }
    public string? GogId { get; init; }
    public string? EpicAppName { get; init; }
    public string? XboxStoreId { get; init; }
}

/// <summary>Which legacy arrays / mining sources contributed this entry, and its curation status.
/// In Phase 0 the sources are the legacy-array tags in <see cref="ManifestSources"/>; the facades
/// filter on them to reproduce each array's exact original membership.</summary>
public sealed record ManifestProvenance
{
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public string Status { get; init; } = "curated";
}

/// <summary>One game's identity + mod-layout overrides. Descriptive data only — it never describes
/// how to enable/disable a mod (that stays compiled, per the operating laws). ModPath is the one
/// trust-sensitive field; <see cref="ManifestValidator"/> gates it.</summary>
public sealed record GameManifestEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Engine { get; init; }              // null when the engine isn't known (nexus-only entries)
    public StoreIds Stores { get; init; } = new();
    public string? NexusDomain { get; init; }
    public int? CurseforgeGameId { get; init; }
    public string? ModPath { get; init; }             // override to the engine-default mod folder
    public IReadOnlyList<string>? FileExtensions { get; init; }
    public string? GroupingRule { get; init; }
    public int? Featured { get; init; }               // quick-pick rank; null = not in the quick-pick list
    public ManifestProvenance Provenance { get; init; } = new();
}

/// <summary>The on-disk / embedded manifest: a schema version plus the game list.</summary>
public sealed record GameManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string? GeneratedUtc { get; init; }
    public string? MinBinaryVersion { get; init; }
    public IReadOnlyList<GameManifestEntry> Games { get; init; } = Array.Empty<GameManifestEntry>();
}

/// <summary>Provenance source tags. Phase 0 uses the legacy-array names so the facades can
/// reproduce each array's original membership exactly. The miner adds its own tags in Phase 1.</summary>
public static class ManifestSources
{
    public const string KnownEngines = "known-engines";
    public const string NexusDomains = "nexus-domains";
    public const string PopularGames = "popular-games";
}

/// <summary>Serializer options for the manifest: camelCase on disk (project rule), indented,
/// case-insensitive read. Mirrors <see cref="AtomicJson"/>'s policy.</summary>
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameManifestJsonTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/GameManifest.cs tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs
git commit -m "feat(manifest): GameManifest records + camelCase JSON options"
```

---

### Task 2: ManifestValidator — skip unknown engines, reject unsafe modPath

**Files:**
- Create: `src/ModManager.Core/Manifest/ManifestValidator.cs`
- Test: `tests/ModManager.Tests/Manifest/ManifestValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/Manifest/ManifestValidatorTests.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestValidatorTests
{
    private static GameManifest Wrap(params GameManifestEntry[] games)
        => new() { Games = games };

    private static GameManifestEntry Entry(string id, string? engine = "bethesda", string? modPath = null)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            ModPath = modPath,
            Stores = new StoreIds { SteamAppId = "1" },
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
        };

    private static readonly IReadOnlySet<string> KnownEngineKeys =
        new HashSet<string> { "bethesda", "ue-pak", "fromsoft", "custom" };

    [Fact]
    public void Unknown_engine_is_skipped_not_fatal_and_reported()
    {
        var result = ManifestValidator.Validate(
            Wrap(Entry("good", "bethesda"), Entry("future", "rpgmaker-mz")),
            KnownEngineKeys);

        Assert.DoesNotContain(result.Manifest.Games, g => g.Id == "future");
        Assert.Contains(result.Manifest.Games, g => g.Id == "good");
        Assert.Contains("future", result.SkippedUnknownEngines);
    }

    [Fact]
    public void Null_engine_entry_is_kept_not_skipped()
    {
        // nexus-only entries (Windrose / Witchfire) carry no engine and must survive.
        var result = ManifestValidator.Validate(Wrap(Entry("witchfire", engine: null)), KnownEngineKeys);
        Assert.Contains(result.Manifest.Games, g => g.Id == "witchfire");
        Assert.Empty(result.SkippedUnknownEngines);
    }

    [Theory]
    [InlineData("C:/Windows/System32")]   // absolute
    [InlineData("../../escape")]           // traversal
    [InlineData("a/../b")]                 // traversal mid-path
    [InlineData("D:relative")]             // drive-qualified
    public void Unsafe_modPath_is_rejected(string modPath)
    {
        var result = ManifestValidator.Validate(Wrap(Entry("bad", "bethesda", modPath)), KnownEngineKeys);
        Assert.DoesNotContain(result.Manifest.Games, g => g.Id == "bad");
        Assert.Contains("bad", result.RejectedEntries);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("Content/Paks/~mods")]
    [InlineData("Pal/Content/Paks/~mods")]
    public void Clean_relative_modPath_passes(string modPath)
    {
        var result = ManifestValidator.Validate(Wrap(Entry("ok", "bethesda", modPath)), KnownEngineKeys);
        Assert.Contains(result.Manifest.Games, g => g.Id == "ok");
        Assert.Empty(result.RejectedEntries);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestValidatorTests"`
Expected: FAIL — `ManifestValidator` does not exist.

- [ ] **Step 3: Implement the validator**

Create `src/ModManager.Core/Manifest/ManifestValidator.cs`:

```csharp
namespace ModManager.Core.Manifest;

/// <summary>Result of validating a manifest: the surviving entries, plus what was dropped and why.</summary>
public sealed record ManifestValidationResult(
    GameManifest Manifest,
    IReadOnlyList<string> SkippedUnknownEngines,
    IReadOnlyList<string> RejectedEntries);

/// <summary>
/// Pure validation gate for a manifest from any source (embedded or, later, remote). Two rules:
///  - An entry whose non-null engine is unknown to this binary is SKIPPED (forward-compat: an old
///    binary reading a newer manifest simply doesn't see games it can't handle). Null engine is fine.
///  - An entry with an unsafe ModPath (absolute / drive-qualified / contains a ".." segment) is
///    REJECTED. ModPath is the one trust-sensitive field; the forbidden-paths gate at intake is the
///    downstream backstop, this is defense in depth.
/// </summary>
public static class ManifestValidator
{
    public static ManifestValidationResult Validate(GameManifest manifest, IReadOnlySet<string> knownEngines)
    {
        var kept = new List<GameManifestEntry>();
        var skipped = new List<string>();
        var rejected = new List<string>();

        foreach (var g in manifest.Games)
        {
            if (g.Engine is { } engine && !knownEngines.Contains(engine))
            {
                skipped.Add(g.Id);
                continue;
            }
            if (g.ModPath is { } path && !IsSafeRelativePath(path))
            {
                rejected.Add(g.Id);
                continue;
            }
            kept.Add(g);
        }

        return new ManifestValidationResult(
            manifest with { Games = kept },
            skipped,
            rejected);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;          // absolute
        if (path.Contains(':')) return false;               // drive-qualified (e.g. "D:relative")
        var segments = path.Split('/', '\\');
        return !segments.Contains("..");                    // traversal
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestValidatorTests"`
Expected: PASS (all 4 theories + 2 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/ManifestValidator.cs tests/ModManager.Tests/Manifest/ManifestValidatorTests.cs
git commit -m "feat(manifest): validator — skip unknown engines, reject unsafe modPath"
```

---

### Task 3: The embedded snapshot + EmbeddedGameManifest loader

**Files:**
- Create: `src/ModManager.Core/Manifest/games-manifest.json`
- Modify: `src/ModManager.Core/ModManager.Core.csproj`
- Create: `src/ModManager.Core/Manifest/EmbeddedGameManifest.cs`
- Test: `tests/ModManager.Tests/Manifest/EmbeddedGameManifestTests.cs`

- [ ] **Step 1: Author the 16-game snapshot**

Create `src/ModManager.Core/Manifest/games-manifest.json` (camelCase; this is the generated-from-legacy-data union — see the membership table above):

```json
{
  "schemaVersion": 1,
  "generatedUtc": "2026-06-12T00:00:00Z",
  "minBinaryVersion": "0.6.0",
  "games": [
    { "id": "elden-ring", "name": "Elden Ring", "engine": "fromsoft", "stores": { "steamAppId": "1245620" }, "nexusDomain": "eldenring", "provenance": { "sources": ["known-engines", "nexus-domains"], "status": "curated" } },
    { "id": "dark-souls-iii", "name": "Dark Souls III", "engine": "fromsoft", "stores": { "steamAppId": "374320" }, "provenance": { "sources": ["known-engines"], "status": "curated" } },
    { "id": "sekiro", "name": "Sekiro", "engine": "fromsoft", "stores": { "steamAppId": "814380" }, "provenance": { "sources": ["known-engines"], "status": "curated" } },
    { "id": "armored-core-vi", "name": "Armored Core VI", "engine": "fromsoft", "stores": { "steamAppId": "1888160" }, "provenance": { "sources": ["known-engines"], "status": "curated" } },
    { "id": "skyrim-se", "name": "Skyrim Special Edition", "engine": "bethesda", "stores": { "steamAppId": "489830" }, "nexusDomain": "skyrimspecialedition", "modPath": "Data", "featured": 1, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "fallout-4", "name": "Fallout 4", "engine": "bethesda", "stores": { "steamAppId": "377160" }, "nexusDomain": "fallout4", "modPath": "Data", "featured": 2, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "starfield", "name": "Starfield", "engine": "bethesda", "stores": { "steamAppId": "1716740" }, "nexusDomain": "starfield", "modPath": "Data", "featured": 3, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "stardew-valley", "name": "Stardew Valley", "engine": "smapi", "stores": { "steamAppId": "413150" }, "nexusDomain": "stardewvalley", "modPath": "Mods", "featured": 4, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "rimworld", "name": "RimWorld", "engine": "smapi", "stores": { "steamAppId": "294100" }, "modPath": "Mods", "featured": 5, "provenance": { "sources": ["popular-games"], "status": "curated" } },
    { "id": "valheim", "name": "Valheim", "engine": "bepinex", "stores": { "steamAppId": "892970" }, "nexusDomain": "valheim", "modPath": "BepInEx/plugins", "featured": 6, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "lethal-company", "name": "Lethal Company", "engine": "bepinex", "stores": { "steamAppId": "1966720" }, "nexusDomain": "lethalcompany", "modPath": "BepInEx/plugins", "featured": 7, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "palworld", "name": "Palworld", "engine": "ue-pak", "stores": { "steamAppId": "1623730" }, "nexusDomain": "palworld", "modPath": "Pal/Content/Paks/~mods", "featured": 8, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "hogwarts-legacy", "name": "Hogwarts Legacy", "engine": "ue-pak", "stores": { "steamAppId": "990080" }, "nexusDomain": "hogwartslegacy", "modPath": "Phoenix/Content/Paks/~mods", "featured": 9, "provenance": { "sources": ["known-engines", "nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "cyberpunk-2077", "name": "Cyberpunk 2077", "engine": "custom", "stores": { "steamAppId": "1091500" }, "nexusDomain": "cyberpunk2077", "modPath": "archive/pc/mod", "fileExtensions": ["archive"], "featured": 10, "provenance": { "sources": ["nexus-domains", "popular-games"], "status": "curated" } },
    { "id": "windrose", "name": "Windrose", "stores": { "steamAppId": "3041230" }, "nexusDomain": "windrose", "provenance": { "sources": ["nexus-domains"], "status": "curated" } },
    { "id": "witchfire", "name": "Witchfire", "stores": { "steamAppId": "3156770" }, "nexusDomain": "witchfire", "provenance": { "sources": ["nexus-domains"], "status": "curated" } }
  ]
}
```

- [ ] **Step 2: Mark the JSON as an embedded resource**

Edit `src/ModManager.Core/ModManager.Core.csproj` — add a new `ItemGroup` (after the existing `PackageReference` group):

```xml
  <ItemGroup>
    <EmbeddedResource Include="Manifest\games-manifest.json" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing loader test**

Create `tests/ModManager.Tests/Manifest/EmbeddedGameManifestTests.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class EmbeddedGameManifestTests
{
    [Fact]
    public void Loads_the_sixteen_game_union()
        => Assert.Equal(16, EmbeddedGameManifest.Current.Games.Count);

    [Fact]
    public void Elden_ring_resolves_engine_and_nexus_domain()
    {
        var er = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "elden-ring");
        Assert.Equal("fromsoft", er.Engine);
        Assert.Equal("1245620", er.Stores.SteamAppId);
        Assert.Equal("eldenring", er.NexusDomain);
    }

    [Fact]
    public void Nexus_only_games_carry_no_engine()
    {
        var witchfire = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "witchfire");
        Assert.Null(witchfire.Engine);
        Assert.Equal("witchfire", witchfire.NexusDomain);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EmbeddedGameManifestTests"`
Expected: FAIL — `EmbeddedGameManifest` does not exist.

- [ ] **Step 5: Implement the loader**

Create `src/ModManager.Core/Manifest/EmbeddedGameManifest.cs`:

```csharp
using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>
/// The game manifest baked into the binary — always present, always offline-safe. The single
/// source of truth the KnownEngines / NexusDomains / PopularGames facades read from. Loaded once
/// (cached), validated on load. In Phase 1 a remote source merges over this; the embedded copy is
/// the fallback the remote path can never break.
/// </summary>
public static class EmbeddedGameManifest
{
    private static readonly Lazy<GameManifest> Cached = new(Load);

    /// <summary>The validated embedded manifest (cached after first access).</summary>
    public static GameManifest Current => Cached.Value;

    private static GameManifest Load()
    {
        var asm = typeof(EmbeddedGameManifest).Assembly;
        // Match by suffix so a RootNamespace / folder change can't silently break resource lookup.
        var resourceName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("games-manifest.json", StringComparison.Ordinal));

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("embedded games-manifest.json stream was null");

        var raw = JsonSerializer.Deserialize<GameManifest>(stream, ManifestJson.Options)
            ?? throw new InvalidOperationException("games-manifest.json failed to deserialize");

        var knownEngines = EnginePresets.Presets.Keys.ToHashSet();
        return ManifestValidator.Validate(raw, knownEngines).Manifest;
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EmbeddedGameManifestTests"`
Expected: PASS. (If `Loads_the_sixteen_game_union` reports fewer than 16, the validator dropped a row — a JSON typo in an engine key or modPath; fix the JSON, not the test.)

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/Manifest/games-manifest.json src/ModManager.Core/ModManager.Core.csproj src/ModManager.Core/Manifest/EmbeddedGameManifest.cs tests/ModManager.Tests/Manifest/EmbeddedGameManifestTests.cs
git commit -m "feat(manifest): embedded 16-game snapshot + cached loader"
```

---

### Task 4: KnownEngines facade

**Files:**
- Modify: `src/ModManager.Core/KnownEngines.cs`
- Test: `tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs` (new — guards the union's leak risk)
- Parity proof (unchanged): `tests/ModManager.Tests/KnownEnginesTests.cs`

- [ ] **Step 1: Write the failing membership-leak regression test**

The existing `KnownEnginesTests` proves the 12 known app-ids still resolve. This NEW test proves the union does **not** leak — games that were only in `NexusDomains` or `PopularGames` must still return null from `KnownEngines`. The old hardcoded `Map` physically couldn't contain them; the union can, so this guards the exact risk.

Create `tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests.Manifest;

// The manifest is a UNION of three arrays with different memberships. These tests guard against
// a game leaking from one facade into another now that they share one backing store.
public class FacadeMembershipTests
{
    [Theory]
    [InlineData("3156770")] // Witchfire — nexus-domains only
    [InlineData("3041230")] // Windrose — nexus-domains only
    [InlineData("1091500")] // Cyberpunk — nexus-domains + popular-games, never known-engines
    [InlineData("294100")]  // RimWorld — popular-games only
    public void KnownEngines_does_not_leak_non_known_engine_games(string appId)
        => Assert.Null(KnownEngines.ByAppId(appId));
}
```

- [ ] **Step 2: Run it against the current implementation**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeMembershipTests"`
Expected: PASS (the old hardcoded `Map` doesn't contain these). This test documents the invariant before the refactor so a regression during it goes red.

- [ ] **Step 3: Refactor KnownEngines to read from the manifest**

Replace the body of `src/ModManager.Core/KnownEngines.cs` (keep the `namespace ModManager.Core;` and public signatures `ByAppId(string?)` and `AllMappedEngines` exactly):

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID -> engine map. The reliable signal for games whose install folder carries
/// no detectable signature — notably FromSoftware's proprietary engine (Elden Ring et al.), where
/// only the app id tells you it's a Mod Engine 2 game. Checked before folder heuristics.
///
/// Facade over <see cref="EmbeddedGameManifest"/>: reads only entries tagged with the
/// "known-engines" provenance source, so its membership is exactly what it always was even though
/// the manifest is a union of three legacy arrays. Every value is a real key in
/// <see cref="EnginePresets.Presets"/> (guarded by tests).
/// </summary>
public static class KnownEngines
{
    private static readonly IReadOnlyDictionary<string, string> Map = Build();

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EmbeddedGameManifest.Current.Games)
        {
            if (g.Provenance.Sources.Contains(ManifestSources.KnownEngines)
                && g.Stores.SteamAppId is { } appId
                && g.Engine is { } engine)
            {
                map[appId] = engine;
            }
        }
        return map;
    }

    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var e) ? e : null;

    public static IEnumerable<string> AllMappedEngines => Map.Values.Distinct();
}
```

- [ ] **Step 4: Run the parity + membership tests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~KnownEnginesTests|FullyQualifiedName~FacadeMembershipTests"`
Expected: PASS — all of `KnownEnginesTests` (unchanged) plus the membership test. If `KnownEnginesTests` goes red, the manifest's `known-engines` provenance tags or engine values are wrong; fix the JSON.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/KnownEngines.cs tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs
git commit -m "refactor(manifest): KnownEngines reads from the embedded manifest"
```

---

### Task 5: NexusDomains facade

**Files:**
- Modify: `src/ModManager.Core/NexusDomains.cs`
- Test: add to `tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs`
- Parity proof (unchanged): `tests/ModManager.Tests/NexusGameDomainTests.cs`

- [ ] **Step 1: Add the failing membership-leak regression test**

Append to `tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs` (inside the class):

```csharp
    [Theory]
    [InlineData("374320")]  // Dark Souls III — known-engines only, no Nexus slug
    [InlineData("814380")]  // Sekiro — known-engines only
    [InlineData("1888160")] // Armored Core VI — known-engines only
    [InlineData("294100")]  // RimWorld — popular-games only, no Nexus slug
    public void NexusDomains_does_not_leak_games_without_a_slug(string appId)
        => Assert.Null(NexusDomains.ByAppId(appId));
```

- [ ] **Step 2: Run it against the current implementation**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeMembershipTests"`
Expected: PASS (the old hardcoded `NexusDomains.Map` doesn't contain these app-ids).

- [ ] **Step 3: Refactor NexusDomains to read from the manifest**

Replace the body of `src/ModManager.Core/NexusDomains.cs` (keep `ByAppId(string?)` and `Effective(GameEntry)` exactly):

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID → Nexus Mods game-domain slug map. Nexus keys games by a URL slug
/// (nexusmods.com/&lt;slug&gt;), not a numeric id — and md5 metadata identify needs that slug.
///
/// Facade over <see cref="EmbeddedGameManifest"/>: reads only entries tagged with the
/// "nexus-domains" provenance source, preserving its original membership (which includes games not
/// in <see cref="KnownEngines"/>, e.g. Windrose/Witchfire/Cyberpunk). An unmapped app id leaves the
/// domain unset (metadata identify no-ops cleanly).
/// </summary>
public static class NexusDomains
{
    private static readonly IReadOnlyDictionary<string, string> Map = Build();

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EmbeddedGameManifest.Current.Games)
        {
            if (g.Provenance.Sources.Contains(ManifestSources.NexusDomains)
                && g.Stores.SteamAppId is { } appId
                && g.NexusDomain is { } domain)
            {
                map[appId] = domain;
            }
        }
        return map;
    }

    /// <summary>The Nexus domain slug for a Steam app id, or null when unmapped / id is null/empty.</summary>
    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var d) ? d : null;

    /// <summary>
    /// The effective Nexus domain for a game: its stored <see cref="GameEntry.NexusGameDomain"/> if
    /// set, else resolved from the Steam app id. Read-time fallback so games registered BEFORE the
    /// domain was set on add still resolve a domain for md5 metadata identify, with no migration.
    /// </summary>
    public static string? Effective(GameEntry game)
        => !string.IsNullOrWhiteSpace(game.NexusGameDomain) ? game.NexusGameDomain : ByAppId(game.SteamAppId);
}
```

- [ ] **Step 4: Run the parity + membership tests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~NexusGameDomainTests|FullyQualifiedName~FacadeMembershipTests"`
Expected: PASS. If a Nexus-dependent test elsewhere (e.g. metadata identify) reads a slug, it still resolves because the 12 slugs are unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/NexusDomains.cs tests/ModManager.Tests/Manifest/FacadeMembershipTests.cs
git commit -m "refactor(manifest): NexusDomains reads from the embedded manifest"
```

---

### Task 6: PopularGames facade

**Files:**
- Modify: `src/ModManager.Core/PopularGames.cs`
- Parity proof (unchanged): `tests/ModManager.Tests/PopularGamesTests.cs`

- [ ] **Step 1: Confirm the parity test pins order + fields (no new test needed)**

`PopularGamesTests` already asserts the exact 10-game order, Skyrim's full field set, Cyberpunk's `archive` override, and that every engine is a real preset. That is the spec for this facade. The refactor must keep it green. (No membership-leak test needed here: `PopularGames.All` is a self-contained list, not an app-id lookup — nothing external can leak *into* it.)

- [ ] **Step 2: Refactor PopularGames to project from the manifest**

Replace the body of `src/ModManager.Core/PopularGames.cs`. **Keep the `PopularGame` record exactly as-is** (the WinUI `AddGameDialog` binds to its properties — see `src/ModManager.App/AddGameDialog.xaml.cs:47,241`); only `All` and `Find` change their backing:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// One curated quick-pick game. Picking it in the Add Game wizard pre-fills the engine, mod
/// folder, and Steam App ID. <see cref="Engine"/> is an <see cref="EnginePresets.Presets"/> key.
/// <see cref="FileExtensions"/> is an optional override for games whose engine preset's default
/// extensions don't match (e.g. Cyberpunk's "custom" engine ships .pak, but its mods are .archive).
/// </summary>
public sealed record PopularGame(
    string Id,
    string Name,
    string Engine,
    string ModPath,
    string SteamAppId)
{
    public IReadOnlyList<string>? FileExtensions { get; init; }
}

/// <summary>
/// Curated catalog of popular moddable games for the Add Game wizard's quick-pick. Facade over
/// <see cref="EmbeddedGameManifest"/>: projects entries tagged with the "popular-games" provenance
/// source, ordered by their <see cref="GameManifestEntry.Featured"/> rank. The list order is
/// intentional and asserted by tests.
/// </summary>
public static class PopularGames
{
    public static IReadOnlyList<PopularGame> All { get; } = Build();

    private static IReadOnlyList<PopularGame> Build()
        => EmbeddedGameManifest.Current.Games
            .Where(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames))
            .OrderBy(g => g.Featured ?? int.MaxValue)
            .Select(g => new PopularGame(g.Id, g.Name, g.Engine!, g.ModPath!, g.Stores.SteamAppId!)
            {
                FileExtensions = g.FileExtensions,
            })
            .ToList();

    /// <summary>Look up a game by id; null when unknown (or the id is null/empty).</summary>
    public static PopularGame? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(g => g.Id == id);
}
```

The `!` on `Engine`/`ModPath`/`SteamAppId` is safe because Task 7's invariants test asserts every `popular-games` entry has all three non-null; if the JSON ever violates that, the invariants test fails loudly rather than a null slipping through.

- [ ] **Step 3: Run the parity test**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PopularGamesTests"`
Expected: PASS — all 7 facts, including `Catalog_has_the_ten_games_in_order` and `Cyberpunk_carries_the_archive_file_extensions_override`. If order is wrong, a `featured` rank in the JSON is off.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/PopularGames.cs
git commit -m "refactor(manifest): PopularGames projects from the embedded manifest"
```

---

### Task 7: Manifest invariants test (the banked honor-the-builders win)

**Files:**
- Create: `tests/ModManager.Tests/Manifest/ManifestInvariantsTests.cs`

This is the 2026-05-31 spec's `CatalogInvariantsTests` idea, applied to the manifest: machine-enforced rules that guard every future hand-edit or miner-generated entry, with the compiler still behind the types.

- [ ] **Step 1: Write the invariants test**

Create `tests/ModManager.Tests/Manifest/ManifestInvariantsTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestInvariantsTests
{
    private static IReadOnlyList<GameManifestEntry> Games => EmbeddedGameManifest.Current.Games;

    [Fact]
    public void Ids_are_unique()
    {
        var dupes = Games.GroupBy(g => g.Id).Where(grp => grp.Count() > 1).Select(grp => grp.Key).ToArray();
        Assert.Empty(dupes);
    }

    [Fact]
    public void Every_entry_has_id_name_and_a_steam_app_id()
    {
        foreach (var g in Games)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Id), $"empty id: {g.Name}");
            Assert.False(string.IsNullOrWhiteSpace(g.Name), $"empty name: {g.Id}");
            Assert.False(string.IsNullOrWhiteSpace(g.Stores.SteamAppId), $"no steam app id: {g.Id}");
        }
    }

    [Fact]
    public void Every_non_null_engine_is_a_real_preset()
    {
        foreach (var g in Games.Where(g => g.Engine is not null))
            Assert.True(EnginePresets.Presets.ContainsKey(g.Engine!), $"{g.Id}: unknown engine '{g.Engine}'");
    }

    [Fact]
    public void Every_entry_has_a_provenance_source()
    {
        foreach (var g in Games)
            Assert.NotEmpty(g.Provenance.Sources);
    }

    [Fact]
    public void Popular_games_carry_the_fields_the_quick_pick_projection_needs()
    {
        foreach (var g in Games.Where(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames)))
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Engine), $"{g.Id}: popular entry needs engine");
            Assert.False(string.IsNullOrWhiteSpace(g.ModPath), $"{g.Id}: popular entry needs modPath");
            Assert.NotNull(g.Featured);
        }
    }

    [Fact]
    public void No_field_carries_a_url_or_binary_path()
    {
        // honor-the-builders: layer-1a identity data never carries a binary or a download URL.
        foreach (var g in Games)
        {
            var values = new[] { g.Id, g.Name, g.Engine, g.NexusDomain, g.ModPath, g.GroupingRule }
                .Concat(g.FileExtensions ?? Array.Empty<string>())
                .Where(v => v is not null)!;
            foreach (var v in values)
            {
                Assert.DoesNotContain("http://", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".dll", v, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".exe", v, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Mod_paths_are_safe_relative_paths()
    {
        foreach (var g in Games.Where(g => g.ModPath is not null))
        {
            Assert.False(Path.IsPathRooted(g.ModPath!), $"{g.Id}: rooted modPath");
            Assert.DoesNotContain("..", g.ModPath!.Split('/', '\\'));
        }
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestInvariantsTests"`
Expected: PASS (all 7 facts) against the embedded snapshot.

- [ ] **Step 3: Commit**

```bash
git add tests/ModManager.Tests/Manifest/ManifestInvariantsTests.cs
git commit -m "test(manifest): invariants — unique ids, real engines, no binaries, safe paths"
```

---

### Task 8: Full Core suite + purity green, final verify

**Files:** none (verification only).

- [ ] **Step 1: Run the complete Core test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests, including the unchanged `KnownEnginesTests`, `PopularGamesTests`, `NexusGameDomainTests`, `EnginePresetsTests`, and `CorePurityTests` (the `Manifest/` module is pure — no WinUI/WinRT — so purity stays green). Note the total count is ≥ the pre-refactor count plus the new manifest tests.

- [ ] **Step 2: Confirm no stray behavior change in dependent suites**

The facades feed `SteamGameImport`, `EnginePresets.BuildGameEntry`, `Scanner` metadata identify, and `Ue4ssLuaInstaller`. The full-suite run in Step 1 already exercises these. If any go red, the cause is a wrong provenance tag or value in `games-manifest.json` — fix the JSON, never loosen a test.

- [ ] **Step 3: Final commit (if any uncommitted verification fixups)**

```bash
git add -A
git commit -m "chore(manifest): Phase 0 verification — full Core suite green"
```

(Skip if the working tree is clean.)

---

## Self-Review

**Spec coverage (against `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md`):**

- §3 schema (id, name, engine, stores, nexusDomain, curseforgeGameId, modPath, fileExtensions, groupingRule, featured, provenance) → Task 1 records. ✓ (`saveDirHint`/`saveTypes`/`launchExe`/`windowTitle` are optional descriptive fields not exercised by the three Phase 0 facades; deliberately omitted from the records until a consumer needs them — YAGNI. The legacy arrays never carried them.)
- §4 Core `Manifest/` module: `GameManifestEntry`, `ManifestValidator`, `EmbeddedManifestSource`/`EffectiveManifest`, facades → Tasks 1–6. ✓ (Remote/cached source is Phase 1, out of scope; `EmbeddedGameManifest` is the Phase 0 `EffectiveManifest` with one source.)
- §5 unknown engines skip not crash → Task 2 + Task 8 purity. ✓
- §6 `modPath` re-validated, relative-only → Task 2. ✓ (Signature verify is Phase 1, out of scope.)
- §7 camelCase round-trip, facade parity golden tests, validator tests → Tasks 1, 2, 4–7. ✓ (Facade parity is the existing suite run unchanged + the new membership-leak tests — a stronger guard than a hand-written snapshot.)
- §10 non-goals (catalogs stay compiled, no remote, no GOG/Epic probing) → Scope section + nothing in the tasks touches them. ✓

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The JSON snapshot is fully enumerated (16 entries). ✓

**Type consistency:** `EmbeddedGameManifest.Current` (property) used identically in Tasks 4–7. `ManifestSources.{KnownEngines,NexusDomains,PopularGames}` constants used in Tasks 1, 4, 5, 6, 7. `GameManifestEntry` property names (`Stores.SteamAppId`, `Engine`, `NexusDomain`, `ModPath`, `FileExtensions`, `Featured`, `Provenance.Sources`) consistent across the JSON, records, validator, loader, and facades. `ManifestValidator.Validate(GameManifest, IReadOnlySet<string>)` signature matches its call in `EmbeddedGameManifest.Load` and the tests. ✓

**One judgment call flagged for the executor:** provenance tags double as facade-membership filters in Phase 0. This is deliberate (reuses the schema's provenance concept instead of adding Phase-0-only marker fields) and is locked by the existing parity tests + the new membership-leak tests. If Phase 1's miner changes provenance semantics, revisit the facade filters then — they may be retired entirely once the manifest is canonical.
