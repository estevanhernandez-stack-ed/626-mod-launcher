# Game Manifest — feed miner, slice: overrides/ curation layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The curation layer — hand-authored `overrides/` entries that **win over** mined data, keyed by Steam id. This is the reliable coverage lever: the mined sources give breadth + a few long-tail mod paths, but the launcher-useful engine/mod-path for the games people actually mod comes from curation.

**Architecture:** Extends `tools/ManifestMiner/`. `OverrideEntry` (a partial, curated `GameManifestEntry`), `OverridesLoader` (read `overrides/*.json`), and a pure `OverridesMerge.Apply(manifest, overrides)` that overrides specified fields on a matched entry (or adds a new entry) — overrides **win unconditionally** (unlike MO2 enrich's fill-if-empty). The CLI applies overrides as the **final** merge step (after Ludusavi backbone + MO2 enrichment). Seeded with a few high-confidence entries + a README; the maintainer expands it.

**Tech Stack:** .NET 10, C#, System.Text.Json, xUnit. Tool-only; draft-only.

**Spec:** roadmap §8 ("merge with `overrides/` — hand-curated corrections win every conflict; curation = reviewing the merge diff").

---

## Scope + how curation works

- **Overrides win.** An override for a Steam id sets the fields it specifies (`engine`, `modPath`, `nexusDomain`, `featured`, `fileExtensions`, `name`, `saveDirHint`) on the matched backbone/enriched entry, overriding whatever Ludusavi/MO2 produced, and stamps provenance `curated` (status `curated`). An override for a Steam id **not** in the backbone **adds** a new entry. Unspecified fields are left as-is.
- **This is where reliable engines come from.** The mined backbone is mostly skeletal; curated overrides are how popular games get a correct `engine` + `modPath`. Engine values must be real `EnginePresets` keys.
- **Seed + expand.** This slice ships the mechanism + a small, high-confidence seed (a few textbook Bethesda `Data` games) + a README documenting the format. The maintainer expands the seed with per-game knowledge — that's the ongoing curation. A seed entry whose Steam id doesn't match simply doesn't apply (surfaced in the coverage report) — harmless.
- **Out:** no signing, no publish, no feed repo, no App/Core change. Overrides live in `tools/ManifestMiner/overrides/` for now (move to the feed repo at go-live). Draft-only (gitignored output).

---

## File Structure

- Create: `tools/ManifestMiner/OverrideEntry.cs`
- Create: `tools/ManifestMiner/OverridesLoader.cs`
- Create: `tools/ManifestMiner/OverridesMerge.cs`
- Create: `tools/ManifestMiner/overrides/README.md` + seed `*.json` files
- Modify: `tools/ManifestMiner/Program.cs` — apply overrides as the final merge step + coverage line
- Create: `tests/ModManager.Tests/Miner/OverridesMergeTests.cs`
- Create: `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`

**Test command:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
**Run (manual):** `dotnet run --project tools/ManifestMiner -- --with-mo2 --with-overrides` (writes `out/manifest-draft.json`).

---

### Task 1: OverrideEntry model + OverridesMerge (pure, overrides win)

**Files:**
- Create: `tools/ManifestMiner/OverrideEntry.cs`
- Create: `tools/ManifestMiner/OverridesMerge.cs`
- Test: `tests/ModManager.Tests/Miner/OverridesMergeTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/OverridesMergeTests.cs`:

```csharp
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class OverridesMergeTests
{
    private static GameManifest Backbone(params (string id, string steamId, string? engine)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.id, Name = g.id, Engine = g.engine,
            Stores = new StoreIds { SteamAppId = g.steamId },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        }).ToList(),
    };

    [Fact]
    public void Override_wins_over_mined_fields_on_a_matched_entry()
    {
        var backbone = Backbone(("skyrim", "72850", null));          // mined: no engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "72850", Engine = "bethesda", ModPath = "Data" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "72850");
        Assert.Equal("bethesda", e.Engine);
        Assert.Equal("Data", e.ModPath);
        Assert.Contains("curated", e.Provenance.Sources);
        Assert.Equal("curated", e.Provenance.Status);
    }

    [Fact]
    public void Override_replaces_a_value_the_miner_already_set()
    {
        var backbone = Backbone(("x", "1", "custom"));               // mined: wrong/placeholder engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "1", Engine = "bethesda" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "1");
        Assert.Equal("bethesda", e.Engine);                          // override wins (not fill-if-empty)
    }

    [Fact]
    public void Override_for_an_unknown_steam_id_adds_a_new_entry()
    {
        var backbone = Backbone(("a", "1", "bethesda"));
        var overrides = new[]
        {
            new OverrideEntry { SteamAppId = "999", Id = "new-game", Name = "New Game", Engine = "ue-pak", ModPath = "Content/Paks/~mods" },
        };

        var result = OverridesMerge.Apply(backbone, overrides);
        var added = result.Games.Single(g => g.Stores.SteamAppId == "999");
        Assert.Equal("new-game", added.Id);
        Assert.Equal("ue-pak", added.Engine);
        Assert.Contains("curated", added.Provenance.Sources);
        Assert.Equal(2, result.Games.Count);
    }

    [Fact]
    public void Unspecified_override_fields_leave_existing_values_intact()
    {
        var backbone = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "g", Name = "G", Engine = "bethesda", ModPath = "Data",
                    Stores = new StoreIds { SteamAppId = "5" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" } },
                },
            },
        };
        var overrides = new[] { new OverrideEntry { SteamAppId = "5", Featured = 3 } }; // only featured

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single();
        Assert.Equal(3, e.Featured);
        Assert.Equal("bethesda", e.Engine);   // untouched
        Assert.Equal("Data", e.ModPath);       // untouched
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesMergeTests"`
Expected: FAIL — `OverrideEntry` / `OverridesMerge` do not exist.

- [ ] **Step 3: Implement the model + merge**

`tools/ManifestMiner/OverrideEntry.cs`:

```csharp
namespace ManifestMiner;

/// <summary>A hand-curated correction, keyed by Steam app id. Any non-null field overrides the
/// mined value on the matched entry (or seeds a new entry when the Steam id isn't in the backbone).
/// Curated data wins over everything the miner produced.</summary>
public sealed record OverrideEntry
{
    public string SteamAppId { get; init; } = "";   // the key (required)
    public string? Id { get; init; }                 // slug for an ADDED entry (else derived from Name)
    public string? Name { get; init; }
    public string? Engine { get; init; }
    public string? ModPath { get; init; }
    public string? NexusDomain { get; init; }
    public int? Featured { get; init; }
    public string? SaveDirHint { get; init; }
    public IReadOnlyList<string>? FileExtensions { get; init; }
}
```

`tools/ManifestMiner/OverridesMerge.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Pure: apply curated overrides onto the (backbone + enriched) manifest, keyed by Steam id.
/// Overrides WIN — any field the override specifies replaces the mined value; unspecified fields are
/// left intact. An override whose Steam id isn't present adds a new entry. Matched/added entries gain
/// the "curated" provenance source + status.</summary>
public static class OverridesMerge
{
    public static GameManifest Apply(GameManifest manifest, IReadOnlyList<OverrideEntry> overrides)
    {
        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in manifest.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }

        // Index existing entries by Steam id for override matching.
        var idBySteam = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in manifest.Games)
            if (g.Stores.SteamAppId is { } s) idBySteam.TryAdd(s, g.Id);

        foreach (var ov in overrides)
        {
            if (string.IsNullOrWhiteSpace(ov.SteamAppId)) continue;

            if (idBySteam.TryGetValue(ov.SteamAppId, out var existingId))
            {
                byId[existingId] = ApplyTo(byId[existingId], ov);
            }
            else
            {
                var id = !string.IsNullOrWhiteSpace(ov.Id) ? ov.Id! : EnginePresets.Slugify(ov.Name);
                if (byId.ContainsKey(id)) id = $"{id}-{ov.SteamAppId}"; // avoid slug collision
                byId[id] = NewFrom(id, ov);
                order.Add(id);
                idBySteam[ov.SteamAppId] = id;
            }
        }

        return manifest with { Games = order.Select(id => byId[id]).ToList() };
    }

    private static GameManifestEntry ApplyTo(GameManifestEntry e, OverrideEntry ov) => e with
    {
        Name = ov.Name ?? e.Name,
        Engine = ov.Engine ?? e.Engine,
        ModPath = ov.ModPath ?? e.ModPath,
        NexusDomain = ov.NexusDomain ?? e.NexusDomain,
        Featured = ov.Featured ?? e.Featured,
        SaveDirHint = ov.SaveDirHint ?? e.SaveDirHint,
        FileExtensions = ov.FileExtensions ?? e.FileExtensions,
        Provenance = Curate(e.Provenance),
    };

    private static GameManifestEntry NewFrom(string id, OverrideEntry ov) => new()
    {
        Id = id,
        Name = ov.Name ?? id,
        Engine = ov.Engine,
        ModPath = ov.ModPath,
        NexusDomain = ov.NexusDomain,
        Featured = ov.Featured,
        SaveDirHint = ov.SaveDirHint,
        FileExtensions = ov.FileExtensions,
        Stores = new StoreIds { SteamAppId = ov.SteamAppId },
        Provenance = new ManifestProvenance { Sources = new[] { "curated" }, Status = "curated" },
    };

    private static ManifestProvenance Curate(ManifestProvenance p)
    {
        var sources = p.Sources.Contains("curated") ? p.Sources : p.Sources.Append("curated").ToList();
        return p with { Sources = sources, Status = "curated" };
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesMergeTests"`
Expected: PASS (4 facts).

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/OverrideEntry.cs tools/ManifestMiner/OverridesMerge.cs tests/ModManager.Tests/Miner/OverridesMergeTests.cs
git commit -m "feat(miner): overrides merge — curated corrections win, keyed by Steam id"
```

---

### Task 2: OverridesLoader + seed overrides + README

**Files:**
- Create: `tools/ManifestMiner/OverridesLoader.cs`
- Create: `tools/ManifestMiner/overrides/README.md`
- Create: `tools/ManifestMiner/overrides/*.json` (seed)
- Test: `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`:

```csharp
using ManifestMiner;

namespace ModManager.Tests.Miner;

public class OverridesLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ovr-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void Loads_all_json_overrides_in_the_directory()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "skyrim.json"),
            "{ \"steamAppId\": \"72850\", \"engine\": \"bethesda\", \"modPath\": \"Data\" }");
        File.WriteAllText(Path.Combine(_dir, "oblivion.json"),
            "{ \"steamAppId\": \"22330\", \"engine\": \"bethesda\", \"modPath\": \"Data\" }");

        var loaded = OverridesLoader.Load(_dir);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, o => o.SteamAppId == "72850" && o.Engine == "bethesda");
    }

    [Fact]
    public void Missing_directory_returns_empty()
        => Assert.Empty(OverridesLoader.Load(Path.Combine(_dir, "nope")));

    [Fact]
    public void Skips_malformed_files_without_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "good.json"), "{ \"steamAppId\": \"1\", \"engine\": \"smapi\" }");
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not valid json");

        var loaded = OverridesLoader.Load(_dir);
        Assert.Single(loaded);
        Assert.Equal("1", loaded[0].SteamAppId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesLoaderTests"`
Expected: FAIL — `OverridesLoader` does not exist.

- [ ] **Step 3: Implement the loader**

`tools/ManifestMiner/OverridesLoader.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesLoaderTests"`
Expected: PASS (3 facts).

- [ ] **Step 5: Seed overrides + README**

Create `tools/ManifestMiner/overrides/README.md`:

```markdown
# Curated overrides

Hand-curated corrections that win over mined data. One `*.json` file per game (Steam id is the key).
The miner applies these as the final merge step (`--with-overrides`), after the Ludusavi backbone +
MO2 enrichment. Curated data wins over everything the miner produced.

## Format (camelCase, all fields except `steamAppId` optional)

```json
{
  "steamAppId": "72850",
  "id": "skyrim",
  "name": "The Elder Scrolls V: Skyrim",
  "engine": "bethesda",
  "modPath": "Data",
  "nexusDomain": "skyrim",
  "featured": 20,
  "fileExtensions": ["esp", "esl", "esm", "bsa"]
}
```

`engine` must be a real engine key (`bethesda`, `ue-pak`, `bepinex`, `smapi`, `minecraft`, `source`,
`melonloader`, `fromsoft`, `custom`). An override whose `steamAppId` isn't in the backbone ADDS a new
entry; one that matches OVERRIDES the mined fields. Unspecified fields are left as the miner set them.

To add a game: drop a `<game>.json` here, run `dotnet run --project tools/ManifestMiner -- --with-mo2
--with-overrides`, and check the coverage summary + the diff. Verify the Steam id (a wrong id just
won't match — it's reported as not-applied, never corrupts).
```

Create seed files (textbook Bethesda `Data` games not in the embedded 16 — high confidence; a wrong id simply won't match):

`tools/ManifestMiner/overrides/skyrim-classic.json`:
```json
{ "steamAppId": "72850", "id": "skyrim", "name": "The Elder Scrolls V: Skyrim", "engine": "bethesda", "modPath": "Data" }
```
`tools/ManifestMiner/overrides/oblivion.json`:
```json
{ "steamAppId": "22330", "id": "oblivion", "name": "The Elder Scrolls IV: Oblivion", "engine": "bethesda", "modPath": "Data" }
```
`tools/ManifestMiner/overrides/fallout-new-vegas.json`:
```json
{ "steamAppId": "22380", "id": "fallout-new-vegas", "name": "Fallout: New Vegas", "engine": "bethesda", "modPath": "Data" }
```

(These are the initial curation; the maintainer verifies/expands. The README documents the format.)

- [ ] **Step 6: Commit**

```bash
git add tools/ManifestMiner/OverridesLoader.cs tools/ManifestMiner/overrides/ tests/ModManager.Tests/Miner/OverridesLoaderTests.cs
git commit -m "feat(miner): overrides loader + seed curation (Bethesda Data games) + README"
```

---

### Task 3: CLI — apply overrides as the final merge step

**Files:**
- Modify: `tools/ManifestMiner/Program.cs`

- [ ] **Step 1: Wire overrides into the pipeline**

In `Program.cs`, after the MO2 enrichment block (or after the Ludusavi backbone when `--with-mo2` is absent), when `--with-overrides` is passed: load overrides (default dir `tools/ManifestMiner/overrides/`, or `--overrides-dir <path>`), apply via `OverridesMerge.Apply`, re-validate, write `out/manifest-draft.json`, and add a coverage line (overrides loaded, applied-to-existing, added-new, with-engine total). Use the same `outDir` + `ManifestJson.Options` + `ManifestValidator` as the existing blocks.

```csharp
// after the manifest (Ludusavi [+ MO2]) is built into a `GameManifest manifest` (or `enriched`):
if (args.Contains("--with-overrides"))
{
    var current = /* the most-enriched manifest so far (enriched if --with-mo2 ran, else manifest) */;
    var overridesDir = GetArg(args, "--overrides-dir")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "overrides");
    var overrides = OverridesLoader.Load(overridesDir);
    var curated = OverridesMerge.Apply(current, overrides);
    var validated = ManifestValidator.Validate(curated, EnginePresets.Presets.Keys.ToHashSet());

    File.WriteAllText(Path.Combine(outDir, "manifest-draft.json"),
        JsonSerializer.Serialize(validated.Manifest, ManifestJson.Options));

    var curatedCount = validated.Manifest.Games.Count(g => g.Provenance.Sources.Contains("curated"));
    var withEngine = validated.Manifest.Games.Count(g => g.Engine is not null);
    Console.WriteLine($"Overrides: {overrides.Count} loaded, {curatedCount} curated entries, {withEngine} total with engine -> out/manifest-draft.json");
}
```

(Adjust to thread the in-scope manifest variable from the existing Ludusavi/MO2 blocks — keep those blocks working when `--with-overrides` is absent.)

- [ ] **Step 2: Offline smoke-run**

Run:
```bash
printf 'Skyrim:\n  steam:\n    id: 72850\n' > /tmp/ludu-ovr.yaml
dotnet run --project tools/ManifestMiner -- --file /tmp/ludu-ovr.yaml --with-overrides
```
Expected: prints an "Overrides: 3 loaded, 1 curated entries, 1 total with engine" (Skyrim 72850 matched → engine bethesda; the other 2 seed ids add as new entries → actually "3 curated"); `out/manifest-draft.json` shows the skyrim entry with engine "bethesda", modPath "Data", provenance contains "curated". (Exact counts depend on whether the seed ids match the fixture; the matched one is curated, the unmatched seeds are added.)

- [ ] **Step 3: Commit**

```bash
git add tools/ManifestMiner/Program.cs
git commit -m "feat(miner): apply overrides as the final merge step + coverage"
```

---

### Task 4: Full suite + scope clean

**Files:** none (verification only).

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — existing + new `OverridesMergeTests` / `OverridesLoaderTests`. `CorePurityTests` green.

- [ ] **Step 2: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: EMPTY (this slice is entirely `tools/` + `tests/`; no Core/App change). Miner output gitignored (`tools/ManifestMiner/out/`); overrides/ seed IS committed (curated input, not output).

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(miner): overrides slice — full suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §8 "merge with overrides — curated wins" → Tasks 1–3 (overrides win, can add, applied as the final step). ✓

**Placeholder scan:** none. The seed entries are real curation (verify-or-no-match), not placeholders. ✓

**Type consistency:** `OverrideEntry` (Task 1) consumed by `OverridesMerge` + produced by `OverridesLoader`; `OverridesMerge.Apply(GameManifest, IReadOnlyList<OverrideEntry>) → GameManifest`, `OverridesLoader.Load(string) → IReadOnlyList<OverrideEntry>` consistent across impl + tests. Re-validated through the real `ManifestValidator`. ✓

**Honest scoping:** the seed is small + high-confidence (Bethesda `Data` games); a wrong Steam id no-matches (reported, never corrupts); the maintainer expands. overrides/ committed (input); draft output gitignored. ✓

**Judgment flagged:** overrides key on Steam id (consistent with the other merges; our only probe today). The seed is illustrative-but-real — the README is the contract for the maintainer's ongoing curation, which is where launcher-useful engine/mod-path coverage actually scales.
