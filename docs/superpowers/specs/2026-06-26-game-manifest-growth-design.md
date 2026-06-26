# Game-manifest growth — design

**Date:** 2026-06-26
**Status:** Spec (brainstormed + approved direction; grounded against the live system). Awaiting review → writing-plans.
**Companion:** the feed went live in v0.6.0 (`docs/manifest-feed-runbook.md`); this is the plan to *grow its coverage* from 38 games toward broad support.

## Goal

A repeatable, mostly-agent-driven pipeline that grows the signed `626-game-manifest` feed from **38 games today** toward broad coverage of the moddable games people actually play — without lowering the quality bar or the facts-only legal basis, and without an app release per game.

## What exists today (grounded)

- **Feed:** `626-game-manifest` publishes a signed `games-manifest.json` — **38 games now**, schemaVersion 1. CI (`build-manifest.yml`) runs the miner `--with-mo2 --with-overrides --sign` weekly (Mon 06:00 UTC), on `overrides/**` pushes, and on manual dispatch. Data licensed CC0; tooling MIT.
- **Miner (`tools/ManifestMiner`):** three stages — Ludusavi backbone (skeletal: name + steamAppId, **no engine**) → MO2 `basic_games` enrich (adds `modPath` + `nexusDomain`, still **no engine**) → curated `overrides/` (sets `engine` + corrections) → filter to functional-tag earners + sign. **6 curated overrides** exist.
- **Engine is curated-only by design.** The miner refuses to infer engine (a game's modPath doesn't determine it — "Data" is Bethesda *or* FromSoft). The launcher folder-detects engine at runtime (`EngineScan`). The 9 engine keys: `ue-pak, bethesda, minecraft, bepinex, smapi, source, melonloader, fromsoft, custom`.
- **Two publish paths** (from the publish filter — an entry ships only if it earns a tag):
  - **`nexus-domains`** — has a verified `nexusDomain` (no engine needed). Ships; the launcher folder-detects engine when the user adds it. *This is the long-tail path.*
  - **`known-engines` / `popular-games`** — has `engine` (+ `modPath` + `featured` for the quick-pick). *This is the curated-tier path.*
- **Schema fields:** `id, name, engine?, stores{steam/gog/epic/xbox}, nexusDomain?, curseforgeGameId?, modPath?, saveDirHint?, fileExtensions?, groupingRule?, featured?, banRisk?(null|low|medium|high), provenance`. Validator rejects unknown engine + unsafe modPath (rooted / drive-qualified / `..`). Remote merges over embedded by id, never-downgrade.
- **Request-line hook:** `AddGameDialog` already splits installed Steam games into addable vs **"Set up (engine not detected)"** rows (when `KnownEngines.ByAppId()` + `EngineScan.Detect()` both miss). `FrameworkUnrecognizedNudgeDialog` already opens a prefilled GitHub issue (`labels=framework-request`) via `Launcher.LaunchUriAsync` + `SafeUrl`. No flavor gating on add-game.

## The gaps this plan fills

1. **No demand ranking / candidate queue.** No mod-count or popularity signal anywhere; overrides are added ad-hoc. We can't currently answer "what are the top unsupported moddable games?"
2. **No request line.** Detection stops at "Set up (engine not detected)" + hands to the manual form — no "request support" affordance, no signal capture.
3. **No contribution infra in the feed repo.** No CONTRIBUTING, no issue/PR templates, no banRisk curation guide.
4. **Curation throughput.** Engine + modPath + nexusDomain per game is real research; doing it one at a time is the bottleneck.

## Strategy (approved)

**Hybrid: mine wide, curate the top. Rank by modding demand. Two-tier quality bar. Agent-curated with your batch approval. Users vote via a request line.**

### Component 1 — Demand-ranked candidate queue *(net-new)*

A ranked work-list of *unsupported* moddable games. Inputs:
- **Modding demand** — per-game Nexus mod count + endorsements (and CurseForge where relevant), researched per candidate. The objective ranking.
- **Player demand** — open `game-request` GitHub issues from the request line (👍 = votes).
Cross-referenced against the current 38 → "top N unsupported moddable games, ranked." Built as a miner mode or a standalone script that emits a ranked candidate report (the miner today has no drill-down — this adds the "what's missing + how much it matters" view).

### Component 2 — Agent-curation loop *(the throughput engine)*

Per batch off the ranked queue, a subagent fan-out (one agent per candidate game):
- Research **engine** (which of the 9 keys — from the game's documented mod loader, not guessed from modPath), **modPath**, **nexusDomain**, **fileExtensions**, **banRisk** (anti-cheat / online), **store ids**.
- **Cross-verify each datum** against a second primary source (the facts-only legal condition): Steam/SteamDB for ids, the game's Nexus page for domain + mod activity, the documented loader for engine.
- Draft `overrides/<game>.json` (feed-repo format). Flag low-confidence entries.
**You review/approve the batch** (the curation gate) → merge to `626-game-manifest/overrides/` → CI signs + publishes. Adversarial verification on the engine field specifically (a wrong engine = wrong mod handling) before it reaches you.

### Component 3 — Request line *(launcher feature; both flavors; needs a release)*

On the `AddGameDialog` "Set up (engine not detected)" rows, add **"Request support for `<game>`"** → opens a prefilled GitHub issue in `626-game-manifest` (`labels=game-request`, title + Steam AppId + name auto-filled), via the existing `FrameworkUnrecognizedNudgeDialog` / `SafeUrl` pattern. Public issue queue feeds Component 1's player-demand axis. Ships in **both** STORE and FULL (it's a benign issue link — unlike the EAC toggle, no flavor gate).

### Component 4 — Feed-repo contribution infra

In `626-game-manifest`: `CONTRIBUTING.md` (the data-PR workflow: drop an override, the field meanings, the cross-verify rule, the banRisk guide), a `game-request` **issue template** (what the request line files), and a **PR template** for override PRs. Turns the request line + community into a real contribution loop on the facts-only/CC0 basis.

### Component 5 — Two-tier quality bar *(grounded)*

- **Publish bar (long tail):** a verified **`nexusDomain`** (engine folder-detected at runtime) **or** a curated **`engine` + safe `modPath`**. Each datum cross-verified. Enough to manage the game.
- **Top tier (most-modded / most-requested):** + `engine` + `modPath` + `fileExtensions` + `featured` (quick-pick) + `banRisk` + `saveDirHint`.

## Sequencing

- **Phase 1 — data, no release:** build the demand-ranked candidate-queue report; run the first agent-curated batch (target **38 → ~80**); add feed-repo CONTRIBUTING + templates. Pure data + tooling; ships via the feed's existing CI.
- **Phase 2 — launcher release:** the request-line feature (both flavors) → turns on player-demand signal.
- **Phase 3 — steady state:** recurring batches (~15–25 games each) on a cadence; community data PRs welcome; the request queue + mod-demand drive the next batch.

## Non-goals

- **No heuristic engine auto-inference in the miner.** Engine stays curated (override) or runtime folder-detected — mining it is unreliable by design. The agent *researches* engine; it isn't auto-derived from modPath.
- **No scraping PCGamingWiki or other access-restricted sources.** Facts-only, cross-verified, hand/agent-curated (the established legal basis; PCGamingWiki blocks automated fetch — that's a ToS matter, honored).
- **No fully-automated publish.** The human approval gate stays — it's the quality + legal cross-verify checkpoint.
- **No new schema fields / no engine additions.** A new *engine* is still launcher code (the layer-3 law); this plan grows *games on the 9 known engines*.

## Legal basis (reaffirmed)

Facts-only (Steam/GOG/Epic ids, names, engine keys, mod paths, Nexus slugs — uncopyrightable under *Feist*) + cross-verify each datum against a second primary source. App stays free/not-sold. Established + signed off 2026-06-13 (`docs/manifest-feed-runbook.md`); the agent-curation loop enforces the cross-verify condition per datum.

## Success criteria

- A ranked candidate report exists + drives curation.
- First batch lands the feed at ~80 games, each entry cross-verified, your approval on every batch.
- The request line ships (both flavors) + files structured `game-request` issues.
- The feed repo documents the contribution loop.
- No regression: every published entry passes the validator (known engine or null, safe modPath); the signature + gate path is untouched.
