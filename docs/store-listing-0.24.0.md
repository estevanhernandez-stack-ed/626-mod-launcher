# Microsoft Store submission — 0.24.0.0 (the game's own files stay put)

> **What a reviewer will notice, said up front:** two behaviours a tester can meet without looking for
> them. Close to tray (Settings, off by default) leaves an Open / Quit icon in the notification area
> when the window closes. Uninstall deletes files, after a confirm that names every one of them, with
> Cancel as the default. The letter leads with both. Everything else makes the app touch less.
>
> **No new declared capabilities, no manifest extensions, no new network endpoints, no new data
> collection.** `runFullTrust` remains the only capability, verified out of the bundle below.
>
> **Package to upload:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.24.0.0_Store_Test/
>             ModManager.App_0.24.0.0_x64_Store.msixbundle
> Size      : 83.7 MB
> SHA-256   : FDD0EF2F017145D5A250A5303E07AE79D0EBBA7B5FEE1F849CED81C96EEA2B5D
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.24.0.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 3797 passing (Core), 42 of 42 UI Automation smoke checks
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Source    : v0.24.0 (37090b5) plus the version bump in this commit
> ```
>
> Identity, version and capability above were read **out of the bundle**, not off the manifest on disk.

---

## What changed for a user

**The game's own files are never turned off.** Disable all, loadouts, profiles and Safe Clear skip a
Bethesda game's masters, DLC, Creation Club content and their archives, and base paks in an Unreal
game's `Content/Paks`. 0.23.0.0 could move `Skyrim.esm` into holding and stop the game launching. A game
file already in holding still lists and switches back on.

**Load order never renames Bethesda plugins.** Reorder refuses there and says why. 626's own numbered
prefixes are undoable on every game through a LOAD ORDER chip.

**Return to vanilla means vanilla.** Safe Clear turns off and holds the mods it can prove are mods, and
Restore puts them back. What it cannot prove is left in place and named.

**Mods turn off in every folder they use.** Cyberpunk 2077 mods used to lose only their `.archive`.

**Uninstall names what it deletes**, held copies included, with Cancel as the default.

**Also:** holding folders for names Windows aliases (`Foo.`, `CON`), the OLD LOADER warning, Windrose
save mods, wrong-folder restores refused, EA app saves listed and backed up but never written, one
library list, close to tray, every mod location editable, a live Nexus name-search fallback in
discovery.

**Not in the Store build:** the agent tool (MCP) work. The server is not in the package (the bundle
holds `ModManager.App.exe` and the Windows App SDK's `RestartAgent.exe`, nothing else). Bringing it into
the Store package is a separate decision, tracked as its own dashboard task.

## What's new in this version

Paste [`store/whats-new-0.24.0.txt`](store/whats-new-0.24.0.txt) — **1,076 of 1,500 characters**,
opening with the `Version 0.24.0` header. Plain text, written for a shopper. It leads with the Bethesda
fix because that is the one a 0.23.0.0 user may already have hit.

## Short description and description

- [`store/short-description-0.24.0.txt`](store/short-description-0.24.0.txt) — **238 of 270**, carried
  forward unchanged.
- [`store/description-0.24.0.txt`](store/description-0.24.0.txt) — **4,641 of 10,000**. Three list lines
  extended, none added: reversible toggling adds "never turns off the game's own files"; load order adds
  undoing its own renaming and never renaming Bethesda plugins; the attention row adds outdated loaders.

**Not changed: the search terms.** Still the seven from 0.8.1.

## Product features

[`store/product-features-0.24.0.txt`](store/product-features-0.24.0.txt) — **20 of 20**, longest
**173 of 200**. The same three facts as the description, inside lines that already existed.

## Certification notes

Paste [`store/reviewer-letter-0.24.0.0.md`](store/reviewer-letter-0.24.0.0.md)'s letter block into Notes
for certification (3,986 characters; 0.23.0.0's was 4,632). It leads with close to tray and uninstall,
then what the app now touches less, saves, and what the release does not add.

## Age rating

Unchanged. Nothing in this release displays user-generated content.

## Screenshots

**The seven published screenshots carry forward** (`01-library-home` to `07-saves-snapshots`; 08-10 from
`screenshots-0.22/` were never uploaded). Nothing a shopper sees in the set
changed in a way that misleads. Known gap, stated: no shot of a GAME FILE chip, the OLD LOADER chip or
close to tray. A recapture belongs to the next listing refresh.

## Store half of the release

Follows `store-listing-console/docs/release-loop.md` — **one owner per section**:

1. Copy lands in `store-listing-console/apps/mod-launcher/copy/` (what's-new, listing copy, reviewer
   letter) and `config.toml` bumps to `0.24.0.0` with this package path. `console.py --json`, then
   `submit_write.py plan` against the published listing. Read the plan.
2. On the owner's word: `apply` (text) and `captions`, both read back; then `verify`.
3. **The owner's pages only:** Packages (upload the bundle above), Notes for certification (the letter),
   and Properties (the support address). **Do not open the Store listing page.**
4. `verify` again (the package line still shows the old package until the commit — expected), then
   `commit` from the console on the owner's word, then `verify` once more to see 0.24.0.0.

Submission `1152921505702151373`: apply, captions and verify green on 2026-10-16, before Partner
Center was opened. Then, once the public catalog serves 0.24.0.0, publish the GitHub draft `v0.24.0` retitled
`v0.24.0 — Live on the Microsoft Store`.

## Build procedure

1. Confirm the submodule pin: `git -C external/626-mod-plugins describe --tags` → `nexus-v0.14.0`.
2. `Version="0.24.0.0"` in `src/ModManager.App/Package.appxmanifest` (done in this commit).
3. `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64`
4. `pwsh scripts/check-store-seal.ps1 -SkipBuild` → `STORE seal OK ... Nexus compiled in.`
5. Read identity, version and capability out of the bundle; hash it.

## Known gaps, stated rather than hidden

- **Elden Ring's own `Game/regulation.bin` is detected as a mod by name**, so Return to vanilla moves it.
  Open, high priority, next fix.
- **No `Plugins.txt` writer.** Bethesda load order is refused rather than managed.
- **Fallout 4 / Starfield `.ba2` archives are not listed as mod files**, so a mod's `.ba2` stays live
  when it is turned off.
- **Cyberpunk standalone ArchiveXL `.xl` mods are not listed** as their own rows.
- **No screenshot shows the new chips or the tray.** See Screenshots.
