Elden Ring's own regulation.bin stays put, Cyberpunk's standalone .xl mods and Fallout 4 / Starfield's .ba2 archives finally move with the mods they belong to, and the ban-risk warning says what is at stake without guessing why.

## What's new

**Elden Ring's own regulation.bin stays put**

- Fixed Disable all and Return to vanilla moving Elden Ring's own `Game/regulation.bin` aside, which left the game unable to start until you turned it back on.
- A `regulation.bin` now counts as a mod only when 626 installed it (an install record, or 626 replaced the original and kept it). Otherwise it is the game's file: GAME FILE chip, switch locked on, skipped by Disable all, loadouts, profiles, Return to vanilla and the agent tool.
- Installed a modded `regulation.bin` by hand? It now reads as the game's file. Drop that mod onto 626 once and its switch comes back.
- Until then, Return to vanilla lists it under STILL IN PLACE and says so plainly: verify the game's files in Steam before playing online. It never claims a vanilla it can't vouch for.
- A copy an older build already moved aside still lists and can be turned back on. If Steam has since put a fresh one in place, 626 says which file is the game's and where the old copy is held, instead of a dead-end "turn that copy off".

**Cyberpunk 2077: standalone ArchiveXL mods**

- A mod that is just a `.xl` file, or an `<X>.archive.xl` with no `<X>.archive`, now lists as its own row and turns off and on like any other mod.
- A plain `Foo.xl` next to `Foo.archive` rides with the `Foo` row. Paired `.archive.xl` files are unchanged.
- A `.xl` left live never hides a copy of the same mod that is already turned off; that row lists as off and turning it on reunites the pair.

**Fallout 4 / Starfield: .ba2 archives move with their plugin**

- `<Plugin> - Main.ba2` and `<Plugin> - Textures.ba2` now ride on their plugin's row, so turning a mod off takes its archives with it instead of leaving them loading.
- The longest plugin name wins (`MyMod - Plus - Main.ba2` belongs to `MyMod - Plus.esp`, not `MyMod.esp`), plugins you turned off still count, and every archive has exactly one owner.
- The game's own archives (`Fallout4 - *.ba2`, DLC, Creation Club, Starfield's base archives) are game files and never move.
- Skyrim's `.bsa` behaviour is unchanged.

**The ban-risk warning says the risk, not a mechanism**

- Every BAN RISK surface used to say "This game uses anti-cheat". Some flagged games have none and ban modded online play by policy alone. It now reads "Modding this game for online play can get your account banned." The same goes for the save-change prompt.

**Smaller fixes**

- Enable all keeps going past a mod it can't turn on, and the status line names the ones it skipped.

## Under the hood

- **regulation.bin provenance (#397).** `BaseGameFiles.IsDirectInjectGameFile` with a lazy `Placed` reader over install records and both replaced stores; without provenance `DirectInjectListing` sets `Mod.IsBase`, so the #386 guard applies everywhere. Backstops in `RestorePointEngine.PlanVanillaMoves` and `ModToggle.SetDirectInjectEnabled`; the bulk loop moved into Core as `ModToggle.SetDirectInjectAll`. Return to vanilla writes a `leftInPlace` note for an unprovable game file; its wording is frozen because sealed manifests are matched by it.
- **Standalone sidecars (#396).** `StandaloneSidecars` lists `.xl` files no mod claims; `ModKey` split into `ModKey` + `GroupKey`; held names are read once per scan so a live file never hides a held copy.
- **Archive ownership (#398).** `PluginArchives.Ownership` settles every archive's owner once per folder over live and held plugins: on a 600-plugin, 1,200-archive fixture the scan is 15 ms (an intermediate version was 8.4 s and never merged). A 20,000-case fuzz against the slower rule found no differences apart from one overlapping-separator case, which is now fixed and pinned.
- **Review.** Every file-moving change went through `reversibility-auditor` and `core-purity-reviewer`. The audits sent these three PRs back six times between them, and none of what they caught shipped.
- **CI.** The release test gate now runs on every PR and push to master (#390).
- 3,866 Core tests passing; the UI Automation smoke harness ran 42 of 42 on the merged code.

## Known issues / not yet

- Dropping a Fallout 4 zip onto 626 still leaves out its `.ba2` files, and a Cyberpunk zip its standalone `.xl`. The scanner knows them now; install doesn't yet.
- Turning off a `regulation.bin` 626 installed leaves the game without one until you turn it back on (the displaced original isn't swapped back in).
- 626 doesn't write `Plugins.txt` yet, so Bethesda load order is refused rather than managed.

**Full Changelog**: https://github.com/estevanhernandez-stack-ed/626-mod-launcher/compare/v0.24.0...v0.25.0
