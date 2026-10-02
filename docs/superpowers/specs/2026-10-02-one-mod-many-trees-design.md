# One mod, many trees — design

**Date:** 2026-10-02
**Backlog:** B4
**Status:** Stage one ("see first") shipped in #370. Stage two ("toggle") signed off 2026-10-02 and built
on `feat/b4-multi-tree-toggle`, live-verified the same day (see "Live verification"). Safe Clear does not
yet know a mod's extra trees (see "Follow-ups").
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
- the tree is not inside a folder another tool owns (`ToolOwnership`). `ReDeployed` counts as owned: a
  folder the user took over and the other manager deployed back into holds that manager's files again,
  not 626's to move
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
- **The folder name is not always the mod's name (`HoldingName`, `fix/toggle-name-alias`).** Both holding
  roots, `disabled/<Mod>` and `disabled-trees/<Mod>`, name the folder with `HoldingName.Folder(modName)`.
  Mod names come from file names, and Windows opens a path after normalising it: a trailing dot or space is
  stripped and `CON`, `NUL`, `COM1` and the rest (with or without an extension) are devices. So `Foo _P.pak`
  (the mod `Foo `) was held in `Foo`'s folder, `Foo..archive` (`Foo.`) likewise, and `CON_P.pak` could reach
  the console device (Windows 11 relaxed the device rule for a full path; Windows 10 did not, and other tools
  reading the data folder may not). An ordinary name is its own folder, unchanged, so an older build's
  hold of an ordinary name needs no migration. A name that is not safe as written (empty or whitespace, a trailing dot or space, an
  invalid file-name character, a device name on the part before the first dot (`CONIN$` and `CONOUT$` included), `.` or `..`, a name already
  starting with `~626~`) is held in `~626~` plus the lowercase hex of its UTF-8 bytes: `Foo.` in
  `~626~466f6f2e`. An ordinary name is never encoded, whatever its length. A risky name over 125 bytes would
  encode past NTFS's 255-character limit, so it has no holding folder and turning it off refuses. Lone surrogates are carried through (WTF-8) so two names that differ
  other than by case never share a folder. `HoldingName.ModName` is the exact inverse, used wherever a
  folder name is read back as a mod (`ListDisabled`, the library-inference disabled keys, held library
  dependents, a restore point's re-enable): a `~626~` folder decodes only when `Folder` of the decoded name
  gives it back. Anything else, a hand-made or malformed `~626~` folder included, is a raw name.
  - **Legacy holds are read through a fallback, not migrated.** Windows 11 lets a plain `CreateDirectory`
    make `disabled/Aux`, `disabled/CON` or `disabled/Con.Fix`, so an older build may have held such a mod
    under its raw name, in `disabled` and (v0.23.0) `disabled-trees`. Those names now encode, so the
    toggle would look only in `~626~...` and leave the row stuck off. `HoldingName.LegacyPath` finds the
    raw-named folder by its exact real name when the encoded one has no record (`disabled`) or does not
    exist (`disabled-trees`). Turn-on reads and tears it down there, uninstall deletes it, and a new
    turn-off refuses while it holds files (a record-only legacy hold protects nothing: the new hold goes to the encoded folder, which the listing and turn-on prefer, and the old record is left alone). A raw prefixed folder reaches the same fallback. A tagged encoding was chosen over a lossy slug or a hash because it is reversible from the
  folder name alone (the layout stays the record) and changes nothing for the names that were already safe.
- **A move across volumes undoes itself.** When the game and the data folder sit on different drives, a
  move can't be a rename and falls back to copy then delete. That fallback (`SafeMove`) now removes its
  own partial copy if it fails, and refuses a source that contains a link, so a junction can't drag a
  tree from somewhere else into the holding area.

### The operation

It all runs through the existing `Scanner.DisableEntry` and `Scanner.EnableMod`; there is no second path.
Bulk toggles, loadouts, profiles and the MCP's `set_mod_enabled` reach it unchanged. Safe Clear's
mods-active end state re-enables through `EnableMod` too. Its vanilla move does not: it runs through
`RestorePointEngine`, which does not yet know a mod's extra trees. It is listed under "Follow-ups".

**Off:**
1. Work out the movable entries (the rules above) and check every holding destination before anything moves.
2. Move the main files (unchanged).
3. Move each extra entry into `disabled-trees`, added to the same rollback list.
4. If anything fails, move every moved item back, newest first, so extras come back before the main
   files. When every item goes back, nothing stays held and no record is written. When a main file can't go
   back, today's stranded handling applies and the message names it. When an extra can't go back, the
   rollback STOPS there: the main files stay held with a `meta.json`, so the mod lists as off, and the
   message names what is held where. Moving the main files back would list the mod as on while part of it
   sits in `disabled-trees`, where no toggle looks for a live mod; held with a record, turning it on
   restores the main files and every held extra together.
5. Write `meta.json` and clear mirrors (unchanged).

**On:**
1. Check every destination, main and extra, before anything is written. A collision refuses and changes
   nothing. Ownership is checked again here, for the main location and for every tree with a held entry:
   a tree another tool took (or re-deployed into, which counts as owned) while the mod was off skips the
   whole turn-on, with "target folder now owned by another tool", rather than bringing the mod back with
   one tree missing. The row's status says so: "<Mod> is still off: <reason>."
2. Restore the main files (unchanged).
3. Move each held extra entry back to `<gameRoot>/<tree>/<entry>`.
4. If anything fails, undo the extras already restored, then remove the main copies this run created
   (unchanged). The holding folders keep everything.
5. Tear down both holding folders.

A mod whose `disabled-trees` folder is missing or empty restores exactly as before. Rows turned off before
stage two shipped are unaffected.

### The row

The wording lives in `ModTreesText` (Core, under test); the App only binds it. Trees are listed in the
manifest's order, each once.

| State | Line | Tooltip |
|---|---|---|
| Live, entries move | `Also has files in r6/scripts, red4ext/plugins` | 626 turns these on and off with the mod. |
| Live, nothing moves (the row is read-only) | `Also has files in r6/scripts` | 626 doesn't move this mod's files in these folders. They stay where they are, on or off. |
| Live, some held back, name can't be told apart (contested or protected) | the same line | Adds: Files in r6/tweaks stay where they are: 626 can't tell they belong only to this mod. |
| Live, some held back, another tool owns the folder | the same line | Adds: Files in red4ext/plugins stay where they are: another tool manages that folder. (Plural: those folders.) |
| Live, a tree where one entry moves and another is kept | the tree is listed once | The sentence opens "Some files in" instead of "Files in". |
| Live, files still held in `disabled-trees` (a leftover) | the line, then `Some files are held in <path>.` | Adds: 626 can't tell which folders these came from. |
| Off, held in `disabled-trees` | `Also turned off in r6/scripts, r6/tweaks` | 626 turned these off with the mod. Turning it on puts them back. |
| Off, entries still live (turned off before stage two) | `Files in r6/tweaks are still on.` | These files didn't move when the mod was turned off. Turn it on and off again to move them. |
| Off, both held and still live | `Also turned off in r6/scripts. Files in r6/tweaks are still on.` | The held tooltip, then: Files in r6/tweaks didn't move when the mod was turned off. Turn it on and off again to move them. |
| Off, files held under no declared tree | `Some files are held in <path>.` | 626 can't tell which folders these came from. |
| Off, holding folder unreadable | `626 couldn't read <path>.` | 626 couldn't check whether this mod's other files are held here. |

An unreadable folder never says files are there. After a turn-on that leaves files held, the status line
reads: "<Mod> is on, but some of its files are still held in <path>. 626 couldn't tell where they go." When
626 couldn't read the folder: "<Mod> is on, but 626 couldn't read <path> to check for leftover files."

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

## Live verification

2026-10-02, on Este's Cyberpunk 2077 install, with BlackChrome. Turning it off moved 31 files in three
trees out of the game. Turning it back on left 2,527 files across the six folders byte-identical to
before. The row text was verified by a UIA walk in both states.

## Uninstall says what it will delete, held folders included

The first cut refused to uninstall a mod with files in `disabled-trees/<Mod>`, because the delete knew only
the main files and would have orphaned them. Este, 2026-10-02: *"let them know what it's going to do and
let them choose to cancel or to proceed."* So the refusal (`UninstallBlock.HeldInOtherFolders`) is gone.
A turned-off mod's held extras and a live mod's leftovers both go with the mod, after the user has been
told.

**The preview.** `ModUninstall.Preview(ctx, mods)` is read-only. It returns an `UninstallPreview`: each
mod's name and files (`mod.Files`), and `HeldFolders`, one per mod whose `disabled-trees/<Mod>` holds
anything, with its absolute path and the declared trees it holds entries under (`TreeHolding.Held`). It
reads by the delete's rule: a link counts as something held, but nothing behind it is read, so no tree is
named from a link's target. A folder whose files fit no declared tree, or whose only content is links, is
listed with no trees. A folder that can't be read is listed with `Unreadable = true` rather than guessed
about. The app's confirm dialog and the agent's `uninstall_mod` both read it.

- **App.** The dialog keeps its first sentence and its buttons (Uninstall, Cancel, default Cancel), and
  adds sentences when something is held. Trees: `626 is also holding some of its files in r6/scripts,
  r6/tweaks, and will delete those too.` A folder with no trees gets its own sentence, never spliced into
  the tree list: `626 is also holding files for it in <path> and will delete those too.` An unreadable
  folder: `626 couldn't read <path> to see what it's holding for it; anything there will be deleted too.`
  A family's variants are merged, each tree once, and read "their" and "them".
- **MCP.** Without `confirm: true` the message lists the main files as before, plus `and the files 626 is
  holding for it in <path> (r6/scripts, r6/tweaks)` per held folder. With it, the tool checks the held
  folder is gone as well as the listing, and returns the paths as `deletedHeld`. When a held folder can't
  be fully deleted the result is `ok: false` with the core message, `modRemoved` from a real listing check,
  `heldLeft` and `deletedHeld`.

**The delete, and its rails.** `Run`/`RunAll` delete each mod's `disabled-trees/<Mod>` right after that
mod's own uninstall succeeds. A failure in the main uninstall leaves the held extras intact and the mod
still listed; a later family member's failure leaves no orphan behind.

- **Containment.** Checked for every mod before anything is deleted, on the mod's holding folder name
  (`HoldingName.Folder`), not its raw name.
  - **The folder.** Every name has one. A name that can't be a folder as written (`Foo.`, `Foo `, `Foo:alt`,
    `x\..\Foo`, `..`, `CON`) is held in its own `~626~` folder, so it reaches that and never the folder
    Windows would normalise it onto. #376 first shipped this as "such a name holds nothing"; once the
    toggle held such names in encoded folders, that rule would have left their held files behind, so it
    became "it uses its encoded folder". The check is textual on purpose: `Path.GetFullPath` expands an
    existing folder's 8.3 alias, so comparing the resolved name would misread a mod named `OTHERL~1`.
  - **Refused (throws, nothing deleted).** A holding folder name that would resolve outside
    `TreeHolding.Root(ctx)`. The encoding makes that impossible (`..` is `~626~2e2e`); the guard stays
    because the delete behind it walks a whole tree. Before the encoding, `..`, `.` and `..\x` were refused
    here. Also refused: a root that isn't strictly under the data folder, or is a link.
- **Real names only.** The folder is touched only when the root lists an entry with the holding folder's
  real name (enumerated without a search pattern). A mod literally named `OTHERL~1` never reaches `Other
  Long Name Mod`, although opening that path would.
- **The same guards on the main uninstall.** `Scanner.UninstallMod` had the same alias hole in its delete of
  `disabled/<name>`: uninstalling `Foo.` (from `Foo..archive`), `Foo ` or an 8.3 alias recursively deleted
  `Foo`'s turned-off copy, and `..` reached the data folder. It now deletes `disabled/<HoldingName.Folder>`,
  only when `disabled` lists it by that real name, behind the same escape guard. A folder whose real name
  ends in a dot or space can only come from a `\\?\`-aware tool, never from 626; it lists under that name
  and is deleted through its exact path, so its lookalike never is. The live-file loop deletes each
  scanned entry by its exact name (`\\?\` when a segment ends in a dot or space).
- **No following links.** `LinkSafeDelete` walks the tree without descending into a reparse point. A
  junction or symlink is removed as the link (`Directory.Delete(path)` non-recursively, after clearing a
  read-only flag on the link itself, or `File.Delete`), and its target is never touched; that includes a
  held folder that is itself a link.
- **Files, then folders.** Files are deleted one by one, then folders deepest first, each non-recursively.
  No `Directory.Delete(recursive)`.
- **Verified, and a failure doesn't stop the run.** A held folder that throws (a file in use, a
  permission) or still holds a file or a link afterwards is recorded, and the run carries on. At the end
  one `HeldFolderLeftException` names every folder left, with the first cause inside: `<Mod> was
  uninstalled, but 626 couldn't delete everything it was holding for it in <path>. Close anything using
  those files and delete the folder, or try again.` The app reloads after an uninstall whatever happened,
  so the rows show what is really on disk.

## Follow-ups

- **Closed: toggles held aliasing names in one folder** (`fix/toggle-name-alias`). Turning off and on built
  `disabled/<name>` and `disabled-trees/<name>` from the raw mod name, so `Foo.` landed in `Foo`'s folders.
  Both now go through `HoldingName`. `GuardNoBasePakMove` sized files through a plain join; it now sizes
  through `FolderNames.ExactPath`, so a `\\?\`-made sidecar is sized as itself.
- **Closed: a slug folder answers only to its own mod.** The direct-inject and loose-root holding folders
  (`EnginePresets.Slugify(mod.Name)`) merge distinct names (`Foo` / `foo-` / `Foo.` are all `foo`), and `con`
  is a device on Windows 10. They stay slug-named: switching to `HoldingName` would stop every folder an
  existing user holds from being found by name. Instead the held record (`__626mod.json`, which already
  stored `name`) is checked both ways, ignoring case. Turning on a name whose slug folder holds a different
  mod's record refuses. Turning off into a slug folder whose record belongs to another mod refuses while
  any of that mod's entries is still held ("Turn <it> on first"); a stale record with none of its entries
  left is treated as empty and replaced. A record without a
  name (older builds) keeps the old behaviour.
- **Closed: long names.** An ordinary name is never encoded, whatever its length. A risky name whose
  `~626~` folder would pass 255 characters (over 125 UTF-8 bytes) has no holding folder: turning it off
  refuses before anything moves ("its name is too long to hold safely"), and uninstalling it has nothing
  held to delete.
- **Still open: case-only names.** Two ORDINARY names differing only in case (`Foo` and `foo`, from two mod
  locations) still share one holding folder, as they always have; `HoldingName` does not change case.
- **Still open: DisableEntry's own moves** join `loc.Abs` and the scanned file name plainly. They can only
  alias for a live entry a `\\?\`-aware tool created; worth the same `FolderNames.ExactPath` treatment.
- **Safe Clear and extra trees.** Replaying a restore point used to copy the archived `disabled-trees`
  back after a mods-active end state, holding a second copy of entries that were live again (fixed: the
  replay skips it, as it skips `disabled`). What is still open: Safe Clear's vanilla move doesn't know
  extra trees, so a mod's entries there stay in the game.
- **The cross-volume fallback is untested on real hardware.** `SafeMove`'s copy-then-delete is covered by
  unit tests, not by a real two-drive install (smoke entry "B4 cross-volume").
- **Bulk disable cost is O(n²) on tree games.** Each `DisableEntry` builds the mod list to find claimants.
  A bulk toggle should build `ExtraTreeRows` once and pass it down.
- **`isOwned` isn't ancestor-aware for extra trees.** Ownership is resolved on the tree folder itself; a
  marker in an ancestor of a declared tree isn't seen.
- **A warning on one toggle path only.** An `EnableOutcome` warning shows on the single-row toggle. Bulk
  enable doesn't surface it.
- **Per-reload cost on tree games.** A reload on a game with extra trees runs a second `BuildModList`.
- **OneDrive placeholder files.** The cross-volume fallback refuses them.
- **A same-length rewrite.** `SafeMove`'s size check doesn't catch a file rewritten to the same length
  mid-move.
