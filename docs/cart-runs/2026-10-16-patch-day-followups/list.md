# Run List: patch-day-followups

Base `cart/patch-day` (f462bbe). Checks: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`, and for App items `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.

- [ ] **1. "Play modded (restore mods)" goes through the ban-risk gate**
  - Spec ref: PR #405, Follow-ups; dashboard 4VVaLpbguyeC3hbSQ59o
  - What to build: (S) Route the Launch menu's `OnPlayModded` → `RestoreAndLaunchAsync` through `GateBanRiskEnableAsync` before restoring the vanilla stash. A declined gate restores nothing and launches nothing. Check every other caller of `RestoreAndLaunchAsync` / `VanillaLaunch.RestoreAsync` for the same gap.
  - Acceptance:
    - Every path that restores the stash on a ban-risk game passes the gate (cite each call site).
    - App builds.
    - Core tests pass.
  - Stop class: hold

- [ ] **2. Settings shows each restore point's kind**
  - Spec ref: patch-day report, item 12 "Still open"
  - What to build: (S) `RestorePointInfo.Kind` is already exposed. Rows in `SettingsDialog`'s restore-point list show "Safe Clear", "Patch Day snapshot" or "Before restore" in plain words, using an App-side mapping; no presentation strings in Core. Bound AutomationProperties.Name.
  - Acceptance:
    - Each kind renders distinctly.
    - App builds.
    - Core tests pass.
  - Stop class: go

- [ ] **3. Prune old `beforeRestore` snapshots**
  - Spec ref: patch-day report, item 12 "Still open"
  - What to build: Write-up only (it deletes restore points automatically).
  - Stop class: stop

- [ ] **4. Journal for an interrupted restore, plus a UI refresh after one**
  - Spec ref: second reversibility audit, findings 5-6
  - What to build: (M)
    - Before a Kind restore's overlay, write a pending-restore record in the `DataDirMoveJournal` style (camelCase, atomic, round-trip test). Clear it on success or refusal.
    - At startup, a record that's still present surfaces a plain message naming the `beforeRestore` snapshot to use. Startup is detection only; nothing is restored automatically.
    - After a successful Kind restore, refresh the main window's rows without restoring `games.json`.
  - Acceptance:
    - Tests cover: the record is written before the first write, cleared on success, cleared on a planned refusal, left behind by a simulated mid-overlay failure, and detected at startup.
    - App builds.
  - Stop class: go

- [ ] **5. Commit `.claude/rules/automation-ids.md` with the Patch Day ids**
  - Spec ref: patch-day report, "Found along the way"
  - What to build: (S) Copy the builder's untracked `C:\Users\estev\Projects\626-mod-launcher\.claude\rules\automation-ids.md` into the branch as-is. Then add the Patch Day ids to its surfaces section: `PatchDaySecondaryAction`, `PatchDayTertiaryAction`, `PatchDayDialog`, `PatchDayPlayVanilla`, `PatchDayPlayModded`, `PatchDaySnooze`, `PatchDayNotNow`, and the bound `GameRowPatchDay.<id>`.
  - Acceptance:
    - The file is tracked.
    - Each id it names exists in the XAML (grep).
  - Stop class: go
