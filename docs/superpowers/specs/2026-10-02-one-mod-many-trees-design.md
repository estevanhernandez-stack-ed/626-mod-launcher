# One mod, many trees — design

**Date:** 2026-10-02
**Backlog:** B4
**Status:** Stage one ("see first") shipped in #370. Stage two ("toggle") designed below and signed off
2026-10-02.
**Decided by:** Este, 2026-10-02: "See first, toggle later." Stage two: safety is decided by Core rules,
not a manifest field, and multi-tree toggling is on by default.

## The problem

A Cyberpunk 2077 mod is often several mods' worth of folders. Its archive goes in `archive/pc/mod`,
its redscript in `r6/scripts/<Mod>`, its tweaks in `r6/tweaks`, and a RED4ext plugin in
`red4ext/plugins/<Mod>`. A Cyber Engine Tweaks script goes in `bin/x64/plugins/cyber_engine_tweaks/mods/<Mod>`.
A game's manifest names one `modPath`. So the launcher lists such a mod from one folder, and turning it
off moves that one folder's files. The rest stays live. That is a reversibility problem, not only a
display gap: the user believes the mod is off when part of it still loads.

## The decision

Two stages, in this order:

1. **See first (this branch).** The manifest can name a game's other mod folders. Each row then shows
   which of them hold files with its name. Toggling is unchanged and still moves the primary folder
   only. The row says that out loud.
2. **Toggle later (not built).** Moving a mod's files in every tree as one reversible operation. That
   needs answers this stage is meant to earn: which trees are safe to move (a RED4ext plugin is safe;
   a shared framework folder is not), how to tell a mod's files from a framework's, and how the holding
   area records a multi-tree move so it can be put back exactly.

## Stage one

### The manifest field

`GameManifestEntry.ExtraModTrees: IReadOnlyList<string>?` (`extraModTrees` on disk, camelCase). It
holds paths relative to the game root, in the order the row should list them.

- **Descriptive only**, like every manifest field: it says where mods put files, never how to enable
  them. Whether a tree may be toggled is deliberately NOT a field yet. It is stage two's question, and
  a flag shipped now would be answered by guesswork.
- **Gated like `modPath`, but per tree.** `ManifestValidator.ExtraTreeProblem` drops any tree that is
  empty, absolute (a leading `/` or `\` counts on every OS), drive-qualified, contains `..`, has a
  segment of only dots or spaces, or names no folder below the game root (`.`, `./`), and keeps the rest
  of the entry. The verdict is the same on Linux, where the miner signs the feed, and Windows, where it
  is read. The miner's `OverridesValidate` refuses a curated file holding such a tree and names the
  rule, so the drop is never silent at build time (reviews on 626-game-manifest#27 and #370).
- **The game's own folders are a runtime question.** `ModTrees.Build` skips a tree that is, holds or
  sits inside one of the game's real mod locations (engine preset, the user's choice, a second
  location), and the game root or anything outside it. The manifest only knows the declared `modPath`,
  so it can't decide this.
- **No schema bump.** An older binary ignores the key, as it did for `loaders`.
- **Merge and miner.** `EffectiveManifest.MergeEntry` carries it (remote wins), and the merge
  completeness test covers it. The miner's `OverrideEntry` and `OverridesMerge` carry it from a
  curated file, and the overrides README documents it.

### Which trees hold a mod

`ModTrees.Build(gameRoot, trees)` lists each tree's top level once per reload. `For(modName)` returns
the trees holding an entry whose name EQUALS the mod's, compared on letters and digits,
case-insensitively. A folder is known by its whole name and a file by its stem. `NameMatch`'s
cleaner is deliberately not used: it drops short all-caps and version tokens to help a search, and
here that would collapse `BetterHUD` and `BetterUI` into one name. A tree that is, holds or sits inside one
of the game's own mod folders is skipped, and a tree spelled two ways is listed once.

- **Equality, not similarity.** Telling a user a folder belongs to a mod it doesn't is worse than
  saying nothing. `CoolModExtras` is not `CoolMod`.
- **Top level only.** `r6/scripts/CoolMod/` matches; a file three folders down does not. Most mods
  name their folder in each tree after themselves. A mod that doesn't is the honest "can't tell" case.
- **Read only.** Nothing moves.

### The row

Under the credit line: `Also has files in r6/scripts, red4ext/plugins`, in muted mono. Its tooltip
says: "626 turns this mod on and off in its main folder only. Its files in these other folders stay
where they are, on or off."

## Data

The curated data lives in the `626-game-manifest` repo. Cyberpunk 2077 is the first entry, carrying
`"extraModTrees": ["r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods"]`
(626-game-manifest#27). Until a feed carries the field, no row shows the line.

## Out of scope

- Toggling across trees: stage two, below.
- Intake placing an archive's files into several trees. Intake still extracts into the primary
  location, and a zip laid out from the game root is a separate problem.
- Inferring trees for games the manifest doesn't describe.

## Testing

`ModTreesTests` covers:
- matching in manifest order
- equality only, never fuzzy
- missing trees and a missing root
- case-insensitivity
- the camelCase round trip, with string-contains asserts
- an absent field reading as none
- an unsafe tree (including the game root itself) dropped and the entry kept
- a curated unsafe tree failing the miner's build (`OverridesValidateTests`)
- safe trees passing
- a curated override carrying the field through the miner

`ManifestMergeCompletenessTests` covers the merge.

## Stage two: toggle

Turning a mod off moves its entries in the extra trees along with its main files, as one reversible
operation. Turning it on puts every one of them back.

### What one real install says

Este's Cyberpunk install, on 2026-10-02, against `archive/pc/mod`'s 206 distinct mod names:

| Tree | Entries | Equal to a mod's name |
|---|---|---|
| `r6/scripts` | 124 | 45 |
| `r6/tweaks` | 91 | 49 |
| `r6/input` | 13 | 9 |
| `red4ext/plugins` | 4 | 0 |
| `bin/x64/plugins/cyber_engine_tweaks/mods` | 51 | 18 |

That is 121 entries that stay live today when their mod is turned off. The four `red4ext/plugins` folders
are ArchiveXL, TweakXL, Codeware and mod_settings: frameworks, none of which has a row of its own.

### 1. Which trees are safe to move

**Safety is per entry, decided in Core. It is not a manifest field.** The manifest is descriptive and never
says how to turn a mod on or off (the README's operating laws); a `toggleSafe` flag would be exactly that.
A tree is only a place to look. An entry in it moves with a mod when every one of these holds:

- it sits at the top of a declared tree, and its name equals the mod's (stage one's comparison, unchanged)
- no other row of the game has the same name key, so exactly one mod claims it
- it is not, and does not hold, another declared tree or one of the game's own mod folders
- the tree is not inside a folder another tool owns (`ToolOwnership`)
- the row itself is not read-only

An entry that fails any rule stays where it is, as every entry does today, and the row still lists it.

### 2. A mod's files versus a framework's

Name equality already separates them: a framework's folder is named after the framework, so it moves only
when the framework's own row is turned off, and turning the framework off is what that row means. The
single-claimant rule covers the remaining case, two rows whose names reduce to one key (`Cool_Mod` and
`CoolMod`): neither gets the entry. On Este's install, no framework has a row, so no framework folder
moves at all.

### 3. How the holding area records it

Extra entries are held at `<dataDir>/disabled-trees/<Mod>/<tree>/<entry>`. That folder sits beside the
existing `disabled/<Mod>`, not inside it.

- **The layout is the record.** The last segment is the entry; everything between `<Mod>` and it is the
  tree. No JSON is added, so there is no new on-disk shape and no camelCase surface.
- **Beside, not inside, on purpose.** An older 626 turning on a mod copies every entry of
  `disabled/<Mod>` into the main folder. A held `r6/scripts/CoolMod` inside it would land in
  `archive/pc/mod`. Beside it, an older build never sees the held entries, and they wait there until a
  newer build restores them. That is no worse than today.
- **Collision.** Turning off refuses, moving nothing, when `disabled-trees/<Mod>` already holds files,
  the same rule `HoldingFolder.HoldsFiles` applies to the main holding folder.

### The operation

It all runs through the existing `Scanner.DisableEntry` and `Scanner.EnableMod`; there is no second path.
Bulk toggles, loadouts, Safe Clear, profiles and the MCP's `set_mod_enabled` reach it unchanged.

**Off:**
1. Work out the movable entries (the rules above) and check every holding destination before anything moves.
2. Move the main files (unchanged).
3. Move each extra entry into `disabled-trees`, added to the same rollback list.
4. If anything fails, move every moved item back, extras first. Nothing stays held, and no record is
   written. If an item cannot go back, today's stranded handling applies, and the message names it.
5. Write `meta.json` and clear mirrors (unchanged).

**On:**
1. Check every destination, main and extra, before anything is written. A collision refuses and changes
   nothing.
2. Restore the main files (unchanged).
3. Move each held extra entry back to `<gameRoot>/<tree>/<entry>`.
4. If anything fails, undo the extras already restored, then remove the main copies this run created
   (unchanged). The holding folders keep everything.
5. Tear down both holding folders.

A mod whose `disabled-trees` folder is missing or empty restores exactly as before. Rows turned off before
stage two shipped are unaffected.

### The row

- **Live:** `Also has files in r6/scripts, red4ext/plugins` stays. The tooltip now says: "626 turns these
  on and off with the mod." When an entry stays put because a rule held it back, the row says that
  instead, naming the tree.
- **Turned off:** the line reads `Also turned off in r6/scripts, r6/tweaks`, from the held layout.

### Testing

New `ModTreesToggleTests` and the existing toggle suites cover:

- off then on, round trip: every tree byte-identical to before, both holding folders gone
- a failure midway through the extra moves (an entry locked open): every item back, no `meta.json`, no `disabled-trees` folder
- a collision on enable in one extra tree: nothing written anywhere, everything still held
- each safety rule holding its entry back: two claimants, an entry holding another tree, a tool-owned tree, a read-only row
- a framework folder with no row of its own never moving
- a mod turned off before stage two, with no `disabled-trees` folder, turning on unchanged
- a held entry whose tree is gone on enable: the tree folder is recreated

The App row text is checked with a Debug build and a UIA walk on Este's Cyberpunk install.
