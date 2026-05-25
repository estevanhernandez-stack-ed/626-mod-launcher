# Mod-tool coordination — design

> Status: approved design (2026-05-25). Next: implementation plan via writing-plans.
> Builds on the per-location `Form`/`Managed` work (multi-location scanning).

## The problem

The launcher must work *alongside* the other tools that mod a game — not fight them, not duplicate them. Three kinds of "other tool" exist, with different mechanics:

- **Loaders with a native manifest** — UE4SS (`mods.txt` / `mods.json`), Mod Engine 2 (config TOML). They decide what loads and in what order from a file we can read and write.
- **Managers that deploy + track files** — Vortex (`__folder_managed_by_vortex`, `vortex.deployment.*.json`), MO2 (USVFS overlay). They own folders and keep their own state DB.
- **Nobody** — plain `~mods`, loose LogicMods paks. We own these.

The hard part is **ownership arbitration**: a single folder can be both a loader folder *and* deployed-into by a manager (the user's UE4SS folder is exactly this — UE4SS reads it, Vortex deploys into it). Two managers must never both believe they own the same mod, or they corrupt each other's state.

## Postures

| Posture | When | What we do |
|---|---|---|
| **Conductor** | A loader with a manifest, and no other manager owns the folder | Drive the loader's manifest — toggle enable, set order — **never move files** |
| **Coexist** | Another manager deploys into the folder | Detect ownership, show **read-only**, never touch |
| **Own** | Nobody else manages it | Our reversible-move model (current behavior) |

## Decision: detect & defer

Per-location, at runtime:

```
owned by another manager?   -> Coexist (read-only)
else loader has a manifest? -> Conductor (drive manifest, no file moves)
else                        -> Own (reversible-move)
```

This *refines* the per-location `Managed` hint shipped with multi-location scanning: `Managed` becomes a fallback, and runtime marker detection decides the real posture. The user's Vortex-deployed UE4SS folder stays correctly read-only; a user **without** Vortex gets full UE4SS control for free.

## Architecture (pure-core, test-first)

All logic lives in no-UI `*-core` modules under `ModManager.Core`, unit-tested with `node`-equivalent xUnit over temp dirs. `main`/App stays a thin shell.

### 1. Ownership detection — `ToolOwnership`

`ToolOwnership.Detect(folderAbs) -> OwnerTool?` (enum: `Vortex`, `Mo2`, `null`). Pure, reads on-disk markers only:

- `__folder_managed_by_vortex` present, OR any `vortex.deployment.*.json` -> `Vortex`
- MO2 markers (`*.meta`/`meta.ini` deployment, `modlist.txt` upstream) -> `Mo2`
- else `null` (unowned)

### 2. Loader adapters — `ILoaderAdapter`

One per loader, pure-core. The interface (shape, not final signatures):

```
bool   Detect(GameContext, ModLocationCtx)          // is this loader present for this location?
IReadOnlyList<LoaderMod> ListMods(ModLocationCtx)    // name, enabled, order, configRefs, binds, commands
void   SetEnabled(ModLocationCtx, modName, bool)     // write the manifest — NO file move
void   SetOrder(ModLocationCtx, IReadOnlyList<string> order)
```

- **UE4SS adapter** — parses/writes `mods.txt` (`ModName : 1/0`) for enable state and `mods.json` for order. Toggling flips the bit; ordering rewrites the list. No file moves. Discovers each mod's config files (`*.ini`/`*.cfg`/`config.lua`) and keybind/command registrations (see §4).
- **BepInEx adapter** — no central manifest: enable/disable by `.dll` <-> `.dll.disabled` rename (BepInEx convention; a rename, not a destructive op). Config surfaced from `BepInEx/config/*.cfg` (INI format) — see §4.

### 3. Arbitration — `Coordination.PostureFor`

`Coordination.PostureFor(ModLocationCtx, ToolOwnership, adapters) -> Posture` implements detect-and-defer. The Scanner/VM ask this per location to decide whether a mod row is controllable, conductor-driven, or read-only. This replaces the current "managed => always read-only" hardcode.

### 4. Config + hotkey surface — `ModConfig` (the cockpit)

Two tiers, ordered by how safely we can write:

- **INI/cfg config (top tier, structured)** — `ModConfig.Read(file)` / `ModConfig.Write(file, edits)` for `.ini`/`.cfg` files (UE4SS per-mod configs, `UE4SS-settings.ini`, BepInEx `*.cfg`). Parser preserves comments, ordering, and section structure (round-trip safe). Edits are atomic (`fs-atomic`), originals backed up before first write. This is the "edit the important bits" feature — keybinds *and* any other option a mod exposes via config.
- **Lua-embedded keybinds (heuristic)** — parse `RegisterKeyBind(...)` / `RegisterConsoleCommandHandler(...)` from a mod's Lua. Confidently-parsed binds are editable (careful, reversible Lua edit: original backed up, only the bound key token rewritten, never a structural change); anything ambiguous is shown **read-only** rather than guessed.

**Safe hotkeys** — before writing any bind, `Hotkeys.Conflicts(all binds)` detects collisions (two mods on one key) and game-reserved keys, and the write is refused/warned. "Safe" means we never hand back a binding that silently shadows another.

**Console commands** — surface each mod's registered commands as a read-only list with copy-to-console (and, where a console-enabler mod is present, note it).

## Data model touch points

- `ModLocation` already has `Form` + `Managed` (from multi-location scanning). Add an optional `Loader` field ("ue4ss"/"bepinex") so a profile can name the adapter; absent -> auto-detect.
- `Mod` already has `Managed`. Add `Coordination` posture (or derive in the VM) so the row knows whether the toggle drives a manifest, a file move, or is read-only.
- New core types: `OwnerTool`, `Posture`, `LoaderMod`, `ConfigEntry`, `KeyBind`, `ConsoleCommand`.

## Staging

| Stage | Deliverable | Tests |
|---|---|---|
| **1** | Coordination core: `ToolOwnership.Detect`, `Coordination.PostureFor`, detect-and-defer wired into the scan/VM (upgrades the hardcoded read-only) | ownership markers; arbitration truth table |
| **2** | UE4SS adapter: enable/disable via `mods.txt`, order via `mods.json` (Conductor, no file moves), only where unowned | round-trip mods.txt parse/write; toggle; reorder; defer-when-owned |
| **3** | `ModConfig` INI/cfg read+write (round-trip safe, atomic, backed up) + Lua keybind parse + `Hotkeys.Conflicts` + console-command surface | round-trip fidelity; conflict detection; ambiguous-bind read-only |
| **4** | BepInEx adapter: `.dll`<->`.dll.disabled`, `*.cfg` config via `ModConfig` | enable/disable rename; config round-trip |

## Operating laws honored

- **Pure-core / thin-shell** — adapters, ownership, config parsing are no-UI cores under `node --test`-style xUnit.
- **Test-first** — failing test before each behavior.
- **Reversible + safe** — Conductor toggles flip a bit (no moves); BepInEx toggle is a rename; config/Lua writes are atomic with a backup of the original; we never delete user files.
- **Honor other tools** — detect-and-defer means we never write a folder another manager owns. The "don't touch Vortex folders" law is now enforced by runtime detection, not just a profile hint.
- **No guessing** — ambiguous Lua binds are read-only; we never write what we didn't confidently parse.

## Out of scope (v1)

- Deep MO2 integration beyond detect-and-stay-out (USVFS means files aren't physically present; surfacing requires reading MO2's profile DB — later).
- Two-way sync with Vortex's deployment DB (we read markers for ownership; we don't write Vortex state).
- Authoring new keybinds/commands (we surface + remap existing; we don't inject new registrations).

## Risks

- **Lua keybind parsing is heuristic.** Mitigation: INI/cfg is the primary editable surface; Lua binds are best-effort, read-only when unsure, backed up when written.
- **UE4SS folder shows loader scaffolding** (BPModLoaderMod, ConsoleEnablerMod, `shared`, etc.) alongside real mods. Mitigation (open): a small named ignore-list of UE4SS builtins, or read `mods.txt` membership to separate user mods from framework — decide in Stage 2.
- **`mods.json` vs `mods.txt` precedence** in UE4SS versions. Mitigation: detect which the install uses; prefer `mods.txt` when both exist (the documented default), covered by a test.
