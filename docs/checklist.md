# Build Checklist — Phase 1: ModManager.Core + test contract

**Blueprint:** [`docs/spec.md`](spec.md) (.NET 10 / WinUI 3 rewrite scope)
**Phase:** 1 of 3 — port the pure cores to C#, test-first. No WinUI shell (Phase 2), no packaging (Phase 3).
**Source of truth:** the Electron app at `C:\Users\estev\Projects\mod-manager-builder` (`shell\*.js` cores + `tests\*.test.js`).

## The contract

The ported xUnit suite **is** the acceptance contract. A core is not "ported" until its test passes in C#. The JS suite is green at **134 tests**; **131 port to C#**. The 3 that don't: `proxy-core` (that logic lives in the deployed Cloudflare Worker and stays JS — the C# client only points an `HttpClient` at the proxy URL).

Build order is **leaf-first**: cores with no inter-core dependency port before the cores that compose them. `Scanner` is last because it depends on nearly everything.

| Effort | Meaning |
|---|---|
| S | one sitting, mechanical port |
| M | port + a real design decision (DI seam, registry/IO adapter) |
| L | large surface, many tests, composes other cores |

## Build order

- [ ] **00 — Scaffold the solution** · dep: none · effort: S
  - `dotnet new sln -n ModManager`; `ModManager.Core` (classlib, net10.0) + `ModManager.Tests` (xunit, net10.0, refs Core); add both to the sln.
  - `.gitignore` for .NET (bin/obj/.vs); `Directory.Build.props` with `LangVersion latest`, `Nullable enable`, `TreatWarningsAsErrors true`.
  - **Acceptance:** `dotnet test` runs and reports 0 tests, exit 0.

- [ ] **01 — Core purity guard** · dep: 00 · effort: S · ports `core-purity.test.js` (2)
  - Architecture test: reflect over the loaded `ModManager.Core` assembly; assert it references no WinUI / Windows App SDK / `ModManager.App` assemblies (the C# analog of "cores never `require('electron')`").
  - **Acceptance:** purity test red first (against a deliberately-bad ref), then green; Core stays UI-free.

- [ ] **02 — SafeUrl** · dep: 00 · effort: S · ports `url-core.test.js` (2)
  - `isHttpUrl` → `SafeUrl.IsHttpUrl`. http/https only; everything else false.
  - **Acceptance:** url tests green.

- [ ] **03 — AtomicJson** · dep: 00 · effort: S · ports `fs-atomic.test.js` (4)
  - `writeJsonAtomic` → temp-write + atomic rename. `System.IO`.
  - **Acceptance:** atomic-write tests green (incl. no-partial-file-on-crash semantics).

- [ ] **04 — Fingerprint** · dep: 00 · effort: M · ports `fingerprint-core.test.js` (9)
  - MurmurHash2 (32-bit, seed 1) over whitespace-stripped bytes → `Fingerprint`. **Golden: JEI = 3089143260.** Watch C# `Math.imul`-equivalent overflow semantics (`unchecked`, `uint`).
  - **Acceptance:** all 9 green, golden hash exact.

- [ ] **05 — NameMatch** · dep: 00 · effort: S · ports `name-match-core.test.js` (5)
  - `cleanModName` (strip load-order tags / multipliers / `_P`) + `pickBestMatch` (token-Jaccard, threshold 0.5).
  - **Acceptance:** name-match tests green.

- [ ] **06 — Classification** · dep: 00 · effort: S · ports `classification-core.test.js` (2)
  - MP/SP classification map logic.
  - **Acceptance:** classification tests green.

- [ ] **07 — Variant** · dep: 00 · effort: S · ports `variant-core.test.js` (9)
  - Variant grouping/parsing.
  - **Acceptance:** variant tests green.

- [ ] **08 — EnginePresets + GameEntry** · dep: 00 · effort: S · ports `engine-presets.test.js` (6)
  - `ENGINE_PRESETS` data + `slugify` / `uniqueId` / `buildGameEntry`. Presets port as data classes or embedded resource.
  - **Acceptance:** engine-presets tests green.

- [ ] **09 — Registry (game registry)** · dep: 00 · effort: S · ports `registry-core.test.js` (6)
  - `emptyRegistry` / `getActiveGame` / `setActiveGame` / `upsertGame` → `Registry`. (Game-list registry, not the Windows registry — that's item 13.)
  - **Acceptance:** registry tests green.

- [ ] **10 — Profile** · dep: 00 · effort: S · ports `profile-core.test.js` (10)
  - Profile name safety + load/save shape (`safeProfileName`, profile model).
  - **Acceptance:** profile tests green.

- [ ] **11 — Metadata** · dep: 00 · effort: M · ports `metadata-core.test.js` (4) + `metadata-honor.test.js` (2)
  - `mergeMetadata` (curated-wins) + `prettify` + honor-the-builders display fields (author, source, downloads, donation link).
  - **Acceptance:** both metadata suites green.

- [ ] **12 — Themes** · dep: 00 · effort: S · ports `themes.test.js` (12)
  - `buildThemeList` merge logic (built-ins + user themes, dedup, validation). Pure logic only; the JSON theme resources + actual theming are Phase 2.
  - **Acceptance:** themes tests green.

- [ ] **13 — SteamParse** · dep: 00 · effort: M · ports `steam-core.test.js` (5)
  - `appmanifest` + `libraryfolders.vdf` parsing → `SteamParse` (pure string parsing). The Windows-registry read of the Steam install path and `steam://` launch are the integration adapter (`Steam.cs`) layered on top in Phase 2 wiring — out of Phase 1 test scope.
  - **Acceptance:** steam-core parsing tests green.

- [ ] **14 — CurseForge request builders** · dep: 00 · effort: S · ports `curseforge-core.test.js` (6)
  - Pure request shape: getMod(s), search, `/v1/fingerprints`, resolveGameId, `x-api-key` header, proxy baseUrl.
  - **Acceptance:** curseforge-core tests green.

- [ ] **15 — Intake** · dep: 03 (AtomicJson), 04 (Fingerprint) · effort: M · ports `intake-core.test.js` (4) + `intake-folder.test.js` (2)
  - Zip-slip / path-traversal guards, `System.IO.Compression` extract, folder expand / walk-files. (Smart-intake fingerprint-at-drop wiring lands in item 18 with Scanner.)
  - **Acceptance:** intake + intake-folder tests green; malicious-zip cases rejected.

- [ ] **16 — CurseForgeClient** · dep: 14 (builders), 04 (Fingerprint) · effort: M · ports `curseforge-client.test.js` (4) + `curseforge-search.test.js` (3) + `fingerprint-matches.test.js` (2)
  - Injectable `HttpClient` seam (test with a fake handler — no live network). `getMod(s)` / `search` / `getFingerprintMatches` / `parseFingerprintMatches` / `resolveGameId`.
  - **Acceptance:** all three client suites green against a stub handler.

- [ ] **17 — Scanner + GameContext + data dir** · dep: 03, 06, 07, 08, 09, 10, 11 · effort: L · ports `scanner.test.js` (7) + `scanner-disable.test.js` (3) + `scanner-bulk.test.js` (3) + `scanner-profiles.test.js` (3) + `scanner-intake.test.js` (3) + `data-dir.test.js` (7)
  - The filesystem core: `gameContext`, `buildModList`, enable/disable (phase-ordered disable + rollback), `setAllMods` / `applyMode` (MP/SP), profiles, `dataDirForGame` (library `_626mods/<gameid>`), `migrateDataDir`, no-overwrite `placeMod`. Reversible/atomic file ops throughout.
  - **Acceptance:** all six scanner/data-dir suites green; disable rollback verified; no-overwrite verified.

- [ ] **18 — Scanner metadata integration** · dep: 16 (Client), 17 (Scanner), 04 (Fingerprint) · effort: M · ports `refresh-metadata.test.js` (3) + `smart-intake.test.js` (3)
  - `refreshMetadataByName` (search-by-name) + `fingerprintIdentify` (smart intake: hash at drop → exact CurseForge match). Best-effort — a lookup failure must not fail intake.
  - **Acceptance:** refresh-metadata + smart-intake suites green; lookup failure doesn't throw.

- [ ] **99 — Documentation & Security Verification** · dep: all · effort: M
  - README for the new repo (what it is, build/test commands, link back to the Electron spec repo).
  - **Secrets scan:** confirm no CurseForge API key anywhere in source/build output — the key lives only in the Worker; the client carries only the public proxy URL (operating law #2). Grep the tree + the built artifact.
  - **Dependency audit:** `dotnet list package --vulnerable` clean; pin package versions.
  - Confirm `.gitignore` excludes `bin/`, `obj/`, secrets; full suite green (`dotnet test` = 131 ported tests, 0 fail).
  - **Acceptance:** README present, secrets scan clean, no vulnerable packages, 131/131 green.

## Out of scope for Phase 1

- **`proxy-core` (3 tests)** — Cloudflare Worker logic, stays JS/deployed. The C# `CurseForgeClient` points at the existing proxy URL unchanged.
- **`popular-games` catalog** — Phase 2 (data + Add-Game quick-pick UI). **Note:** not on `master` — it's stranded on the unmerged `feat/popular-games` branch (commit `0b526db`). When Phase 2 ports it, pull the source from that branch (or re-merge it into the Electron repo first).
- **Theme JSON resources + actual theming** — Phase 2 (visual). Only the `buildThemeList` merge logic ports in item 12.
- **`Steam.cs` registry read + `steam://` launch** — integration adapter, Phase 2 wiring. Item 13 ports only the pure parsing.
- **WinUI shell** (Views/ViewModels, DI host, dialogs) — Phase 2.
- **MSIX / Microsoft Store packaging** — Phase 3.
