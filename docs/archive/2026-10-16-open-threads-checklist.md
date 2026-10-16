# Build Checklist — open threads after v0.24.0 (COMPLETE 2026-10-16)

> Source: the 2026-10-16 session wrap (v0.24.0 shipped on both channels). There is no `spec.md` for this
> list: each item's spec is the dashboard task it names, which already carries the root cause and the fix
> direction. Current plans still live in `docs/superpowers/{specs,plans}/`; this file is the working list
> for one autonomous `/build` run and is archived when every box is checked.

## Build preferences

- **Build mode:** autonomous. Este asked for the whole list worked to completion.
- **Verification:** on. Every code item: a failing test first, then the Core suite, then a READ-ONLY probe
  of the real install where one exists on Neb. Never toggle a real mod on a ban-risk game (Elden Ring).
- **Git cadence:** one branch and one PR per item. Code PRs merge when the PR test gate is green AND the
  repo's own reviewer agent signs off (`reversibility-auditor` for file-op changes, `core-purity-reviewer`
  for Core). Docs-only PRs merge on green.
- **Decisions stay Este's:** items that end in a decision produce the analysis and a recommendation, then
  stop. Nothing here changes the Store manifest, submits to the Store, or deletes his files.

## Items

- [x] **1. Elden Ring's own `Game/regulation.bin` is base content unless 626 put a mod there**
  Spec ref: dashboard task `task-1790983984612-fs7zfr7an` (high)
  What to build: `KnownDirectInjectMod` matches `regulation.bin` by name alone (KnownDirectInjectMod.cs ~73-75),
  so the game's own file lists as an enabled mod and Return to vanilla moves it. Treat it as a mod only with
  provenance: 626 replaced it (a snapshot of the original exists in the replace store) or an install record
  claims it. Otherwise it is a game file (`Mod.IsBase`, GAME FILE chip, locked on), using the same guard #386
  added. Update the existing tests that assert the move.
  Acceptance: vanilla `regulation.bin` is never turned off by Disable all, Return to vanilla or the MCP; a
  regulation.bin 626 installed still toggles and restores byte-identical; a hand-installed modded one with no
  provenance is reported honestly (and the trade-off is written in the PR).
  Verify: new tests red then green; Core suite green; read-only scan of the real Elden Ring install reports
  what the row would be, with nothing moved (diff the holding folder before and after).
  **Decided 2026-10-16: merge, then re-drop.** Probe: Este's "Modded regulation.bin" has no provenance and
  reads as a game file after this; he re-drops it through 626 when he wants the switch back. Merges after the
  reversibility audit and the PR gate. (Was: HOLD, does not auto-merge.)
  Original hold note: Este's own "Modded regulation.bin" may have been installed by hand, in which
  case this rule locks it on as a game file. The PR goes to Este with the probe result and the options before
  it merges (raised by Este 2026-10-16).

- [x] **2. Cyberpunk standalone ArchiveXL `.xl` mods get their own rows**
  Spec ref: dashboard task `task-1790988399070-ofprnqily` (medium)
  What to build: a plain `.xl`, or an `<X>.archive.xl` whose `<X>.archive` is absent, becomes its own row in
  `archive\pc\mod` (enable, disable, uninstall like any loose file). Keep A3 sidecar pairing
  (`Scanner.SidecarsFor`) when the `.archive` exists.
  Acceptance: both shapes list and round-trip byte-identical; paired `.archive.xl` files still ride with their
  `.archive`; frameworks (ArchiveXL itself) never move.
  Verify: tests red then green; Core suite green; read-only scan of the real Cyberpunk install on G: lists the
  31 standalone `.xl` mods named in the task, with nothing moved.

- [x] **3. Fallout 4 / Starfield `.ba2` archives move with their plugin**
  Spec ref: dashboard task `task-1790997122496-2pa3yvw3d` (medium)
  What to build: add `ba2` to the bethesda preset; pair `<stem> - *.ba2` / `<stem> - *.bsa` with the
  `<stem>.esp/.esm/.esl` row as companions (same shape as the A3 sidecars); extend `BaseGameFiles` so
  `Fallout4 - *.ba2` and `Starfield - *.ba2` are game files. Sequenced after 2 because both touch sidecar
  pairing in `Scanner`.
  Acceptance: turning a FO4/Starfield mod off moves its archives with it and back byte-identical; base
  archives never move; Skyrim's `.bsa` behaviour is unchanged.
  Verify: fixture tests red then green (no Bethesda install on Neb); Core suite green; the smoke harness
  cases `game-files-stay-on-through-disable-all` and both load-order cases still pass.

- [x] **4. MCP in the Store package: a spec, stopping at Este's decisions**
  Spec ref: dashboard task `task-1792162911674-xc36lcnkv` (medium)
  What to build: `docs/superpowers/specs/2026-10-16-mcp-in-store-design.md` answering packaging (App
  Execution Alias), registration (reuse Sanduhr's consent-gated pattern, read its code), the seal (extend
  `check-store-seal.ps1` to the MCP binaries), write-tool posture, and the reviewer letter and privacy text.
  Recommend a default for each open question. No code, no manifest change.
  Acceptance: every question in the task answered or explicitly handed to Este with a recommendation;
  claims about Sanduhr and the Store checked against code, not memory.
  Verify: read it cold against the task; docs PR green.

- [x] **5. Config mods: a revive-or-retire assessment**
  Spec ref: dashboard task `task-1792166000233-4q78w91e1` (low)
  What to build: rebase `archive/ue-config-mods` onto master on a scratch branch, count and characterise the
  conflicts, run its tests, and check the design against `DropRouter.Plan` and the manifest. Write the finding
  and a recommendation into the task. Nothing merges.
  Acceptance: the task says what reviving costs, what it buys, and which way to go.
  Verify: the scratch branch is deleted afterwards; the archive tag is untouched.

- [x] **6. Housekeeping: stale tasks and the Store listing wording**
  Spec ref: the session wrap; `task-1786332971948-lb8ecsh1y` (A6)
  What to build: close A6 (its two open items shipped in #364; verify in code first). Queue the Store listing
  change, the description and features still say "games with anti-cheat", as exact replacement lines in the
  next-submission reminder task, so the next listing refresh carries them.
  Acceptance: no dashboard task describes work that already shipped; the wording change is written down where
  the next submission will find it.
  Verify: re-list open tasks.

- [x] **7. store-listing-console: the runbook and the `/store-api` command match what ran**
  Spec ref: the session wrap
  What to build: add today's measured row to `docs/release-loop.md` (a `PreProcessing` status after
  `CommitStarted`; package visible at about two minutes; plan, apply, captions, verify, commit on
  `1152921505702151373`). Make the `/store-api` command doc match the committed engine (positional app id plus
  `--payload`, the apply flag as committed), and say `ship.py` is untracked work in progress. Do not touch
  `ship.py`.
  Acceptance: someone following the command doc tomorrow runs commands the engine accepts.
  Verify: `submit_write.py --help` agrees with the doc; PR green.

- [x] **8. Close-out: smoke entries, full suite, everything merged**
  Spec ref: `docs/smoke-tests/pending.md` conventions; `.claude/agents/`
  What to build: a pending.md entry per code item (1-3) with steps and expected outcome; run the full Core
  suite and the smoke harness on the merged master; log every decision; archive this checklist to
  `docs/archive/` when complete.
  Acceptance: master green, no open PRs from this run, no stray branches or worktrees.
  Verify: `gh pr list`, `git worktree list`, `git branch -a`, smoke results.

## Outcome

Every box checked on 2026-10-16. Shipped to master: #393 ban-risk copy, #394 docs archive, #395 MCP-in-Store spec,
#396 Cyberpunk .xl rows, #397 regulation.bin provenance, #398 FO4/Starfield .ba2 companions; store-listing-console
#40. Each code PR cleared `reversibility-auditor` and `core-purity-reviewer`; the audits sent #396 back once, #397
once and #398 three times (a held-copy deletion path, an upgrade-path archive capture, a double claim, a 32x
slowdown, an overlapping-separator miss), and none of those merged. Close-out on merged master `c63f22e`: Core
3866 passed, 0 failed; smoke harness 42 of 42 after correcting its stale ban-risk assertion; real holding folders
unchanged; `games.json` restored byte-identical after the run. Decisions left with Este: the six in the
MCP-in-Store spec, and config mods (recommended retire).
