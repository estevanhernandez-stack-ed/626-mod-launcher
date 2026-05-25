# Save / world mods — design

- **Date:** 2026-05-25
- **Status:** Approved (shape confirmed with Este)
- **Roadmap:** Phase D7 in [docs/2026-05-25-backlog-roadmap.md](2026-05-25-backlog-roadmap.md).
- **Requirements:** [docs/2026-05-25-save-world-mods-note.md](2026-05-25-save-world-mods-note.md)
  (the Pirate Depot README distilled — install path, RocksDB-not-v2 rules, Local-not-Cloud, reset).
- **Why:** Some mods are **save files / world saves**, not paks (Windrose "Pirate Depot"). They
  install into the save tree, with their own path + safety rules. The launcher has no concept of
  this class. This adds it — built on the save manager, not a new system.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Spine | **Extend `SaveManager`** — reuse its snapshot/clone/restore machinery; don't reinvent. |
| Classify at drop | **Auto-detect + confirm**, informed by the game's declared save layout (save types + `saveModPath`) plus a generic fallback. |
| Structure | **Hybrid** — profile `saveModPath`/forbidden, with a built-in RocksDB default (Windrose OOTB). |

## As shipped — v1 slice (2026-05-25)

This PR lands the **safety-critical Core, fully tested + audited** — the irreversible-risk half:

- `SaveModDetect` (pak-veto + Worlds/`<GUID>` / GUID-folder / save-type detection),
  `SaveModInstaller` (`ResolveWorldsTarget` + `InstallWorld`/`ResetWorld`/`RemoveWorld` over
  `SaveManager.Backup`), `SaveModStore` (GUID→name via `AtomicJson`), and `saveModPath` /
  `saveModForbidden` threaded through `GameEntry` / `GameInput` / `BuildGameEntry` / profile / Add-Game.
- **All three load-bearing invariants enforced + pinned by tests:** (1) never write a forbidden
  game-managed folder (`RocksDB_v2`/`_Backups`) — template-first refusal *before* touching disk +
  resolved-path defense-in-depth; (2) snapshot-first on every mutating op; (3) zip-slip guard on
  extract.
- **Security fix (mod-safety audit, CRITICAL):** `worldGuid` is attacker-influenced (it comes from a
  dropped zip) and becomes a directory name — an unvalidated traversal value (`..\..\RocksDB_v2`)
  could have made `RemoveWorld` delete the game-managed tree. Fixed with `RequireSafeGuid` (single
  safe GUID segment) + `IsUnder` defense-in-depth on all three ops, pinned by `SaveModSecurityTests`.

**Deferred (composes on this Core; needs a live Windrose save tree to smoke-test):** the App layer —
the drop → detect → confirm → friendly-name → `InstallWorld` flow, and the save-mods management
surface (list via `SaveModStore.Load`, Reset/Remove via the ready Core ops) in/near the Saves dialog,
plus the Local-not-Cloud reminder.

## Spine: extend the save manager

`SaveManager` already gives the reversible primitives — `Backup` (zip the save tree to a timestamped
auto snapshot), the snapshot-first / never-overwrite pattern (`CloneToType`, `Restore`/`RestoreType`),
and `GameProfile` save-types + saveDir resolution. D7 adds a thin orchestrator, **`SaveModInstaller`**,
that composes those: snapshot the tree → extract a world into it → reset = re-extract the kept
original (a `Restore`-shaped operation). No parallel save system.

## Classify at drop (Core detection, App confirm)

`SaveModDetect.Looks Like SaveMod(zipEntryNames, GameProfile/saveLayout)` — pure, tested:
- **Strong signal:** the zip's structure matches the active game's declared save layout — a
  `Worlds/<GUID>` folder for a game whose `saveModPath` is RocksDB-style, or contents matching the
  game's declared save types (extensions). Uses what the app already knows about the game.
- **Generic fallback:** a GUID-named folder and **no** `.pak`/`.ucas`/`.utoc` entries.
- → returns a verdict the App surfaces as a **confirm** ("This looks like a world save — install
  into your saves?"). Decline → route to normal pak intake. (Auto-detect, never silent.)

## Structure resolution (hybrid)

Install target = `<saveDir>\<profile>\` + the save-mod path:
- **`saveModPath`** from the game profile (e.g. `RocksDB/{version}/Worlds`); `{version}` resolved by
  scanning the on-disk `RocksDB\` for the version subdir (create `Worlds/` if missing, per the FAQ).
- If the profile declares none → the **built-in RocksDB default** (`RocksDB/{version}/Worlds`) so
  Windrose works without an explicit entry.
- **`saveModForbidden`** (profile + built-in default: `RocksDB_v2`, `RocksDB_v2_Backups`). The
  installer **refuses** to resolve/write a target under any forbidden subfolder — hard guard.
- `<profile>` = the single profile folder under `SaveProfiles` (resolved dynamically).

## Install / manage flow

1. **Detect + confirm** (above).
2. **Resolve target** (saveDir + profile + saveModPath, `{version}` scanned, forbidden-subfolder guard).
3. **Snapshot the save tree first** — `SaveManager.Backup(saveDir, snapshotsDir, "before-savemod", auto:true)`. Reversible.
4. **Extract** the world (`<GUID>` folder) into the target `Worlds\`, with zip-slip guards (same as intake). **Keep the original zip** (copy into the save-mod store) for reset.
5. **Warn** about the post-install **Local-not-Cloud** prompt (the app can't make the in-game choice).
6. **Reset-to-pristine:** snapshot-first, then re-extract the kept original over the `<GUID>` folder.
7. **Remove:** snapshot-first, then delete/clear the `<GUID>` folder (reversible).

## Naming — GUID → friendly name

Worlds are GUID folders with no human name. Store `<dataDir>\save-mods.json`
(`GUID → { name, sourceZip, installedUtc }`) via `AtomicJson` so the UI shows "Pirate Depot," lists
installed save-mods, and knows the original zip for reset.

## App

- A **save-mods** surface (in/near the Saves dialog — it's the save tree): list installed worlds (by
  friendly name), with **Reset** and **Remove**, and the Local-not-Cloud reminder.
- Drop a world zip → the detect-confirm flow → install. Friendly-name prompt on install (default
  from the zip/folder name).

## Safety (laws + README rules)

- **Snapshot before every mutating op** (install / reset / remove) — reversible (law #3), reusing `SaveManager.Backup`.
- **Never** write a forbidden subfolder (`RocksDB_v2`/`_Backups`) — installer hard-refuses; the
  detect/confirm guards add a second layer.
- **Local-not-Cloud** warning at install.
- **Zip-slip / path-traversal** guards on extract (reuse the intake guards).

## Error handling

- No `saveDir` / profile resolvable → can't install; tell the user to open the game once / set the save folder.
- Target resolves under a forbidden subfolder → refuse with the reason (never write there).
- Missing original zip on reset → surface ("re-add the world to reset it").
- Extract failure → the pre-op snapshot is the recovery; surface the error.

## Testing (test-first, pure Core)

`tests/ModManager.Tests/SaveModTests.cs`:
1. `SaveModDetect` — a `Worlds/<GUID>` zip with no paks → save-mod; a `.pak`/`.ucas` zip → not;
   contents matching a declared save type → save-mod.
2. Target resolution — `saveModPath` with `{version}` resolves against an on-disk RocksDB version
   dir; default applies when the profile declares none.
3. **Forbidden-subfolder refusal** — a target resolving under `RocksDB_v2*` is rejected.
4. GUID→name map round-trip via `AtomicJson`.
5. Install/reset reuse `SaveManager.Backup` (already tested) — assert a snapshot is taken before the
   mutating op.

App save-mods UI + the detect-confirm intake are build-verified + a live smoke test (drop Pirate
Depot → detect → confirm → install → reset; verify the snapshot + that `RocksDB_v2` is untouched).

## Composes with

`SaveManager` (snapshot/restore reuse), `GameProfile` save types (detection knowledge), the agentic
profile (`saveModPath`/`saveModForbidden` agent-fillable — the hybrid override), the readme viewer
(B3 — readmes carry the install/Local-Cloud rules), and Nexus (D6 — IDs Nexus-hosted save-mods).

## Scope / limits (v1)

- **Worlds** (the Pirate Depot case); other save-mod kinds (character saves, full profiles) follow
  the same machinery later.
- Hybrid path (profile + RocksDB default). Install is **local-drop** (D6 identifies, doesn't download).
- One profile folder assumed (the common case); multi-profile selection later.

## File structure

- Create: `src/ModManager.Core/SaveModDetect.cs` (detection), `src/ModManager.Core/SaveModInstaller.cs`
  (resolve + install/reset/remove over `SaveManager`), `src/ModManager.Core/SaveModStore.cs` (GUID→name map).
- Modify: `src/ModManager.Core/GameEntry.cs` + `GameProfileImport.cs`/`GameProfilePrompt.cs` —
  `saveModPath` + `saveModForbidden` on the game + agentic profile.
- Modify: `src/ModManager.App` — the Saves dialog (save-mods list + Reset/Remove), the detect-confirm
  intake hook, the install friendly-name prompt + Local-not-Cloud warning; DI registration.
- Tests: `tests/ModManager.Tests/SaveModTests.cs`.
