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
| A row whose files are a registered framework's installed files (the DLL mod loader Elden Mod Loader installs, a proxy DLL a framework dropped) | The framework uninstall removes them and its captured state restores them. Turning the row off too held them twice and restored them twice (round 2, I1). |

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
Safe Clear never deletes the data dir, and leaves its `RESTORE-AVAILABLE.json` breadcrumb there; a game
re-added without a restore shows those mods as turned off, which is true.

### The restore point carries a copy (round 1, Este's call)

The data folder is not the only copy. After each vanilla game's turn-offs, every turned-off mod's
data-dir holding folders (`disabled/<HoldingName>`, `disabled-trees/<HoldingName>`, and the
direct-inject / loose-root holding folders) are **copied** into the restore point under
`games/<id>/held/<path relative to the data dir>`, each file size-verified, and recorded per game as
`heldCopies: [{ name, files: [{ rel, bytes, sha256 }] }]`. Copy, not move: the toggle's own holding
stays exactly as the toggle left it. A proxy loader is held inside the play folder and a Mod Engine 2
mod is a config flip, so neither has anything in the data dir to copy. Pre-flight free space counts the
turned-off mods' main files (extra-tree entries are not estimated, so it runs a little low on a
Cyberpunk-shaped game).

**How it fits the seal.** The capture seal stays where Law A puts it, before anything moves. Planning the
held set during capture and sealing after the copy would have moved the seal past the turn-offs, which
is the one thing Law A forbids. Instead, after a game's turn-offs and copy finish, the manifest is
rewritten atomically with `heldCopies` (and `turnOffSkipped`) added for that game, plus the copied bytes
in `totalBytes` / `fileCount`. The rewrite happens only once every copy for the game has finished and
verified, so a non-null `heldCopies` always describes a complete copy. The manifest only ever describes
what is in the archive.

**A crash between the turn-offs and the copy** leaves `heldCopies: null` for that game, possibly a
half-written `held/` folder the manifest does not mention, and the safe-clear lock. Nothing is lost: the
mods are still held in the data folder. Startup recovery sees a sealed point and offers resume or
restore, as before. Restore reads `heldCopies: null` as "no copy" and turns the mods on from the data
folder. If the data folder is also gone, those mods are reported as not back on rather than restore
claiming success. A copy that fails outright (disk full) removes its own half-made `held/` folder,
records no `heldCopies`, and warns that the data folder is the only copy.

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
first, then ordinary rows, minus anything in `turnOffSkipped` (it never went off).

For each mod with a `heldCopies` record: if every recorded file is still in the data folder, it is
turned on from there as before. If any is missing, the archived copy goes back into the data folder's
holding first, in the same `HoldingName` layout. Every archived file is checked against its SHA-256
before anything is written, each destination is PathGate-checked against the data dir, only missing
files are written (never over one the data folder still has), and each is verified after the write.
Then the mod is turned on through the toggle path like any other. A damaged or incomplete copy refuses
that mod alone: nothing is written for it, it is reported, and the others continue.

Vanilla's turn-offs and Restore's turn-ons each run through one `Scanner.BulkScope` (#382), so a large
library pays a constant number of mod-list and extra-tree reads, not one per mod. Still one lane
chooser: `ModToggle`'s dispatch, with the scope handed to the scanner's lane only. Then one listing
reconciles: any recorded mod not on comes back as a warning with the lane's reason when it gave one,
the same way `modsActive` surfaces an `EnableOutcome` skip.

A mod that was off before the clear is not in the set, so it stays off.

**Ban risk.** On a high-risk game without an acknowledgment for enabling mods, Restore turns nothing back
on and says why: the files stay held and the user turns them on from the launcher, through the warning.
The acknowledgment lives in the data dir, so a game acknowledged before the clear is acknowledged after
the copy-back.

## The sheet

For a vanilla game with the new record, the sheet gains a *Mods turned off* section: how many mods 626
turned off and is holding in the named data folder, and that restoring this setup turns exactly those
back on. With `heldCopies` the sheet adds that copies are saved in the restore point too, so Restore
works from the folder or, if it is gone, from the restore point. There is no "don't delete". Without a
copy it says, more softly, to keep that folder until restoring, because then it is the only copy. Each skipped turn-off is listed with its reason as
still active. *Return-to-vanilla honesty* in the phase-1 spec is updated to match.

## What does not change

`modsActive`, what CAPTURE copies, the RESET step, the direct-inject `vanilla-moved` path, and the
skip-archive rule (turn-offs still move to holding, never delete; with no restore point there is no
record, and the mods show as turned off when the game is re-added).

## Round 2 review fixes (2026-10-02)

- **A mod the user already turned back on** between the clear and the restore is live. Restore reads the
  listing before any put-back and never puts an archived copy into holding for a live row: that made a
  second, phantom held copy (I2). The listing is read again after the put-backs, for the ban-risk check and
  the turn-ons.
- **No merged copies (I3).** If some of a mod's recorded held files are missing from the data folder, the
  ones still there are hashed too. If any differs from the record, the data folder holds a different copy,
  and Restore leaves the mod exactly as it is (writes nothing) and says so: "the data folder holds a
  different copy than the restore point, so 626 left it as it is". With nothing missing, nothing changes.
- **No partial files.** Put-back and copy-in each write a temp sibling, size- and SHA-check it, then move it
  into place. Put-back tracks every destination before writing, so a failure partway removes all of them.
  Copy-in replaces an earlier run's file by moving over it: there is no delete-then-copy window.
- **Rooted paths.** A manifest path that is rooted or starts with a separator is refused before PathGate
  (which trims a leading separator) at all three restore sites: vanilla-moved files, framework files, and
  held copies.
- **Exact-name files fail closed.** A held file whose real name ends in a dot or space can only be reached
  by its exact name, so the copy into the restore point is refused up front. The game is warned and gets no
  `heldCopies`, and Restore uses the data folder. (The scanner's own turn-on can't yet move such a file:
  a plain disable/enable round trip fails the same way outside Safe Clear. Restore reports that mod rather
  than claiming it.)
- **Stranded refusals.** A refused turn-off whose rollback stranded files in holding lists as off. Restore
  now tries it too, and reports it as recovered or as not back on.
- **Pre-flight** counts each turn-off's extra-tree entries (the toggle's own `ExtraTreeRows` selection,
  read once).
- **Schema** is 2 only when a game carries `turnedOffByClear`. A modsActive-only point stays at 1.
- **Interrupted, newer schema.** A complete manifest from a newer 626 reads as sealed (`NewerSchema`), is
  never offered for discard (`DiscardPartial` refuses any complete manifest), and the App says "update 626
  to restore it".
- **A failed manifest rewrite** after the held copy warns, and the copies it would have described are
  removed.
- **The sheet** says where each lane's mods went: the data folder (scanner, direct-inject, loose-root), the
  game's `_626\vanilla-proxy` (proxy loaders), or Mod Engine 2's config. It describes a copy in the restore
  point only for the mods that actually have one.

## Round 3: vanilla means vanilla (Este's call, 2026-10-02)

A replica of the real Cyberpunk set showed that after the per-mod turn-offs, 1,195 files stayed live: files
no mod row claims (loose redscript, unpaired tweaks and input files, CET mods, red4ext plugins, ArchiveXL
`.xl` sidecars). The round trip was exact, but the sheet said "returned to vanilla" and the game wasn't.
The phase-1 honesty section already promised that unclaimed loose files go into the restore point. Now
they do.

**The remainder sweep.** After a vanilla game's turn-offs, framework uninstall, loader sweep and held
copy, everything still in the game's **mod-only folders** is moved into the restore point under
`games/<id>/vanilla-remainder/<path relative to the game root>`, recorded as
`vanillaRemainder: [{ rel, bytes, sha256 }]`.

- **Mod-only folders:**
  - the declared extra trees;
  - mod locations that are not the game root, not the direct-inject / loose-root play folder, and not a
    base-content folder. A UE `Paks` root on the loader-less pak lane (`paks-root`) is base content and
    is never swept; it is named instead.
  - Another tool's folder (ToolOwnership Owned or ReDeployed, or a location declared Managed) is left
    whole and named.
- **Left alone inside them, each named in `leftInPlace: [{ path, reason }]` unless noted:**
  - `_626` bookkeeping (not named);
  - a registered framework's installed files (its uninstall and captured state own them; not named);
  - the files of a mod whose turn-off refused (reported as still active, never swept by a second
    mechanism; not named here);
  - a pak, ucas or utoc file `PakClassifier.IsBaseGamePak` protects;
  - a file reachable only by its exact name.
- **Frameworks** that aren't registered (ArchiveXL, Codeware and the like in `red4ext/plugins`) go,
  per Este.

**Seal discipline.** The plan, with every file's SHA-256, is recorded by an atomic manifest rewrite
**before anything moves**. If that write fails, nothing moves and the clear warns. Each file then goes
by `SafeMove.Move`: a rename on one volume, copy-verify-delete across volumes. At every instant a file is
live or archived under its record, never neither. Each archived file is checked against its record, and
a mismatch moves it back. A file that won't move (locked, read-only) stays live, is taken out of the
record and added to `leftInPlace` by a second rewrite, and the clear warns. A crash midway needs no
special recovery: Restore treats a live file with its recorded content as already back.

**Restore.** Before the loader manifests and the turn-ons (a UE4SS `mods.txt` and the `.xl` sidecars
of held mods are part of it), each remainder file goes back:

- a rooted or escaping path is refused;
- a live file with the recorded content is left as it is;
- a **different** live file is never overwritten and is reported;
- the archived copy is SHA-checked, written to a temp sibling, checked again and moved into place.

**Pre-flight.** The free-space check counts the larger of the turn-off estimate and every byte in the
mod-only folders, read per file without hashing. Both the held copies and the remainder come out of those
folders, so this bounds what lands in the restore point, apart from direct-inject and loose-root holds,
which the turn-off estimate covers.

**Skip-archive** (no restore point) does not sweep: with no record there would be nothing to restore
from.

**Sheet.**

- "Returned to vanilla" appears only when the sweep ran, nothing was left in place, no turn-off refused
  and no other tool's mods remain. Otherwise it says the game may not be fully vanilla, and lists
  STILL IN PLACE (path and why).
- A new ALSO MOVED TO YOUR RESTORE POINT line counts the remainder.
- For vanilla, the mod list is headed YOUR MODS (KEPT, TURNED OFF) instead of WHAT'S STILL INSTALLED,
  which read as a contradiction under "turned off 194 mods".
- An archive from before the sweep never claims vanilla.

**Re-review minors.**

- The "already on" skip matches name **and** location. A same-named live row elsewhere is reported as
  "already on (a different copy)" and is neither put back nor turned on.
- The put-back rollback deletes a final file only if this call's own move placed it.
- One held mod reads "A copy of it".
