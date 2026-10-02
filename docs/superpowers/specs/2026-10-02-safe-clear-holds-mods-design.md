# Safe Clear "Return to vanilla" holds every active mod

**Date:** 2026-10-02
**Status:** Approved (Este, "match the spec")
**Branch:** `fix/safe-clear-vanilla-holds-mods`
**Amends:** [Phase 1 Safe Clear + Restore](2026-05-28-phase1-safe-clear-restore-design.md), MUTATE-ALL and *Return-to-vanilla honesty*

## The gap

The phase-1 spec says the vanilla end-state moves "active mod payloads to holding". The code never did.
`RestorePointEngine.ApplyEndState("vanilla")` moved catalog direct-inject files out of the game root,
uninstalled frameworks and flipped UE4SS/BepInEx manifests off. Nothing turned off an ordinary mod, so a
Cyberpunk `.archive` mod, a Windrose pak and their B4 extra-tree entries stayed live after "Return to
vanilla". The game was not vanilla, and the sheet said it was.

## What vanilla turns off

Every enabled, switchable row of the game's listing (`ModListing.Resolve`, the one read path), turned off
through the one write path, `ModToggle`. That brings the lane's own machinery along: the scanner's
`DisableEntry` (B4 extra trees to `disabled-trees/`, `HoldingName`, the held-copy refusals, rollback of
a partial move), Mod Engine 2's config flip, the direct-inject and loose-root moves to their holding
folders, the proxy-loader step-aside, and an idle library row.

Left alone, each for a reason that already exists elsewhere:

| Row | Why it is not in the turn-off set |
| --- | --- |
| Owned (Vortex / MO2, `ReadOnly`) | Another tool's files. Noted in the sheet's "Managed by" line, as before. |
| Other `ReadOnly` rows (a library something needs, or whose need is unknown) | The listing already refuses to switch it. Same rule as Play vanilla's step-aside. |
| UE4SS / BepInEx manifest mods | Already owned by the clear: captured in `loaderMods`, flipped off by the loader sweep, flipped back on Restore. One owner per mod. |
| A row whose files sit at or under a sealed direct-inject move | Already moved to `vanilla-moved` by the existing step. Never double-moved. |

The direct-inject step is unchanged: same plan, same `vanilla-moved` folder in the archive, same
restore. Existing restore points keep restoring. The new turn-offs cover what that step never reached,
including direct-inject rows it did not plan (a play folder under `Game\`, a DLL loader's hosted mods).

### Order inside vanilla

1. Execute the sealed direct-inject moves (unchanged).
2. Turn off the sealed set: ordinary rows first, then loader rows (`IsLoader`, proxy loaders), so a mod
   never outlives the thing that loads it by a step.
3. Uninstall frameworks.
4. Sweep UE4SS/BepInEx manifests off (unchanged).

Turn-offs run before the framework uninstall because the listing has to look the way it looked when the
set was sealed. A UE4SS mods folder exists as a location because UE4SS is there.

## How it is recorded

During CAPTURE, while nothing has moved, the orchestrator plans the turn-offs and seals them in the
manifest as `turnedOffByClear` on the game's archive: one `{ name, location }` per row. Law A: seal
before destroy. MUTATE executes exactly that set and Restore reads it from the seal; nothing is
re-detected.

- `turnedOffByClear: null` is a manifest from before this change, or a `modsActive` game. Restore
  behaves exactly as it did.
- `turnedOffByClear: []` is a vanilla game with nothing to turn off.

The held files stay where every turn-off puts them, in the game's data dir (`disabled/`,
`disabled-trees/`, `direct-disabled/`, `loose-disabled/`, or the play folder's `_626\vanilla-proxy`).
They are not copied into the restore point. Safe Clear never deletes the data dir, and leaves its
`RESTORE-AVAILABLE.json` breadcrumb there; a game re-added without a restore shows those mods as
turned off, which is true. The sheet names the data dir so nobody tidies it away.

Any turn-off that does not take is recorded after MUTATE as `turnOffSkipped` (`{ name, reason }`), by an
atomic rewrite of the already-sealed manifest. It is a note, not part of the seal: if that rewrite fails,
the sealed manifest still restores.

The manifest's `schemaVersion` goes to 2. A build that predates this change would read a v2 manifest,
ignore `turnedOffByClear`, and restore with every mod left turned off while reporting success. Refusing
with "update the launcher" is the honest answer. v1 manifests restore unchanged.

## Ordering and failure

- **One refused turn-off continues.** A held-copy collision, a locked file, a base-pak refusal: the mod
  stays as it was (the toggle rolls back its own partial move), the clear moves on to the next one, and
  the skip is recorded in the manifest, the sheet, and the result's warnings. Vanilla is best-effort,
  which the loader sweep already was. Aborting would leave half the games cleared with the RESET not
  run, which is worse than one mod left live and named.
- **A crash partway through MUTATE** leaves a sealed restore point whose `turnedOffByClear` names mods
  that may still be live. Restore handles that: it turns each recorded row on, then judges by the final
  listing, not by each call's outcome. A mod that never went off is on at the end and is not reported.

## How Restore puts it back

After `ReplayGame` copies the data dir back, returns the `vanilla-moved` files, restores framework files
and re-applies loader manifests, it turns the recorded set back on through `ModToggle`: loader rows
first, then ordinary rows, minus anything in `turnOffSkipped` (it never went off). Then one listing
reconciles: any recorded mod not on comes back as a warning with the lane's reason when it gave one,
the same way `modsActive` surfaces an `EnableOutcome` skip.

A mod that was off before the clear is not in the set, so it stays off.

**Ban risk.** On a high-risk game without an acknowledgment for enabling mods, Restore turns nothing back
on and says why: the files stay held and the user turns them on from the launcher, through the warning.
The acknowledgment lives in the data dir, so a game acknowledged before the clear is acknowledged after
the copy-back.

## The sheet

For a vanilla game with the new record, the sheet gains a *Mods turned off* section: how many mods 626
turned off and is holding in the named data folder, that the folder must not be deleted, and that
restoring this setup turns exactly those back on. Each skipped turn-off is listed with its reason as
still active. *Return-to-vanilla honesty* in the phase-1 spec is updated to match.

## What does not change

`modsActive`, what CAPTURE copies, the RESET step, the direct-inject `vanilla-moved` path, and the
skip-archive rule (turn-offs still move to holding, never delete; with no restore point there is no
record, and the mods show as turned off when the game is re-added).
