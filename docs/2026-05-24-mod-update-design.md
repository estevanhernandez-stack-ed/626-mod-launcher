# Mod update via collision-aware intake — design

- **Date:** 2026-05-24
- **Status:** Approved (shape confirmed with Este)
- **Why:** There is no way to update an installed mod. Both intake paths refuse to overwrite an
  existing file (the "never silently clobber" law) and report it as a skip — surfaced only as a
  one-line status-bar message. Dropping a new version of a mod (e.g. Seamless Co-op, whose
  `ersc.dll` + launcher exe + ini must stay version-matched) does nothing visible and leaves the
  old files in place. Result: the mod can't be updated through the app, and the desync that
  prompted the update persists.

## Current behavior (the gap)

- `Scanner.AddMods` (mod-folder games) → `PlaceFile`: `if (File.Exists(dest)) return false;` →
  reported as `Skipped("already installed")`.
- `DirectInject.Install` (direct-inject games like FromSoft loose-file mods): `if (Exists(dest))
  { Skipped("already present"); continue; }` — same refusal.
- The App (`MainViewModel.AddModsAsync`) surfaces the result as `StatusText` only — a 12px status
  bar line ("Nothing installed — skipped N…"). Easy to miss, and offers no action.

So the two defects: **(1) no update/replace flow**, and **(2) the skip is surfaced too weakly to
act on.**

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Old version on update | **Prompt: replace or skip.** Replace moves the old file to a reversible backup, then installs the new. Never silently overwrites; never deletes. |
| Update unit | **File-by-file.** Each colliding file is its own replace/skip decision. (Not whole-mod-set — Este's call for transparency and to cover unrecognized mods.) |
| Desync safeguard | All collisions from one drop are shown in **one dialog** with a **Replace all / none** toggle, so updating a multi-file mod is a single action. |
| Scope | **Both intake paths** — standard mod-folder and direct-inject. Shared plan/collision types. |

## Architecture — two-phase intake

Today intake detects-and-installs in one pass, which is why it can only skip on conflict. Split it
into a plan phase and an execute phase, with the dialog between them. All file logic stays in the
pure cores (`Scanner`, `DirectInject`); the App owns the dialog and wiring (pure-core / thin-shell).

### Shared types (Core)

```csharp
// A file in the drop whose destination already exists.
public sealed record IntakeCollision(string Name, string RelPath, string ExistingPath, string IncomingSource);

// The result of planning a drop without touching disk.
public sealed record IntakePlan(
    IReadOnlyList<string> ToAdd,                 // new files — will install
    IReadOnlyList<IntakeCollision> Collisions,   // already exist — need a replace/skip decision
    IReadOnlyList<SkippedItem> Unsafe);          // path-traversal / not-a-mod — refused, surfaced
```

### Phase 1 — Plan (Core, no writes)

- `Scanner.PlanIntake(IEnumerable<string> paths, GameContext c) : IntakePlan`
- `DirectInject.Plan(string playFolder, IEnumerable<string> sourcePaths) : IntakePlan`

Each reads the drop the same way its installer does today (expand folders, read zip entries, apply
the path-traversal / wrapper-prefix guards) but, instead of copying, classifies each resolved
destination as **add** (no existing file), **collision** (destination exists), or **unsafe**
(refused). No file is created or moved.

### Phase 2 — Dialog (App)

If `Collisions` is empty, execute straight through (today's behavior — no prompt for a clean
install). If non-empty, show `UpdateModsDialog`:

- One row per collision: the file name/relative path, a **Replace** checkbox (default **checked**),
  and a quiet "currently installed" note.
- A **Replace all / Replace none** master toggle at the top.
- The new (non-colliding) files listed below as "Will install" (informational, not toggleable).
- Confirm / Cancel. Confirm returns the set of collisions the user chose to replace.

This dialog is the prominent notice that replaces the status-bar whisper.

### Phase 3 — Execute (Core)

- `Scanner.ExecuteIntake(IntakePlan plan, ISet<string> replaceRelPaths, GameContext c) : IntakeResult`
- `DirectInject.Execute(string playFolder, string backupRoot, IntakePlan plan, ISet<string> replaceRelPaths) : IntakeResult`

For each planned item:
- **Add** → install (copy/extract), as today.
- **Collision in `replaceRelPaths`** → back up the existing file to the reversible store
  (below), then install the new file in its place. Count as **Updated**.
- **Collision not in `replaceRelPaths`** → skip, reported.
- **Unsafe** → skip, reported (carried from the plan).

`IntakeResult` gains an `Updated` list alongside `Added` / `Skipped`.

## Reversibility — the replaced-versions store

The law: a replaced file is recoverable, never deleted. On replace, the existing file is **moved**
(not copied-over) into a timestamped backup before the new one lands:

```
<dataDir>/replaced/<yyyyMMdd-HHmmss>/<relPath>      # the prior file(s)
<dataDir>/replaced/<yyyyMMdd-HHmmss>/__626replaced.json   # { original abs path, relPath, takenUtc } per file
```

v1 keeps the backups so a replace is always undoable by hand or by a later tool. **A "revert
update" UI is out of scope for v1** (the metadata is written so it can be added cheaply later).
Reuse the cross-volume-safe `MoveAny` helper already in `DirectInject`.

## UX

- Clean install (no collisions): unchanged — no dialog.
- Update: the dialog is modal and explicit; nothing happens until the user confirms.
- After execute, status reads: `Updated N, added M, skipped K` (and `— old versions kept, revert
  anytime` when N > 0).
- Game-running / locked file mid-execute surfaces an honest error (same phrasing as disable: "is
  the game running?") and rolls back that file's partial move.

## Testing (test-first, xUnit over temp dirs)

Per path (`Scanner` and `DirectInject`):

1. **Plan** classifies a drop into add / collision / unsafe correctly (zip + loose files + folder;
   path-traversal entry lands in `Unsafe`).
2. **Execute, replace chosen** → old file moved to `replaced/<ts>/`, new file in place, counted
   `Updated`; backup metadata written.
3. **Execute, replace not chosen** → old file untouched, new not installed, counted `Skipped`.
4. **Execute, new file** → installed, counted `Added`.
5. **Recoverability** → the backed-up prior file exists under `replaced/` and matches the old bytes.
6. **Multi-file drop** (Seamless-like: dll + exe + ini) with replace-all → all three updated in one
   execute; none left at the old version.

## Known limitation (file-by-file trade)

If a new version **removes** a file the old one shipped, that orphan stays (we only act on files
present in the drop). Whole-set replace would remove it, but file-by-file was chosen for
transparency. Acceptable; documented so it's a conscious trade. Recognized-mod whole-set replace
can be a later enhancement on top of this plan.

## Out of scope (v1)

- "Revert update" UI (the backup + metadata exist; the restore button is later).
- Whole-mod-set replace for recognized direct-inject mods.
- Mod-Engine-2 (config-backed) games — drop-to-install isn't wired there yet; this design covers
  the standard and direct-inject paths only.

## File structure

- Modify: `src/ModManager.Core/Scanner.cs` — `PlanIntake`, `ExecuteIntake`; `IntakeResult.Updated`.
- Modify: `src/ModManager.Core/DirectInject.cs` — `Plan`, `Execute`, replaced-store backup.
- Add: `src/ModManager.Core/IntakePlan.cs` — `IntakePlan`, `IntakeCollision` (shared types).
- Add: `src/ModManager.App/UpdateModsDialog.xaml` + `.xaml.cs` — the collision dialog.
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — `AddModsAsync` runs plan → dialog →
  execute for both `DirectInjectBacked` and standard paths.
- Tests: `tests/ModManager.Tests/IntakeUpdateTests.cs` (new) covering both cores.
