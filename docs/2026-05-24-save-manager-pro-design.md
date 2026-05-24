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
(user-managed) + a safety cap**, **(3) granular per-type restore**. Built by extending the existing
`SaveManager` (pure Core, test-first) — not a new subsystem.

## Scope

**In scope**
1. Recognize `.err` (Reforged) alongside `.sl2` (Vanilla) and `.co2` (Seamless); clone/convert a
   save between **any** of the three.
2. Automatic snapshots: always before risky ops (restore, clone); **opt-in** before launch; a
   light retention cap so auto snapshots don't grow forever.
3. Granular restore: restore a **single save type** from a snapshot, not just the whole folder.

**Out of scope (explicitly deferred)**
- Per-character-slot restore (FromSoft saves are an encrypted BND4 archive — needs full
  save-format decrypt/parse; separate, much larger effort).
- Full integrity layer (per-snapshot checksums/verification) — user deprioritized.
- Cloud / offsite backup.
- Timer-based or filesystem-watcher auto-backups (user wants it "mostly user-managed").

## Pillar 1 — all save types + 3-way cloning

- `SaveManager.SaveTypes` gains `".err" → "Reforged"`. (`.sl2`=Vanilla, `.co2`=Seamless Co-op.)
- `CloneToType(saveDir, sourceFileName, targetExt, overwrite=false)` already copies a save to another
  extension, source untouched, no silent overwrite. No logic change needed for 3 types — the UI just
  offers all *other* types.
- **UI:** each save-file row shows its type and a **"Clone to…"** menu listing the other types. If the
  target already exists, surface the existing no-overwrite error AND offer a gated **"Snapshot &
  replace"** (auto-snapshot the folder, then `CloneToType(..., overwrite:true)`).

## Pillar 2 — automatic backups (user-managed) + safety cap

- **Auto snapshots are tagged** so retention can tell them from the user's deliberate backups. A
  snapshot is "auto" when its label carries a reserved `auto-` marker (e.g. `auto-before-launch`,
  `auto-before-restore`, `auto-before-clone`). `SaveSnapshot` gains `IsAuto` (parsed from the label);
  the UI labels them quietly ("auto · before launch"). The `auto-` prefix is **reserved**: the
  user-facing `Backup` strips a leading `auto-`/`auto` from a user's label so only the app can mint an
  auto snapshot — a user backup can never be misclassified and pruned.
- **Before risky ops:** restore and clone auto-snapshot first (restore already does — re-tag as auto;
  add the same to clone's "Snapshot & replace"). Always on; it's just safety.
- **Before launch:** per-game opt-in. `GameEntry.AutoBackupOnLaunch` (bool, default `false`). When on,
  the launch path auto-snapshots (`auto-before-launch`) then prunes, before starting the game. Surfaced
  as an "Auto-backup before launch" checkbox in the Saves dialog.
- **Retention cap:** `Prune(savesDir, keepLastAuto)` keeps **all non-auto (user) snapshots** plus the
  newest `keepLastAuto` auto snapshots; deletes older autos only. `GameEntry.SaveAutoKeep` (int?,
  default `25`; `null` = unlimited). User-adjustable. Only exists so before-launch autos stay bounded.
- **Mostly user-managed:** no timers, no watchers. Manual backups remain the primary flow.

## Pillar 3 — granular per-type restore

- `SaveManager.TypesInSnapshot(snapshotZip)` → the recognized save types present in a snapshot (peek
  zip entries) so the UI only offers types that exist in it.
- `RestoreType(snapshotZip, saveDir, savesDir, extension)` extracts only files of that extension from
  the snapshot into the save folder (leaving other types in place), after auto-snapshotting current
  state (`auto-before-restore`). Whole-snapshot `Restore` stays as-is.
- **UI:** each snapshot row keeps **Restore** (whole) and adds a small **"Restore only <type>"** menu,
  populated from `TypesInSnapshot`.

## Architecture

- All logic in `SaveManager` (pure `System.IO`, no UI) — the existing save seam. New surface:
  `.err` in `SaveTypes`; `SaveSnapshot.IsAuto`; `Backup` auto-tagging (param or `BackupAuto` helper);
  `Prune`; `TypesInSnapshot`; `RestoreType`.
- `GameEntry` gains `AutoBackupOnLaunch` (bool) and `SaveAutoKeep` (int?), persisted camelCase
  (Electron-shared registry convention).
- The launch path (`LauncherService.Launch` / `MainViewModel`) consults `AutoBackupOnLaunch` and runs
  backup + prune before launching. Save folder is already detected (Ludusavi-first) by existing code.
- UI lives in `SavesDialog` (clone menu, per-type restore menu, auto-backup checkbox + keep count).

## Error handling

- Reversibility (operating law #3): every save-changing op (restore, restore-type, clone-replace)
  auto-snapshots first; failures surface as dialog status, never silent.
- No-clobber: clone never overwrites unless the user takes the gated "Snapshot & replace".
- Prune never deletes a user (non-auto) snapshot; a malformed/unrecognized snapshot name is treated as
  user (kept), never pruned.
- Locked files (game running) surface the IO error to the user.

## Testing (test-first, Core, temp dirs)

- `.err` recognized + labeled; clone vanilla↔seamless↔reforged round-trips; clone refuses overwrite.
- `Prune` keeps all user snapshots + newest N autos, deletes older autos, never a user one.
- `TypesInSnapshot` reports exactly the types zipped.
- `RestoreType` restores only the chosen type, leaves others, and auto-snapshots first (reversible).
- `IsAuto` parsed correctly from auto vs user labels.

## Sequencing

1. Pillar 1 (`.err` + 3-way clone UI) — smallest, immediate.
2. Pillar 3 (per-type restore) — `TypesInSnapshot` + `RestoreType` + UI menu.
3. Pillar 2 (auto-tagging + `Prune` + before-launch toggle + retention) — touches the launch path.
