# Run Report: patch-day

9 merged, 2 held, 0 parked, 1 stopped. **Update 23:45:** Este approved item 12, and it's merged into `cart/patch-day` (`403b979`, Core 4020). Item 6 gained library-Play coverage (`329c1fa`, `84c8fba`) and was brought up to the integration tip (`e6b9306`, Core 4031, App build). It now merges cleanly. **Update 23:46:** Este approved item 6, and it's merged (`f462bbe`). On `cart/patch-day` now: the App and MCP builds succeed, Core passes 4031, Miner 15 and App 16. All 11 buildable items are on the branch, and only item 11 (feed CI) remains. It waits on the push step.

Patch Day works end to end on `cart/patch-day`: detection, the automatic snapshot, the PATCH DAY chip and its actions, vanilla and bring-back, the feed field and the miner's release watch, the library line and the agent block. Two pieces wait on you: the Play dialog, and restoring a Patch Day snapshot. Until you merge item 12, a Patch Day snapshot can be taken but restoring one is refused.

## Ready to push

- **Integration branch:** `cart/patch-day` (base `687ecbd`, master at run start)
- **Commits (12, items 1-5 and 7-10):**
  - `5c99389` feat(patch-day): persist the patchDay record on GameEntry
  - `4d8ceae` fix(patch-day): read an unknown patchDay choice as None instead of dropping the registry
  - `a37785c` feat(patch-day): pure evaluator for the five Patch Day states
  - `bf239ef` feat(patch-day): PATCH DAY chip in the state strip
  - `38d1b70` feat(restore-points): capture one game without changing it
  - `fc1f044` fix(restore-points): refuse to restore a capture-only point, unique names, plain-name checks
  - `c0cede8` feat(patch-day): wire detection, snapshot and chip actions
  - `6d80f92` fix(patch-day): bring-back moves the baseline to the live build
  - `b6e5e96` feat(manifest): loaderReleases in the signed feed
  - `3232c40` feat(miner): watch loader releases for Patch Day
  - `8d36a68` feat(patch-day): library row line and get_game_shape block
  - `cf7aac6` docs(patch-day): smoke entries and rule surfaces
- **Checks on the integration tip (`79a62a9`):**
  - App build x64: succeeded, 0 warnings.
  - MCP build: succeeded.
  - Core tests: 3996 passed, 2 skipped (the existing 7z/rar skips).
  - Miner tests: 15 passed.
  - App tests: 16 passed.
- **Per item:** every item ran the Core suite. Every App item also built the App. Item 4 had an independent reversibility audit: the capture was clean, and the restore problems it found became item 12.
- **Close-out gates:** vibe-test and vibe-sec were not run. The repo's own suites stood in for them (D10).
- **Not run:** the app itself, and the smoke harness. Both touch real mods on this machine. The steps are in `docs/smoke-tests/pending.md` under "Patch Day".

Merge the spec PR #404 first, or alongside this one; the code points at it. To push and open the PR:

```bash
git -C C:/Users/estev/Projects/626-mod-launcher push -u origin cart/patch-day
cd C:/Users/estev/Projects/626-mod-launcher && gh pr create --base master --head cart/patch-day --title "feat(patch-day): Patch Day, a game updated and its loader didn't" --body-file docs/cart-runs/2026-10-16-patch-day/report.md
```

Pushing waits for your say-so. Say "push it", or name a subset.

## Waiting on you

- **Item 06: Play dialog and Play honoring the choice** (`cart/patch-day--06-play-gate`, `91ece7e`, worktree `~/.cwt/626-mod-launcher-patch-day/06`)
  - **Held because:** it sits on the Play path, next to the ban-risk gate (D4).
  - **Look at first:**
    - The `Launch()` gate in `MainViewModel.cs` (about lines 2372-2385). Snooze and a dismissed dialog both return without launching.
    - `PatchDayDialog.xaml` maps the chip's labels by position.
    - `GameStateStrip.PatchDayChip` is now public, so the dialog can reuse the chip's sentence.
    - There is one extra button beyond the spec, "Not now" (`PatchDayNotNow`).
  - **Ban-risk gate:** Play modded on a game in vanilla mode calls `GateBanRiskEnableAsync` (MainViewModel ~1977) before it restores the mods.
  - **Gap:** the Play button on the library home calls `_svc.Launch` directly and doesn't ask. Asking there needs the verdict for every game, not just the active one.
  - **Checks:** App build, Core tests 3969.
- **Item 12: Safe restore of a Patch Day snapshot** (`cart/patch-day--12-safe-patchday-restore`, `9817707` + `1703b4a`, worktree `~/.cwt/626-mod-launcher-patch-day/12`)
  - **Held because:** it restores over a live modded game. The first audit found the existing restore unsafe there (D8).
  - **What it does:** it takes a `beforeRestore` snapshot first. Then it applies a planned overlay that never turns loader mods on or off. It merges 626's data files by key, never restores ban-risk acknowledgments or Vortex hand-overs, and puts framework files back only when the framework is unchanged, not ban-gated and has nothing stepped aside (D12).
  - **Checks:** Core tests 4020, App build. A second audit found nothing Critical, and its two Important findings are fixed in `1703b4a`.
  - **Still open:**
    - No journal if it crashes mid-overlay.
    - The main window doesn't refresh after a restore.
    - Nothing cleans up leftover `.rp-tmp` files.
    - `beforeRestore` snapshots are never pruned.
    - Settings → Restore points shows no kind, so these look like Safe Clear points.
  - **Merge note:** it builds on the integration tip, so merge it after `cart/patch-day`.

## Parked

None.

## Stopped

- **Item 11: Feed CI daily schedule and token**
  - **Surface:** deploy-class. The change lives in another repo and changes what the signed feed publishes to every user (D5).
  - **What it would take:** in `626-game-manifest/.github/workflows/build-manifest.yml`:
    1. Change the cron from `0 6 * * 1` (weekly) to daily, for example `0 6 * * *`.
    2. Add `--loader-releases --previous games-manifest.json` to the miner's `--sign` step, pointing at the repo's last published feed.
    3. Set `env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}` on that step.

    The next signed feed then carries `loaderReleases`, and "Update available" starts appearing.
  - **Who:** you, or Cart on your say-so.

## Decisions to skim

Full ledger: `decisions.md` (D1-D12). The highest-stakes ones:

- **D8:** restoring a Patch Day snapshot is refused on `cart/patch-day`. The old restore path, run over a live game, turned mods off, deleted a UE4SS mod's own `enabled.txt`, and could turn mods on for a ban-risk game without the acknowledgment. Item 12 is the safe version.
- **D9:** a Patch Day snapshot covers 626's own data and the loader and framework files, not the mod files in the game folder. The copy doesn't promise more than that.
- **D1:** release data is a new top-level `loaderReleases` list in the feed, not a field on `loaders[]`. Only Mod Engine 2 is a feed loader today.
- **D7:** while playing vanilla, the loader's date comes from the stepped-aside files, otherwise it would falsely read "loader updated". A second patch resets a Modded choice.
- **D11:** Bring my mods back records the build you're on now as the new baseline.

## Found along the way

These are outside this run. Each is logged to the dashboard, or noted here:

- The Launch menu's existing **"Play modded (restore mods)"** item restores the vanilla stash without the ban-risk gate. The new Patch Day paths do gate it. This is a follow-up fix (dashboard 4VVaLpbguyeC3hbSQ59o).
- **`.claude/rules/automation-ids.md` is not committed.** It exists only in your working tree, so clones and worktrees don't get it, and item 10 couldn't add the new ids to it. The new ids are `PatchDaySecondaryAction`, `PatchDayTertiaryAction` and `GameRowPatchDay.<id>`.
- **Item worktrees have an empty `external/626-mod-plugins`.** The App only builds after running `git submodule update --init --reference <main>/external/626-mod-plugins`, and `git worktree remove` refuses until the submodule is deinitialized.

## Left untouched

Your tree was clean at run start. It's still on `master`. The only addition is this run folder, `docs/cart-runs/2026-10-16-patch-day/`, which is untracked.
