# Run Brief: patch-day-followups

## Ask

"take the 5 now", then "yes" to running it unattended (2026-10-16, 23:52 local).

## Source

- **Kind:** follow-up list
- **Location:** PR #405 body, "Not in this PR > Follow-ups"; patch-day run report (`docs/cart-runs/2026-10-16-patch-day/report.md` in the builder's tree).

## Intent

Close the five follow-ups Patch Day left open, on top of `cart/patch-day` (PR #405).

## Done when

- Items 2, 4 and 5 are merged into `cart/patch-day-followups`.
- Item 1 is built and checked, and held for review.
- Item 3 is written up.
- On the branch: the Core suite passes and the App builds.

## Out of scope

- Pushing, PRs, and changes to `626-game-manifest`.
- Running the app or the smoke harness.

## Inferred

- "take" means build them now, not file dashboard tasks. (From: the verb, after the offer to file tasks.)
- The run is based on `cart/patch-day` (`f462bbe`), not `master`, because items 2 and 4 need PR #405's code. The run helper was pointed at the integration worktree so the builder's tree on `master` stays untouched. (From: `cart-run.js init` always uses the target folder's HEAD.)
- Item 1 is `hold`, because it touches the ban-risk gate. Item 3 is `stop`, because it automatically deletes restore points; Este was offered "build 3 held" and didn't take it. (From: stop-class defaults.)

## Mode

- **Mode:** Unattended
- **Triggered by:** Este confirmed the recommendation (late window, 23:51).
