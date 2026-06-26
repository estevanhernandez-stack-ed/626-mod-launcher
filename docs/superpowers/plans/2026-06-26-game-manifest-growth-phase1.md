# Game-manifest growth — Phase 1 (data) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`. Phase 2 (the launcher request-line feature, needs a release) is a SEPARATE plan — not here.

**Goal:** Grow the signed `626-game-manifest` feed from 38 games toward ~80 via an agent-curation fan-out, and stand up the contribution infra — all data-only, shipped through the feed's existing CI, no launcher release.

**Architecture:** Curation drafts are `OverrideEntry` JSON files dropped in the **feed repo** `626-game-manifest/overrides/`; the feed CI (checks out the launcher for the miner, runs `--with-mo2 --with-overrides --sign`) regenerates + signs on the `overrides/**` push. An agent fan-out researches + cross-verifies each game's facts and drafts those overrides for batch approval. A `--report-gaps` miner mode gives a deterministic candidate backbone for future batches.

**Tech Stack:** .NET 10 / C# (miner), JSON overrides (camelCase), GitHub Actions (feed CI), Workflow fan-out for curation.

## Global Constraints

- **Facts-only + cross-verify each datum** against a second primary source (Steam/SteamDB for ids; the game's Nexus page for domain + mod activity; the documented mod loader for engine). Uncopyrightable facts; app stays free/not-sold. No prose copied; no PCGamingWiki automated fetch.
- **Engine is one of 9 keys only:** `ue-pak, bethesda, minecraft, bepinex, smapi, source, melonloader, fromsoft, custom`. Never invent an engine (a new engine is launcher code, not a data PR). Engine is *researched from the game's documented loader*, never guessed from modPath.
- **modPath must be relative + safe:** never rooted/absolute, never drive-qualified (`:`), never contains `..`. The validator rejects these.
- **A game publishes only if it earns a tag:** a verified `nexusDomain` (long-tail; launcher folder-detects engine at runtime) **or** `engine` (+ `modPath`, + `featured` for quick-pick). An entry with neither is dropped by the publish filter — don't draft those.
- **Override JSON is camelCase**, keyed by `steamAppId` (string). Shape = `OverrideEntry`: `steamAppId` (required), `id?, name?, engine?, modPath?, nexusDomain?, featured?, banRisk?(low|medium|high), saveDirHint?, fileExtensions?`.
- **banRisk** = `low|medium|high` for online/anti-cheat titles (high gates enable in the launcher). Mark it whenever the game has anti-cheat or is primarily online.
- **The human approval gate stays:** Este approves every batch before it's PR'd. No auto-publish.
- Conventional commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Feed-repo paths are absolute: `c:/Users/estev/Projects/626-game-manifest/`.

---

## Task 1: Feed-repo contribution infra

**Files (all in `c:/Users/estev/Projects/626-game-manifest/`):**
- Create: `CONTRIBUTING.md`
- Create: `.github/ISSUE_TEMPLATE/game-request.yml`
- Create: `.github/ISSUE_TEMPLATE/config.yml`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

Foundational + cheap; it also defines the `game-request` issue shape the Phase 2 request line will prefill, and documents the override format the curation batch produces.

- [ ] **Step 1: Write `CONTRIBUTING.md`** — the data-PR workflow: (1) one `overrides/<slug>.json` per game keyed by `steamAppId`; (2) the full `OverrideEntry` field table with the 9 engine keys + the "nexusDomain alone is enough; engine is the quick-pick upgrade" rule; (3) the cross-verify rule (each datum from a 2nd primary source); (4) the modPath safety rule (relative, no `..`, no drive); (5) a banRisk guide (`high` = active anti-cheat / ban-on-mod online title, `medium` = online with softer stance, `low`/omit = single-player); (6) "open a PR → CI auto-signs on merge." Copy the `OverrideEntry` field names verbatim from `tools/ManifestMiner/OverrideEntry.cs` in the launcher repo.
- [ ] **Step 2: Write `.github/ISSUE_TEMPLATE/game-request.yml`** — a GitHub issue form, `name: Game request`, `labels: [game-request]`, fields: Game name (input, required), Steam App ID (input), Store link (input), "What mod loader does it use, if you know?" (dropdown of the 9 engines + "not sure"), Notes (textarea). This is exactly what the Phase 2 launcher request line will prefill via query params.
- [ ] **Step 3: Write `.github/ISSUE_TEMPLATE/config.yml`** — `blank_issues_enabled: false` + a contact link to the repo discussions/README. And `.github/PULL_REQUEST_TEMPLATE.md` — a checklist for an override PR: steamAppId verified on SteamDB, engine matches the documented loader (or nexusDomain-only), modPath relative+safe, each datum cross-verified, banRisk set if online/anti-cheat.
- [ ] **Step 4: Verify the issue form is valid** — run `gh` against the repo or eyeball the YAML structure (GitHub issue-form schema: `name`, `description`, `labels`, `body[]` with `type` of `input`/`dropdown`/`textarea`). No code test; the deliverable is the rendered template.
- [ ] **Step 5: Commit** in the feed repo: `docs(contrib): CONTRIBUTING + game-request issue form + PR template`.

## Task 2: First curation batch (agent-curation fan-out)

**Files:**
- Create (drafts): `c:/Users/estev/Projects/626-game-manifest/overrides/<slug>.json` (one per approved game)
- Produces: a per-game verification record (sources + confidence) for the batch-approval review

This is the centerpiece — a Workflow, not classic TDD. It produces the override drafts; the "test" is the manifest validator + the feed CI rebuild + Este's approval.

- [ ] **Step 1: Build the ranked candidate list.** Read the live published set: `c:/Users/estev/Projects/626-game-manifest/games-manifest.json` (the 38 current games + their steamAppIds). Research the most-modded games on the 9 known engines by Nexus mod count + activity (and CurseForge where relevant). Exclude the 38 already present. Produce a ranked list of ~30–40 unsupported high-demand candidates (the batch). Log what was considered + excluded (no silent truncation).
- [ ] **Step 2: Fan-out curate (one agent per candidate).** For each candidate game, an agent researches + **cross-verifies each datum against a second source**: `steamAppId` (Steam store/SteamDB), `engine` (the game's documented mod loader → one of the 9 keys; e.g. "uses BepInEx" → `bepinex`), `modPath` (the loader's mod folder, relative + safe), `nexusDomain` (the game's Nexus slug + that it has real mod activity), `banRisk` (anti-cheat / online?), `fileExtensions`/`saveDirHint`/`featured` for the top tier. Draft the `OverrideEntry` JSON. Output the draft + a sources record per datum + a confidence flag.
- [ ] **Step 3: Adversarial verify the engine field.** A second agent per draft tries to REFUTE the engine classification specifically (the highest-blast-radius field — a wrong engine = wrong mod handling). Default to "flag for human" on any uncertainty. Drafts whose engine survives → batch; flagged ones → a separate "needs Este" list.
- [ ] **Step 4: Validate every draft mechanically.** Each draft must: parse as `OverrideEntry`; have a `steamAppId`; earn a publish tag (`engine` OR `nexusDomain` present); `modPath` (if set) relative + safe (no rooted/`:`/`..`); `engine` (if set) ∈ the 9 keys; `banRisk` (if set) ∈ {low,medium,high}. Reject any that fail back to Step 2. (This mirrors `ManifestValidator`; you can confirm end-to-end by running the miner locally with `--overrides-dir` pointed at a temp dir of the drafts — see Step 6.)
- [ ] **Step 5: Batch-approval review (Este, the gate).** Present the batch as a table: game, engine, modPath, nexusDomain, banRisk, the cross-verify sources, confidence. Este approves / cuts / corrects per row. Only approved rows proceed.
- [ ] **Step 6: Land + verify the rebuild.** Write approved drafts to `626-game-manifest/overrides/`. Locally sanity-check before PR: from the launcher repo, `dotnet run --project tools/ManifestMiner -- --with-mo2 --with-overrides --overrides-dir "c:/Users/estev/Projects/626-game-manifest/overrides"` and confirm `out/games-manifest.json` game count rose by ~the approved count and the run reports no validator rejections. (No `--sign` locally — no key.)
- [ ] **Step 7: PR to the feed repo.** Branch in `626-game-manifest`, commit the overrides (`feat(overrides): curate <N> games — batch 1`), push, open the PR. On merge, the feed CI regenerates + signs + publishes. Confirm the published `games-manifest.json` count rose and the `.sig` regenerated.

## Task 3: `--report-gaps` miner mode (repeatable candidate backbone)

**Files (launcher repo):**
- Create: `tools/ManifestMiner/GapClassifier.cs`
- Create: `tests/ManifestMiner.Tests/ManifestMiner.Tests.csproj` + `tests/ManifestMiner.Tests/GapClassifierTests.cs`
- Modify: `tools/ManifestMiner/Program.cs` (add the `--report-gaps` branch)
- Modify: `ModManager.slnx` (add the test project)

A deterministic gap report for future batches: classify the enriched draft into engine-curated / nexus-only (engine-upgrade candidates) / skeletal (need full curation). Pure function → TDD.

**Interfaces:**
- Produces: `ManifestMiner.GapClassifier.Classify(IReadOnlyList<ModManager.Core.Manifest.GameManifestEntry> games) → GapReport` where `GapReport` has `IReadOnlyList<GameManifestEntry> EngineCurated, NexusOnly, Skeletal`.

- [ ] **Step 1: Write the failing test** in `tests/ManifestMiner.Tests/GapClassifierTests.cs`:

```csharp
using ManifestMiner;
using ModManager.Core.Manifest;

public class GapClassifierTests
{
    private static GameManifestEntry Entry(string id, string? engine, string? nexus) =>
        new() { Id = id, Name = id, Engine = engine, NexusDomain = nexus, Stores = new StoreIds { SteamAppId = id } };

    [Fact]
    public void Classifies_engine_nexus_and_skeletal_into_buckets()
    {
        var games = new[]
        {
            Entry("a", "bethesda", "skyrim"),  // engine-curated
            Entry("b", null, "witcher3"),      // nexus-only (engine-upgrade candidate)
            Entry("c", null, null),            // skeletal (needs full curation)
        };
        var r = GapClassifier.Classify(games);
        Assert.Equal(new[] { "a" }, r.EngineCurated.Select(g => g.Id));
        Assert.Equal(new[] { "b" }, r.NexusOnly.Select(g => g.Id));
        Assert.Equal(new[] { "c" }, r.Skeletal.Select(g => g.Id));
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `dotnet test tests/ManifestMiner.Tests/ManifestMiner.Tests.csproj`. Expected: FAIL (GapClassifier not defined). (First create the csproj: an xUnit net10.0 project referencing `tools/ModManager.Plugins.Abstractions`? No — reference `src/ModManager.Core` + `tools/ManifestMiner`. Mirror `tests/ModManager.Tests` package versions. Add it to `ModManager.slnx` under `/tests/`.)
- [ ] **Step 3: Implement `GapClassifier`:**

```csharp
namespace ManifestMiner;
using ModManager.Core.Manifest;

public sealed record GapReport(
    IReadOnlyList<GameManifestEntry> EngineCurated,
    IReadOnlyList<GameManifestEntry> NexusOnly,
    IReadOnlyList<GameManifestEntry> Skeletal);

public static class GapClassifier
{
    public static GapReport Classify(IReadOnlyList<GameManifestEntry> games) => new(
        games.Where(g => !string.IsNullOrWhiteSpace(g.Engine)).ToList(),
        games.Where(g => string.IsNullOrWhiteSpace(g.Engine) && !string.IsNullOrWhiteSpace(g.NexusDomain)).ToList(),
        games.Where(g => string.IsNullOrWhiteSpace(g.Engine) && string.IsNullOrWhiteSpace(g.NexusDomain)).ToList());
}
```

- [ ] **Step 4: Run the test, verify it passes** — `dotnet test tests/ManifestMiner.Tests/ManifestMiner.Tests.csproj`. Expected: PASS.
- [ ] **Step 5: Wire `--report-gaps` into `Program.cs`** — after the overrides stage builds `current`, add: `if (args.Contains("--report-gaps")) { var r = GapClassifier.Classify(current.Games); ... write out/candidate-gaps.json (the GapReport) + out/candidate-gaps.md (counts + the NexusOnly list = engine-upgrade queue, + the Skeletal count). }`. Match the existing `out/`-writing + `ManifestJson.Options` pattern in the file.
- [ ] **Step 6: Verify end-to-end** — `dotnet run --project tools/ManifestMiner -- --with-mo2 --with-overrides --report-gaps` → confirm `out/candidate-gaps.md` lists the nexus-only upgrade candidates + the skeletal count. Run the Core suite to confirm no regression: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`.
- [ ] **Step 7: Commit** — `feat(miner): --report-gaps candidate classifier + tests`.

## Self-review

- **Spec coverage:** Component 1 (candidate queue) → Task 2 Step 1 (researched ranking) + Task 3 (deterministic backbone). Component 2 (agent-curation) → Task 2. Component 4 (contribution infra) → Task 1. Component 5 (two-tier bar) → Global Constraints (publish-tag rule) + Task 2 Step 2. Component 3 (request line) → **Phase 2, separate plan** (noted). Two-tier publish paths, facts-only cross-verify, the 9-engine constraint, modPath safety, banRisk — all in Global Constraints.
- **No placeholders:** the C# task carries full test + impl; the docs task enumerates exact files + contents; the curation task carries the explicit per-datum cross-verify list + the mechanical validation rules.
- **Type consistency:** `GapClassifier.Classify` / `GapReport` names match between the interface block, the test, and the impl. `OverrideEntry` fields match the launcher source.

## Execution handoff

Phase 1 is one TDD code task (Task 3), one docs task (Task 1), and one Workflow-driven curation run (Task 2). Recommended: Task 1 + Task 3 via subagent-driven-development; Task 2 via a dedicated curation Workflow (rank → fan-out curate → adversarial-verify engine → batch approval → PR). Phase 2 (the request-line launcher feature) is a separate plan to write when we cut that release.
