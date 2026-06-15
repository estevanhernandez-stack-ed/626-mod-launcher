# Handoff: deepen engine detection (more games auto-detect)

> Written 2026-06-15 to hand a fresh run the context to tackle the engine-detection backlog. Read this top-to-bottom before scoping. The opening prompt to paste is at the very bottom.

## Mission

Make **more games auto-detect** their engine + mod layout, so the user doesn't have to hand-configure them. Two named cases drove this (from live smoke): **Marvel Rivals** and **Helldivers 2** don't auto-detect. They fail for *different* reasons, which split the work into tiers — scope deliberately, don't conflate them.

This is the user's explicit backlog ask ("I want more games to be detected"). Dashboard task: `task-1781481557629-hptvulr0q`. Project: **626 Mod Launcher**, id `DP1YCsh7iAN1yAiR8sAd` (bind via `mcp__626labs-cloud__manage_projects findByRepo` on the remote URL at session start).

## How detection works today (grounded — verified 2026-06-15)

The decision is pure Core; the IO is App-side:

- **`src/ModManager.App/Services/EngineScan.cs`** — `Probe(root)` scans the game folder and fills an `EngineProbe` (bools: `HasContentPaks`, `HasBepInEx`, `HasMelonLoader`, `HasDataPlugins`, `HasStardew`, `HasSourceAddons`, `HasUnityData`). **Its doc says it's "bounded to the root + one level of subfolders so it stays fast on large game installs."** This is the constraint that misses nested layouts.
- **`src/ModManager.Core/EngineDetect.cs`** — `GuessEngine(EngineProbe)` picks the most specific engine key (`ue-pak`, `bepinex`, `melonloader`, `bethesda`, `smapi`, `source`) or `null` (user chooses). Pure + tested.
- **`src/ModManager.Core/EnginePresets.cs`** — maps an engine key to a preset (extensions, grouping, default mod path). `DetectUePakModLocation(gameRoot)` finds `<gameRoot>/<projectDir>/Content/Paks` **one level under gameRoot** and decides loader-vs-paks-root layout. Falls back to the static preset path when not found. `Build(input)` honors an explicit `input.ModPath` *over* detection.
- **`src/ModManager.Core/KnownEngines.cs`** — facade over the embedded/remote manifest's `known-engines`-tagged games (engine by Steam app id).
- **`src/ModManager.Core/Manifest/games-manifest.json`** — the shipped game definitions; each game can carry `engine`, `nexusDomain`, `modPath` (override to the engine-default mod folder), `fileExtensions`. The live feed is the separate **`626-game-manifest`** repo (CI-signed; the launcher fetches + verifies it). **A game on a KNOWN engine needs no app release — it's a feed data entry. A NEW engine is compiled code.** (See `docs/manifest-feed-runbook.md`.)

### Why the two named cases fail

- **Marvel Rivals** — it *is* Unreal (`ue-pak`, a supported engine). Its paks live at `MarvelGame/Marvel/Content/Paks` — **two** directory levels under the game root. Both the App probe (`EngineScan`, one level) and `DetectUePakModLocation` (one level) only look one level down, so `HasContentPaks` comes back false → `GuessEngine` returns null → no auto-detect. This is a **probe-depth** problem, not a missing-engine problem.
- **Helldivers 2** — a proprietary engine (Autodesk Stingray / "BitSquid" lineage), not UE/Unity/Bethesda/Source/SMAPI. No probe signature matches → null. This is a **new-engine** problem: it needs a new engine key + preset **and** a real mod enable/disable mechanism (compiled code — the architecture forbids the manifest from describing *how* to enable a mod).

## The three tiers of "more games" — scope before building

1. **Feed data entry (no code, fastest).** A game on a known engine with a non-standard project folder can be added to the `626-game-manifest` feed with an explicit `modPath`. Example: Marvel Rivals as `engine: ue-pak`, `modPath: MarvelGame/Marvel/Content/Paks/~mods` — `EnginePresets.Build` honors the explicit path, so the *curated* entry works with zero app changes. Quickest path to "Marvel Rivals works when picked from the list." Does **not** help arbitrary/uncurated games.
2. **Probe deepening (code, contained).** Bump `EngineScan.Probe` + `DetectUePakModLocation` to find `Content/Paks` nested up to ~2 levels (bounded — do **not** walk the whole game tree; it must stay fast on huge installs). This makes *arbitrary* nested-UE games auto-detect on add/drop without a curated entry. Catches Marvel Rivals and the long tail of UE games that wrap the project in an outer folder. Highest leverage for "more games detected" generically.
3. **New engine family (code, biggest).** Helldivers 2 (and other proprietary engines) need a new engine key + preset + a tested enable/disable mechanism. Requires research into how that engine's mods actually install/load *before* any code. Treat each new engine as its own slice.

**Recommended first slice:** tier 2 (probe deepening) — it's the generic win and directly fixes Marvel Rivals. Optionally pair with a tier-1 feed entry for Marvel Rivals as an immediate stopgap. Defer tier 3 (Helldivers) to its own slice after its mod mechanism is researched.

## Open questions to resolve in the research phase

- How deep should the probe recurse, and should it be capped by a directory-count/time budget so it stays fast on large installs? (The current one-level bound exists for a reason — don't regress it.)
- Are there UE games nested *3* levels (or with multiple `Content/Paks` under different projects)? Pick the first? Let the user disambiguate?
- For Helldivers 2 specifically: where do mods go, how are they enabled/disabled, is there a community loader? (This is a research task — verify against real sources, like the StateFlags-on-disk and Nexus-API verifications did. Don't guess a mechanism.)
- Does deepening the probe risk false positives (e.g. picking a UE `Content/Paks` from a bundled tool/subgame)? Add a guard/test.

## Workflow + laws (this repo's established rhythm)

Ultracode is on — use the **Workflow** tool. The proven loop this session (Nexus arc, 3 shipped slices):

1. **Research-verify** workflow — parallel readers ground every fact (engine layouts on disk, the probe code, how a new engine's mods load) before design. Don't build on assumptions.
2. **Spec** → `docs/superpowers/specs/YYYY-MM-DD-<slug>-design.md`. Present the real decisions, get a nod.
3. **Plan** → `docs/superpowers/plans/YYYY-MM-DD-<slug>.md` (Core-first, TDD, bite-sized tasks).
4. **Build** — subagent-driven Workflow: sequential units, each `implement → review → fix` with a build/test gate, then a whole-branch adversarial review.
5. **Verify yourself** (git log + Core suite + App build) → **PR** off master → **rebuild + relaunch the dev** for the user to smoke.

**Laws (non-negotiable):** pure-Core (no WinUI/WinRT in `src/ModManager.Core`; `CorePurityTests` enforces — `GuessEngine` stays pure, IO stays in `EngineScan`); camelCase JSON on disk via `AtomicJson` (round-trip test for any new persisted field); reversibility (atomic writes, additive nullable fields, no destructive ops); the three-places rule for displayed/persisted metadata. **Never run bare `dotnet build`/`dotnet test` at the repo root** — target `tests/ModManager.Tests/ModManager.Tests.csproj` (Core) or `src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (App). **Kill any running `ModManager.App` before App builds** — it locks `ModManager.Core.dll` (MSB3027). Commits: conventional, `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Log significant decisions to the dashboard MCP (`manage_decisions`, projectId above).

## Repo state at handoff

- Branch context: most recent work is the **Nexus arc** — enrichment (#144), by-mod-id poll (#145), endorse (#146), all merged to `master` and smoke-confirmed.
- **Unreleased batch on `master`:** richer Steam detection + the Set up fix (#143) + the three Nexus PRs. The user is intentionally **batching, no release cut yet** ("keep building, one release later"). Don't cut a release unless asked; if asked, `release-notes-drafter` agent + `docs/RELEASE.md` flow, and the user publishes the draft himself.
- Key files for this work: `src/ModManager.App/Services/EngineScan.cs`, `src/ModManager.Core/EngineDetect.cs`, `src/ModManager.Core/EnginePresets.cs`, `src/ModManager.Core/KnownEngines.cs`, `src/ModManager.Core/Manifest/games-manifest.json`, `src/ModManager.Core/Scanner.cs`; the `626-game-manifest` feed repo for data entries; tests under `tests/ModManager.Tests/`.

---

## Opening prompt to paste into the new run

```
We're deepening engine detection so more games auto-detect — the backlog item (Marvel Rivals,
Helldivers 2, and the long tail). Read docs/superpowers/handoffs/2026-06-15-engine-detection-handoff.md
first; it has the grounded picture (EngineScan.Probe is bounded to one subfolder level, which is why
Marvel Rivals' two-level MarvelGame/Marvel/Content/Paks misses; Helldivers is a proprietary engine =
new-engine work) and the three tiers of scope.

Bind the project (findByRepo), then kick off a research-verify Workflow before any design: confirm the
probe code + real on-disk layouts + (for any new engine) how its mods actually install/load. Then put a
tight spec in front of me with the scope decision — I lean toward tier 2 (probe deepening, the generic
win that also fixes Marvel Rivals), with Helldivers as its own later slice once its mod mechanism is
researched. Ultracode is on; follow the repo's research → spec → plan → subagent-driven build → verify
→ PR → smoke rhythm. Don't cut a release (we're batching on master).
```
