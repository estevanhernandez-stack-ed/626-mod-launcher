# Decisions: patch-day

## D1
- **Decision:** Release data ships as a new top-level `loaderReleases` list keyed by loader name, not as `latestRelease` on `loaders[]`.
- **Why:** The feed's `loaders[]` holds only mod-engine-2 and seamless-coop. REFramework, UE4SS and Elden Mod Loader are compiled catalog rows, so the spec's field would watch one loader of three.
- **Alternative not taken:** Adding feed loader entries for REFramework and UE4SS, which would give them loader behavior they don't need.
- **Item:** 7, 8
- **How to reverse:** Move the fields onto `LoaderManifestEntry` and drop the top-level list; the evaluator's lookup is the only consumer. Dashboard C6WDauZAJxuMKCGNjKRD.

## D2
- **Decision:** The automatic restore point is a new capture-only, single-game path, not Safe Clear.
- **Why:** `SafeClearAsync` walks every game and always applies an end state, which would change files on detection and break "warn and offer, never act".
- **Alternative not taken:** Calling Safe Clear with `modsActive`, or dropping the automatic restore point.
- **Item:** 4, 5
- **How to reverse:** Remove the capture call in item 5; the chip still works without it. Dashboard yg7m3kIqUOZYV4EP04EB.

## D3
- **Decision:** While PATCH DAY shows, UPDATED and OLD LOADER are not emitted.
- **Why:** The spec folds the Steam banner into the chip; OLD LOADER would repeat the same loader sentence beside it.
- **Alternative not taken:** Hiding only UPDATED, as the spec literally says.
- **Item:** 3
- **How to reverse:** Drop the stale-loader suppression line in `GameStateStrip.For`.

## D4
- **Decision:** Item 6 (Play dialog) is `hold`.
- **Why:** It sits on the Play path beside the ban-risk gate, the one law that can cost an account; access gating defaults to hold.
- **Alternative not taken:** `go`, merging unreviewed.
- **Item:** 6
- **How to reverse:** Merge `cart/patch-day--06-play-gate` into `cart/patch-day` after review.

## D5
- **Decision:** Item 11 (feed CI daily schedule) is `stop`, written up only.
- **Why:** It lives in another repo and changes what the signed feed publishes to every user: a deploy-class change.
- **Alternative not taken:** Editing the workflow locally in `626-game-manifest`.
- **Item:** 11
- **How to reverse:** n/a; build it on Este's say-so.

## D6
- **Decision:** Invoking /take on the spec counts as approval of the written spec.
- **Why:** The previous handoff asked for review before planning; the next message ran /take on the file.
- **Alternative not taken:** Asking again.
- **Item:** all
- **How to reverse:** Edit the spec on PR #404 and re-run affected items.

## D7
- **Decision:** `Evaluate` claims LoaderUpdated only in modded mode with a game exe found; during vanilla, item 5 must build the loader report from the vanilla stash. A second patch resets a Modded choice to None and keeps Vanilla.
- **Why:** Vanilla steps the loader proxies aside, so a live scan finds no loaders and would read as "loader updated". Modded was a choice about the old build; Vanilla still describes what's on disk.
- **Alternative not taken:** Trusting the live scan in both modes; keeping Modded across patches.
- **Item:** 2, 5
- **How to reverse:** The guard and the reset are one line each in `PatchDay.Evaluate` / `PatchDay.Detect`. Dashboard FPDXImfwUpA8fDcI6Mas.

## D8
- **Decision:** Item 4 ships capture only; `RestoreAsync` refuses a restore point with a `Kind`. Safe restore becomes item 12, `hold`.
- **Why:** Reversibility audit, verified at `RestorePointEngine.cs:810-820`: `ReplayGame` step 4 replays UE4SS/BepInEx enable states with no ban-risk gate, and `Ue4ssManifest.SetEnabled(false)` deletes the mod's own `enabled.txt`. Step 3 overwrites framework files with no snapshot. The data overlay has no pre-restore snapshot. All safe after a Safe Clear, unsafe over a live modded game. Capture audited clean.
- **Alternative not taken:** Holding item 4, which would hold items 5, 6, 7 and 9 behind it; shipping restore as-is.
- **Item:** 4, 12
- **How to reverse:** Item 12's branch, after review. Dashboard 1CxKCKKCaRGSLGrnWPX9.

## D9
- **Decision:** A Patch Day snapshot is described as covering 626's own data and loader/framework state, not the mod files in the game folder.
- **Why:** The engine's capture never copies enabled mod files out of the game folder (item 4 report). Copy that says "your setup before the patch" would over-promise.
- **Alternative not taken:** Redesigning capture to copy enabled mod files (large, and a disk-cost question for Este).
- **Item:** 5, 12
- **How to reverse:** Widen capture in a follow-up; then the copy can say more.

## D10
- **Decision:** Close-out used the repo's own checks on `cart/patch-day` (App + MCP builds, Core 3996, Miner 15, App tests 16) instead of the vibe-test/vibe-sec gates.
- **Why:** Both gates target JS/Python coverage and secret scanning and have exited 2 on win32; this run added no secrets, keys or dependencies, and the .NET suites are the contract surface.
- **Alternative not taken:** Running the gates and logging exit 2.
- **Item:** close-out
- **How to reverse:** Run `/vibe-sec:gate` and `/vibe-test:gate` on the branch before pushing.

## D11
- **Decision:** Bring my mods back sets the Steam baseline to the live build, not the record's build.
- **Why:** The spec says "moves the baseline to the current build"; using the record's build would reopen a stale UPDATED banner when a newer patch landed while the loader looked fresh.
- **Alternative not taken:** The Core helper's record build (kept as the fallback when the live build is unknown).
- **Item:** 5
- **How to reverse:** `PatchDayBringBackAsync` in `MainViewModel.cs`.

## D12
- **Decision:** Item 12's overlay merges keyed launcher data by key and skips any framework with a proxy in holding or a vanilla stash present; ban-risk acks and Vortex hand-over files are never restored.
- **Why:** Second reversibility audit: `FrameworkRegistry.IsDisabled` needs ALL proxies missing, so a half-finished step-aside read as enabled and the overlay would have turned the loader back on; whole-file replace dropped source and update tracking for mods added since capture. Restoring an old ack would re-grant consent the player revoked.
- **Alternative not taken:** `IsDisabled` as the guard; whole-file replace with a note.
- **Item:** 12
- **How to reverse:** `RestorePointOverlay.cs` (`PlanOverlay` / `ApplyOverlay`). Dashboard RwkEhsBuytd8LU9qhjNk.
