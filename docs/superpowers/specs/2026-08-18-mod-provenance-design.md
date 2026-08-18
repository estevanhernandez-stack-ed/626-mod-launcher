# Mod provenance: track what we placed, infer the rest — Design

**Date:** 2026-08-18 · **Backlog:** A19 (path-preserving intake), plus the grouping question it exposed
**Status:** design, awaiting review

## The problem

626 lists a mod per *file*. That is right for a pak game — a pak is one file and its name is its
identity — and wrong for every engine whose mods are script trees.

Measured on a real Monster Hunter Wilds install. Under `reframework/autorun`:

| On disk | What it is | What 626 does today |
|---|---|---|
| `KittyBig.lua` | one mod, one file | lists it — correct |
| `mhwilds_overlay.lua` + `mhwilds_overlay/` | **one** mod, two artifacts | lists the file, ignores the folder |
| `_CatLib/` (33 files) | a mod other mods depend on | ignores it entirely |
| `utility/Statics.lua` | shipped by one mod, named as shared | ignores it entirely |

Three of those four are wrong, and all three are wrong *silently*.

`Scanner.PlanIntake` makes it worse on the way in: it sets `RelPath` to the bare filename, and
`ExecuteIntake` copies to `primary.Abs + RelPath`. So installing CatLib would drop 33 files loose into
`autorun/` instead of `autorun/_CatLib/`, and two mods each shipping `utility/Statics.lua` would
collide on one name with the second silently winning. **The install would report success and not
work** — the failure mode this repo keeps finding and keeps disliking.

## The question underneath

When one mod is several files, and some of those files are shared with other mods: how does the
launcher know which files belong to which row, and what happens when turning one off would remove a
file another mod still needs?

Two possible answers, and the design is to take **both**.

## The design

**A file is either claimed by an install record or it is not.** That single rule is what lets the two
halves coexist without a conflict to arbitrate.

### 1. Tracked — mods 626 installed

Intake already computes `IntakeResult.Added` and throws it away. Persist it.

The shape is not new: `FrameworkInstallManifest` already records `InstalledFiles` + `InstalledUtc` to
`<dataDir>/frameworks/<id>/install.json`. Mods get the same treatment one directory over.

```
<dataDir>/installs/<modKey>/install.json
```

```jsonc
{
  "modKey": "kittybig",
  "displayName": "Kitty Big",
  "installedUtc": "2026-08-18T14:02:11Z",
  "sourceArchive": "Kitty Big-35-1-0-1740560264.zip",
  "location": "mods",                      // which declared mod location it landed in
  "files": ["KittyBig.lua"],               // relative to that location, paths preserved
  "replacedBackup": "..."                  // existing ReplacedStore pointer, when it overwrote
}
```

camelCase, `AtomicJson`, per the on-disk rule. Add to the surfaces list in
`.claude/rules/camelcase-json-on-disk.md`.

With this, a row **is** a mod: toggling moves exactly its files, uninstall removes exactly its files,
and *"does any other enabled mod claim this file?"* becomes answerable — which is what makes shared
libraries safe rather than lucky.

### 2. Inferred — mods already on disk

Everything not claimed by a manifest gets grouped by rules. Best-effort, and **the row says so**,
exactly as the loader row says *"626 did not install this."*

Rules, in order:

1. **Stem pairing.** A script and a folder sharing a stem are one mod: `mhwilds_overlay.lua` +
   `mhwilds_overlay/` is one row, not two. This is the single highest-value rule — it is the case the
   current listing gets visibly wrong.
2. **Bare script.** A top-level script with no matching folder is a mod. `KittyBig.lua`.
3. **Unpaired folder.** A folder no script matches is *probably a library* — `_CatLib`, `utility`.
   Show it, mark it **shared**, and do **not** offer a toggle. We cannot know who depends on it, and a
   one-click switch that silently breaks four other mods is worse than no switch.

Rule 3 is a deliberate refusal, not an omission. Compare the loader row, which *is* toggleable behind
a warning: there we know exactly what the file does. Here we do not, so the honest surface is
visibility without a switch.

### 3. The merge

```
claimed files  -> their tracked mod's row
leftovers      -> inference rules above
```

A mod can be **partly tracked**: 626 installs a newer CatLib over Fluffy's copy and claims only the
files it wrote. Leftovers stay inferred. **An uninstall never removes a file it did not place** — that
is the rule that keeps partial tracking safe rather than a trap.

### 4. Path-preserving intake (A19)

The manifest is only worth having if intake stops flattening. The anchor is derivable, not guesswork:
every one of these archives states its own path.

```
_CatLib/reframework/autorun/_CatLib/action.lua   ->  _CatLib/action.lua
reframework/autorun/utility/Statics.lua          ->  utility/Statics.lua
KittyBig-v1.0/reframework/autorun/KittyBig.lua   ->  KittyBig.lua
```

Find the segment matching the declared mod location and keep everything below it. The wrapper folder
varies (`_CatLib/`, `KittyBig-v1.0/`, none at all) so the match must not assume the path starts at the
archive root.

**No anchor found → today's flatten, unchanged.** `Shop Tweaks/.../Shop Tweaks - Everything.pak`
contains no `reframework/autorun`, and flat is correct for it. This keeps every existing pak game
byte-identical: the change is additive, not a behaviour swap.

## What this earns

**It heals itself.** Every mod installed through 626 moves from inferred to tracked. Nothing needs
migrating and the guessing shrinks on its own.

**The limit stays visible.** For a Fluffy-placed `_CatLib` we can show it and say we do not know who
depends on it. That is a real limit and should read as one — not be papered over with a confident
guess, which is how the flat install would have "succeeded".

## Decisions taken

- **Track and infer, not one or the other.** Refusing to manage what we did not install would make the
  launcher useless on any library a user already has, which is most of them.
- **Claim only what we wrote.** No uninstall of a file 626 did not place, ever.
- **Unpaired folders are shown, not switched.** Visibility without a dangerous affordance.
- **Flat stays the default when no anchor matches.** Pak games are the majority and must not move.

## Open questions

1. **Key.** `modKey` from `Scanner.ModKeyFor` works for a file; what keys a folder-shaped mod? The
   folder name is the obvious candidate, but two mods can ship the same folder name.
2. **Reference counting on disable.** When a tracked mod's file is also claimed by another *enabled*
   tracked mod, disable must leave it. Is that per-file, or does the row refuse and explain?
3. **Adoption's relationship to this.** A14 established adoption is metadata-only. Does adopting an
   inferred group also mint a manifest — a claim we did not earn — or stay metadata and leave the
   grouping inferred? Leaning: stay metadata. A manifest should mean "we wrote these bytes."

## Testing

- Intake fixtures built from the **real archive shapes** in this document (wrapper folder, no wrapper,
  nested library, pak-with-no-anchor). The pak cases pin that existing behaviour did not move.
- Round-trip: install → manifest lists exactly what landed → uninstall → folder byte-identical to
  before.
- Shared file: two tracked mods claiming one path, disable one, assert the file survives.
- Inference: the four real Wilds shapes above produce the rows the table says they should.
- A partly-tracked mod: claimed files move, unclaimed leftovers stay.
