# Per-mod UE4SS need — stop the false "needs UE4SS" chip on plain content paks

**Date:** 2026-06-06
**Status:** Design — approved in brainstorm, pending spec review
**Branch:** `feat/paks-root-base-game-filter` (rides with the Witchfire work / PR #116)

## Problem

On a loader-less UE-pak game (Witchfire), every mod row shows a red "needs UE4SS" chip — but Witchfire's mods are plain content paks that load with no framework (there isn't even a UE4SS download for Witchfire on Nexus). The chip is flat wrong for this game.

Root cause: the row-builder assigns the missing-framework chip engine-wide, not per-mod. In `MainViewModel` (~line 430-432), the non-fromsoft branch is:

```csharp
else
{
    primaryMissing = MissingFrameworks.FirstOrDefault();  // UE4SS, blindly, on EVERY ue-pak row
}
```

So when UE4SS isn't installed, `CheckPresent` reports it missing for the engine and EVERY ue-pak row shows the chip — including plain content paks that don't need it. The catalog entry's own note already says so: *"Required for Lua mods and Blueprint LogicMods paks. Plain content paks don't need it."* The chip just never honored that.

## Goal

Show "needs UE4SS" only on rows that genuinely need it — Lua/script mods and Blueprint LogicMods paks — never on a plain content pak. Fixes Witchfire (all content paks → no chip) and also a Windrose plain content pak that is falsely chipped today.

## The rule (settled in brainstorm)

A ue-pak mod needs UE4SS when EITHER:
- it is a **Lua/script mod** — `mod.Loader == "ue4ss"` (set by the scanner when the mod's folder has a UE4SS manifest), OR
- it is a **Blueprint LogicMods pak** — the mod's location path ends in `LogicMods` (UE4SS's BPModLoader mounts that folder).

Otherwise (a plain content pak in `~mods` or a `paks-root` location, `Loader == null`, not LogicMods) it needs nothing → no chip.

## Decisions (from brainstorm)

- **Pure Core helper** owns the per-mod decision (single source of truth, unit-testable), not inline App logic and not a catalog schema change.
- **UE4SS-only scope.** This gates only the UE4SS chip on ue-pak rows. Other engines' dependency chips (BepInEx, SMAPI, ME2, etc.) keep today's behavior untouched.
- No `FrameworkDep` schema change (per-kind targeting for other frameworks is YAGNI for now; the helper is reusable if it's ever wanted).

## Architecture

### 1. Core — `FrameworkApplicability.ModNeedsUe4ss`

New file `src/ModManager.Core/FrameworkApplicability.cs`. Pure, no IO — decides from signals already on `Mod` + the resolved location path passed in (so the helper has no `GameContext` dependency).

```csharp
public static bool ModNeedsUe4ss(Mod mod, string locationPath)
```

- `mod.Loader == "ue4ss"` → true (Lua/script mod).
- `locationPath` ends in `LogicMods` (case-insensitive, separator-normalized — handle `\` and `/`) → true (Blueprint pak).
- else → false.

`Mod` already carries `Loader` (`Mod.cs:30`). The location path is what the row-builder resolves per row; pass the `ModLocationCtx.Path` (or `.Abs`) for the row's location. (LogicMods is identified by the location's path tail, matching how Windrose configures a `mods2` location at `R5/Content/Paks/LogicMods`.)

### 2. App — gate the UE4SS chip

In `MainViewModel` row construction (~line 423-432), the non-fromsoft branch: when the first missing framework is **UE4SS**, attach it only if `FrameworkApplicability.ModNeedsUe4ss(rep, <row location path>)` is true; otherwise leave `primaryMissing` null (no chip). The row-builder already resolves the row's location (it computes `folderAbs`/`loc` for the row) — use that location's `Path`/`Abs`.

Concretely: keep `var primaryMissing = MissingFrameworks.FirstOrDefault();` then, if `primaryMissing?.Name == "UE4SS" && !FrameworkApplicability.ModNeedsUe4ss(rep, locPath)`, set `primaryMissing = null`. Frameworks other than UE4SS are unaffected.

## Non-goals

- Not changing `FrameworkDeps.Catalog` / `CheckPresent` (the engine-wide "is UE4SS present" probe stays; only the per-row chip assignment becomes mod-aware).
- Not touching other engines' dependency chips.
- Not adding per-kind targeting to `FrameworkDep` (reusable helper instead; revisit if a second framework needs it).

## Testing

**Core (xUnit), `FrameworkApplicabilityTests`:**
- Lua mod (`Loader == "ue4ss"`) → true (any location).
- LogicMods pak (`Loader == null`, location path `R5/Content/Paks/LogicMods`) → true.
- Plain content pak in `~mods` (`Loader == null`, path `R5/Content/Paks/~mods`) → false.
- Plain content pak in a paks-root location (`Loader == null`, path `Witchfire/Content/Paks`) → false.
- Case/separator robustness: `...\logicmods` and `.../LogicMods/` both → true.

**App:** none (WinUI VM not unit-testable here) — build-verified + live smoke: Witchfire content paks show NO UE4SS chip; a Windrose Lua mod still shows the chip when UE4SS is absent; a Windrose LogicMods pak still shows it.

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/FrameworkApplicability.cs` | NEW — `ModNeedsUe4ss(mod, locationPath)` |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | Gate the UE4SS chip on the per-mod helper in the row-builder |
| `tests/ModManager.Tests/FrameworkApplicabilityTests.cs` | NEW |
