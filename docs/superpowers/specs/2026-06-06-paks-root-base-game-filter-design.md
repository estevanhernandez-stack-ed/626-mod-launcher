# Loader-less UE-pak games — Paks-root location + base-game filter

**Date:** 2026-06-06
**Status:** Design — approved in brainstorm, pending spec review
**Branch:** `feat/paks-root-base-game-filter`

## Problem

Witchfire (Unreal pak engine, `ue-pak`) was added to the launcher but no mods were detected, even after a rescan. Investigation:

- Witchfire is a **loader-less** UE-pak game — no UE4SS, no proxy DLL injected next to the exe, no `.sig` bypass, no `~mods` subfolder. Mods load by being dropped **directly into** `Witchfire\Content\Paks\` (the engine always mounts every pak there).
- But the `ue-pak` engine preset hardcodes the mod location as `Content/Paks/~mods` — the UE4SS convention (right for Windrose, which runs UE4SS). So the scanner looks in a `~mods` subfolder that doesn't exist and finds nothing.
- The user's actual mods sit in `Content\Paks\` alongside the **base game** paks (`pakchunk0-WindowsNoEditor.pak` ~3.9GB, `pakchunk0optional-WindowsNoEditor.pak` ~591MB). Pointing the location at `Content\Paks` naively would surface the base game as toggleable "mods" — the user could disable the game itself. There is **no base-game-pak filter anywhere** in the scanner today.

This is a recurring class, not a one-off: many UE games have no UE4SS and take mods straight in `Content\Paks`.

## Goal

Make loader-less UE-pak games first-class: the launcher manages `Content\Paks` directly, shows the user's mod paks, and **never** lists or moves the base-game paks. Witchfire is the first case; the fix generalizes.

## Decisions (from brainstorm)

- **Base-vs-mod rule:** name pattern (`pakchunk<N>-WindowsNoEditor` + `optional` variant) is the primary signal, OR'd with a size sanity guard (implausibly large = base). Accepted edge: a mod that mimics the base name is treated as base (rare, arguably correct).
- **Safety net:** belt-and-suspenders — name + size classify; plus a HARD rule that a base-classified pak can NEVER be moved by a toggle/disable/vanilla-step-aside, even if classification is wrong. The game file can't be stranded by a misjudgment.
- **Configuration:** a NEW location form `"paks-root"` (alongside `"files"`/`"folders"`). The `ue-pak` preset auto-detects: `~mods` subfolder present (loader) → existing `folders`/`~mods` location; else → `Content/Paks` with `form: "paks-root"`. Add-from-Steam picks the right one.
- **No UE4SS.** The user's Witchfire mods are pak mods that already load; UE4SS (Lua mods) is a separate, unwanted concern here.

## Non-goals

- No manifest parsing (`Manifest_NonUFSFiles_Win64.txt`) — name+size is sufficient; manifest is a possible future enhancement.
- No UE4SS install, no change to loader-based `~mods` games.
- No catalog-override for a mod legitimately named like a base pak (flagged, not solved).

## Architecture

Pure-Core classifier + scanner wiring + a thin preset change. No App code (the new form produces the same `Mod` shape the existing pak rows already render).

### 1. Core — `PakClassifier` (the base-vs-mod single source of truth)

New `src/ModManager.Core/PakClassifier.cs`. Pure function, no IO.

```
public static bool IsBaseGamePak(string fileName, long sizeBytes)
```

A pak is **base game** (hidden + protected) when EITHER:
- **Name** matches the UE shipping convention — `pakchunk<N>[optional]-WindowsNoEditor.pak` (case-insensitive regex on the filename), OR
- **Size** exceeds a "too big to be a mod" threshold (constant, e.g. 1.5 GB — well above any real mod pak, below the multi-GB base chunks). This is the net for a base pak whose name doesn't match the convention.

Everything else is a **mod**. The OR is deliberate: name-match alone hides (cheap, common case); size alone also hides (safety net). A base pak slips through only if it is BOTH un-conventionally-named AND mod-sized — which a real game file never is.

Witchfire truth table:
| pak | size | verdict |
|---|---|---|
| `pakchunk0-WindowsNoEditor.pak` | 3.9 GB | base (name + size) |
| `pakchunk0optional-WindowsNoEditor.pak` | 591 MB | base (name) |
| `pakchunk30-2x-witchfire_P.pak` | 5.7 KB | mod |
| `zz_Funner_Witchfire.pak` | 22 MB | mod |

### 2. Core — `paks-root` location form + scanner wiring

`ModLocationCtx.Form` already carries `"files"`/`"folders"` (string, persisted camelCase). Add a third value `"paks-root"`.

**`Scanner.BuildModList`, the pak branch:** when `loc.Form == "paks-root"`, run each pak from `ListPakFiles` through `PakClassifier.IsBaseGamePak(name, size)` — base paks are skipped entirely (never become a `Mod`), the rest group into rows exactly as the `"files"` form does today (`ModKey` + `strip_underscore_p_suffix`). For `"files"`/`"folders"`, behavior is byte-for-byte unchanged — the filter engages ONLY for the new form. (Size is read via the existing file enumeration; `ListPakFiles` returns names — the scanner resolves the size from `loc.Abs` + name, or `ListPakFiles` is extended to carry size. Implementation detail for the plan.)

**The hard reversibility guard:** the pak disable/move path (`Scanner.DisableMod` / `DisableEntry`, and by extension anything that moves a pak — vanilla step-aside, bulk toggle) refuses to move a pak that classifies as base, throwing a clear "that's the base game, not a mod" refusal before any file op. Classification can misjudge; the game file still can't move. This is the operating-law application: even a wrong read can't strand the game.

**`ue-pak` engine preset detection** (`EnginePresets` / wherever the preset resolves a location at add-time): if `<Project>/Content/Paks/~mods` (or `LogicMods`) exists → use the existing loader-style `folders` location; else → `<Project>/Content/Paks` with `form: "paks-root"`. So add-from-Steam configures the right one automatically.

### 3. App — none required

The `paks-root` form yields the same `Mod` rows the existing `"files"` pak path produces, so rows / toggle / launch button / vanilla-modded all work unchanged. Base paks simply never appear. An all-base folder yields an empty mod list (matches today's empty-folder behavior).

## Data shape

`ModLocation.Form` is an existing persisted string (camelCase JSON on disk). `"paks-root"` is a new value — no schema change, no migration. Old games keep `"files"`/`"folders"`/null. A round-trip test covers the new value.

## The Witchfire migration

Witchfire's current `games.json` entry has the wrong location (`Content\Paks\~mods`). Once this ships, the clean recovery is **remove + re-add from Steam** — the updated preset detects no `~mods` and configures `Content\Paks` with `form: "paks-root"`; the 2 mods appear. (A one-line manual correction of the existing entry's location also works, but re-add is the path the feature makes right.)

## Edge cases

- Folder has only base paks → empty mod list, not an error.
- `ucas`/`utoc` siblings of a mod pak → grouped with their `.pak` by `ModKey` as today; classification keys on the `.pak`.
- Disable guard hit (a base pak reaches the move path) → clear refusal, game folder untouched.
- A mod genuinely named like a base pak → treated as base (the accepted edge), pinned by a test so it's intentional.

## Testing

**Core (xUnit):**
- `PakClassifier`: each Witchfire pak classifies correctly; size-only base (un-conventional name, huge); name-only base (conventional name, modest size like the optional chunk); the accepted edge (mod-named-like-base → base) pinned explicitly; a normal mod pak → mod.
- Scanner `paks-root`: lists the mod paks, never the base paks; `files`/`folders` forms unchanged (regression).
- Disable guard: refuses to move a base-classified pak, clear error, no file moved.
- Preset detection: picks `paks-root` when no `~mods` exists, `folders`/`~mods` when it does.
- `Form` camelCase round-trip with `"paks-root"`.

**App:** none (WinUI VM not unit-testable here) — build-verified + a live smoke on Witchfire (re-add → 2 mods show, base paks don't, toggle a mod works, base pak never appears / can't move).

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/PakClassifier.cs` | NEW — `IsBaseGamePak(name, size)` |
| `src/ModManager.Core/Scanner.cs` | `paks-root` branch in BuildModList; base-pak disable guard; size available to the pak scan |
| `src/ModManager.Core/EnginePresets.cs` (+ the add-time location resolver) | `ue-pak` preset picks `paks-root` vs `~mods` by on-disk detection |
| `tests/ModManager.Tests/` | PakClassifier, scanner paks-root, disable guard, preset detection, Form round-trip |
