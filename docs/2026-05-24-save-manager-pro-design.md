# Save Manager — professional expansion (design)

**Date:** 2026-05-24
**Status:** approved (design); pending implementation plan
**Repo:** 626-mod-launcher (.NET 10 / WinUI)

## Overview

The save manager already does whole-folder snapshot **Backup** (timestamped, labeled, stored
outside the save dir), undoable **Restore** (snapshots current state first), **List/Delete**, plus
save-type recognition (`.sl2`/`.co2`) and **clone-to-other-type**. This expands it toward a
professional, "never lose a save" tool while staying dead-simple and **user-managed** — the 626
goal of enterprise-grade features for every user.

Three approved pillars: **(1) all save types + 3-way cloning**, **(2) automatic backups
(user-managed) + a safety cap**, **(3) granular per-type restore** — built by extending the existing
`SaveManager` (pure Core, test-first).

Crucially, this work also lays a **repeatable foundation**: a declarable **Game Profile** so this
isn't hard-coded FromSoft logic. The app consults the profile to *know which features a game needs*.
We define the full profile shape now and wire only the **save** slice; the other per-game catalogs
migrate onto it over time. (User decision 2026-05-24: "both — save slice of the foundation.")

## Repeatability — the Game Profile (foundation)

Today, per-game knowledge is scattered across separate catalogs: `LaunchOptions.For(appId)`,
`KnownEngines`, `ModEngine2`, `EnginePresets`, the `DirectInject` signatures, and (currently) a flat
`SaveManager.SaveTypes` dict. The principle: these should converge into one **declarable profile the
app reads to decide which features apply to a game** — so adding a game is *data*, not new code, and
the app *understands when* a feature is relevant.

**Shape (defined now; only `SaveTypes` populated + wired this round):**

```
GameProfile  — resolved per game (engine-level defaults, optional per-appId overrides)
  Engine        : string         e.g. "fromsoft"           [already on GameEntry]
  SaveTypes     : SaveType[]      ← IMPLEMENTED THIS ROUND  (extension + label)
  -- convergence targets (stay in their current catalogs for now; migrate later) --
  LaunchOptions : ...            (today: LaunchOptions.For(appId))
  AntiCheat     : ...            (today: AntiCheat swap + AntiCheatToggle option)
  ModLayout     : ...            (today: ModLocations / DirectInject signatures)
```

**Resolution:** `GameProfiles.Resolve(engine, steamAppId)` returns a `GameProfile`. Layered —
engine-level defaults, overridable per App ID. For saves now: `fromsoft` →
`[.sl2 Vanilla, .co2 Seamless, .err Reforged]`. Adding another engine's save types (e.g. `bethesda`
→ `.ess`) is a one-line catalog entry — that is the repeatability.

**How the app "knows when":** two layers, both required.
- **Declared:** the resolved profile says which save types a game *can* have. Clone targets come from
  here (a Vanilla save can clone to the profile's other declared types).
- **Observed:** what's actually on disk / in a snapshot. Per-type restore offers only types present in
  the snapshot; the clone/convert UI shows only when the game's profile declares >1 type.
- **Baseline always works:** a game with no profile entry (unknown) still gets whole-folder
  backup/restore — the universal floor. The gated extras (clone, per-type restore) appear only when
  the profile declares types. Never guess; gate on the catalog. (Same stance as Launch Options.)

## Scope

**In scope**
1. `GameProfile` shape + `GameProfiles.Resolve`, with the **SaveTypes** slice implemented and wired.
2. Recognize `.err` (Reforged) with `.sl2` (Vanilla) and `.co2` (Seamless) **via the profile**; clone
   a save between any of a game's declared types.
3. Automatic snapshots: always before risky ops (restore, clone); **opt-in** before launch; a light
   retention cap that never prunes user backups.
4. Granular restore: restore a **single save type** from a snapshot.

**Out of scope (explicitly deferred)**
- Migrating LaunchOptions / AntiCheat / ModLayout onto `GameProfile` (later; shape is reserved for it).
- Per-character-slot restore (encrypted BND4 — needs save-format decrypt/parse; separate large effort).
- Integrity checksums; cloud/offsite; timer or filesystem-watcher auto-backups (user deprioritized).

## Pillar 1 — all save types + 3-way cloning (profile-driven)

- Save types are resolved from the active game's profile (`GameProfiles.Resolve(...).SaveTypes`), not a
  global hard-coded dict. `fromsoft` declares `.sl2`/`.co2`/`.err`.
- `SaveManager.ListSaveFiles(saveDir, saveTypes)` labels files using the resolved types.
- `CloneToType(saveDir, sourceFileName, targetExt, overwrite=false)` (exists) copies a save to another
  extension — source untouched, no silent overwrite. Works for any number of types.
- **UI:** each save-file row shows its type and a **"Clone to…"** menu of the *other* declared types.
  If the target already exists, surface the no-overwrite error AND offer a gated **"Snapshot &
  replace"** (auto-snapshot, then `CloneToType(..., overwrite:true)`).

## Pillar 2 — automatic backups (user-managed) + safety cap

- **Auto snapshots are tagged** so retention can tell them from deliberate user backups. A snapshot is
  "auto" when its label carries a reserved `auto-` marker (`auto-before-launch`, `auto-before-restore`,
  `auto-before-clone`). `SaveSnapshot` gains `IsAuto`. The `auto-` prefix is **reserved**: user-facing
  `Backup` strips a leading `auto-`/`auto` from a user label, so a user backup can never be
  misclassified and pruned. UI shows autos quietly ("auto · before launch").
- **Before risky ops:** restore and clone auto-snapshot first (restore already does — re-tag as auto;
  add to clone's "Snapshot & replace"). Always on.
- **Before launch:** per-game opt-in. `GameEntry.AutoBackupOnLaunch` (bool, default `false`). When on,
  the launch path auto-snapshots (`auto-before-launch`) then prunes, before starting the game. Shown as
  an "Auto-backup before launch" checkbox in the Saves dialog.
- **Retention cap:** `Prune(savesDir, keepLastAuto)` keeps **all user snapshots** plus the newest
  `keepLastAuto` autos; deletes older autos only. `GameEntry.SaveAutoKeep` (int?, default `25`; `null`
  = unlimited). User-adjustable; exists only so before-launch autos stay bounded.
- **Mostly user-managed:** no timers, no watchers.

## Pillar 3 — granular per-type restore

- `SaveManager.TypesInSnapshot(snapshotZip, saveTypes)` → the declared save types actually present in a
  snapshot (peek zip entries) so the UI offers only types that exist in it.
- `RestoreType(snapshotZip, saveDir, savesDir, extension)` extracts only files of that extension into
  the save folder (leaving other types in place), after auto-snapshotting current (`auto-before-restore`).
  Whole-snapshot `Restore` stays as-is.
- **UI:** each snapshot row keeps **Restore** (whole) and adds a **"Restore only <type>"** menu from
  `TypesInSnapshot`.

## Architecture

- **New (Core, pure):** `GameProfile` + `SaveType(Extension, Label)`; `GameProfiles.Resolve(engine,
  appId)` (engine-keyed save types, appId override hook). The other profile fields are declared but
  unfilled this round.
- **`SaveManager` (Core, pure System.IO):** consumes resolved `saveTypes` (no global dict); adds
  `SaveSnapshot.IsAuto`, auto-tagging in `Backup`, `Prune`, `TypesInSnapshot`, `RestoreType`.
- **`GameEntry`:** `AutoBackupOnLaunch` (bool), `SaveAutoKeep` (int?), persisted camelCase
  (Electron-shared registry convention).
- **Launch path** (`LauncherService.Launch` / `MainViewModel`): consult `AutoBackupOnLaunch`; backup +
  prune before launching. Save folder already detected (Ludusavi-first) by existing code.
- **UI** (`SavesDialog`): profile-driven clone menu, per-type restore menu, auto-backup checkbox +
  keep count. Clone/per-type UI shows only when the profile declares >1 type.

## Error handling

- Reversibility (operating law #3): every save-changing op (restore, restore-type, clone-replace)
  auto-snapshots first; failures surface as dialog status, never silent.
- No-clobber: clone never overwrites unless the user takes the gated "Snapshot & replace".
- Prune never deletes a user (non-auto) snapshot; an unrecognized snapshot name is treated as user (kept).
- Locked files (game running) surface the IO error.

## Testing (test-first, Core, temp dirs)

- `GameProfiles.Resolve`: `fromsoft` yields `.sl2/.co2/.err`; unknown engine yields no declared types
  (baseline-only); appId override (if added) wins over engine default.
- `.err` recognized + labeled via profile; clone vanilla↔seamless↔reforged round-trips; clone refuses
  overwrite.
- `Prune` keeps all user snapshots + newest N autos, deletes older autos, never a user one.
- `TypesInSnapshot` reports exactly the declared types zipped.
- `RestoreType` restores only the chosen type, leaves others, auto-snapshots first (reversible).
- `IsAuto` parsed correctly; user label `auto-…` is de-reserved (treated as user, not pruned).

## Sequencing

1. `GameProfile` + `GameProfiles.Resolve` (save slice) — the foundation.
2. Pillar 1 (profile-driven `.err` + 3-way clone UI).
3. Pillar 3 (per-type restore: `TypesInSnapshot` + `RestoreType` + UI).
4. Pillar 2 (auto-tagging + `Prune` + before-launch toggle + retention).
