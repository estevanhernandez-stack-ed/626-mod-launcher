# UE config-tweak mods — a 7th mod class (Engine.ini & friends)

**Date:** 2026-06-11
**Status:** Design — approved in brainstorm, pending spec review
**Branch:** `feat/ue-config-mods`

## Problem

Dropping `Peformance Enhancer - UE - Witchfire-9-1-0-0-1727451230.zip` did nothing. The zip contains exactly one file: `Engine.ini` (8.7KB). It's a **config-tweak mod** — UE performance mods work by adding settings to the game's config tree (`%LOCALAPPDATA%\Witchfire\Saved\Config\WindowsNoEditor\Engine.ini`), not by adding a pak to `Content/Paks`.

The launcher has no mod class for this. `Intake.ClassifyDrop` only knows the game's pak extensions plus zip/skip; a bare `.ini` classifies as "not a mod" and is skipped silently. Zero `Engine.ini`/config-mod handling exists anywhere in the codebase (grep-confirmed).

The existing mod classes for reference: content paks (files/paks-root), loader folder-mods (UE4SS Lua / BepInEx), direct-inject (FromSoft loose DLLs), save mods, tools, frameworks. Config mods are the 7th.

## Goal

A dropped config mod installs into the game's config tree — **merged** into the existing file so the user's and the game's own settings survive — snapshot-first, fully reversible, and managed as a first-class toggleable row. UE-pak scoped for now; Witchfire's Performance Enhancer is the first case.

## Decisions (from brainstorm)

- **Merge, not replace.** The game co-owns `Engine.ini` (it writes resolution, keybinds, etc.). The mod's sections/keys overlay onto the existing file — mod wins on key conflict, everything else survives. Snapshot-first so it's reversible.
- **Detection: known UE config filenames** — `Engine.ini`, `Scalability.ini`, `Input.ini`, `GameUserSettings.ini`, `Game.ini` (case-insensitive basename). Precise; no false trigger on a random `.ini`. Target resolves to the game's `Saved/Config/WindowsNoEditor/` tree, derived per-game the way `saveDir` already is.
- **Managed row; toggle/uninstall = restore snapshot.** Install snapshots the whole target file first, then merges. Disable/uninstall restores that snapshot wholesale. Accepted bluntness: a restore reverts the *whole file* to its install-time state, so later game/user edits to other keys are lost on undo — matches how Restore Previous already behaves. Re-enable re-merges against the file's *current* content with a fresh snapshot.

## The rule set

**A drop is a config mod when** its recognized payload consists entirely of known UE config filenames (basename match anywhere in the archive, e.g. `Config/Engine.ini` counts). A zip containing paks *plus* a config file is still a **pak mod** — config-only is the trigger; we never hijack normal mod zips that happen to bundle a config.

**Merge semantics** (UE INI dialect):
- Plain `Key=Value` in a `[Section]`: mod value **replaces** the existing key, or is added if absent.
- UE array syntax (`+Key=`, `-Key=`, `.Key=`): mod lines **append** to the section (UE treats these as list operations; replacing positionally would corrupt them).
- Sections the mod has and the file lacks: appended whole.
- Existing sections/keys the mod doesn't mention: untouched.
- The existing file's newline style is preserved (`DetectNewline`/`NormalizeNewlines` — the bare-CR lesson from `cf8aa3d`).

## Architecture

Pure-Core detection + merge + installer + registry; thin App wiring for the drop route, config-root resolution, and rows.

### 1. Core — `ConfigMod` (detection)

New `src/ModManager.Core/ConfigMods/ConfigMod.cs`. Pure.

```csharp
public static bool IsConfigFile(string fileName)        // known-name basename match
public static bool IsConfigOnlyPayload(IEnumerable<string> names, IEnumerable<string>? pakExts)
```

`Intake.ClassifyDrop` gains a `"config"` verdict for a loose known-config file; the archive pre-check uses `IsConfigOnlyPayload` (all recognized entries are config files, none are pak/mod files) to route the whole drop to config intake.

### 2. Core — `ConfigMerge` (the merge engine)

New `src/ModManager.Core/ConfigMods/ConfigMerge.cs`. Pure string → string.

```csharp
public static string Merge(string existingIni, string modIni)
```

Section-aware overlay implementing the merge semantics above. Preserves the existing file's newline style and unrecognized lines (comments, blank lines) outside merged keys. Fully unit-testable with no IO.

### 3. Core — `ConfigModInstaller` + `ConfigModStore`

New `src/ModManager.Core/ConfigMods/ConfigModInstaller.cs` + `ConfigModStore.cs`. Follows validate-then-extract and the file-op laws:

1. **Validate** — payload is known config filenames only; forbidden-paths gate (no `..`, no absolute paths); config root exists (refuse with "run the game once" if not — the game must own the tree first).
2. **Snapshot** — each existing target file is backed up BEFORE any write, via the `IniEditService` `.bak` pattern (timestamped, under the game's data dir, keyed by config-mod id). A missing target records `existedAtInstall: false` so undo = delete that file.
3. **Merge + atomic write** — `ConfigMerge.Merge` then temp-write + rename.
4. **Register** — `ConfigModEntry` (id, name, files touched with snapshot pointers + `existedAtInstall`, the mod's raw payload per file for re-enable, installedUtc, enabled) persisted camelCase via `AtomicJson`, modeled on `SaveModStore`. Round-trip test required.

Failure before step 3 leaves the config tree untouched; mid-write failure is recoverable from the snapshot (restore + surface the error).

**Disable** — restore each touched file from its install-time snapshot (delete files that didn't exist at install); keep the registry entry, `enabled: false`.
**Re-enable** — re-merge the stored payload against the file's CURRENT content (the game may have rewritten it), fresh snapshot first, update pointers.
**Uninstall** — restore like disable, then remove the entry + prune its snapshots.
**Re-drop of the same mod** — restore the old snapshot first, then fresh merge + new snapshot (no double-merge stacking).

**Reversibility guard:** the restore path never deletes a config file the mod didn't create (`existedAtInstall: false` files only); everything else is snapshot-restore, never delete.

### 4. App — wiring

- **Drop route:** the `MainViewModel` drop pipeline gains a config pre-check (alongside tool/save-mod/Lua pre-checks): `"config"` verdict → `ConfigModInstaller.Install` with the App-resolved config root. Status line: *"Merged Engine.ini into Witchfire's config — your previous config is snapshotted."*
- **Config root resolution (App, IO):** `%LOCALAPPDATA%\<ProjectName>\Saved\Config` — `<ProjectName>` from the same UE project-folder discovery the saveDir logic uses. UE-pak engine only.
- **Rows:** registered config mods render as toggleable rows via a `Location = "config"` pseudo-location (modeled on direct-inject rows — no folder glob; identity from `ConfigModStore`). Toggle → disable/re-enable paths; uninstall → uninstall path. Metadata enrichment (Nexus md5 on the original archive) works as for other mods.

## Edge cases

- Config root missing (game never run) → clear refusal, nothing written.
- Same mod re-dropped → restore-then-remerge (no stacking).
- Two config mods touching the same file → snapshots chain in install order; disabling the older warns the newer's keys may revert too (wholesale-restore bluntness, surfaced honestly).
- Config file in an archive subfolder (`Config/Engine.ini`) → basename match recognizes it.
- Locked/read-only target (game running) → refusal, nothing changed.
- Mod ini with sections only (no keys), or empty → validate refuses ("nothing to apply").

## Testing

**Core (xUnit):**
- `ConfigMod`: each known filename recognized (case-insensitive, subfolder basename); random `.ini` → not config; pak+config payload → NOT config-only (stays pak mod); config-only payload → config.
- `ConfigMerge`: key replace, key add, `+`/`-`/`.` append, new-section append, untouched-section preservation, comment/blank-line preservation, CRLF and LF preservation, empty-existing (mod becomes file), conflict (mod wins).
- `ConfigModInstaller`: happy path; snapshot precedes write (inject a write failure, assert target restorable + tree clean); missing config root → refusal, nothing written; forbidden path → refusal; missing target file → `existedAtInstall:false`, undo deletes it; registry camelCase round-trip; disable restores snapshot; re-enable re-merges current content with fresh snapshot; uninstall removes entry + restores; re-drop restores-then-remerges.

**App:** build-verified + live smoke with the actual Performance Enhancer zip on Witchfire (drop → merged, row appears, toggle off → pre-install Engine.ini back, toggle on → re-merged, uninstall → clean).

## Non-goals

- Non-UE engines (Bethesda INI tweaks, etc.) — later; the merge engine is reusable.
- Surgical un-merge (remove only the mod's keys) — wholesale snapshot-restore accepted; revisit only if it proves too blunt.
- Config-mod *editing* — the existing INI editor (pencil) already works on any INI in place.

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/ConfigMods/ConfigMod.cs` | NEW — detection (known filenames, config-only payload) |
| `src/ModManager.Core/ConfigMods/ConfigMerge.cs` | NEW — pure section-aware INI overlay |
| `src/ModManager.Core/ConfigMods/ConfigModInstaller.cs` | NEW — validate → snapshot → merge → register; disable/re-enable/uninstall |
| `src/ModManager.Core/ConfigMods/ConfigModStore.cs` | NEW — camelCase registry (AtomicJson), modeled on SaveModStore |
| `src/ModManager.Core/Intake.cs` | `"config"` verdict in ClassifyDrop |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | drop pre-check route + config-mod rows + toggle/uninstall wiring |
| `src/ModManager.App/Services/` | config-root resolution (LOCALAPPDATA + UE project discovery) |
| `tests/ModManager.Tests/ConfigMods/` | NEW — detection, merge, installer, store round-trip |
| `.claude/rules/camelcase-json-on-disk.md` | add ConfigModStore to the governed-surfaces list |
