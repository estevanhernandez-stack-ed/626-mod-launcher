# Deepen engine detection — bounded 2-level Unreal probe (Marvel Rivals + the nested-UE long tail)

**Date:** 2026-06-15
**Status:** Design — pending spec review
**Branch:** `feat/engine-detection-probe-deepening`
**Dashboard task:** `task-1781481557629-hptvulr0q` · project `DP1YCsh7iAN1yAiR8sAd`
**Research:** verified 2026-06-15 (code read + web, four adversarial verdicts all `refuted=false`). This spec rests on confirmed facts, not the handoff summary.

## Problem

The user wants more games to auto-detect their engine + mod layout. Two named cases fail, for *different* reasons:

- **Marvel Rivals (UE5)** — it *is* Unreal (`ue-pak`, a supported engine), but its paks live at `<GameRoot>/MarvelGame/Marvel/Content/Paks` — **two** wrapper folders deep. Every detection path in the launcher is bounded to **one** subfolder level, so `HasContentPaks` comes back false → `GuessEngine` returns null → no auto-detect. This is a **probe-depth bug**, not a missing engine.
- **Helldivers 2** — a proprietary engine (Autodesk Stingray / Bitsquid). No probe signature matches. This is a **new-engine** problem (new key + preset + a compiled, reversibility-safe enable/disable). Out of scope here — see Non-goals.

Marvel Rivals is the head of a real class: UE games wrap the project in an outer folder, and the wrapper name is the arbitrary UE project short-name (`Phoenix`, `b1`, `Pal`, `Marvel`) — **not guessable by string-match**, only discoverable structurally.

### The grounded finding the handoff under-specified — three sites, one trap

Detection is split across **three** code paths, all stuck at one level. They must move in lockstep:

| Site | Layer | Role | Today |
|---|---|---|---|
| `EngineScan.Probe` ([EngineScan.cs:25-26](../../../src/ModManager.App/Services/EngineScan.cs#L25-L26)) | App-IO | engine-decision **gate** (sets `HasContentPaks`) | root + 1 level |
| `EnginePresets.DetectUePakModLocation` ([EnginePresets.cs:111-130](../../../src/ModManager.Core/EnginePresets.cs#L111-L130)) | Core | add-wizard mod-path **seeder** | 1 level, passes a *leaf* project name |
| `ModLocator.UnrealProjects` ([ModLocator.cs:56-68](../../../src/ModManager.App/Services/ModLocator.cs#L56-L68)) | App-IO | runtime install-target **picker** | top-level subfolders with a `Content` dir |

**The trap:** deepen only the gate and Marvel Rivals detects as `ue-pak` but the seeder/picker still miss the nested project → mods route to the root-level `Content/Paks/~mods` that doesn't exist. A half-fix that's worse than no fix. And `project` has to graduate from a leaf name (`R5`) to a **relative path** (`MarvelGame/Marvel`). The downstream primitives already `Path.Combine(project, …)` ([ModLocations.cs:33,75-76](../../../src/ModManager.Core/ModLocations.cs#L73-L76)), so a multi-segment `project` flows through cleanly and the existing single-segment tests stay green.

## Goal

Arbitrary nested-UE games auto-detect on add/drop **without** a curated feed entry — gate, seeder, and picker all resolve the right `Content/Paks` up to two wrapper levels, fast on huge installs, and never the wrong one. Marvel Rivals is the regression that proves it; the fix generalizes to the long tail.

## Scope decision (the fork for review)

- **This slice = Tier 2 (probe deepening).** The generic win that also fixes Marvel Rivals' detection. Contained, well-understood code.
- **Tier 1 (Marvel Rivals feed entry) = optional companion**, in the *separate* `626-game-manifest` repo. With Tier 2 shipped, an uncurated add already auto-detects; the feed entry only adds the curated name + Nexus domain + `appId→engine` fast-path. It needs a store-id check (Marvel Rivals is Epic-primary; the Steam id `2767030` was **not** code-grounded here). Recommendation: ship Tier 2 first, do the feed entry as a follow-up data PR. See *Tier-1 companion* below.
- **Tier 3 (Helldivers 2) = deferred**, its own later slice once its mechanism is verified live and the anti-cheat product call is made. Rationale in Non-goals.

## Decisions (from research)

- **Depth cap: 2 wrapper levels** (`<root>/<W1>/<W2>/Content/Paks`, plus the 1-wrapper and no-wrapper cases). This covers the entire verifiable shipped UE corpus — Marvel Rivals (2 wrappers) is the deepest found; no shipped 3-wrapper game exists. Capping at 2 is a pragmatic bound (absence-of-evidence on 3+, flagged), **not** a blind `**/Content/Paks` walk — that would regress the documented one-level-for-speed rationale.
- **Budget:** the walk is hard-bounded by a directory-count cap (`ScanBudget.MaxDirs`, default 200) — it stops and returns what it found once the cap is hit. Combined with the depth-2 ceiling and the denylist skip (engine/AC/redist trees are never descended), this keeps it fast on huge installs. The gate short-circuits on the first qualifying match. **Same budget shape in all sites** so they stay in parity. (A wall-clock cap was considered and deferred: the dir-count cap plus the depth-2 + denylist bound covers every realistic game layout. Revisit only if a pathological single-directory-with-millions-of-immediate-children case ever surfaces — `Directory.GetDirectories` materializes eagerly, so that one case would need lazy enumeration or a timer.)
- **False-positive guard (layered, ranked)** — the #1 false positive is `Engine/Content/Paks`: every shipped UE game ships an `Engine/` sibling of the project at the same depth, so *prefer-shallowest alone cannot disambiguate it*.
  1. **Skip** a case-insensitive sibling-name denylist *before* considering any candidate: `Engine`, `Binaries`, `EasyAntiCheat`, `EasyAntiCheat_EOS`, `BattlEye`, `CommonRedist`/`_CommonRedist`/`Redist`/`Redistributable`/`Prerequisites`/`DirectXRedist`/`VCRedist`, and any `*Server` build folder unless explicitly chosen. `Engine` is mandatory.
  2. **Prefer shallowest** — a `Content/Paks` directly under a top-level subfolder beats a deeper one. Never descend past 2 wrappers.
  3. **Score** — prefer the project dir with a game-exe / `Binaries\Win64\*-Shipping.exe` sibling and/or shipping paks already inside that `Content/Paks` (`PakClassifier` shipping-name match). This is the real discriminator the denylist can't name (sub-game / `*Server` folders).
  4. **`.uproject` sibling = tie-breaker bonus only**, never a requirement — shipped pak builds normally strip it.
  5. **Multi-match → do not auto-pick.** If two-or-more project candidates survive skip+score, fall through to the existing "don't guess" path ([ModLocator.cs:41](../../../src/ModManager.App/Services/ModLocator.cs#L41), `projects.Count == 1`) and let the user choose. This discipline must survive deepening.
- **`PakClassifier` UE5 variant.** The exe/shipping-pak scoring signal (and base-game-pak hiding) leans on `PakClassifier`'s regex, which requires `-WindowsNoEditor` (UE4). UE5 ships plain `pakchunk0-Windows.pak`. **Marvel Rivals is UE5** — so this is in-scope: add a UE5 regex variant or the headline game's scoring signal is unreliable.
- **Pure decision, IO at the edge — but parity by construction.** One shared Core component owns the bounded walk + the pick rules, so the gate, seeder, and picker agree by definition (the same pattern `ModLocations.UePakModLocation` already uses as the single source of truth). Core doing bounded `System.IO` is established precedent (`DetectUePakModLocation` already does); `CorePurityTests` bans WinUI/WinRT, not `System.IO`.

## Non-goals

- **No new engine.** `GuessEngine`, the `EngineProbe` record, and the `ue-pak` preset are **unchanged** — all 9 existing `EngineDetectTests` + `CorePurityTests` are regression gates.
- **Helldivers 2 / Tier 3 — deferred, with rationale.** (1) New-engine, shares zero mechanism with Marvel Rivals — no UE probe change helps Stingray. (2) Its enable/disable collides with the reversibility law: the community "purge" is delete-from-`/data/`, but the launcher forbids `File.Delete` in toggle paths — disable must *move* the patch triad (`NAME.patch_N` + optional `.gpu_resources` + `.stream`) to a holding folder and re-enable must move it back *and* re-assert the patch number, all three files atomically. Real reversible-primitive design + tests (partial-triad states), not a one-liner. (3) The fingerprint (base hash `9ba626afa44a3aa3` + binary tables) is a moving target tied to game version that must be re-verified live, plus an unresolved product go/no-go on nProtect GameGuard auto-ban risk (mesh/model mods called out). Full research is captured in [`docs/superpowers/research/2026-06-15-engine-detection-research.md`](../research/2026-06-15-engine-detection-research.md) (written alongside this spec).
- **No blind recursive walk, no depth > 2.** No 3-wrapper shipped game found; revisit only if one surfaces.
- **No change to the disable/move guards** — `PaksRootGuard` base-pak protection stays as-is; deepening only changes *which* `Content/Paks` is chosen, not how paks within it are handled.

## Architecture

Pure-Core shared resolver + thin parity wiring at the three sites + a small `PakClassifier` UE5 addition. No new App UI.

### 1. Core — `UeProjectScan` (the single source of truth)

New `src/ModManager.Core/UeProjectScan.cs`. Owns the bounded walk *and* the pick rules so all sites agree by construction.

```
public sealed record UeProjectCandidate(
    string RelativeProjectPath,   // "" (root) | "Pal" | "MarvelGame/Marvel"
    int    WrapperDepth,          // 0 = root is the project, 1, or 2
    bool   HasShippingPak,        // a PakClassifier shipping-name pak inside its Content/Paks
    bool   HasBinariesSibling,    // Binaries\Win64\*-Shipping.exe next to the project dir
    bool   HasUprojectSibling);   // .uproject present (tie-breaker bonus only)

public readonly record struct ScanBudget(int MaxDirs = 200);  // wall-clock cap applied by the enumerator

// Bounded walk: root + up to 2 wrapper levels, denylist-skipped, budget-capped.
// Returns every <rel>/Content/Paks found (minus denylisted siblings). IO — bounded + deterministic.
public static IReadOnlyList<UeProjectCandidate> Enumerate(string gameRoot, ScanBudget? budget = null);

// Fast gate path: does at least one non-denylisted Content/Paks exist within 2 levels? Short-circuits.
public static bool HasAnyProjectPaks(string gameRoot, ScanBudget? budget = null);

// PURE decision over candidates: skip already applied by Enumerate; here prefer-shallowest + score
// + multi-match=no-pick. Trivially unit-testable with synthetic candidate lists.
public static UeProjectPick Pick(IReadOnlyList<UeProjectCandidate> candidates);
// UeProjectPick = Chosen(rel) | None | Ambiguous(candidates)

public static IReadOnlyList<string> Denylist { get; }  // the sibling-name skip set
```

- **Pick rules:** filter to candidates that look like a real game project (`HasShippingPak || HasBinariesSibling`, with `HasUprojectSibling` as a bonus); among those prefer the shallowest `WrapperDepth`; if exactly one survives → `Chosen`; if zero project-looking candidates but exactly one raw candidate → `Chosen` (lenient single-candidate fallback, matches today's behavior); if ≥2 survive → `Ambiguous` (no auto-pick).
- **Purity:** `Pick`, `Denylist` are pure (no IO). `Enumerate` / `HasAnyProjectPaks` do bounded `System.IO` (consistent with `DetectUePakModLocation` today). No WinUI/WinRT — `CorePurityTests` stays green.

### 2. Core — `EnginePresets.DetectUePakModLocation` delegates to `UeProjectScan`

Replace the one-level `Directory.GetDirectories(gameRoot)` loop with `UeProjectScan.Enumerate` + `Pick`. On `Chosen(rel)`, derive `loaderPresent` (a `~mods`/`LogicMods` child of *that* `rel/Content/Paks`) and build via the existing `ModLocations.UePakModLocation(rel, loaderPresent)` — now passed a multi-segment `rel`. `None`/`Ambiguous` → return null (caller falls back to the static preset path, unchanged). `BuildGameEntry`'s explicit-`input.ModPath` precedence is untouched ([EnginePresets.cs:66-74](../../../src/ModManager.Core/EnginePresets.cs#L66-L74)) — Tier-1 feed entries with an explicit `modPath` keep bypassing detection entirely.

### 3. App — `EngineScan.Probe` parity (the gate)

`HasContentPaks = UeProjectScan.HasAnyProjectPaks(root)` — same denylist + budget as the seeder/picker, so the gate can never say `ue-pak` on an `Engine`-only false positive, and never *miss* a 2-level project the picker would resolve. The other six probe bools are unchanged. Stays App-IO; the pure `GuessEngine` decision is untouched.

### 4. App — `ModLocator` parity (the picker)

`UnrealProjects` → `UeProjectScan.Enumerate` (returns relative project paths, not leaf names). `Detect` uses `Pick`: `Chosen` seeds the project path; `Ambiguous`/`None` preserve the existing "don't guess on multi-match" fall-through. `ModLocations.Candidates` already accepts multi-segment project names and `Path.Combine`s them — no change needed there.

### 5. Core — `PakClassifier` UE5 shipping-pak variant

Add a UE5 name variant (`pakchunk\d+.*-Windows\.pak`) alongside the existing `-WindowsNoEditor` (UE4), so the scoring signal (step 3 of the guard) and base-game-pak hiding both work for UE5 titles like Marvel Rivals. Pure Core, name+size only — no behavior change for UE4 games.

## Data shape

No new persisted shape and no migration. `ModLocation.Path` now sometimes carries a 2-segment relative path (`MarvelGame\Marvel\Content\Paks\~mods`) instead of a 1-segment one — same field, same camelCase-on-disk JSON, written through `AtomicJson` as today. A round-trip test covers a multi-segment path value.

## Tier-1 companion (optional, separate repo)

Marvel Rivals as a curated feed entry in `626-game-manifest` (mirror into the embedded `games-manifest.json` snapshot as the offline fallback), matching the verified Palworld/Hogwarts nested-`modPath` pattern:

```json
{ "id": "marvel-rivals", "name": "Marvel Rivals", "engine": "ue-pak",
  "stores": { "steamAppId": "<VERIFY — Epic-primary; 2767030 unconfirmed>" },
  "nexusDomain": "marvelrivals",
  "modPath": "MarvelGame/Marvel/Content/Paks/~mods",
  "provenance": { "sources": ["known-engines", "nexus-domains"], "status": "curated" } }
```

Zero app code: `BuildGameEntry` honors the explicit `modPath`, and `ManifestValidator.IsSafeRelativePath` accepts the forward-slash relative path. Adds the curated name + Nexus domain + (via `known-engines` + `steamAppId`) the `appId→engine` fast-path. **Caveats:** verify the store id before committing (consider `epicAppName`); this only helps Marvel Rivals when picked/detected, *not* the long tail — which is exactly Tier 2's job.

## Edge cases

- **Root *is* the project** (Stalker 2, `<root>/Content/Paks`) → `WrapperDepth 0` candidate, resolved as today.
- **`Engine/Content/Paks` beside `Phoenix/Content/Paks`** (same depth) → `Engine` denylisted; `Phoenix` chosen.
- **`Engine` + two real projects** (e.g. game + `*Server`, both with shipping paks) → `Ambiguous` → no auto-pick, user chooses.
- **Huge install** (1000s of subdirs) → walk stops at the dir-count/time budget, returns what it found; never hangs.
- **2-level project with explicit `input.ModPath`** → detection skipped entirely, explicit path wins (Tier-1/Tier-2 interaction, pinned by a test).
- **UE5 plain `pakchunk0-Windows.pak`** → recognized by the new variant as a shipping pak (scoring) and hidden by base-game filtering in paks-root form.

## Testing

**Core (xUnit) — the contract:**
- `UeProjectScan.Pick` (pure, synthetic candidates): skips `Engine`; skips AC/redist siblings; `None` vs `Chosen` vs `Ambiguous`; prefers shallowest; prefers the exe/shipping-pak sibling over a `*Server`; single-candidate lenient fallback.
- `UeProjectScan.Enumerate` (temp-dir fixtures, like `PaksRootPresetTests`): two-wrapper `MarvelGame/Marvel/Content/Paks` found; single-wrapper `Pal` found (no regression); no-wrapper root `Content/Paks` found; budget cap stops on 500+ subfolders; denylisted `Engine` never returned.
- `DetectUePakModLocation`: resolves the Marvel two-wrapper project; Palworld single-wrapper unchanged; no paks → null; multi-match → null (fallback).
- `BuildGameEntry_honors_explicit_modPath_over_two_level_detection`.
- `PakClassifier`: `IsBaseGamePak` matches the UE5 `-Windows.pak` variant; UE4 behavior unchanged.
- `ModLocation` multi-segment path camelCase round-trip.
- **Regression gates:** all 9 `EngineDetectTests` (pure decision untouched), `PaksRoot*` suite, `CorePurityTests` (new code stays pure).

**App:** not unit-testable (WinUI VM) — build-verified + live smoke. Smoke: add Marvel Rivals from Steam/Epic → detects `ue-pak`, mod path resolves to `MarvelGame/Marvel/Content/Paks/~mods`, a dropped `.pak` shows as a mod row and toggles; a single-wrapper game (Palworld/Hogwarts) still works (no regression); a game with an `Engine` sibling never mis-detects. Append to `docs/smoke-tests/pending.md`.

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/UeProjectScan.cs` | **NEW** — bounded 2-level walk + denylist + pure `Pick` rules; single source of truth |
| `src/ModManager.Core/EnginePresets.cs` | `DetectUePakModLocation` delegates to `UeProjectScan`; passes multi-segment `rel` to `UePakModLocation` |
| `src/ModManager.Core/PakClassifier.cs` | UE5 `-Windows.pak` shipping-name variant |
| `src/ModManager.App/Services/EngineScan.cs` | `HasContentPaks` via `UeProjectScan.HasAnyProjectPaks` (parity gate) |
| `src/ModManager.App/Services/ModLocator.cs` | `UnrealProjects` via `UeProjectScan.Enumerate`; `Detect` uses `Pick` (keeps multi-match discipline) |
| `tests/ModManager.Tests/` | `UeProjectScan` (pick + enumerate), `DetectUePakModLocation` 2-level, `PakClassifier` UE5, multi-segment round-trip, regression gates |
| `docs/smoke-tests/pending.md` | Marvel Rivals + single-wrapper + Engine-sibling smoke entries |
| `626-game-manifest` (separate repo) | *Optional Tier-1 companion* — Marvel Rivals curated entry (store id verified) |
