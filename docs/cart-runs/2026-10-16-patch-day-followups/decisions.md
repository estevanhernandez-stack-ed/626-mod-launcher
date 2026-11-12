# Decisions: patch-day-followups

## D1

- **Decision:** The run is based on `cart/patch-day` (`f462bbe`) by initializing it against the integration worktree.
- **Why:** Items 2 and 4 need PR #405's code. `cart-run.js init` always uses the target folder's HEAD, and the builder's tree is on `master`.
- **Alternative not taken:** Checking out `cart/patch-day` in the builder's tree, which the run rules forbid.
- **Item:** all
- **How to reverse:** Rebase `cart/patch-day-followups` onto `master` once #405 merges.

## D2

- **Decision:** Item 3 (prune `beforeRestore` snapshots) is stop, written up only.
- **Why:** It deletes restore points automatically, which is a destructive data op. Este was offered "build 3 held" and confirmed the run without it.
- **Alternative not taken:** Building it as a held item.
- **Item:** 3
- **How to reverse:** Say "build 3 held".
