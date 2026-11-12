# Run List: patch-day

Spec: `docs/superpowers/specs/2026-10-16-patch-day-design.md` (branch `docs/patch-day-spec`). Core checks: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App checks add `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Never run bare `dotnet test`/`dotnet build` at the repo root.

- [x] **1. `GameEntry.PatchDay` record persists as camelCase**
  Spec ref: `spec > 2. Data > GameEntry`
  What to build: (S) A `PatchDayRecord` in Core (`buildId`, `detectedUtc`, `restorePointId` nullable, `choice` enum none|vanilla|modded serialized as camelCase string, `snoozedUntilChange` bool) and an optional `PatchDay` property on `GameEntry`, omitted from JSON when null. Make sure `GameEntry` cloning (see `GameEntryCloneTests`) carries it.
  Acceptance: Done when a round-trip test asserts `"patchDay"`, `"buildId"`, `"restorePointId"`, `"snoozedUntilChange"` appear camelCase and PascalCase does not; an entry without the record serializes with no `patchDay` key and loads back null; clone copies the record.
  Verify: Run the Core test suite; all pass.
  Stop class: go

- [x] **2. `PatchDay.Evaluate` decides the five states, plus pure transitions**
  Spec ref: `spec > 1. States and transitions`, `spec > 5. Edge cases`
  What to build: (M) `src/ModManager.Core/PatchDay.cs`. `Evaluate(...)` over: stored baseline build, the `PatchDayRecord?`, live Steam build, `StaleLoaderReport`, an optional latest-release lookup (name → version, publishedUtc, url; may be empty), and `LaunchMode`. Returns a `PatchDayState` (Quiet, Patched, PlayingVanilla, UpdateAvailable, LoaderUpdated) plus the facts the chip needs (game name/date, loader name, release url). Pure transition helpers: `Detect` (new record; second patch keeps the first restorePointId and updates buildId), `Choose(vanilla|modded)`, `Snooze`, `BringBack` (clears record, returns new baseline). Snoozed hides until the state changes. Uses `SteamBuildCheck` and `StaleLoaders` types; no IO.
  Acceptance: Done when tests cover: first sighting quiet; same build quiet; build changed + loader newer than exe quiet; build changed + stale version-locked loader → Patched; vanilla choice + Vanilla mode → PlayingVanilla; release newer than loader file → UpdateAvailable; loader file newer than exe → LoaderUpdated; two patches keep first restorePointId; snooze hides until change; null restorePointId still yields Patched.
  Verify: Run the Core test suite; all pass.
  Stop class: go

- [x] **3. PATCH DAY chip in the state strip**
  Spec ref: `spec > 3. What the player sees > Game view`
  What to build: (S) `GameStateConditions` gains the Patch Day state/facts; `GameStateStrip.For` emits `patch-day` (Warning severity, `Caution` tone in `GameStateChip.Tone`) directly after `ban-risk`, with the per-state sentence and primary `ActionLabel` from the spec table; first Patched also carries "If you launch from Steam, your mods load as they are."; restore-point failure appends "Couldn't save a restore point: <reason>." When patch-day is present, `steam-updated` and `stale-loader` are not emitted. Expose the secondary action label for states that have two buttons.
  Acceptance: Done when tests assert order (ban-risk first, patch-day second), tone Caution, suppression of steam-updated and stale-loader only while patch-day shows (both still appear without it), each state's sentence and action labels, and the restore-point failure line.
  Verify: Run the Core test suite; all pass.
  Stop class: go

- [x] **4. Single-game capture-only restore point**
  Spec ref: `spec > 1. Transitions > Entering Patched`; ledger D2
  What to build: (M) A capture-only path on `RestorePointOrchestrator` (and `RestorePointService` passthrough) that snapshots ONE game with `RestorePointEngine.CaptureGame`, writes the restore-point manifest, and never calls `ApplyEndState` or touches the game folder. Labelled so the list shows it as the Patch Day snapshot. Same lock and free-space preflight as Safe Clear; returns a result with the timestamp or a refusal reason, never throws for expected failures. Restoring it must work through the existing `RestoreAsync`.
  Acceptance: Done when tests show: the game folder is byte-identical before and after capture; only the named game is in the manifest; a free-space refusal returns a reason and writes no partial restore point; `ListRestorePoints` shows it; restore of it round-trips.
  Verify: Run the Core test suite; all pass. Run the reversibility-auditor agent on the diff.
  Stop class: go

- [x] **5. App wiring: detection, automatic restore point, chip actions**
  Spec ref: `spec > 1`, `spec > 2 > banner folds in`, `spec > 3 > Game view`
  What to build: (L) In `MainViewModel` reload (next to the existing `SteamBuildCheck.Evaluate` + `StaleLoaders.Find` block, ~line 1070): run `PatchDay.Evaluate`; on entering Patched, persist the record through the registry setter pattern `SetSteamBuildBaseline` uses (atomic) and take the item-4 restore point off the UI thread, recording its id or the failure reason. Feed the strip conditions. Chip actions: Play vanilla for now (`VanillaLaunch.StepAsideAsync`, record choice, launch vanilla), Play modded anyway (record choice, launch modded), Tell me when it's fixed (snooze), Bring my mods back (`VanillaLaunch.RestoreAsync`, clear record, move baseline), Get the update (open release URL). Second action in the detail row with `AutomationProperties.AutomationId="PatchDaySecondaryAction"` and a Name. No Play-button dialog here (item 6).
  Acceptance: Done when the App builds; Core tests pass; every new control has an AutomationId; no file op happens without a button press except the restore-point capture; the steam-updated banner path is unchanged on games without a version-locked loader.
  Verify: Stop nothing that is running; build the App project in the worktree and run the Core test suite. Both pass.
  Stop class: go

- [ ] **6. Play dialog and Play honoring the choice**
  Spec ref: `spec > 3 > At Play`, `spec > 5 > Ban-risk game`
  What to build: (M) When state is Patched and choice is none, Play opens `PatchDayDialog` (AutomationIds `PatchDayDialog`, `PatchDayPlayVanilla`, `PatchDayPlayModded`, `PatchDaySnooze`) with the chip's sentence. After a choice, Play does it without asking. Play modded on a ban-risk game still goes through the existing ban-risk gate.
  Acceptance: Done when the App builds; Core tests pass; the dialog only opens in Patched + none; the ban-risk path is not bypassed (cite the call site).
  Verify: Build the App project and run the Core test suite.
  Stop class: hold

- [x] **7. Feed `loaderReleases`: schema, validator, app read**
  Spec ref: `spec > 2 > Signed feed`; ledger D1
  What to build: (M) Top-level `GameManifest.LoaderReleases` list of `{ loader, version, publishedUtc, url }` (camelCase via `ManifestJson.Options`). `ManifestValidator` drops a row with non-https url, a future `publishedUtc`, or an empty loader name, and keeps the rest. `EffectiveManifest` takes the remote list over the embedded one. An older binary reading a feed with the field must still load. The app passes the lookup into `PatchDay.Evaluate`.
  Acceptance: Done when tests cover camelCase round-trip, each drop rule, keep-the-rest, merge precedence, and an unknown-field-tolerant load; Core tests pass; App builds.
  Verify: Run the Core test suite and build the App project.
  Stop class: go

- [x] **8. Miner writes `loaderReleases` from GitHub releases**
  Spec ref: `spec > 4. The release watch`
  What to build: (M) In `tools/ManifestMiner`, a step that, for the GitHub-hosted version-locked loaders (repo slugs from the compiled catalog URLs: REFramework, UE4SS, Mod Engine 2), reads the newest non-prerelease release via the GitHub REST API (`GITHUB_TOKEN` env when present), and writes `loaderReleases` before signing. On any fetch failure keep the previous value (from the prior published manifest the miner already reads, or the embedded one). HTTP behind an injectable handler so tests run offline.
  Acceptance: Done when tests with a fake handler cover: newest non-prerelease picked; prerelease skipped; failure keeps prior value; output passes `ManifestValidator`. Never runs network in tests.
  Verify: Build the miner project and run its tests (or the Core suite if miner tests live there).
  Stop class: go

- [x] **9. Library row line and `get_game_shape` patchDay block**
  Spec ref: `spec > 3 > Library home row`, `spec > 3 > Agent tool`
  What to build: (M) Library game row state line: "Updated since your last modded launch" while Patched, "Playing vanilla since the patch" while vanilla (from the persisted record; no live Steam scan per row if one isn't already done). `get_game_shape` gains a read-only `patchDay` block: state, detected build, choice, loader and latest release if known. No new write tools.
  Acceptance: Done when the App builds; Core/MCP tests pass with a test for the new block shape (camelCase, null when quiet).
  Verify: Build the App project and run the Core test suite.
  Stop class: go

- [x] **10. Smoke entries and rule list**
  Spec ref: `spec > 6. Testing`
  What to build: (S) Append the Patch Day smoke entries to `docs/smoke-tests/pending.md` (MH Wilds full flow, Elden Ring no-watch flow, appmanifest buildid edit restored after). Add `GameEntry.patchDay` and `loaderReleases` to `.claude/rules/camelcase-json-on-disk.md`'s surface list with their test names. Add the new AutomationIds to `.claude/rules/automation-ids.md` if it lists surfaces.
  Acceptance: Done when the docs name the exact test methods that exist on the integration branch.
  Verify: grep each named test method in `tests/`; every one is found. Run the Core test suite.
  Stop class: go

- [ ] **11. Feed CI: daily schedule and token to the miner**
  Spec ref: `spec > 4 > Where it runs`
  What to build: Write-up only. `626-game-manifest/.github/workflows/build-manifest.yml` runs weekly (`0 6 * * 1`) and would need a daily cron plus `GITHUB_TOKEN` passed to the miner step.
  Acceptance: Write-up in the report.
  Verify: n/a
  Stop class: stop

- [ ] **12. Safe restore of a Patch Day snapshot**
  Spec ref: `spec > 1 > Entering Patched`; ledger D8; item 4 reversibility audit
  What to build: (M) Lift item 4's refusal with a restore path for `Kind` points that is itself undoable: take a capture-only snapshot of the current state first (unlocked capture core; `_gate` is not reentrant), skip `ReplayGame` step 4 (loader enable states), skip frameworks on a ban-gated game and never put back a framework the player uninstalled without the gate, replace delete-then-move swaps with `File.Move(tmp, target, overwrite: true)`, refuse while `safe-clear.lock` exists.
  Acceptance: Done when a fixture with a UE4SS mod (off at capture, on now, with `enabled.txt`), a BepInEx plugin and a framework changed after capture restores with: the player's current mod enable states unchanged, `enabled.txt` present, a pre-restore snapshot listed, ban-risk game frameworks untouched; refusal under the lock; Core tests pass; App builds.
  Verify: Run the Core suite, build the App, run the reversibility-auditor agent on the diff.
  Stop class: hold
