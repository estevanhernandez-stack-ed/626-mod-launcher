# One mod, many trees — design

**Date:** 2026-10-02
**Backlog:** B4
**Status:** Stage one ("see first") built in the same branch. Stage two ("toggle") waits on sign-off.
**Decided by:** Este, 2026-10-02: "See first, toggle later."

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

- Toggling across trees (stage two).
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
