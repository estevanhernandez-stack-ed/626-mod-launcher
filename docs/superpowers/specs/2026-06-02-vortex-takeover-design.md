# Vortex takeover — break free from Vortex management, gracefully

**Date:** 2026-06-02
**Status:** Design — approved in brainstorm, pending spec review
**Branch:** `feat/vortex-takeover`

## Problem

When a user moves from Vortex to the 626 Mod Launcher, Vortex leaves ownership markers in the
folders it deployed into — `__folder_managed_by_vortex`, `vortex.deployment.*.json`, and (for MO2)
`meta.ini`. `ToolOwnership.Detect` reads those markers and `Coordination.PostureFor` resolves the
location to `Coexist` (read-only) so the launcher never touches another tool's files. That safety law
is correct in general — but it's wrong once the user has *moved off* Vortex. The launcher's own
freshly-installed mods land in a folder that still carries a stale Vortex marker, so:

- The per-mod **uninstall** affordance is hidden (`canUninstall: !rep.ReadOnly` → false).
- **Toggle** falls back to the owned-folder warning path.
- `UninstallMod` would throw `"managed by another tool — uninstall it there."` even if reached.

Concretely: a UE4SS Lua mod (`Windrose Shanties Anywhere`) installed by the launcher into
`R5/Binaries/Win64/ue4ss/Mods/` is unmanageable because that folder still holds
`vortex.deployment.windrose-scripts.json` from before the migration.

## Goals

- Let the user **take over** a Vortex-managed folder so the launcher manages it normally
  (toggle / uninstall / conduct) — with explicit consent, never automatically.
- **Reversible**: takeover archives the marker (move-to-holding), it is never deleted; an undo
  restores the pre-takeover state byte-for-byte.
- **Per-folder primitive** with a **game-wide** convenience layer built on top of it.
- Maximize the chances to say yes: an ambient **banner** plus a just-in-time **on-block prompt**.
- Detect and surface a later **Vortex re-deploy** into a taken-over folder, rather than letting the
  two tools fight silently.

## Non-goals (explicit)

- **Never system-wide or cross-game.** A takeover acts only on the *active game's* Vortex-owned
  locations. A folder belonging to a game the launcher doesn't support is never touched just because
  Vortex marked it. There is no whole-disk or all-games sweep anywhere.
- **Never reach into or disable Vortex itself.** We don't run Vortex, purge its deployment, or edit
  its config. We archive the marker on our side and warn the user to manage here from now on.
- **Never delete a marker.** Archive (move) only. Undo restores, it does not rebuild.
- **Never auto-take-over.** Every takeover is consent-gated.

## Architecture

Pure-core operation + state, thin App surfaces. Three layers:

### 1. Core — the reversible takeover primitive (`VortexTakeover`)

New class `src/ModManager.Core/VortexTakeover.cs`. System.IO + System.Text.Json only.

The set of ownership markers is extracted to a shared helper so `ToolOwnership.Detect` and
`VortexTakeover` can never drift on what counts as a marker:

```
OwnershipMarkers.MarkerFilesIn(folderAbs) →
    - __folder_managed_by_vortex            (Vortex)
    - vortex.deployment.*.json (glob)       (Vortex)
    - meta.ini                              (MO2)
```

`ToolOwnership.Detect` is refactored to use `OwnershipMarkers` (no behavior change — same matches).

**`TakeOver(GameContext ctx, string folderAbs) → TakeoverResult`**

1. Enumerate markers via `OwnershipMarkers.MarkerFilesIn(folderAbs)`. If none, no-op success
   (already ours).
2. **Move** each marker to `<dataDir>/vortex-takeover/<locationKey>/` where `locationKey` is a
   stable slug of `folderAbs` relative to the game root (collision-free, human-readable).
3. Write `takeover.json` in that archive dir (camelCase) recording each marker's original absolute
   path, archived filename, owner tool, and the takeover UTC.
4. Append `folderAbs` to the persisted set at `<dataDir>/taken-over.json`.

Reversibility: stage-then-commit shape mirroring `Ue4ssLuaInstaller` — if a marker move fails partway,
roll back the already-moved markers and leave the folder owned (no half-taken-over state).

**`Undo(GameContext ctx, string folderAbs)`**

Read `takeover.json`, move each marker back to its original path byte-for-byte, drop `folderAbs` from
`taken-over.json`, remove the archive dir. Restores the exact pre-takeover state.

**`TakeOverGame(GameContext ctx) → IReadOnlyList<TakeoverResult>`** — the game-wide convenience:
loop `TakeOver` over every Vortex-owned location of the active game (from `ctx.Locations` filtered by
current ownership). This is the seam the banner's "Take them over" button drives. It is intrinsically
game-scoped because it only ever iterates `ctx.Locations`.

### 2. Core — posture consults the takeover state

The ownership read gains the persisted taken-over set as input.

`ToolOwnership.Detect` gets an overload (or a thin `OwnershipResolver`) that takes the taken-over set
and returns one of three states for a folder:

- **Not owned** — `folderAbs` is in the taken-over set (even if a marker is still physically present).
  This is what unblocks toggle / uninstall.
- **Owned** — a marker is present and the folder is *not* taken over (today's behavior, unchanged).
- **Re-deployed** — the folder *is* taken over **and** a fresh marker has reappeared. Distinct
  signal; falls out of the same two facts with no separate scan logic.

`Coordination.PostureFor` gains the re-deployed input: taken-over → `Own`/`Conductor` as normal;
re-deployed → still `Own` (we keep managing) but the App raises the re-deploy notice.

The taken-over set is loaded once per `GameContext` build (same spot `FrameworkRegistry.List` already
loads), so the per-location scan cost is one `HashSet` lookup. `Scanner.BuildModList` reads the
posture exactly as today; the only change is that posture is now a function of (marker, taken-over).

### 3. App — the two consent surfaces

All file ops are in Core. The App shows dialogs, calls the VM, and repaints via `ReloadModsAsync`.
Each takeover ends with a reload so rows flip from read-only to managed in the same pass.

**On-block prompt (just-in-time).** When the user toggles/uninstalls a mod in a Vortex-owned folder,
or right after the launcher installs into one, a `ContentDialog` (modeled on
`FrameworkUnrecognizedNudgeDialog`):

> **Vortex manages this folder.** "<mod>" lives in a folder Vortex used to deploy. Take it over so
> you can manage it here?
> *After this, manage these mods in the launcher — re-deploying in Vortex may undo your changes.*
> **[Take over folder]** · **[Not now]**

On "Take over": `VortexTakeover.TakeOver(ctx, folder)`, then continue the original action. "Not now"
leaves it owned, action cancelled (today's behavior).

**Ambient banner (discoverability).** When a scan finds any Vortex-owned (not-yet-taken-over) folder
for the active game, a dismissible bar above the mod list:

> Some folders here are managed by Vortex. **[Take them over]** · **[Dismiss]**

"Take them over" → `VortexTakeover.TakeOverGame(ctx)` (game-wide, active game only). Dismiss is
session-level (same pattern as `_suppressOwnedToggleWarning`).

**Re-deploy notice.** When posture reports re-deployed for any folder, the banner switches copy:

> Vortex re-deployed into a folder you took over — take it over again? **[Take over again]** ·
> **[Dismiss]**

→ re-runs takeover for those folders (archives the fresh marker into the same location).

## Data shapes (camelCase on disk)

`<dataDir>/taken-over.json`:

```json
{ "version": 1, "folders": ["C:\\...\\R5\\Binaries\\Win64\\ue4ss\\Mods"] }
```

`<dataDir>/vortex-takeover/<locationKey>/takeover.json`:

```json
{
  "version": 1,
  "takenOverUtc": "2026-06-02T...Z",
  "markers": [
    { "originalPath": "C:\\...\\ue4ss\\Mods\\vortex.deployment.windrose-scripts.json",
      "archivedName": "vortex.deployment.windrose-scripts.json",
      "owner": "vortex" }
  ]
}
```

Both written via `AtomicJson` with `PropertyNamingPolicy = CamelCase`. Each ships a round-trip test
asserting camelCase keys on disk (per the camelCase-JSON-on-disk rule).

## Error handling (laws-first)

- **Marker move fails mid-takeover** → roll back already-moved markers, leave the folder owned,
  surface the error. No half-taken-over state.
- **Undo with missing/malformed manifest** → degrade safely: restore what's recoverable, don't throw
  into the UI.
- **`taken-over.json` absent/corrupt** → treated as empty set; folders read owned as today. Never
  blocks a scan.
- **Re-takeover of an already-taken-over folder** → idempotent: archives the fresh marker into the
  same location, no duplicate set entry.

## Testing

**Core (xUnit):**
- Take-over flips posture to `Own` with the marker still physically present.
- Undo restores every marker byte-for-byte to its original path; folder reads owned again.
- Mid-move failure rolls back (no half-taken-over state).
- Re-deploy detection: taken-over folder + a fresh marker → re-deployed signal.
- Idempotent re-takeover (no duplicate `taken-over.json` entry).
- **Game-scoped**: a sibling game's owned folder is untouched by the active game's `TakeOverGame`.
- camelCase round-trip on both persisted shapes.
- `OwnershipMarkers` shared helper matches the same set `ToolOwnership.Detect` matched before the
  refactor (regression guard so detection doesn't drift).

**App:** dialog consent paths (take over / not now), banner visibility (shown when owned folders
exist, hidden after takeover), re-deploy banner copy switch, reload-after-takeover repaints rows from
read-only to managed.

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/OwnershipMarkers.cs` | NEW — shared marker glob set |
| `src/ModManager.Core/VortexTakeover.cs` | NEW — TakeOver / Undo / TakeOverGame + result types |
| `src/ModManager.Core/ToolOwnership.cs` | Refactor to use `OwnershipMarkers`; add taken-over-aware resolve |
| `src/ModManager.Core/Coordination.cs` | `PostureFor` gains the re-deployed input |
| `src/ModManager.Core/Scanner.cs` | Load taken-over set in `GameContext`; pass to posture read |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | Takeover VM action; banner state; reload-after |
| `src/ModManager.App/` (new dialog) | On-block takeover prompt (modeled on the nudge dialog) |
| `src/ModManager.App/MainWindow.xaml(.cs)` | Banner bar + buttons |

## Out of scope / future

- A future "Migrate this game off Vortex" entry point in settings can call `TakeOverGame` directly —
  the primitive is built for it.
- MO2 takeover uses the same primitive (its `meta.ini` is in the marker set), but MO2-specific UX copy
  is deferred until there's a real MO2 user.
