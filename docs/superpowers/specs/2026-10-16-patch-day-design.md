# Patch Day: a game updated, its loader didn't, and the player keeps playing

**Date:** 2026-10-16 · **Status:** design, approved section by section by Este; written spec for review.
**Research:** `docs/superpowers/research/2026-10-16-backlog-and-top-three.md` (bet 1).

## Why

The loudest modder pain we found: a game patch breaks the mod loader or script extender, and nobody
says when it will be fixed. Fallout 4 Next Gen broke F4SE for weeks, Skyrim AE broke SKSE, BG3 patches
broke Script Extender, and Cyberpunk players "assume all mods are broken" after every update. The fixes
people use are folk remedies: turn off Steam auto-update, roll back through the Steam console, copy the
whole game folder before patching.

626 already knows both halves of the problem. The Steam build-update banner knows the game changed
(`lastKnownSteamBuildId`, `SteamBuildCheck.Evaluate`). The OLD LOADER chip knows the loader predates the
game executable (`StaleLoaders`). It can set mods aside and bring them back (`VanillaLaunch`) and save
the whole setup (`RestorePointEngine`). Patch Day joins them into one honest flow.

## What success looks like

After a patch, a player never meets a crash or a silent mod failure through 626 without first being
told why and offered a one-click way to keep playing. Every change to their files is a button they
press, and every one can be undone.

## Decisions (Este, 2026-10-16)

1. **Warn and offer, never act.** Nothing changes until the player clicks, the same law as the ban-risk
   gate.
2. **Trigger:** Steam's build id moved since the last modded check **and** a version-locked loader is
   dated before the new game executable. Non-Steam games keep today's OLD LOADER chip unchanged.
3. **Release watch through the signed feed**, not per-user calls to GitHub.
4. **Automatic restore point when the patch is detected**, before the player touches anything.
5. **Play vanilla lasts until they bring the mods back.** Nothing returns on its own.
6. **Shape:** one Patch Day state per game, evaluated in Core, shown as a state-strip chip and checked
   again at Play (approach A).

Also settled by an existing rule: **626 does not download the loader.** The privacy policy says the
app doesn't download mods. "Get the update" opens the release page; the player drops the zip and the
existing framework intake (validate, snapshot, install) handles it.

## Version-locked loaders in scope

The set `StaleLoaders` already treats as version-locked: **REFramework**, **Elden Mod Loader**,
**UE4SS** and **Mod Engine 2**. BepInEx is not version-locked there and is out of scope. Of the four,
REFramework, UE4SS and Mod Engine 2 are GitHub-hosted and get a release watch (below). Elden Mod Loader
is Nexus-hosted and gets detection and vanilla, but no "update available" in this cut.

## 1. States and transitions

`PatchDay.Evaluate(...)` is a pure Core function over: the game's stored baseline build and `patchDay`
record, the live Steam build id, the `StaleLoaders` report, the feed's `latestRelease` for the loader,
and the vanilla-launch mode. It returns one state:

| State | When | Chip text | Actions |
|---|---|---|---|
| **Quiet** | No build change, no version-locked loader, or the loader is newer than the game exe | none | none |
| **Patched** | Build id differs from the baseline AND a version-locked loader is dated before the new game exe | "<Game> updated on <date>. Your mod loader is from before that update and probably won't load yet." | **Play vanilla for now** · **Play modded anyway** · **Tell me when it's fixed** |
| **Playing vanilla** | Player chose vanilla; mods set aside via `VanillaLaunch` | "Your mods are set aside since the patch. Bring them back any time." | **Bring my mods back** |
| **Update available** | Feed's `latestRelease.publishedUtc` is after the installed loader file's date | "<Loader> has an update since the patch." | **Get the update** (opens release page) · **Bring my mods back** |
| **Loader updated** | The loader file on disk is now newer than the game exe | "Your loader is newer than the game now. Bring your mods back?" | **Bring my mods back** |

Transitions:

- **Entering Patched** takes the automatic restore point and writes the `patchDay` record.
- **Play vanilla for now** steps mods aside with `VanillaLaunch.StepAsideAsync`, records
  `choice: vanilla`, and launches vanilla.
- **Play modded anyway** records `choice: modded`, shrinks the chip to a quiet note, and launches
  modded. Launch verification then reports whether the mods loaded, so the choice is informed.
- **Tell me when it's fixed** sets `snoozedUntilChange`. The chip hides until the state changes (feed
  shows a newer release, or the loader file changes).
- **Bring my mods back** runs `VanillaLaunch.RestoreAsync`, clears the `patchDay` record, and moves the
  baseline to the current build. The game returns to Quiet.
- **Nothing moves a game between states on its own** except detection and the feed. Every file change
  is a click.

## 2. Data

**`GameEntry` in `games.json`** (camelCase, round-trip test, added to the camelCase rule's surface list):

- `lastKnownSteamBuildId` already exists and is reused as the baseline ("the build the mods were last
  checked against"). No second baseline.
- New optional `patchDay` record, present only while a patch is in play:
  - `buildId`: the new build detected
  - `detectedUtc`
  - `restorePointId`: the automatic snapshot, or null if it could not be taken
  - `choice`: `none` | `vanilla` | `modded`
  - `snoozedUntilChange`: bool
- Absent record means Quiet. No migration.

**The Steam update banner folds in.** On a game with a version-locked loader, the PATCH DAY chip
replaces the banner. On a game without one, the banner stays exactly as it is today.

**Signed feed, `loaders` entries** gain an optional field:

```json
"latestRelease": {
  "version": "v1.5.9.2",
  "publishedUtc": "2026-10-17T03:12:00Z",
  "url": "https://github.com/praydog/REFramework/releases/tag/v1.5.9.2"
}
```

`ManifestValidator` gates it: `url` must be https; `publishedUtc` may not be in the future. A bad
value drops the field and keeps the rest of the entry. Descriptive only: it never tells the app to
install anything. `LoaderManifestEntry` gains the property, serialized with `ManifestJson.Options`
(camelCase), with a round-trip test.

Nothing else on disk changes: restore points, the vanilla stash and holding folders stay where they are.

## 3. What the player sees

- **Game view:** a PATCH DAY chip in the state strip, amber tone, ordered right after BAN RISK (which
  stays first and the only red chip). Id `StateChip.patch-day` through `GameStateChip.Id`; the existing
  singletons `StateChipDetail`, `StateChipAction`, `StateChipDismiss`. Where a state has a second action,
  it sits in the detail row as its own control with its own `AutomationId` (`PatchDaySecondaryAction`).
- **At Play:** if the state is Patched and `choice` is `none`, Play opens one small dialog with the same
  sentence and the same three actions (`PatchDayDialog`, `PatchDayPlayVanilla`, `PatchDayPlayModded`,
  `PatchDaySnooze`). Once a choice is recorded, Play does that without asking.
- **First Patched chip** also says: "If you launch from Steam, your mods load as they are." 626 can't
  intercept launches outside itself.
- **Library home row:** "Updated since your last modded launch" while Patched; "Playing vanilla since
  the patch" while vanilla.
- **Agent tool:** `get_game_shape` gains a read-only `patchDay` block (state, detected build, choice,
  loader and its latest release if known). No new write tools.

Not in this cut: a Windows notification from the tray on detection; Nexus-hosted loader watch.

## 4. The release watch (626-game-manifest CI)

- A scheduled job in the `626-game-manifest` repo (daily, plus manual dispatch) asks GitHub's releases
  API for the newest non-prerelease release of each loader whose `getUrl` is a GitHub releases page
  (REFramework, UE4SS, Mod Engine 2) and writes `latestRelease` into the generated feed before signing.
- `tools/ManifestMiner` owns the field, so it passes the same validator and signature as everything
  else. Uses the Actions token; never runs on a player's machine.
- **Wording discipline:** a newer release does not prove it supports the new game build. The chip says
  "has an update since the patch", never "fixed". The only "fixed" 626 trusts is a launch where
  verification says the loader ran.
- **Failure:** CI can't reach GitHub, the field keeps its last value; the app can't fetch the feed, it
  uses its cache; no data at all, the chip never offers "update available". Detection, the restore
  point and vanilla never depend on the watch.

## 5. Edge cases

- **First sighting of a game:** no baseline yet; record the current build silently. No false Patch Day
  on install.
- **Steam verify or reinstall with the same build:** the build id doesn't move; nothing fires.
- **Build changes but the loader is newer than the new exe:** Quiet; baseline moves.
- **Two patches in a row:** `patchDay.buildId` updates to the newest build; `restorePointId` keeps the
  first snapshot (the setup from before any patch).
- **Vanilla step-aside fails partway:** all-or-nothing as `VanillaLaunch` is today; the chip names what
  didn't move.
- **Restore point fails** (disk full): still warn, still offer vanilla; the chip adds "Couldn't save a
  restore point: <reason>." The warning never depends on the snapshot.
- **Ban-risk game:** Play modded anyway still passes the ban-risk gate.

## 6. Testing

- **Core, test first:** `PatchDay.Evaluate` across every state and transition (fake build ids, file
  dates, feed entries); `patchDay` round-trip as camelCase; `latestRelease` validation (non-https and
  future dates dropped); banner suppression only where a version-locked loader exists; restore-point
  failure still yields the warning; first sighting is silent.
- **Feed:** miner writes `latestRelease`; validator tests; signature still verifies.
- **Smoke harness:** a fixture game with a fake `appmanifest_*.acf` and a version-locked loader file:
  change the build id, confirm `StateChip.patch-day` reaches the tree, Play shows `PatchDayDialog`,
  vanilla round-trips byte-identical, the restore point exists. Restore the fixture in a `finally`.
- **Reviewers:** `reversibility-auditor` and `core-purity-reviewer` on every PR.
- **On Neb, read-and-warn only:** Monster Hunter Wilds (REFramework, GitHub-watched) for the full flow
  including "update available"; Elden Ring (Elden Mod Loader, no watch) for detection and vanilla.
  Simulate a patch by editing the `buildid` in the game's `appmanifest_*.acf`, the same way the Steam
  banner's smoke entry does, and restore it after.

## Slices

1. Core evaluator + `patchDay` record + chip + Play dialog + automatic restore point + vanilla and
   bring-back (no feed field yet; "Update available" never appears).
2. Feed field: `LoaderManifestEntry.latestRelease`, validator, app reads it; miner + scheduled job in
   `626-game-manifest`.
3. Library-home row line and the `get_game_shape` block.
