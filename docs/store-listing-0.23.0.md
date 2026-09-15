# Microsoft Store submission — 0.23.0.0 (games from the EA app)

> **What a reviewer will notice, said up front:** the app now reads the EA app's install records
> (registry keys under `HKLM\SOFTWARE\{EA Sports, EA Games, Electronic Arts}` and each install's
> `__Installer\installerdata.xml`) and hands Play for those games to the EA app through an
> `origin2://` link. Both are read-and-hand-off. The reviewer letter leads with exactly what is read
> and what is launched.
>
> **No new declared capabilities, no new network endpoints, no new data collection.** `runFullTrust`
> remains the only capability, verified out of the bundle below.
>
> **Package to upload:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.23.0.0_Store_Test/
>             ModManager.App_0.23.0.0_x64_Store.msixbundle
> Size      : 83.6 MB
> SHA-256   : 50A14E6493B63A38999E1A78773719DDB144B6B5045EFB395CA0BAD0A7B05A94
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.23.0.0            (application x64)
> Capability: runFullTrust  — the only one declared
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 2614 passing
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> ```
>
> Identity, version and capability above were read **out of the bundle**, not off the manifest on disk.

---

## What changed for a user

**Games from the EA app.** The launcher finds games installed through the EA app, starting with EA
SPORTS College Football 27 and Madden NFL 27. They appear with the other installed games not added yet,
add in one click, and Play hands the launch to the EA app. Only games the launcher's signed
game-definition list already knows are offered, so an unknown EA title is never guessed at. These two
run EA's kernel-level anti-cheat and the tools that mod them replace EA's anti-cheat launcher, so the
launcher does not turn mods on or off for them yet and says so in one sentence wherever a mod action
would otherwise be offered. Verified on the maintainer's machine: both found, both added, Play started
each through the EA app, nothing written under the EA install folders.

**Anti-cheat warnings follow the game, not the store it came from.** Ban risk used to resolve by Steam
app id alone, so a game added from anywhere else showed no warning, no prompt, and no refusal to the
agent tool.

**Changing a save on a game with anti-cheat asks first.** Editing a character, installing a save mod,
and bringing a save in from a backup now ask every time until the user ticks *don't ask again* for that
game. Putting a save back never asks. **This is new friction on a shipped feature:** Elden Ring players
who use the character editor will see the prompt.

**Turning a mod on or off never deletes a held copy.** With a fresh install of a mod in place and an
older copy turned off, turning either one on or off could delete the older copy during cleanup. It now
refuses, moves nothing, and names the files in the way.

**Only the anti-cheat warning is red.** A launch option to set is amber and a missing framework is blue,
so the one warning that can cost an account stands out.

## What's new in this version

Paste [`store/whats-new-0.23.0.txt`](store/whats-new-0.23.0.txt) — **1,120 of 1,500 characters**,
opening with the `Version 0.23.0` header. Plain text, written for a shopper.

**The groundwork line is the owner's, and it is worded to promise direction, not a date or a
feature.** "This lays the groundwork for mod support on these two" says what this release is for; the
next sentence says mod switching is not here yet. It does not say mods *will* work on these games: they
run EA's kernel anti-cheat, and whether the launcher can offer anything there without replacing EA's
anti-cheat launcher is still an open question in the EA football plan.

## Short description and description

- [`store/short-description-0.23.0.txt`](store/short-description-0.23.0.txt) — **238 of 270**, carried
  forward unchanged. Still accurate; nothing in this release earns displacing a clause.
- [`store/description-0.23.0.txt`](store/description-0.23.0.txt) — **4,511 of 10,000**. Three list
  lines extended, none added: installed-game detection now names the EA app; reversible toggling adds
  "never overwrites a copy it is already holding"; the anti-cheat line adds changing a save and saying
  plainly when a game's mods are not managed at all.

**Not changed: the search terms.** Still the seven from 0.8.1.

## Product features

[`store/product-features-0.23.0.txt`](store/product-features-0.23.0.txt) — **20 of 20**, longest
**162 of 200**. Extension at no cost, no displacement: the same three facts land inside lines that
already existed (installed games, reversible toggling, the anti-cheat warning).

## Certification notes

Paste [`store/reviewer-letter-0.23.0.0.md`](store/reviewer-letter-0.23.0.0.md)'s letter block into Notes
for certification. It leads with the EA app: what registry keys and file are read, that nothing is
written or started, and the `origin2://` hand-off. Then the new save prompt, the held-copy fix, and what
the release does not add.

## Age rating

Unchanged. The EA app games add no displayed user-generated content.

## Screenshots

**All ten carried forward from `docs/store-assets/screenshots-0.22/`.** No screen a shopper sees in the
existing set changed in a way that misleads. Known gap, stated: there is no shot of an EA app game row
or of the save prompt. A recapture belongs to the next listing refresh rather than blocking this one.

## Store half of the release

Follows `store-listing-console/docs/release-loop.md` — **one owner per section**:

1. Copy lands in `store-listing-console/apps/mod-launcher/copy/` (what's-new, listing copy, reviewer
   letter) and `config.toml` bumps to `0.23.0.0` with this package path. `console.py --json`, then
   `submit_write.py plan` against the published listing. Read the plan.
2. On the owner's word: `apply` (text) and `captions`, both read back; then `verify`.
3. **The owner's pages only:** Packages (upload the bundle above), Notes for certification (the letter),
   and Properties (the support address). **Do not open the Store listing page.**
4. `verify` again (the package line still shows the old package until the commit — expected), then
   `commit` from the console on the owner's word, then `verify` once more to see 0.23.0.0.

## Build procedure

1. Confirm the submodule pin: `git -C external/626-mod-plugins describe --tags` → `nexus-v0.14.0`.
2. Wipe `src/ModManager.App/AppPackages/`.
3. `Version="0.23.0.0"` in `src/ModManager.App/Package.appxmanifest` (done in this commit).
4. `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64`
5. `pwsh scripts/check-store-seal.ps1` → `STORE seal OK ... Nexus compiled in.`
6. Read identity, version and capability out of the bundle; hash it.

## Known gaps, stated rather than hidden

- **The add-game dialog does not list EA app games.** They are offered at the bottom of the library
  home instead. The owner looked for them under + Game first; it is first in the next EA slice.
- **Save snapshots for these two games are not wired.** Their save files have no extension and the save
  listing matches extensions. Next slice.
- **No screenshot shows an EA game or the save prompt.** See Screenshots.
- Carried over from earlier submissions and unchanged here: the same-filename-in-two-mod-folders
  scanner collapse, and installing a framework over one in a different layout producing two copies.
