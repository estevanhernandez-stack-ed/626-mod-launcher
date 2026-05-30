# Pending smoke tests

Running log of post-merge smoke needs the orchestrator can't verify automatically. Each entry: what shipped, what to test, why it matters. Strike entries through (or move to a "Cleared" section) once smoked.

> **2026-05-29 remediation status (verified against `git log` + code).** The 2026-05-28 live ER + Safe Clear pass surfaced 7 issues (`docs/superpowers/plans/2026-05-28-smoke-remediations.md`, decision log 2026-05-28). The priority-1/2 fixes are now **merged** — the old BLOCKED banners below have been cleared where the fix shipped:
>
> | Remediation | Status | Evidence |
> |---|---|---|
> | Task 1 — IniEdit bare-CR corruption | ✅ merged | `cf8aa3d` (PR #81) — `DetectNewline`/`NormalizeNewlines`, `.bak` byte-exact |
> | Task 2 — Safe Clear refusal fails open (bootstrapper games) | ✅ merged | `db1b6b2` + `bce00ae` (PR #82) — engine-runtime-exe match + fail-closed probe |
> | Task 3 — Play-vanilla with direct-inject DLLs crashes | ✅ merged | `946f4dc` + `66907a1` (PR #83) — vanilla step-aside verdict |
> | F3 — Seamless catalog config path wrong | ✅ merged | `bacc3d6` — covers all 3 Seamless layouts |
> | Task 4 — loader "required" → conditional framing | ✅ PR #88, live-smoked | `SelfProvidesProxy` + amber "MAY NEED"; amber confirmed on screen 2026-05-30 |
> | Task 5 — exe launch doesn't ensure Steam running | ✅ PR #89, live-smoked | `NeedsSteamRunning` + `SteamService.IsRunning`/`EnsureRunning`; Steam-closed → auto-start → Seamless launched, confirmed 2026-05-30 |
> | Task 6 — loader row hidden while its mods active | ✅ PR #93, decoupled + merged 2026-05-30 | inline distinguished LOADER row; toggling the loader moves only its own `dinput8.dll` — DECOUPLED (the cascade was built, then dropped after live testing showed the hosted `mods\` mods are inert-but-harmless with the loader off) |
> | Task 7 — Safe Clear success gives no confirmation | ⛔ OPEN | no `SafeClearSummary` / `SuccessBar` |
>
> Net: the merged fixes need a **live re-smoke** (code-verified, not yet exercised on the rig). Task 7 is the one remaining ⛔ item. Safe Clear is still only 1-of-8 live-smoked. Merged-fix re-smokes are consolidated in the new section at the bottom.

---

## PR #49 — BND4 file-table walk (merged 2026-05-26)

> **STATUS — SMOKED 2026-05-27 (per Este).** ER save editing exercised on real saves and working; steps 1-3 considered cleared. Confirm step 4 (edit -> in-game round-trip) if not already run.

**Shipped:** ER save editor's reader + writer now locate save sections by BND4 entry NAME (`USER_DATA011` for the save header, `USER_DATA000`..`USER_DATA009` for the 10 character slots) instead of hardcoded byte offsets like `0x019003B0`. Future ER patches that reshape the file layout still work; patches that rename the save-header entry fail loud with `InvalidDataException` listing the names that WERE found — never silently corrupt a save.

**Synthesized-fixture tests cover** the read/write contract, the resilience claim (12-entry layout + relocated section + renamed header), and the per-entry bounds check at the parser seam. **What they can't cover:** real bytes shipped by FromSoft. The read path is the higher-regression-risk class.

**Smoke steps:**
- [ ] Open a real Elden Ring save in the Saves dialog → Characters section populates with correct name / level / runes / stats matching in-game values.
- [ ] Open a Seamless Co-op save (`.co2`) → Characters section populates correctly.
- [ ] Open a real save with NO active characters (every slot empty) → empty list, no exception toast.
- [ ] (Optional, higher value) Edit a stat on a real save → confirm the edit lands, a snapshot appears in the Snapshots list, and the in-game character reflects the change after launch.

---

## PR #51 — Mod-dependency detection (merged 2026-05-26)

> **STATUS — BLOCKED on remediation Task 4 (still OPEN as of 2026-05-29).** The "NEEDS Elden Mod Loader" chip framing is changing to conditional: a loader is not required when a DLL proxy / Seamless / ReShade is already present. Task 4 (`SelfProvidesProxy` + "MAY NEED" framing) is not yet built — `FrameworkDeps.cs:92` still asserts "Most ER mods need this." Smoking now would re-assert the wrong thing. Re-smoke after Task 4 ships.

**Shipped:** Every mod row in a framework-gated game (UE4SS, BepInEx, SMAPI, ME2, DLL proxy, Forge/Fabric) gets a red `NEEDS X` chip with a clickable get-link when the framework isn't installed. Post-drop status line names the missing framework and host (`". Heads up: this mod needs UE4SS — get it at github.com."`). Pure-core probe covered by 13 unit tests; App wiring verified by build only.

**Smoke steps:**
- [ ] Switch to Windrose on a machine where `R5/Binaries/Win64/ue4ss/UE4SS.dll` does NOT exist → every mod row shows a red `NEEDS UE4SS` chip; clicking opens the UE4SS releases page in the browser.
- [ ] Restore the UE4SS folder + click Redetect → chips disappear (Redetect funnels through `ReloadModsAsync`, which triggers the framework refresh).
- [ ] With UE4SS still missing, drop a `.pak` mod onto the window → post-drop status line ends with `. Heads up: this mod needs UE4SS — get it at github.com.`
- [ ] Switch to Elden Ring with neither `dinput8.dll` nor `modengine2_launcher.exe` present → direct-inject rows show `NEEDS DLL PROXY...`; folder rows (if any) show `NEEDS MOD ENGINE 2`. Drop a direct-inject mod → drop-line says "needs DLL proxy" (not ME2 — the chip-vs-drop-line agreement fix in commit `534c507`).
- [ ] Install `dinput8.dll` → DLL proxy chip disappears on direct-inject rows; ME2 chip remains on folder rows.

---

## PR #?? — Mod dashboard (Windrose-first tools + INI editor) (merged 2026-05-27)

> **STATUS — UNBLOCKED 2026-05-29 (CRLF fix merged `cf8aa3d`).** The INI editor's bare-CR corruption is fixed in Core (`DetectNewline`/`NormalizeNewlines`; `.bak` stays byte-exact). The INI-editor steps below are ready for live re-smoke. Tools-panel steps were always standing — both groups are live-smokable now.

**Shipped:** Per-game **mod dashboard** surface above the mod list. Two day-one features:

1. **Tools panel** — drop-zip installable third-party tools (WSE Save Editor + WSE Save Fix day-one catalog entries, by RimmyCode / WSE Project). Smart classifier in the existing drop pipeline routes tools to `ToolIntake.Install` (extract → pick runnable → register). Save-editing tools auto-snapshot the save folder before launching. Right-click a tool button → configure dialog (change runnable, toggle EditsSaves, rename, uninstall). Known-but-uninstalled catalog tools show `[Get ↗]` chips that open Nexus.
2. **Inline INI editor** — pencil icon on any mod row whose folder contains `.ini` files. Click → in-app text editor → save creates a `.bak` first (snapshot-before-write, ten retained per file). Restore previous reads the most recent `.bak`.

**Honor-the-builders:** NOTICE attribution block + Settings → About "Installed tools" section + tool button tooltip — all explicitly say "catalog metadata only, never bundled."

**Synthesized-fixture tests cover** classifier + intake + registry round-trip + INI snapshot retention. **What they can't cover:** the actual click → snapshot → Process.Start → exit detection flow on Windows, and the WinUI dialogs.

**Smoke steps:**
- [ ] Switch to Windrose, drop a tool zip whose filename contains "WSE Save Editor" → toast confirms catalog-matched install, button appears in the tools row, snapshot ran before any extraction.
- [ ] Drop an unknown utility zip with a single `.exe` and no mod signatures → toast says "Installed [name] as a tool for [game]", button appears (heuristic install).
- [ ] Click an installed tool tagged `EditsSaves: true` → status reads "Snapshotting save before launching…" → a new snapshot lands in the Saves dialog → tool process launches → close it → toast updates with the snapshot label.
- [ ] Right-click a tool button → configure dialog opens with current values populated → change the runnable → save → next click launches the new runnable. Click Uninstall → install folder + registry entry both removed.
- [ ] With a clean Windrose (no tools installed yet) → tools row shows `[Get WSE Save Editor ↗]` and `[Get WSE Save Fix ↗]` chips → click one → Nexus page opens in browser.
- [ ] Find a mod with `.ini` files → pencil icon appears on the row → click → editor dialog opens with file contents → edit + save → confirm a `.bak` lands in `_626mods/<game>/.ini-history/<modId>/` → re-open editor → Restore Previous loads the prior contents into the textbox → Save commits the restored state (now there are TWO `.bak`s).
- [ ] Drop a `.pak` mod zip → routes through normal mod intake (NOT tool intake) — confirms the mod-signature-wins rule.
- [ ] Settings → About → confirm the "Installed tools" section lists each installed tool with the never-bundled disclaimer.

**Why these matter:** every layer below the dialogs is unit-tested, but the click-to-launch flow, Process.Exited threading marshal, snapshot-then-launch ordering, and the WinUI dialog plumbing only get exercised on a real Windows machine.

---

## PR #?? — Framework intake (Elden Mod Loader) (merged YYYY-MM-DD)

> **STATUS — BLOCKED on remediation Task 4 (still OPEN as of 2026-05-29).** Live ER session showed the "required Elden Mod Loader" framing drove an unnecessary install (red tag, degraded setup) — ELM is not required when a proxy is already present. Task 4 (conditional framing) is not yet built. The install/uninstall mechanics (steps 2-3, 5-6) are independent of the framing and could be smoked now; the chip-text steps (1, 4) wait for Task 4.

**Shipped:** Per [`docs/superpowers/specs/2026-05-27-framework-intake-design.md`](../superpowers/specs/2026-05-27-framework-intake-design.md):

1. **ER chip rename** — `NEEDS DLL PROXY (DINPUT8/VERSION/WINHTTP)` → `NEEDS Elden Mod Loader`. Get-link points at Nexus mods/117.
2. **Drop-zip framework intake** — new Pre-check 0 in `AddModsAsync` runs before the existing engine-specific branches. `KnownFramework.Classify` matches catalog entries (day-one: Elden Mod Loader for FromSoft) by signature-files-all-present. On match: confirmation dialog → `FrameworkInstaller.Install` at game root with replaced files backed up to `_626mods/<game>/frameworks/<id>/backup/`. Manifest written atomically (camelCase JSON).
3. **Looks-like-framework nudge** — when a FromSoft zip has a proxy DLL at its root but doesn't match the catalog: nudge dialog with a GitHub feedback link.
4. **Settings → Installed frameworks** — lists every framework the launcher installed across all per-game data dirs. Per row: display name, author + install time + path, Get-link, Uninstall button (restores backup + removes installed files + tears down the framework dir).

**Honor-the-builders:** NOTICE attribution block calls out Elden Mod Loader (by TechieW) with "metadata only, never bundled" language. Settings → Installed frameworks shows author credit + Get-link per row.

**Synthesized-fixture tests cover:** `KnownFramework.Classify` (catalog match + engine-scoping + looks-like heuristic + nested-DLL no-match), `FrameworkInstaller.Install` (extraction + backup + manifest + forbidden-path refusal + directory-traversal refusal + no-overwrite-no-backup-snapshot), `FrameworkRegistry.List + Uninstall` (manifest enumeration + file restore + idempotent partial state). **What they can't cover:** the actual dialog flow on Windows + the post-install chip-disappear via `ReloadModsAsync`.

**Smoke steps:**

- [ ] Switch to Elden Ring without Elden Mod Loader installed → every direct-inject mod row reads `NEEDS Elden Mod Loader`; clicking the chip opens `https://www.nexusmods.com/eldenring/mods/117` in the browser.
- [ ] Drop the ELM zip into the launcher → confirmation dialog opens with the file list + author credit → confirm → toast: `Installed Elden Mod Loader (N files at game root)` → chip disappears on next reload → ELM's `dinput8.dll` is at the game root.
- [ ] Settings → Installed frameworks → ELM row visible with author + install date + path → click Uninstall → ELM files gone from game root; any backed-up files restored; chip returns on next reload.
- [ ] Make a copy of `dinput8.dll`, zip it alone (so it doesn't match ELM's signature which requires `mod_loader_config.ini` too) → drop it → feedback nudge dialog appears → "Open feedback link" launches the GitHub issue template; "Continue as mod" or Cancel both work.
- [ ] Manually craft a zip with `eldenring.exe` inside → drop it → install refused with a toast naming the forbidden path; no files extracted at game root.
- [ ] Drop a regular `.pak` or direct-inject mod zip → falls through to the existing intake unchanged; no framework confirmation dialog appears.

**Why these matter:** the install + uninstall paths touch the game root with file overwrites — the unit tests verify the backup + rollback math but the actual Windows file ops + the dialog flow only exercise on a real machine.

---

## PR #?? — Unified-catalog Phase 1: direct-inject mod config discovery (F3) (merged YYYY-MM-DD)

> **STATUS — UNBLOCKED 2026-05-29 (both blockers merged).** (a) Catalog path fixed in `bacc3d6` — the resolver now checks all three Seamless layouts, `SeamlessCoop/seamlesscoopsettings.ini` first ([KnownDirectInjectMod.cs:56](../../src/ModManager.Core/Catalog/KnownDirectInjectMod.cs#L56)). (b) Bare-CR corruption fixed in `cf8aa3d`. Steps below are ready for live re-smoke on a real Seamless install.

**Shipped:** Per [`docs/superpowers/specs/2026-05-27-unified-catalog-direct-inject-config-design.md`](../superpowers/specs/2026-05-27-unified-catalog-direct-inject-config-design.md):

- New `KnownDirectInjectMod` schema in `ModManager.Core.Catalog` (kind-tagged; future phases fold Tools + Frameworks into the same shape).
- Migrated `DirectInject.Catalog` from the private `Signature` array — same detection behavior, plus a new `ConfigPaths` field per entry. Seamless Co-op's path: `SeamlessCoop/seamlesscoopsettings.ini` + `ersc_settings.ini`.
- `DirectInjectModConfigResolver` looks up a mod's known config files, applies per-user override, returns only paths that exist on disk.
- Row builder hook — direct-inject rows now get a pencil icon when the catalog's known INI exists on disk (Seamless Co-op specifically).
- Settings → Direct-inject mod configs — minimum-viable override UX. Per-row "Override…" file picker; saved override re-renders rows on dialog close.

**Smoke steps:**

- [ ] On ER with Seamless Co-op installed at `<gameRoot>/Game/SeamlessCoop/` → mod list shows the Seamless Co-op row → pencil icon visible → click → INI editor opens with the actual `seamlesscoopsettings.ini` contents → edit + save → `.bak` lands under `<gameData>/.ini-history/seamless-coop/`.
- [ ] Custom-location scenario: move your Seamless INI to a different drive (e.g. `D:\some-other-place\settings.ini`) → pencil icon disappears. Settings → Direct-inject mod configs → row for Seamless's INI shows the catalog default → click "Override…" → pick the moved INI → status reads "Override saved for Seamless Co-op → D:\..." → close Settings → pencil icon returns; clicking it edits the override location.
- [ ] On ER WITHOUT Seamless installed → no Seamless row, no pencil icon, no errors. (Confirms the resolver returns empty when the catalog default doesn't exist on disk and no override is set.)
- [ ] Folder-tracked mods on any engine still get their existing recursive `*.ini` glob behavior — pencil icon visible for any mod with `.ini` files in its folder.

**Why these matter:** the resolver path is unit-tested but the row-render hook + Settings picker integration only exercise on a real Windows machine with a real Seamless install.

---

## Safe Clear + Restore (Phase 1B) (merged YYYY-MM-DD)

> **STATUS — 1 of 8 live-smoked; Scenario 4 fix merged, needs re-smoke (2026-05-29).** Step-1 orientation PASSED. Scenario 4 (game-running refusal) FAILED on 2026-05-28 — refusal didn't fire for bootstrapper-launched games — but is now **fixed** (`db1b6b2` matches engine runtime exes like `eldenring.exe`/`start_protected_game.exe`; `bce00ae` fails closed when the probe is unavailable, PR #82). Re-smoke Scenario 4 to confirm live. Remaining 6 scenarios STANDING and destructive on a live rig — run them only with a backup. Note: Scenario "Safe Clear success confirmation" (remediation Task 7) is still OPEN — the dialog closes without a success message.

**Shipped:** Per [`docs/superpowers/specs/`](../superpowers/specs/) — Settings → Reset launcher surface. User picks a clear mode ("Return to vanilla" or "Leave mods active"), optionally creates a restore point (timestamped archive of game folder + mod data + Nexus auth), optionally keeps Nexus connected, then executes. A `626-launcher-how-to-launch.txt` sheet is written at the game root describing the resulting state. Restore points are listed in Settings → Restore points; restoring one reverses the clear and removes the sheet. A `safe-clear.lock` file guards crash recovery: sealed point → offer restore; unsealed/missing → offer discard.

**Synthesized-fixture tests cover:** archive round-trip (tar/compress + extract), lock file state machine (sealed vs. unsealed), sheet text generation per clear mode, conflict detection (game root mismatch), Nexus-exclude logic, and forbidden-path gate during restore. **What they can't cover:** the WinUI dialog flow, process-running detection against a live game, actual Windows file moves + archive creation at scale, recovery dialog routing on next launch, and the end-to-end round-trip across a real two-drive install.

**Smoke steps:**

- [ ] **Vanilla clear + restore round-trip (two-drive ideal):** With a game on D: and `%APPDATA%` on C:, and a game that has at least one mod + a framework (e.g. ELM) + a direct-inject mod (e.g. Seamless Co-op): Settings → Reset launcher → "Return to vanilla", Create restore point ON, Keep Nexus ON → Clear. Confirm: dialog reports success + closes; launcher drops to empty-state; the game folder is vanilla + launches normally; `626-launcher-how-to-launch.txt` exists at the game root and says the game was returned to vanilla; Nexus still connected. Then Settings → Restore points → the new timestamped point is listed (games + size) → Restore → confirm → the game + its mods come back; the in-game-folder sheet is removed.

- [ ] **Leave-mods-active clear:** Reset → "Leave mods active" → Clear. Confirm: game stays modded + launchable; the sheet says "your mods are still active, launch with \<the mod launcher\>" + the required-launcher caveat; restore brings the launcher view back.

- [ ] **Keep-Nexus OFF:** Reset with the Keep-Nexus toggle OFF → Clear. Confirm: after the clear, Nexus is disconnected (re-auth required on next launch); `nexus.json` is absent and was NOT copied into the restore point.

- [ ] **Game-running refusal:** Launch the game process, then Settings → Reset → Clear. Confirm: dialog shows a refusal ("Close \<game\> before resetting") in the InfoBar and stays open; nothing is archived or moved; the game process is unaffected.

- [ ] **Skip archive:** Reset with "Create a restore point" UNCHECKED → Clear. Confirm: launcher resets; Settings → Restore points shows no new entry; the mod files were MOVED (not deleted) — verify the mod files still exist on disk in the restore-points working directory; nothing destroyed.

- [ ] **Interrupted-clear recovery:** Simulate a leftover `%APPDATA%\ModManagerBuilder\safe-clear.lock` before next launch. Two sub-cases: (a) lock points at a SEALED restore point → recovery dialog on next launch offers "Restore your saved setup" → confirm routes correctly into restore flow; (b) lock points at an UNSEALED or missing restore point → recovery dialog offers "Discard the incomplete archive" → confirm cleans up the partial state and continues to normal launch.

- [ ] **Restore conflict:** Clear a game (producing a restore point), then re-add a game with the same game id but a DIFFERENT game root path, then Settings → Restore points → Restore the earlier point. Confirm: restore is refused with a conflict message (the game root moved); nothing is overwritten; the restore point is intact.

- [ ] **Off-boarding sheet honesty (sideloaded mods):** On a game with at least one sideloaded mod (no recorded source URL), run a clear. Confirm: the `626-launcher-how-to-launch.txt` sheet lists those mods as "source not recorded — sideloaded" and leads with "your mods are preserved in the restore point" — a missing URL never implies a missing mod.

**Why these matter:** the archive + lock + restore state machine is unit-tested, but the WinUI dialog flow, process-running detection against a live game EXE, actual cross-drive file moves + archive creation at scale, and the recovery-dialog routing on next cold launch only exercise on a real Windows machine with a real game install.

---

## ReloadModsAsync unification — App behavior unchanged (2026-05-29)

After unifying mod listing on `ModListing.Resolve` (App + MCP share one read path), verify each engine world still renders identically:
1. [x] **Direct-inject world** — switch to elden-ring (Seamless + EML installed). **CLEARED 2026-05-29 (per Este): "looks the same as before"** on a live dev instance — Seamless Co-op + EML-loaded DLL mods, same chips, no bare "DLL mod loader" row.
2. [ ] **Scanner world** — switch to a BepInEx game (e.g. R.E.P.O.).
   - Expected: mod list unchanged; `classification.json` still refreshes on disk (the migrate + persist writes stayed App-side), no stale entries.
3. [ ] **Mod Engine 2 world** — switch to an ME2 config-backed game if available.
   - Expected: mod list unchanged from before the refactor.
4. [ ] Toggle a direct-inject mod off/on.
   - Expected: still reversible (moves to holding, returns), no behavior change.

---

## Loader visible + independently toggleable (DECOUPLED — 2026-05-30)

The DLL mod loader (Elden Mod Loader = `dinput8.dll`) now stays a visible row when hosting mods, marked with a **LOADER** chip, and toggling it moves **only its own `dinput8.dll`** — the hosted `mods\` mods are left in place. (The cascade this entry originally described was dropped: live testing proved the hosted `mods\` mods sit inert-but-harmless when the loader is off — they don't load, but cause no crash and the game launches fine, so dragging them to holding solved a non-problem. See PR #93 / handoff `docs/superpowers/handoffs/2026-05-30-decouple-loader-toggle.md`.) Core is unit-tested (`DirectInjectLoaderRowTests`, 6 cases: row present/tagged, disabled-loader tagged, disable-moves-only-dinput8, re-enable-restores-only-dinput8, transient flag); these confirm the App wiring + UX on a real install.

1. **Loader visible + chipped** — ER with Seamless + EML installed (`Game/dinput8.dll` + `Game/mods/*.dll`): the mod list shows a **DLL mod loader** row with a cyan **LOADER** chip, alongside its hosted mods (AdjustTheFov, etc.). Previously the loader row vanished — this is the fix.
2. **Toggle the loader OFF → only `dinput8.dll` moves** — toggle the loader row off → its own `dinput8.dll` goes to holding; the hosted `mods\*.dll` stay exactly where they are (still listed, just inert without the loader). The game's own files (eldenring.exe) are untouched. Game still launches.
3. **Toggle the loader ON → `dinput8.dll` restored byte-for-byte** — the hosted mods never moved, so nothing else changes.
4. **Individual hosted-mod toggle unchanged** — toggling a single hosted mod (e.g. AdjustTheFov) off/on still works independently.
5. **Toggle while the game is running** (the rollback path) — launch ER, then toggle the loader OFF → it should fail (the running-game guard) and leave `dinput8.dll` in the play folder (nothing stranded in holding). Same per-mod guard every other direct-inject row uses.

---

## 2026-05-28 remediation fixes — merged, need live re-smoke (2026-05-29)

These four shipped after the live session and are code-verified only. Each needs one live confirmation on the rig.

- [x] **IniEdit CRLF (Task 1, `cf8aa3d`) — PASSED 2026-05-30.** Edited Seamless `ersc_settings.ini` via the pencil → Save → on-disk bytes still CRLF (CR=59, LF=59, **bare-CR=0**); a byte-exact `.bak` landed in `_626mods/elden-ring/.ini-history/`. Forensic confirmation the bug was real: pre-fix `.bak` snapshots from 05/28 show **bare-CR=53/59**, every post-fix save shows bare-CR=0.
- [ ] **Safe Clear game-running refusal (Task 2, PR #82):** launch ER via Seamless (`ersc_launcher.exe` exits, `eldenring.exe` runs) → Settings → Reset → Clear → must **Refuse** ("Close ELDEN RING before resetting"); nothing archived or moved. Also: if the game-running probe can't enumerate, the clear refuses rather than proceeding.
- [x] **Play-vanilla step-aside (Task 3, PR #83) — FAILED then FIXED (PR #90), PASSED 2026-05-30.** First smoke: picking vanilla with `dinput8.dll` active crashed straight to `0xc000007b`, **no dialog**. Root cause: `AnyActiveProxyDll` scanned the mod-row list, which drops the loader row (owner of `dinput8.dll`) when its `mods\` has contents — guard went blind. Same root cause as the loader-vanishes-from-UI bug. Fix (PR #90): guard now checks **physical** top-level proxy-DLL presence via pure `DirectInject.AnyProcessLoadProxy`, decoupled from `Enabled()`. Re-smoke: guard dialog now fires before the crash. ✅
- [x] **Seamless catalog path (F3, `bacc3d6`) — PASSED 2026-05-30** (implicitly, via the IniEdit smoke). The Seamless row's pencil opened the real `SeamlessCoop/ersc_settings.ini` (the LukeYui-middle layout — catalog covers all three) and the edit round-tripped. Pencil found the right file, not a wrong/empty path.

## Still-open remediation backlog

Tracked in `docs/superpowers/plans/2026-05-28-smoke-remediations.md`:

- **Task 4** ✅ shipped (PR #88) + live-smoked — amber "MAY NEED" confirmed.
- **Task 5** ✅ shipped (PR #89) + live-smoked — Steam-closed → auto-start → Seamless launched.
- **Task 6** ✅ shipped (PR #93) + live-smoked — inline distinguished LOADER row; toggling the loader moves only its own `dinput8.dll` (DECOUPLED; the cascade was built then dropped after live testing showed the hosted `mods\` mods are inert-but-harmless with the loader off).
- **Task 7** ✅ shipped (`fix/safe-clear-success-confirm`) — Safe Clear now confirms on success: the Reset-launcher dialog shows a Success InfoBar naming the restore point + pointing to Settings → Restore points, and stays open (button relabels to "Done") so the message is seen before close. Needs live re-smoke (below). **This was the last open remediation — Tasks 1–7 are all shipped.**

---

## Safe Clear success confirmation (Task 7 — 2026-05-30)

Before this, a successful Safe Clear closed the dialog instantly — no confirmation, no pointer to the restore point. Now `SafeClearSummary.SuccessMessage` (pure Core, 4 tests) formats the confirmation and `SafeClearDialog` shows it in the existing `ResultBar` InfoBar switched to `Severity=Success`, keeping the dialog open until the user clicks **Done**.

1. **Vanilla clear + restore point ON** — Settings → Reset launcher → "Return to vanilla (restorable)", "Create a restore point" ON → Clear. Expected: dialog **stays open**; a green InfoBar reads "Reset complete. Saved a restore point from `<friendly date+time>`. Find it in Settings → Restore points."; primary button now says **Done**; clicking Done closes the dialog and the launcher refreshes. Confirm the named restore point actually appears in Settings → Restore points.
2. **Leave-mods-active clear** — Reset → "Leave mods active" → Clear. Expected: same green confirmation + restore-point line + Done button. (Message is mode-neutral — "Reset complete." — by design: the result doesn't carry the end-state, so it never claims "vanilla" on a leave clear.)
3. **Restore point OFF** — Reset with "Create a restore point" UNCHECKED → Clear. Expected: green InfoBar says "Reset complete. No restore point was created." and does NOT point to Settings → Restore points (nothing was saved). Done closes.
4. **With a warning** — if a non-fatal warning occurs (e.g. a Keep-Nexus path that can't copy), the green InfoBar still confirms success + restore point, then appends "Note: `<warning text>`".
5. **Friendly timestamp** — confirm the InfoBar shows a human stamp like `2026-05-30 14:32`, never the raw `20260530-143200`.
6. **Failure path unchanged** — a refused/failed clear still shows the red (Error) InfoBar and keeps the dialog open to retry; the button stays "Clear".

**Why this matters:** the formatter is unit-tested, but the InfoBar render, the keep-open-then-Done flow, the Error→Success severity switch, and the button relabel only exercise on a real WinUI dialog.
