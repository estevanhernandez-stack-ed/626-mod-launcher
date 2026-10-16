The game's own files stay put: Disable All, loadouts and load order can no longer break a Bethesda game, "Return to vanilla" actually gets you there, and a Cyberpunk mod turns off in every folder it uses.

## What's new

**The game's own files are never turned off**

- Fixed Disable All moving `Skyrim.esm` into holding in 0.23.0, which left the game unable to launch until you turned it back on.
- Skyrim, Fallout, Starfield and Oblivion masters, their DLC, your Creation Club content (read from the game's own `.ccc` list) and the archives that belong to them now show a GAME FILE chip, and the switch stays locked while they're on.
- Disable All, loadouts and profile loads skip them. A single toggle or uninstall says "Skyrim.esm is part of the game, so 626 won't turn it off." Safe Clear leaves them alone too.
- Base paks in an Unreal game's `Content/Paks` get the same protection. Real mods in the same folder toggle exactly as before.
- Already hit by the old bug? A game file sitting in holding still lists and can always be switched back on. Once it's on, it locks.

**Load order never renames Bethesda plugins**

- Reorder on a Bethesda game no longer renames `MyMod.esp` to `0010__MyMod.esp`, which broke every plugin and save that lists it as a master. It refuses before touching anything and tells you why: Bethesda load order lives in `Plugins.txt`, and 626 doesn't edit that yet.
- 626's own load-order prefixes can now be undone, on any game. A LOAD ORDER chip appears when they're on disk, and Undo puts the original names back.
- Undo only strips exactly what 626 wrote (`0010__`, `0020__`, and so on), so an author's own `10__thing.pak` is left alone. It moves, never deletes, never overwrites an existing name, and skips folders another tool manages.

**Return to vanilla means vanilla**

- Safe Clear's "Return to vanilla" now turns off and holds every mod 626 can prove is a mod. Before, it ran the framework and loader steps and left your mods live in the game folder.
- 626 only sweeps folders it knows hold nothing but mods, and only turns off a mod elsewhere when it can prove it installed every one of its files. Anything it can't tell apart from the game's own files is left where it is.
- When it leaves something in place, the off-boarding sheet names the folder, says what happened in counts, and doesn't claim vanilla.
- Restore puts all of it back, byte for byte.

**Mods turn off in every folder they use**

- Turning a mod off now moves its files in every folder it uses, and turning it on brings them all back. A Cyberpunk mod used to lose only its `.archive`, while its redscript, tweaks and CET scripts kept loading.
- The row says what's going on: "Also has files in ...", "Also turned off in ...", or "Files in ... are still on.", with the reason when a folder was held back.
- Covers the row toggle, Enable all / Disable all, loadout modes, profile loads and the agent tool. Frameworks like ArchiveXL, TweakXL and Codeware never move.
- Bulk toggles on these games read the mod list once per operation instead of once per mod, so Disable All on a big Cyberpunk load order no longer slows down with the square of the mod count.
- Fixed loading a profile on a direct-inject or Mod Engine 2 game changing nothing.

**Uninstall says what it will delete**

- The uninstall confirm now names everything it will remove, including files 626 is holding in other folders, and Cancel is the default.
- Fixed uninstalling a mod named `cool.` or `cool ` deleting `cool`'s turned-off copy.

**Holding folders that can't collide**

- A mod named `Foo.`, `Foo ` or `CON` now gets its own holding folder. Windows quietly drops trailing dots and spaces and treats `CON`, `NUL` and friends as devices, so turning one off could land its files in another mod's holding or make them vanish from the list.
- Ordinary names are unchanged, so every holding folder you already have keeps working.

**OLD LOADER warning**

- A new OLD LOADER chip warns when a version-locked loader (REFramework, Elden Mod Loader, UE4SS, Mod Engine 2) is older than the game's executable. That's the shape behind Monster Hunter Wilds crashing on start after a patch.
- It names both dates and the release page. Mark as checked hides it until the next patch rewrites the game.
- Frameworks installed through 626 now keep their release dates instead of the install time, so the comparison means something.

**Saves**

- Windrose save mods install. Before, every install stopped at "Multiple save profiles found."
- A world the game has already imported and you've played is never installed over again.
- Restoring a snapshot into a different folder than it was taken from is refused before anything changes. Before, it could empty the target and unpack a whole profile one level too deep.
- Reinstalling a save mod over an existing world is refused before it writes, instead of leaving a mix of two versions.
- Reset keeps working after you delete the download, and two worlds whose zips share a file name keep their own copies.
- The Saves dialog's Reset and Remove now tell you how to undo when the snapshot lives outside the Saves list.

**EA app games**

- College Football 27 and Madden NFL 27 saves are listed by name and backed up, and 626 never writes them. The EA app syncs those folders to EA's cloud, so restore, clone and save mods are turned off for these two and the dialog says so.
- If you added either game in 0.23.0, its saves now show up. The folder stored back then was the parent of the real saves folder.
- Ban risk now follows the EA copy of a game you already own on Steam. Before, that second copy read no risk: no chip, no prompt, no save-write gate.

**Library and setup**

- The library home is one list. Installed Steam and EA games you haven't added sit beside your managed ones, marked "Not managed by 626", with Play through the store and Start managing. Listing or playing them writes nothing.
- Close to tray. Off by default; turn it on under Settings > Appearance and closing the window keeps 626 in the notification area.
- Every mod location in game setup is editable now, not just the first, through a "Which mod folder" picker.
- Game setup labels a folder the game's definition corrected as "corrected from ..." instead of calling it undeclared, and a corrected folder that doesn't exist yet no longer raises the SETUP chip.
- If 626 closes partway through moving a game's data folder, the next launch finishes the move or tells you exactly which two folders to look at. It never guesses.
- Second store copies and older registrations now pick up the game definition's fixes to mod paths, extensions and grouping.

**Finding your mods**

- Discovery falls back to a live Nexus name search when the name index can't place a file, up to 50 per add and 200 per "Identify my mods" run. Whatever it didn't get to stays listed and the result line says so.
- When the file-hash match and the name search find the same mod, the review shows one ticked row and links the other, and "Apply N" counts what will actually land.

**Agent tool (MCP)**

- Every tool in the original catalog now exists. New write tools:
  - `apply_loadout` and `list_profiles`.
  - `dry_run_intake` and `intake`, on the same drop router the app uses, so a dry run describes the drop the app would actually do.
  - `register_game` and `uninstall_mod` (needs `confirm: true`, and lists what it would delete without it).
  - `install_save_mod`, `reset_save_mod` and `remove_save_mod`.
  - `apply_theme`. A running launcher switches theme live when it changes on disk.
- New reads: `list_themes`, `get_app_settings` (describes the Nexus sign-in, never returns a token) and `list_save_mods`.
- Every write keeps the app's gates: a high-risk game the user hasn't acknowledged refuses the whole batch, frameworks install only with `allowFrameworks`, and every call is audited.
- `set_mod_enabled` and `uninstall_mod` return `refusal: "game_file"` for the game's own files. `list_mods` exposes `isBase`, and `get_game_shape` reports `correctedFrom` plus the OLD LOADER note.

## Under the hood

- **Base game files (#386).** One rule, `BaseGameFiles`: a known Creation Engine master list, the game's `.ccc` file, archives owned by a base plugin (only on archive-only rows, so `Dawnguard - Fixes.esp` + `.bsa` stays a mod), and the existing base-pak predicate. `Mod.IsBase` is set once when the list is built and `DisableEntry` refuses it, so every lane gets the guard at one choke point. Live-smoked on a fixture Skyrim as the new harness case `game-files-stay-on-through-disable-all`.
- **Load order (#387).** `LoadOrderSupport` gives each game prefix-rename, config (Mod Engine 2) or not-supported, and plugin-shaped games are not-supported. `Scanner.PostureOf` is now the one ownership rule for scan, apply and undo, after review caught undo and apply disagreeing about a Vortex folder with no marker. Mirrors stay in lockstep, and `loadorder.json` clears only when every rename lands.
- **Return to vanilla (#385).** Vouch by allowlist or proof of ownership, never by an exclusion list. Every exclusion list leaked under adversarial review, including a first sweep that would have moved `Skyrim.esm` and Total War's `data.pack`. New manifest field `modPathModOnly`, bound to its own `modPath` and refused on base-content and executable folders; set on Cyberpunk in the embedded snapshot. Real paths resolve through `GetFinalPathNameByHandleW` before any containment check. The restore-point manifest goes to schema 2, and install records gain `locationPath`. Verified against a byte-exact replica of a 194-mod, 2,527-file Cyberpunk setup with the live signed feed. [Spec](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/v0.24.0/docs/superpowers/specs/2026-10-02-safe-clear-holds-mods-design.md).
- **One mod, many trees (#369, #370, #374, #382).** New manifest field `extraModTrees`, gated per tree with one verdict on every OS, since the miner signs on Linux and the launcher reads on Windows. The miner now fails the build on a curated tree the gate would drop. Held extras go to `disabled-trees/<Mod>/<tree>/<entry>`, so the folder layout is the record and there's no new JSON. `SafeMove` leaves a source byte-identical on a failed cross-drive move and refuses junctions and symlinks. `Scanner.BulkScope` takes Disable All on a 200-mod fixture from 201 list builds to 1. Cyberpunk's trees ship in the embedded manifest, so a fresh or offline install gets this on first launch. [Spec](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/v0.24.0/docs/superpowers/specs/2026-10-02-one-mod-many-trees-design.md) / [plan](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/v0.24.0/docs/superpowers/plans/2026-10-02-b4-multi-tree-toggle.md).
- **Uninstall and holding names (#376, #378).** `ModUninstall.Preview` is read-only and drives the confirm text. `LinkSafeDelete` removes a junction as a link and never touches its target. `HoldingName.Folder` encodes only risky names, as `~626~` plus hex, and `ModName` is its exact inverse, so there's no migration. `HoldingName.LegacyPath` still reaches an older build's `disabled/CON`. The new harness case `alias-names-hold-apart` fails on pre-fix master and passes here.
- **Ban-safe loaders in the feed (#355, #356).** Which loaders are ban-safe, and how to spot and fetch them, moved from compiled code into a top-level `loaders` list in the game manifest, so the feed can offer a safe loader for a newly flagged game without a launcher release. Exe names must be bare `*.exe` filenames and `getUrl` must be `https`. Loaders can pin by manifest game id (`gameIds`), so they reach EA app games, and a loader carrying a field this binary doesn't know is skipped rather than treated as engine-wide. The two had to ship together, which is why they land in the same release. [Spec](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/v0.24.0/docs/superpowers/specs/2026-10-01-safe-loaders-in-the-feed-design.md).
- **Store identity joins (#357, #358, #359, #362).** `BanRiskCatalog.Effective` resolves EA content ids inside its one cached snapshot and gains a floor by EA content id for the two football titles. `ManifestIdLookup.EntryFor` joins a registration by Steam id, then EA content id, then its own id, so `<id>-2` copies and old name slugs get manifest corrections. Save lookups take the whole `GameEntry` and fail closed on a store-id contradiction, so a `doom` slug never gets Doom's save folder. New `GameEntry.manifestId` records the entry a game was added as, for store-less copies.
- **EA saves (#360, #361).** `SaveWritePolicy` refuses every write into an EA app game's save folder, and `SaveWriteCallSiteTests` holds every App save-write call site to it; it caught three real gaps on its first run. `SaveDirRefresh.Narrowed` swaps a stored parent folder for the curated one at read time only, and never over a folder you picked.
- **Registry and settings locks (#353, #364, #375).** `RegistryStore.Update` loads, changes and saves `games.json` as one step under an in-process lock plus a `games.json.lock` file, so a second launcher waits. Without the lock, the 24-writer concurrency test lost writes 3 runs out of 3. `DataDirMoveJournal` writes a breadcrumb before a data-folder move and `RecoverInterruptedMoves` resolves it at the next launch. `AppSettingsFile.WriteKey` is now the single locked, atomic writer for `app-settings.json`, shared by the app and the agent.
- **Saves (#379, #380, #384).** Kept zips move to `<dataDir>/save-mods/<world>/<zip>`, and `SaveModStore.KeptZip` still finds an older build's copy. Every snapshot records its source folder in the zip comment, and restores refuse a mismatch. `SaveModInstaller.PreflightInstall` decides an install read-only, before the ban-risk question.
- **Old loaders (#383).** `StaleLoaders` compares a version-locked loader's date against the game build: the newest `.exe` within 10% of the largest one's size, so the anti-cheat swap's same-size old copy of `eldenring.exe` isn't taken for it. Mark as checked stores `loaderCheckedExeUtc`.
- **Agent surface, E1 (#369, #371, #372, #373, #375, #379).** `DropRouter.Plan` decides every route of a drop without writing, and `MainViewModel.AddModsAsync` now acts on its plan, so the app and `dry_run_intake` can't drift. Engine and mod-folder detection (`ModLocator`, `LaunchScan`, `EngineScan`) and game add / re-detect moved into Core. `catalogVersion` is 6. Still open: the consent surface and the development posture.
- **Discovery (#363, #367).** `LooseIdentify.SearchEachAsync` is the one capped engine behind both name passes, and only names you approve grow the per-game index. `AdoptionReviewText` gives both review dialogs one spelling.
- **Registration-repair smoke (#381).** All 15 repair steps were driven live through UI Automation and found the two setup bugs fixed above. `DiscoverySweep.ModPathsFor` makes the sweep read the same corrected folders as the listing.
- **Build hygiene (#366).** All 33 `[ObservableProperty]` fields are partial properties, and the App builds with warnings as errors again.
- 3797 Core tests passing, 2 skipped, at the cut. The full smoke harness ran 42 of 42 green before it.

## Known issues / not yet

- 626 doesn't write `Plugins.txt` yet, so there's no real load order for Bethesda games. Reorder refuses there instead of renaming.
- Fallout 4 and Starfield `.ba2` archives aren't listed as mod files.
- Elden Ring's own `Game/regulation.bin` is still detected as an enabled mod by name, so Return to vanilla moves it.
- A Bethesda game registered under a different engine gets no game-file protection.
- Load-order prefixes on files sitting in holding aren't in the Undo plan. Turning one back on brings the chip back, and a second Undo clears it.
- The Safe Clear sheet matches mods by name only, so two same-named mods in different locations can collide on it.
- Save writes for College Football 27 and Madden NFL 27 aren't built. Backups only, for now.
- "Identify my mods" doesn't remember which finds it already searched, so a second run asks Nexus about the same first 200.
- Two ordinary mod names that differ only by case, from different mod locations, still share a holding folder.
- Dates aren't versions: a fresh game install after you downloaded a loader looks the same as a patch to OLD LOADER. Mark as checked covers it.

**Full Changelog**: https://github.com/estevanhernandez-stack-ed/626-mod-launcher/compare/v0.23.0...v0.24.0
