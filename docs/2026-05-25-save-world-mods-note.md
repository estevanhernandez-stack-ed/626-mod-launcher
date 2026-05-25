# Feature note — save / world mods (a new mod class)

- **Date:** 2026-05-25
- **Status:** Requirements capture (not yet scoped/brainstormed)
- **Source example:** Windrose "Pirate Depot" — a downloadable **world save** (a fully-stocked
  resource depot world; your character + inventory travel between worlds, so you visit the depot,
  fill up, and sail home). Its README is the requirements source below.

## The gap

The launcher handles **pak / loose-file mods** (Content/Paks, ~mods). It has **no concept of a mod
that is a save file** — but these exist and are common: downloadable worlds, character saves,
prebuilt bases. They install into the **save tree**, not the mod folder, with their own rules. This
is a distinct **mod class** to model alongside pak and direct-inject.

## Install target (structured, per the structure-not-absolute principle)

Windrose world save:

```
<saveDir>\<profile>\RocksDB\<version>\Worlds\<WorldGUID>\
```

- `<saveDir>` — the app already resolves this (Windrose: `%LOCALAPPDATA%\R5\Saved\SaveProfiles`).
- `<profile>` — the single steam-id-named profile folder inside SaveProfiles (resolve dynamically).
- `<version>` — the game/save version, e.g. `0.10.0` (dynamic; the README says create `Worlds\` if absent).
- `<WorldGUID>` — world folders are GUIDs (e.g. `5391A30D5D70487C9486B8E60428ED3B`). Notably this
  same GUID appeared in the co-op save-backup logs — the save manager already sees these worlds.

## Load-bearing safety rules (must encode — straight from the README)

1. **Only ever install into `RocksDB\<version>\Worlds\`.** NEVER write to `RocksDB_v2` or
   `RocksDB_v2_Backups` — the game manages those (cloud saves + internal backups); installing there
   fails or corrupts. The app must target the right tree and refuse the v2 ones.
2. **Local vs Cloud prompt:** first launch after install, the game may ask Local or Cloud save —
   the user must choose **LOCAL**, or Cloud overwrites the freshly-installed world. The app should
   **warn** about this at install time (it can't make the in-game choice).
3. **Reversible (operating law #3):** installing a world modifies the user's saves. Snapshot the
   existing Worlds state first (tie into the existing **save manager** — it already snapshots saves),
   so a world install is undoable.
4. **Create-if-missing:** if `RocksDB\<version>\Worlds\` doesn't exist, create it (per the README FAQ).

## Actions beyond install

- **Reset world to pristine:** the README's reset flow is "delete the world (in-game + on disk),
  re-extract the original zip." The app can offer one-click reset by keeping the original archive and
  re-extracting — much safer than the manual GUID-folder dance.
- **List installed save-mods** per game (scan `Worlds\` for known GUIDs), enable/disable/remove
  reversibly like other mod classes.

## How it composes with what exists

- **Save manager** (GameProfile save-types, `SaveManager`, saveDir resolution) — the natural home;
  world-mods live in the same tree it already snapshots.
- **Agentic game profile** (`docs/2026-05-24-agentic-game-profiles-design.md`) — extend the
  structured profile to declare a game's **save-mod install path + forbidden subfolders** (the
  RocksDB / not-v2 rule), so the agent can fill it per game. This is the structure-not-absolute idea
  applied to save mods.
- **Intake routing** — a dropped zip needs classification: pak mod (has .pak/.ucas/.utoc) vs
  save/world mod (GUID-named RocksDB world, no paks). Route to the save tree, not ~mods.
- **Readme viewer** (queued) — readmes carry exactly these install + Local-save + reset instructions;
  surfacing them is half the battle for save-mods.

## Open questions for scoping

- Detection: auto-classify a dropped save-mod vs pak, or let the user pick the type?
- Per-game generality: the RocksDB/Worlds structure is Windrose-specific; other games' save-mods
  differ — so the install path + forbidden-folder rules belong in the (agent-fillable) game profile,
  not hardcoded.
- Multi-world management: worlds are GUIDs with no friendly names on disk — need a name/source map.
