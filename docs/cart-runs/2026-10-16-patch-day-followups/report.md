# Run Report: patch-day-followups

3 merged, 1 held, 0 parked, 1 stopped.

## Ready to push

- **Integration branch:** `cart/patch-day-followups`. It's based on `cart/patch-day` at `f462bbe` (PR #405), not `master` (D1).
- **Commits:**
  - `14e7dd8` feat(settings): say what made each restore point
  - `1e5fa28` feat(restore-points): journal an interrupted restore and refresh after one
  - `45ebff3` docs(rules): track the automation-ids rule, with the Patch Day ids
- **Checks on the tip (`8dced1d`):**
  - App build x64: succeeded.
  - MCP build: succeeded.
  - Core tests: 4053 passed, 2 skipped (the existing 7z/rar skips).
  - Miner tests: 15 passed.
  - App tests: 16 passed.
- **Not run:** the app, a UIA walk, and the vibe-test/vibe-sec gates. The repo's own suites stood in for the gates, as in the first run.

To push and open the PR, stacked on #405:

```bash
git -C C:/Users/estev/Projects/626-mod-launcher push -u origin cart/patch-day-followups
cd C:/Users/estev/Projects/626-mod-launcher && gh pr create --base cart/patch-day --head cart/patch-day-followups --title "Patch Day follow-ups"
```

Once #405 merges, retarget this PR to `master`.

## Waiting on you

- **Item 01: "Play modded (restore mods)" passes the ban-risk gate** (`cart/patch-day-followups--01-gate-play-modded-restore`, `cc3ecb3`)
  - **Look at:** `RestoreAndLaunchAsync` in `MainViewModel.cs`. The gate now lives inside it, so the Launch menu item and Patch Day's Play modded anyway both pass it once. The Patch Day path's own call was removed so nobody is asked twice. Bring my mods back keeps its own gate. The MCP server has no restore path.
  - **Held because:** it changes the ban-risk gate.
  - **Checks:** App build, Core tests 4031.

## Parked

None.

## Stopped

- **Item 03: Prune old `beforeRestore` snapshots**
  - **Surface:** a destructive data op. It would delete restore points automatically (D2).
  - **What it would take, one of two ways:**
    - **(a) Automatic:** a Core retention rule. It keeps the newest N (e.g. 3) `beforeRestore` points per game, never touches a `patchDay` or Safe Clear point, and never one under 24 hours old. It runs after each Kind restore and deletes only sealed `beforeRestore` folders through `DeleteRestorePoint`, with a test that no other kind is ever touched.
    - **(b) Manual, the safer variant:** a "Clear old undo snapshots" button in Settings that shows what it will delete before it deletes.
  - Say which, and it becomes a held item.

## Decisions to skim

Full ledger: `decisions.md`.

- **D1:** the run stacks on PR #405 because items 2 and 4 need its code.
- **Item 4 (from its report):**
  - The pending-restore record lives at `<dataRoot>/pending-restore.json`, written just before the overlay's first write.
  - At startup, the notice "A restore didn't finish" names the undo snapshot. "Got it" clears the reminder; nothing is ever restored automatically.
  - The main window refreshes after a Kind restore through `ViewModel.RefreshAsync()` on the App side, so Core still never writes `games.json`.
  - The confirmation text for a Kind restore now says what it actually does.

## Left untouched

Your tree is still on `master`, with its untracked `docs/cart-runs/` folder from the first run. This run's folder lives in the `cart/patch-day` worktree at `~/.cwt/626-mod-launcher-patch-day/int/docs/cart-runs/`, untracked.
