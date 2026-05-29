# v0.3.x Smoke Remediations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the seven bugs / UX gaps surfaced by the 2026-05-28 live Elden Ring + Safe Clear smoke session (all logged in the 626 decision log).

**Architecture:** Each fix follows the pure-core / thin-shell law — testable logic lands in `ModManager.Core` (test-first, xUnit), platform IO lives behind an interface implemented in `ModManager.App/Services/`. WinUI view/dialog wiring is build-verified (`-p:Platform=x64`) + manual-smoke, never unit-tested. `CorePurityTests` must stay green. No new `File.Delete` in any toggle/replace path; reversibility preserved.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3. Build the test project explicitly: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Build the App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Never run bare `dotnet build/test` at the repo root.

**Branching:** one `fix/<slug>` or `feat/<slug>` branch per task off `master`; PR into `master` (no direct commits to master).

---

## Priority order

| # | Task | Severity | Priority | Effort | Layer |
|---|---|---|---|---|---|
| 1 | IniEdit bare-CR line-ending corruption | critical | 1 | S | Core (+App belt) |
| 2 | Game-running refusal fails open (bootstrapper games) | high | 1 | S | Core (+App) |
| 3 | Play-vanilla with direct-inject DLLs crashes | medium | 2 | M | Core (+App) |
| 4 | Mod-loader "required" framing should be conditional | high | 3 | M | Core (+App) |
| 5 | Exe launch doesn't ensure Steam is running | medium | 4 | M | Core (+App) |
| 6 | Mod-loader row hidden while its mods are active | low | 9 | M | Core (+App) |
| 7 | Safe Clear success gives no restore-point confirmation | low | 9 | S | Core (+App) |

**Recommended sequencing:** do the two priority-1 / effort-S fixes first (Task 1, Task 2) — both are small, both fix a silently-broken core behavior. Then Tasks 3–5 (the launch-path family). Tasks 6–7 are polish, batch last.

---

## Task 1: IniEdit writes bare-CR — corrupts mod .ini files (PRIORITY 1, critical)

**Files:**
- Modify: `src/ModManager.Core/IniEdit/IniEditService.cs` (`SaveWithBackup`, the `File.WriteAllText` at ~line 37)
- Test: `tests/ModManager.Tests/IniEdit/IniEditServiceTests.cs`
- App belt (separate commit): `src/ModManager.App/IniEdit/IniEditorDialog.xaml.cs` (~lines 28, 42)

**Root cause:** The WinUI `TextBox` collapses every `\r\n`/`\n` in `.Text` to a single `\r` on round-trip (`IniEditorDialog.xaml.cs:28` load → `:42` save). `IniEditService.SaveWithBackup` then writes that string verbatim (`File.WriteAllText`, ~line 37) → bare-CR on disk (observed CR=53/LF=0) → the game's line-based INI parser can't split lines (this is what broke Seamless `ersc_settings.ini`). Core write is faithful; the corruption enters at the App boundary but the caller-proof fix lives in Core.

**Fix:** In `IniEditService`, add private helpers `DetectNewline(string original)` (→ `\r\n` if original contains any `\r\n`; `\n` if it contains `\n` and no `\r\n`; else default `\r\n`) and `NormalizeNewlines(string contents, string newline)` (collapse `\r\n`→`\n`, `\r`→`\n`, then `\n`→newline). In `SaveWithBackup`, reuse the `currentContents` already read for the snapshot, compute `targetNewline = DetectNewline(currentContents)`, and write `NormalizeNewlines(newContents, targetNewline)`. **Keep the `.bak` snapshot byte-exact — do NOT normalize the backup**, so Restore Previous still round-trips the true previous bytes.

- [ ] **Step 1:** failing test `SaveWithBackup_normalizes_bare_CR_to_CRLF` — existing CRLF ini, save bare-CR newContents, assert output Contains `\r\n` and has zero bare-CR (no `\r` not followed by `\n`). Run → fails.
- [ ] **Step 2:** failing test `SaveWithBackup_defaults_new_file_to_CRLF` — no pre-existing file, LF input, assert CRLF out, zero bare-CR. Run → fails.
- [ ] **Step 3:** failing test `SaveWithBackup_preserves_LF_only_style` — existing LF-only ini, save mixed content, assert output is LF-only (no `\r`). Run → fails.
- [ ] **Step 4:** failing test `SaveWithBackup_backup_is_byte_exact_not_normalized` — existing bare-CR file on disk, save, assert the `.bak` equals original bytes exactly. Run → fails/lock-in.
- [ ] **Step 5:** implement `DetectNewline` + `NormalizeNewlines`; change the write to `NormalizeNewlines(newContents, DetectNewline(currentContents))`; leave `.bak` write untouched.
- [ ] **Step 6:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → 4 new + 6 existing IniEdit tests + CorePurityTests green.
- [ ] **Step 7:** commit `fix(ini-edit): write CRLF / preserve original newline style on save, never bare-CR`.
- [ ] **Step 8 (App belt, separate commit):** normalize `ContentsBox.Text` to `\r\n` before passing to `SaveWithBackup`. Build x64. Manual smoke: edit a Seamless `ersc_settings.ini` via the pencil → Save → file opens CRLF (CR==LF) and the game parses it. Append a smoke entry. Commit `fix(ini-edit): normalize TextBox newlines on the App boundary`.

**Design decisions (defaults chosen):** preserve original newline style with CRLF fallback (the safe superset — fixes bare-CR without force-converting a deliberately-LF file); Core save-normalization is the load-bearing fix, App load-normalize is optional belt-and-suspenders.

**Risk:** low, fully reversible. One caller. Newline-only byte change; `.bak` stays byte-exact; no `File.Delete`; atomic temp+rename preserved.

---

## Task 2: Game-running refusal fails open for bootstrapper-launched games (PRIORITY 1, high)

**Files:**
- Create: `src/ModManager.Core/RestorePoints/EngineRuntimeProcesses.cs` (engine → runtime exe-name map)
- Modify: `src/ModManager.Core/RestorePoints/ProcessNameMatch.cs` (also match engine runtime exes)
- Test: `tests/ModManager.Tests/RestorePoints/ProcessNameMatchTests.cs`, new `EngineRuntimeProcessesTests.cs`, `RestorePointOrchestratorSafeClearTests.cs`
- Modify: `src/ModManager.App/Services/GameProcessProbe.cs` (stop failing open on enumeration failure)

**Root cause:** `ProcessNameMatch.AnyRunning` (ProcessNameMatch.cs:13-18) only checks exe-kind LaunchTargets. ER's only exe target is `ersc_launcher.exe` (Seamless bootstrapper that exits); the live process is `eldenring.exe`/`start_protected_game.exe`, never a target → returns false while the game runs. `RestorePointOrchestrator.SafeClearAsync` (RestorePointOrchestrator.cs:43-46) trusts that false. `GameProcessProbe` (GameProcessProbe.cs:18,22) swallows enumeration exceptions → also fails open. Net: a destructive Safe Clear can run mid-game.

**Fix:** Core — add `EngineRuntimeProcesses` map keyed by `GameEntry.Engine`; seed `"fromsoft"` → `["eldenring.exe","start_protected_game.exe"]`. Extend `ProcessNameMatch.AnyRunning` to also match those (additive — existing LaunchTarget matching untouched). App — make `GameProcessProbe` fail closed on enumeration failure (typed throw / tri-state); orchestrator treats probe-unavailable as a Refusal.

- [ ] **Step 1:** failing test `Matches_engine_runtime_exe_when_only_bootstrapper_is_a_launch_target` (Engine=fromsoft, targets=[ersc_launcher exe, steam], running=["explorer","eldenring"] → true). Run → fails.
- [ ] **Step 2:** add `EngineRuntimeProcesses.For(string? engine)` (extension-less names, empty for null/unknown); in `AnyRunning`, after the LaunchTarget loop, match `For(game.Engine)`. Re-run → passes; existing 4 ProcessNameMatch tests still green.
- [ ] **Step 3:** `EngineRuntimeProcessesTests` — `Fromsoft_includes_runtime_and_eac_exe_names`, `Unknown_or_null_engine_returns_empty`.
- [ ] **Step 4:** `Start_protected_game_alone_triggers_running` (steam-only target, running=["start_protected_game"] → true).
- [ ] **Step 5:** full Core suite green incl. CorePurityTests; commit `fix(restore-points): match engine runtime exe in game-running pre-flight`.
- [ ] **Step 6:** App — `GameProcessProbe` outer catch → typed throw/tri-state; orchestrator pre-flight Refuses on probe-unavailable; orchestrator test `Refuses_when_running_probe_is_unavailable` (FakeProbe throws → Ok=false). Build x64.
- [ ] **Step 7:** commit `fix(restore-points): fail closed when game-running probe is unavailable`. Smoke entry: launch ER via Seamless, attempt Safe Clear → must Refuse.

**Design decisions (defaults chosen):** fail-closed on probe-unavailable for this destructive gate; seed only confirmed `fromsoft` runtime names now (add other engines as verified).

**Risk:** low, reversible. Core change is additive to a read-only decision — can only make the gate more conservative. App fail-closed is the safe direction.

---

## Task 3: Play-vanilla with direct-inject DLLs present crashes at app start (PRIORITY 2, medium)

**Files:** `src/ModManager.Core/LaunchGuard.cs`, `tests/ModManager.Tests/LaunchGuardTests.cs`, `src/ModManager.App/ViewModels/MainViewModel.cs`, `src/ModManager.App/MainWindow.xaml.cs`

**Root cause:** `LaunchGuard.RequiresLauncher` only fires for `RequiredLauncher` games; loose direct-inject DLLs (dinput8/ersc/ReShade) set no `RequiredLauncher`, so `NeedsVanillaConfirm` is false. The App fires a plain vanilla target with the DLL still loose → Windows auto-loads it into the vanilla process → 0xc0000142 "unable to start correctly."

**Fix:** Core — add a separate pure verdict `LaunchGuard.NeedsDirectInjectStepAside(LaunchTarget target, bool anyDirectInjectDllsActive)` → `anyDirectInjectDllsActive && target.Kind != "exe"`. App — compute `AnyDirectInjectDllsActive` from enabled rows (proxy names dinput8/d3d11/dxgi/version/winmm/ersc or direct-inject ChipKind); add a guard branch to both launch entry points; dialog Primary "Disable mods and launch vanilla" reversibly `DirectInject.Disable`s then launches, Secondary "Launch anyway", Close "Cancel".

- [ ] **Step 1:** failing test `NeedsDirectInjectStepAside_true_for_a_steam_target_when_dlls_active`. Run → fails to compile.
- [ ] **Step 2:** add the static method. Re-run → green.
- [ ] **Step 3:** `NeedsDirectInjectStepAside_false_for_an_exe_launcher_target` + `NeedsDirectInjectStepAside_false_when_no_dlls_active`.
- [ ] **Step 4:** full suite green — six pre-existing LaunchGuardTests + 635+ unperturbed.
- [ ] **Step 5:** commit `feat(launch-guard): add direct-inject DLL vanilla step-aside verdict`.
- [ ] **Step 6 (App):** `AnyDirectInjectDllsActive` on MainViewModel; guard branch in `OnLaunchTargetClick` + `MainViewModel.Launch`; Primary disables via reversible `DirectInject.Disable` then launches. Build x64.
- [ ] **Step 7:** smoke entry + commit `feat(launch): step aside reversibly before a vanilla launch with direct-inject DLLs present`.

**Design decisions (defaults chosen):** Primary auto-disables (reversible move-to-holding) rather than refuse-only; auto-disabled DLLs stay disabled after exit (no process-exit watcher today) with a status line noting it.

**Risk:** low-moderate, reversible. Additive Core method; auto-disable reuses reversible `DirectInject.Disable/Enable`; no new file-op code.

---

## Task 4: Mod-loader "required" framing should be conditional (PRIORITY 3, high)

**Files:** `src/ModManager.Core/FrameworkDeps.cs`, `src/ModManager.Core/Catalog/KnownDirectInjectMod.cs`, `tests/ModManager.Tests/FrameworkDepsCatalogTests.cs`, `tests/ModManager.Tests/Catalog/KnownDirectInjectModCatalogTests.cs`, `src/ModManager.App/ViewModels/MainViewModel.cs`, `src/ModManager.App/ViewModels/ModRowViewModel.cs`, `src/ModManager.App/MainWindow.xaml`

**Root cause:** Three spots assert "missing proxy = NEEDS this loader" unconditionally: FrameworkDeps.cs:92 Note "Most ER mods need this"; ModRowViewModel.cs:236-237 renders red "NEEDS X"; MainViewModel.cs:371-389 assigns the ELM "missing" to EVERY direct-inject row, including self-proxying mods (Seamless, ReShade) → false red "NEEDS ELDEN MOD LOADER". The disk-probe half is already correct (FrameworkDeps.CheckPresent credits dinput8/version/winhttp).

**Fix:** Core (test-first) — reword the ELM Note to conditional ("...Only needed if you have a direct-inject mod that doesn't bring its own proxy; Seamless Co-op and ReShade do."); add `bool SelfProvidesProxy` to `KnownDirectInjectMod`, set true for `reshade` + `seamless-coop`. App — skip the ELM assignment for rows whose mod `SelfProvidesProxy`; reframe the chip from red "NEEDS X" to amber "MAY NEED X" (retarget brush from `ThemeDanger` to a warning/soft accent).

- [ ] **Step 1:** failing `FrameworkDepsCatalogTests.Elden_mod_loader_note_is_conditional_not_asserted` (Note lacks "Most ER mods need this", contains "Only needed"/"if"). Run → fails.
- [ ] **Step 2:** reword Note. Re-run → green; `Fromsoft_dll_proxy_entry_displays_as_Elden_Mod_Loader` still green (Note keeps the "Elden Mod Loader" substring).
- [ ] **Step 3:** commit `fix(frameworks): reword Elden Mod Loader note from asserted to conditional`.
- [ ] **Step 4:** failing `KnownDirectInjectModCatalogTests.Seamless_and_reshade_self_provide_proxy` + `Regulation_mod_does_not_self_provide_proxy`. Run → fails to compile.
- [ ] **Step 5:** add `SelfProvidesProxy` to the record + `Mk` factory; set true for reshade + seamless-coop. Re-run + full suite green (watch catalog-shape/count tests).
- [ ] **Step 6:** commit `feat(catalog): flag self-proxying direct-inject mods (Seamless, ReShade)`.
- [ ] **Step 7 (App):** in `ReloadModsAsync` fromsoft non-folder branch, set `primaryMissing = null` when the row's mod `SelfProvidesProxy`. Build x64.
- [ ] **Step 8 (App):** chip "NEEDS " → "MAY NEED "; retarget MainWindow.xaml:380/383 brushes to warning/soft. Build x64.
- [ ] **Step 9:** smoke entry + commit `fix(viewmodel): conditional loader hint — skip self-proxying mods, soften assertion`.

**Design decisions (defaults chosen):** interpret "show only when struggling" as "direct-inject mod present that needs a proxy AND no proxy detected AND mod doesn't self-provide" (testable); softer "MAY NEED" amber framing; leave the engine-level MissingFrameworks banner as-is for now. **NEEDS MAINTAINER:** confirm the exact warning brush token (`ThemeWarning` vs `ThemeAccentSoft`).

**Risk:** low, reversible. Detection metadata + display string + brush only; no write path. Run full Core suite after the record-field change.

---

## Task 5: Exe launch target doesn't ensure Steam is running (PRIORITY 4, medium)

**Files:** `src/ModManager.Core/LaunchGuard.cs`, `tests/ModManager.Tests/LaunchGuardTests.cs`, new `src/ModManager.App/Services/ISteamPresence.cs`, `src/ModManager.App/Services/SteamService.cs`, `src/ModManager.App/Services/LauncherService.cs`, `src/ModManager.App/App.xaml.cs`

**Root cause:** `LauncherService.Launch(LaunchTarget,...)` (LauncherService.cs:133-143) `Process.Start`s the exe with zero Steam awareness. A Steam-gated exe launcher (e.g. `ersc_launcher.exe`) with Steam closed silently fails — the game's Steam DRM bootstrap never completes. (This is what bit Este: launching Seamless with Steam closed.)

**Fix:** Core — `LaunchGuard.NeedsSteamRunning(GameEntry game, LaunchTarget target)` → `target.Kind == "exe" && !string.IsNullOrEmpty(game.SteamAppId)` (steam:// targets self-start Steam, excluded). App — `ISteamPresence { bool IsRunning(); bool EnsureRunning(string steamAppId, TimeSpan timeout); }` on `SteamService` (IsRunning via `Process.GetProcessesByName("steam")`; EnsureRunning starts `steam://run/<appId>` and polls). Inject into `LauncherService`; before `Process.Start`, if `NeedsSteamRunning` and Steam isn't up, `EnsureRunning` or refuse with a clear status. Default the ctor param to a `NullSteamPresence` so existing call sites/tests keep compiling.

- [ ] **Step 1:** failing `NeedsSteamRunning_true_for_exe_target_on_a_steam_game`.
- [ ] **Step 2:** failing `NeedsSteamRunning_false_for_steam_url_target` + `NeedsSteamRunning_false_when_no_steam_app_id`.
- [ ] **Step 3:** run → all three fail (method absent).
- [ ] **Step 4:** add the pure boolean. Run → green incl. CorePurityTests.
- [ ] **Step 5:** commit `feat(launch): Core verdict — exe launcher on a Steam game needs Steam running first`.
- [ ] **Step 6 (App):** add `ISteamPresence` + `NullSteamPresence`; implement on `SteamService`; thread the active `GameEntry` into `Launch`; gate `Process.Start` behind the verdict + `EnsureRunning(... , 20s)`; register DI in App.xaml.cs. Build x64.
- [ ] **Step 7:** smoke entry (close Steam, launch via exe target → Steam starts + game launches, or a clear "Start Steam first" status — never a silent no-op) + commit.

**Design decisions (defaults chosen):** auto-start Steam (`steam://run/<appId>`) + poll ~20s, refuse only if never ready; don't gate steam:// targets; ensure the poll runs off the UI thread (launch handlers are async).

**Risk:** contained to the launch path; no file ops. NullSteamPresence default keeps existing tests compiling.

---

## Task 6: Mod-loader row hidden while its mods are active (PRIORITY 9, low)

**Files:** `src/ModManager.Core/DirectInject.cs`, `src/ModManager.Core/Mod.cs`, `tests/ModManager.Tests/DirectInjectLoaderTests.cs`, `src/ModManager.App/Services/DirectInjectService.cs`, `src/ModManager.App/ViewModels/MainViewModel.cs`, `src/ModManager.App/ViewModels/ModRowViewModel.cs`, `src/ModManager.App/MainWindow.xaml`

**Root cause:** intentional hide-until-empty filter at `DirectInjectService.cs:38` drops the loader row whenever `mods\` holds ≥1 DLL, so the loader only reappears once every dependent mod is disabled.

**Fix:** keep the intent but make it consistent — always render the loader row, lock its toggle while it hosts active mods, with a tooltip. Move the decision into a pure `DirectInject.ComposeEnabledRows(...)`; add transient `string? Mod.ToggleLockReason` (set when loaderMods.Count>0, never serialized); App gates `canToggle` on it + binds a tooltip; delete the line-38 filter.

- [ ] **Step 1 (RED):** `DirectInjectLoaderTests.Loader_row_is_present_and_toggle_locked_while_mods_are_active`. Fails to compile.
- [ ] **Step 2 (GREEN):** add `Mod.ToggleLockReason` + `DirectInject.ComposeEnabledRows` (always keep loader row, set reason when mods\ has DLLs). Filter green.
- [ ] **Step 3 (RED):** `Loader_row_toggle_is_unlocked_when_no_active_mods` + `Detected_mods_still_surface_as_their_own_rows`.
- [ ] **Step 4 (GREEN):** finish `ComposeEnabledRows`. Filter green; CorePurityTests green.
- [ ] **Step 5:** commit `feat(direct-inject): compose enabled rows with always-visible toggle-locked loader`.
- [ ] **Step 6 (App):** delete the line-38 filter, delegate to `ComposeEnabledRows`, map `ToggleLockReason`, gate `canToggle` (MainViewModel.cs:379), add `ToggleLockTooltip`, bind `ToolTipService.ToolTip` on the disabled switch. Build x64.
- [ ] **Step 7:** smoke entry + commit `feat(direct-inject): always show DLL-loader row, lock its toggle while it hosts active mods`.

**Design decisions (defaults chosen):** pure Core `ComposeEnabledRows`; dedicated `ToggleLockReason` (not overloading `ReadOnly`, which drives the managed badge).

**Risk:** low, reversible (re-add the one-line filter). Transient field, never serialized.

---

## Task 7: Safe Clear success gives no restore-point confirmation (PRIORITY 9, low)

**Files:** new `src/ModManager.Core/RestorePoints/SafeClearSummary.cs`, new `tests/ModManager.Tests/RestorePoints/SafeClearSummaryTests.cs`, `src/ModManager.App/SafeClearDialog.xaml`, `src/ModManager.App/SafeClearDialog.xaml.cs`

**Root cause:** `SafeClearDialog.OnPrimary` only opens the `ResultBar` InfoBar on failure/exception (Severity=Error); on success it sets `Cleared=true` and returns without `args.Cancel`, so the dialog closes immediately — the user never sees confirmation. `SafeClearResult.RestorePointTimestamp` is available on success (yyyyMMdd-HHmmss).

**Fix:** Core — pure `SafeClearSummary.SuccessMessage(SafeClearResult)` (parse timestamp via `TryParseExact`, render friendly stamp + "Find it in Settings → Restore points."; handle no-restore-point + warnings + unparseable fallback). App — add a dedicated `SuccessBar` InfoBar (Severity=Success); on success set its message, `args.Cancel=true`, relabel the button to "Done" so the confirmation is seen before close.

- [ ] **Step 1:** failing `SafeClearSummaryTests.SuccessMessage_with_timestamp_names_the_restore_point_and_points_to_settings`.
- [ ] **Step 2:** run filter → fails to compile.
- [ ] **Step 3:** add `SafeClearSummary.SuccessMessage`.
- [ ] **Step 4:** run filter → green.
- [ ] **Step 5:** add `..._without_restore_point...`, `..._with_warnings...`, `..._with_unparseable_timestamp...`, red→green each.
- [ ] **Step 6:** App — add `SuccessBar`, wire success branch with `args.Cancel=true`. Build x64; CorePurityTests green.
- [ ] **Step 7:** smoke entry + commit on `fix/safe-clear-success-confirm`: `fix(restore): confirm restore point on Safe Clear success`.

**Design decisions (defaults chosen):** keep the dialog open on success (auto-close can't show a toast); formatter in Core so it's unit-tested + reuses the canonical "Settings → Restore points" wording.

**Risk:** tiny, reversible. New Core file + test touch nothing existing; one InfoBar; success path now waits for dismissal.

---

## Self-review notes

- Every task is test-first in Core where testable; WinUI wiring is explicitly marked build-verified + manual-smoke.
- Open maintainer decisions are flagged inline (Task 4 brush token is the only hard one; all other forks have a chosen default).
- All commit messages end with the `Co-Authored-By: Claude Opus 4.8 (1M context)` trailer per repo convention.
- Source findings + full rationale: 626 decision log entries dated 2026-05-28.
