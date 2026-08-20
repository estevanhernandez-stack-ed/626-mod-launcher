# Working on one world instead of the whole folder

**Date:** 2026-08-19 · **Follows:** `2026-08-19-saves-are-three-shapes.md`
**Status:** design. Three parts are ready to build; one is blocked on a question only the game can answer.

## Why

Palworld's saves panel now lists worlds. Everything you can *do* still operates on the whole folder:
back up all of it, restore all of it. The unit a player thinks in and the unit the app acts on
disagree, and the gap matters most at exactly the moment Pocketpair's mod guideline tells people to
care — *"back up important save data in advance"* before modding a world.

Four operations close that gap. **None of them read the save format.** They are folder operations on
folders the panel already lists, which is the entire point: character editing would need the `PlM1`
container decoded and a GVAS tree rewritten on a game that patches often, and these deliver most of
the same safety for none of that risk.

## What exists to build on

`SaveManager.Backup(saveDir, snapshotsDir, label, auto)` is `ZipFile.CreateFromDirectory` — it will
zip **any** directory, including a single world. `Restore` deletes everything in the target and
extracts over it. `ListWorlds` already enumerates worlds with size, date, role and player count.

So the machinery is nearly there, which is what makes the first foot-gun so easy to walk into.

---

## 1. Back up one world

`Backup(worldDir, …)` works today with no change. The danger is where the result goes.

**A world snapshot must not land in the same list as a whole-folder snapshot.** They look identical —
both are `yyyyMMdd-HHmmss__label.zip` — and restoring a world zip through the whole-folder `Restore`
would delete every other world and extract one world's files loose at the top level. Silent, total,
and recoverable only via `before-restore`.

So world snapshots are scoped by **location, not by naming convention**:

```
<dataDir>/saves/                       whole-folder snapshots (today, unchanged)
<dataDir>/saves/worlds/<worldId>/      that world's snapshots
```

`ListSnapshots` reads one directory and never recurses, so it cannot see them — the separation is
enforced by the existing code rather than by everyone remembering a rule.

**Retention.** Whole-folder snapshots prune old autos and keep every user snapshot. World snapshots
inherit that, per world, or a long-played world quietly accumulates gigabytes nobody is looking at.

## 2. Restore one world

Restore into `<saveDir>/<worldId>/` only. Siblings and top-level files are untouched.

Three things this must do that the whole-folder restore does not:

- **Snapshot that world first**, world-scoped, labelled `before-restore` — same guarantee, same name,
  smaller blast radius.
- **Refuse a mismatched source.** A zip taken from world A extracted into world B is a silent
  corruption that looks like a successful restore. The world id goes in the snapshot's path (see
  above) and is checked before extracting; a mismatch is refused with a sentence, not a warning.
- **Confirm**, per `2026-08-19-confirming-in-the-saves-panel-design.md` — it replaces a world.
  The copy says which world and what it holds, and that the pre-restore state is kept.

**This is the one Pocketpair's guideline is really asking for.** Their advice is that a save loaded
with mods may keep misbehaving after the mods are gone. The answer to that is putting *one world*
back, not rolling your whole library to a point in time.

## 3. Duplicate a world — dropped here, and later un-dropped

> **Superseded by `2026-08-19-the-world-name-is-readable-design.md`.** The reasoning below is correct
> about the symptom and wrong about the cause. `LevelMeta.sav` is 2 KB and its tail is plaintext — the
> world name can be read and rewritten in place, so the copy CAN be told apart from its original, and
> the feature shipped. The conclusion held only because the scan that produced it was run badly. Left
> here unedited because the test it describes is real and the reasoning is worth being able to check.

## 3. Duplicate a world — tested, and dropped

**Answered on the real game, 2026-08-19. The test cost ten minutes and saved building the wrong
feature.**

A hosted world was copied to a fresh GUID folder and Palworld launched. Result:

> *"Yeah they are both there and have the same name so I do not know which is the original."*

So the copy **does** appear and the original **does** still load — the outcome that looked like a
green light. It is not one. Palworld reads a world's display name from **inside** the save, not from
the folder, so a duplicate produces two worlds with the same name and no way to tell them apart in the
game's own list.

That defeats the entire purpose. The point of duplicating was *"experiment somewhere safe"*, and you
cannot experiment safely on a world you cannot reliably identify. Shipping it would hand people a
button whose most likely outcome is playing — or deleting — the wrong one.

Renaming the copy so the two are distinguishable means writing into the `PlM1` container. That is the
dependency this whole approach exists to avoid, and it would sit on the *destructive* side of the app
rather than the display side.

**Dropped, because 1 and 2 already deliver its value.** Back up one world, experiment on the real one,
restore if it goes wrong. Same safety, one world in the list, nothing to confuse.

**Verified clean afterwards:** the original was byte-identical before and after the game ran with the
duplicate present — 0 changed, 0 missing — and the copy was removed.

## 3b. Who started the world, not single- versus multi-player

**Corrected 2026-08-20 against the game's own World Select screen.** Both worlds listed there were
hosted and both were marked `Multiplayer: ON`, while this panel was calling one of them *"Solo so far
— you are the only player in it."* Two words for one object, and ours was the wrong one: we were
counting character files and reporting it as though it were the game's setting.

**We cannot read that setting.** It lives in `WorldOption.sav`, whose property region — unlike
`LevelMeta.sav`'s — is compressed: 4,446 bytes yield exactly one readable string, `Difficulty`. So the
label says only what the filesystem shows, which is who started the world and how many characters have
played in it.

A second thing the same screen settled: **a joined world is not in Palworld's list at all.** There is
no world there to load — 360 KB of `LocalData.sav` against the host's 32 MB — and you reach it by
joining their session. The panel showing it is right, because it is real disk worth backing up, but it
now says so rather than leaving the player to notice the discrepancy and file it as a bug.

## 4. Name your worlds

`World 1` and a GUID is not a thing anyone can choose between under pressure — and every operation
above asks them to choose.

Labels are **ours, not the game's**: a small map at `<dataDir>/world-labels.json`, camelCase keys via
`AtomicJson`, keyed on the world folder name, written on rename. That choice is the point — it needs no
parser, survives every Palworld patch, and the name is one the player picked rather than one we
guessed.

- A world with no label keeps `World 1`-style ordinals, so the panel is never blank.
- A label survives restore, because the folder id does not change.
- A label for a world that no longer exists is kept, not pruned — a deleted world may come back from a
  snapshot, and silently forgetting its name would be a small betrayal of the one thing the user typed.

**Not stored inside the save snapshot.** Labels are launcher metadata; restoring an old snapshot should
not rename the world you are looking at.

## Order

**4, then 1, then 2. 3 was tested and dropped — see above.**

Naming first because it is the cheapest and it makes the other two legible — a confirm that says
*"Replace **Ridgeline Base** with the backup from Tuesday"* is a different sentence from one naming a
GUID, and confirms are where this panel does its most important work.

## Tests

Core, pure and per-operation: a world snapshot lands in its own directory and is invisible to
`ListSnapshots`; restoring a world leaves siblings byte-identical; a mismatched world id is refused;
labels round-trip camelCase and survive a world being restored.

**The end-to-end sits on a copy**, the way the restore test did: manifest the real tree, point the app
at a copy, exercise, verify the copy byte-for-byte, verify the real tree untouched. That method is now
proven and should be the default for anything touching saves.

## Non-goals

- ~~Reading or writing the `PlM1` container. Every item here is a folder operation.~~ **Retired** —
  see the amendment. The world NAME is read and written in place, under a byte budget, with the
  compressor still firmly out of scope.
- Editing characters, levels, or inventories. See the format analysis — active-development game,
  compressed container, corruption rather than a wrong list as the failure mode.
- Merging worlds. Nothing sane can be done there without understanding the format.
