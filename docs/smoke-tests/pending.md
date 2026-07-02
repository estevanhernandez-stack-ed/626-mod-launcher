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

---

## Vortex takeover — break free from Vortex management (2026-06-02)

> **STATUS — no unit-test coverage (WinUI takeover UI).** The ownership probe + archive math (`ToolOwnership`, `VortexManifest`, taken-over registry) are pure-Core and tested; the banner, the on-block dialog, the dismiss-for-session behavior, and the re-deploy banner only exercise on a real WinUI instance against a real Vortex-managed folder.

**Shipped:** Per the Vortex Takeover plan (Task 10). The launcher detects folders a Vortex deploy still owns (marker files `vortex.deployment.*.json` / `__folder_managed_by_vortex`), surfaces a "break free" banner, locks owned mods read-only until taken over, and gates uninstall/toggle behind an on-block dialog. Take-over archives the marker out to `<gameDataDir>/vortex-takeover/<location>/` and records the folder in `taken-over.json` (camelCase) — the archive + registry are the reversible record (no UI undo yet). Re-deploy is detected separately and never re-locks an already-taken-over folder.

**Smoke steps:**

1. **Owned folder → banner + read-only rows.** With a game whose mod folder still holds a Vortex marker (`vortex.deployment.*.json` or `__folder_managed_by_vortex`), open the launcher on that game. EXPECT: a banner "Some folders here are managed by Vortex" with "Take them over" / "Dismiss" appears above the mod list, and mods in that folder show as read-only (no uninstall/toggle).
2. **Take them over.** Click "Take them over". EXPECT: status line confirms the takeover; the banner disappears; the folder's mods become managed (uninstall/toggle now available). On disk, the Vortex marker is moved out to `<gameDataDir>/vortex-takeover/<location>/` (folder no longer reads as owned), and `taken-over.json` lists the folder.
3. **On-block dialog (uninstall in a still-owned folder).** Try to uninstall a mod in a still-owned folder WITHOUT using the banner. EXPECT: the on-block dialog "Vortex manages this folder — take it over?" appears; confirming takes over the folder then proceeds; "Not now" cancels the uninstall.
4. **Dismiss persists for the session.** Dismiss the banner, then trigger a rescan (switch games and back, or redetect). EXPECT: the dismissed banner stays hidden for the session.
5. **Reversibility check.** After a takeover, confirm the archived marker exists under `vortex-takeover/`. (Undo is not yet surfaced in the UI — the archive + `taken-over.json` are the reversible record.)
6. **Re-deploy case (if you have Vortex).** After taking a folder over, re-deploy in Vortex so a marker reappears. EXPECT: a "Vortex re-deployed into a folder you took over" banner with "Take over again"; the folder's mods STAY managed (not re-locked).

**Why these matter:** the ownership resolver + marker-archive + registry round-trip are unit-tested, but the banner render, the read-only row state, the on-block dialog routing, the dismiss-for-session flag, and the re-deploy-without-re-lock behavior only exercise on a real WinUI instance with a real Vortex-managed install.

---

## PR (feat/vanilla-modded-launch) — honest vanilla vs modded launch (2026-06-03)

> **STATUS — NEEDS LIVE SMOKE.** UI behavior + on-disk step-aside/restore; covered by unit tests at the Core layer (FrameworkDisable, VanillaLaunch, DirectInjectSingleFile) but the launch-button flow + in-game vanilla run can't be unit-verified.

**Shipped:** The launch split-button now tells the truth per engine. "Play vanilla" used to be a static label that lied on file-presence games (pak/UE4SS mods load on any launch). Now it's a real two-mode launch: **Play vanilla** steps EVERY active loader aside (pak mod rows → holding, UE4SS proxy `dwmapi.dll` → `frameworks/ue4ss/disabled-proxy/`, direct-inject proxies → `_626/vanilla-proxy/`) and they STAY aside until **Play modded** restores EXACTLY the prior-active set (a deliberately-off mod stays off — the "8 of 12" guarantee). Stateful, no game-exit detection. One smart split-button whose label tracks on-disk mode; the opposite mode is in the dropdown. Reversible (move-to-holding, never delete; a stash-write failure rolls back the moves).

**Smoke steps (Windrose — pak + UE4SS):**
1. With mods enabled, the button reads "▶ Play (modded)" (or the effective target). Open the dropdown → "Play vanilla (no mods)" is the top item.
2. Click "Play vanilla". EXPECT: status "Vanilla mode — mods stepped aside"; the mod rows show OFF; the button re-labels to "▶ Play vanilla". On disk: pak mods moved to the disabled holding root, `dwmapi.dll` moved to `<gameData>/frameworks/ue4ss/disabled-proxy/`, and `<gameData>/vanilla-stash.json` lists exactly what was active.
3. Launch in-game and confirm it's actually vanilla — no UE4SS console, no pak mods loaded.
4. Back in the launcher, dropdown → "Play modded (restore mods)". EXPECT: the EXACT prior-active set comes back (a mod you'd deliberately left off stays off); button reads "▶ Play (modded)"; `vanilla-stash.json` gone.
5. Manual-toggle-clears-vanilla: step aside (Play vanilla), then manually toggle one mod row back ON. EXPECT: the stash clears and the button reverts to "▶ Play (modded)" (it stops claiming vanilla while a mod is live).
6. FromSoft (Elden Ring) check if available: Play vanilla steps the Seamless/dinput8 proxy aside too (vanilla isn't a lie there either); Play modded restores it.
7. **VARIANT FAMILY (regression — found in live smoke 2026-06-03, fixed):** with a multi-variant family like Faster Ships (FasterShips10 / _B / aaUltraFastShips collapsed onto one row with option chips), have ONE variant enabled. Play vanilla. EXPECT: the ENABLED VARIANT's `.pak` (e.g. `FasterShips10_P.pak`) is moved out of `~mods` — verify on disk, not just the row state. The original bug stepped aside the family's *representative* member (often the wrong/already-off variant) and left the active variant loading. `vanilla-stash.json` must list the real variant name (`FasterShips10`), not the representative. Play modded restores exactly that variant.

**Why it matters:** the label was actively misleading on the exact games that prompted this (Windrose). The reversibility law is load-bearing — a failed step-aside or stash-write must never strand the user's mod set in holding with no record. The auditor found + we fixed the stash-write-rollback gap. **Variant families** were the subtle miss: the family row's representative `Mod` isn't the active variant (that's in the option chips), so `ActiveModRows` had to expand families to their enabled variant members by real name — otherwise the active variant's pak never stepped aside (Faster Ships "seemed off" but still loaded). The in-game run + on-disk variant check are the parts tests can't reach (the App VM isn't unit-testable here).

---

## PR (feat/paks-root-base-game-filter) — loader-less UE-pak games (Witchfire) (2026-06-06)

> **STATUS — NEEDS LIVE SMOKE.** Core logic is unit-tested (PakClassifier, paks-root scan, base-pak guard incl. mirrors, preset auto-detect, Form round-trip); the App add-game flow + the on-disk result need a real-rig pass.

**Shipped:** Loader-less Unreal-pak games (no UE4SS) keep mods directly in `<Project>/Content/Paks` alongside the base game — Witchfire is the case. A new `paks-root` location form lets the launcher manage that folder while filtering base-game paks out (`PakClassifier`: name `pakchunk*-WindowsNoEditor` OR size ≥ 1.5GB). A hard guard refuses to ever move/delete a base-classified pak (covers `loc.Abs` + mirrors, on both the disable-move and uninstall-delete paths). The `ue-pak` add-game detection auto-picks: `~mods`/`LogicMods` subfolder present → loader convention; else → `paks-root` on `Content/Paks`. Single `UePakModLocation` primitive drives both the add-time seeder and the runtime `ModLocator` resolver (no drift).

**Smoke steps (Witchfire — loader-less UE-pak):**
1. Remove Witchfire from the launcher if present (its old entry has the wrong `Content/Paks/~mods` location from before this fix). Re-add it from Steam.
2. EXPECT: the launcher configures the mod location as `Witchfire/Content/Paks` with form `paks-root` (check games.json: `"path": "Witchfire/Content/Paks"`, `"form": "paks-root"`).
3. EXPECT in the mod list: the 2 mods show — `pakchunk30-2x-witchfire` and `zz_Funner_Witchfire`. The base game paks (`pakchunk0-WindowsNoEditor.pak` ~3.9GB, `pakchunk0optional-WindowsNoEditor.pak` ~591MB) DO NOT appear as rows.
4. Toggle a mod off → its pak moves to the disabled holding folder (reversible); toggle back → returns. The base game paks never appear and can't be disabled.
5. Confirm in-game: the 2 mods load as before (this fix only changes management, not how the paks load).

**Why it matters:** the prior add silently pointed Witchfire at a non-existent `Content/Paks/~mods`, so no mods were detected. The base-game filter is reversibility-critical — listing `pakchunk0` as a mod and letting the user disable it would break the game; the guard + classifier ensure the 4GB base pak can never be moved or deleted. App-VM/add-flow behavior isn't unit-testable here (test project can't reference the WinUI App), so the re-add + on-disk check are the parts only a live rig can confirm.

---

## PR #116 follow-on — per-mod UE4SS chip (false-chip fix) (2026-06-06)

> **STATUS — NEEDS LIVE SMOKE.** Core rule unit-tested (FrameworkApplicability.ModNeedsUe4ss); the row-chip gating is App-VM (not unit-testable here) — verify on the rig.

**Shipped:** The "needs UE4SS" chip was assigned engine-wide on every ue-pak row, so plain content paks (Witchfire) were falsely flagged. Now it's per-mod: a pure-Core `ModNeedsUe4ss(mod, locationPath)` returns true only for Lua/script mods (`Loader=="ue4ss"`) and Blueprint LogicMods paks (location leaf == `LogicMods`); the row-builder drops the UE4SS chip for any row that doesn't need it. Plain content paks in `~mods` or a `paks-root` location show no chip.

**Smoke steps:**
1. **Witchfire** (loader-less, plain content paks): the 2 mod rows show NO "needs UE4SS" chip. (Was: both falsely chipped.)
2. **Windrose** with UE4SS NOT installed: a Lua/script mod row (e.g. a `ue4ss/Mods` mod) STILL shows the "needs UE4SS" chip; a LogicMods pak row STILL shows it; a plain `~mods` content pak shows NO chip.
3. **Windrose** with UE4SS installed: no chips anywhere (framework present) — unchanged behavior.

**Why it matters:** UE4SS loads Lua scripts + Blueprint LogicMods; plain content paks load with no framework (there isn't even a UE4SS download for Witchfire). The old engine-wide chip told content-pak users to install something they don't need. The gating is App-VM logic the test project can't reach, so the per-mod-kind behavior is the part only a live rig confirms.

---

## Richer Steam detection (v0.6.2)
- Add Game → pick a popular game installed on Steam (e.g. Cyberpunk 2077): Name, Engine, Mod folder, App ID, AND Game folder all fill; Add works without Browse. Expected: game registers in one step.
- Add Game → pick a popular game NOT installed: Game folder stays blank, Browse still works. Expected: no crash, manual path still possible.
- Add Game → "Quick add from Steam" list shows cover art per game; games with no cached art show the empty placeholder, not a broken image.
- Main-window game switcher (top-bar ComboBox) shows each game's cover art beside its name (open + selected/closed state); non-Steam or no-art games show the placeholder swatch, not a broken image.

## App-wide exception sink (v0.6.2)
- Trigger an unhandled exception in a UI handler (e.g. temporarily throw in a button click): the app does NOT die or freeze, and `%LOCALAPPDATA%\ModManagerBuilder\app-errors.log` gets a timestamped `ui` line. Expected: a logged near-miss, not a silent dead UI. (Behavior is entry-point glue the test project can't reach — only a live rig confirms it.)

## Steam build-update warning (Phase 2)
- Switch to an installed Steam game for the first time on this build: no warning (baseline set silently). Re-switch: still no warning.
- Simulate an update: edit the game's `appmanifest_*.acf` `buildid` to a different value (or let Steam update it), then re-switch to the game in the launcher → the "Steam updated <game> — your installed mods may need rechecking" banner appears.
- Click "Mods rechecked" → banner clears and stays cleared on the next switch (baseline re-recorded). Confirm `games.json` shows the new `lastKnownSteamBuildId`.
- A non-Steam game (no app id) never shows the banner.

## Steam install-state filter (v0.6.2)
- Add Game → "Quick add from Steam": games with a pending Steam **update** (StateFlags=6, e.g. Marvel Rivals / Helldivers 2 / Wallpaper Engine) STILL appear — they're installed, just update-pending. Expected: not wrongly hidden.
- Start downloading a not-yet-installed game in Steam, then open Add Game while it's mid-download → it does NOT appear in the quick-add list. Expected: only fully-installed games are offered.
- Multi-library edge (if reproducible): the same app id installed in one library and mid-move/partial in another → the fully-installed copy is the one listed. (App-side IO + multi-library dedup — not unit-coverable; only a live rig with two libraries confirms.)

## Steam show-all picker (Phase 3)
- Add Game → the quick-add list is ordered most-recently-played first (your recent games at the top).
- Engine-undetected games (Marvel Rivals, Helldivers 2) now appear as "Set up" rows with art, not a plain text note. Clicking "Set up" pre-fills the manual form (name, game folder, app id) and leaves the engine for you to pick; Add then registers it.
- A fully-detected game still one-click-adds via the checkable list unchanged.

## Nexus enrichment — surfaced fields
- A mod identified via Nexus (md5/metadata) shows its endorsement count and a real download count on the row (download count was always blank before).
- A mod whose Nexus page was removed/taken down (available=false) shows a "Removed from Nexus" hint instead of a dead link.
- metadata.json carries nexusModId / nexusFileId / version for identified mods (persisted, survives a rescan — the groundwork for the future updates check).

---

## Nexus by-mod-id poll — refresh stats + updates-available (2026-06-14)

> **STATUS — NEEDS LIVE SMOKE.** Core is fully unit-tested (rate-limit parse + typed 429, `updated.json` mapper, `NexusRefresh` primitive + sweep/candidate/period selection, `NexusPollStamp`, `NexusLatestVersion` three-places carry-through + round-trip). The manual menu action, the debounced auto-check service, the setting toggle, and the UPDATE chip are App wiring the test project can't reach — verify against a real Nexus account with a personal API key on a modded library. See `docs/superpowers/specs/2026-06-14-nexus-by-mod-id-poll-design.md`.

**Shipped:** Refresh live Nexus stats on the *existing* library by polling Nexus **by mod id** (no archive re-find), and flag when a newer version exists on Nexus. One Core refresh primitive (`NexusRefresh.RefreshOne`: `GetMod` by id → refresh endorsements/downloads/available + capture `NexusLatestVersion`, preserve the installed `Version`/`NexusFileId`) fed two ways — a full manual sweep ("Refresh Nexus stats") and a debounced `updated.json`-narrowed auto-check on game load. Rate-limit-hardened client underneath (`x-rl-*` headers parsed, typed `NexusRateLimitException` on 429, ToS `Application-Name`/`Application-Version` headers). `UpdateAvailable` is computed (`NexusLatestVersion != Version`), never trusted blindly from disk.

**Smoke steps (Nexus connected with a personal API key, on a modded library — Windrose or Elden Ring with Backfilled or URL-identified mods):**

- [ ] **(a) Refresh fills stats with no archive.** On a game whose mods were identified earlier (have `nexusModId` or a Nexus `url` in `metadata.json`) but show blank/stale endorsements + downloads, open the game-options menu → "Refresh Nexus stats…". EXPECT: status line reads `Refreshed N mod(s), M update(s) available`; the rows now show live endorsement + download counts — with **no archive re-find** (the original zips aren't needed). Mods with no resolvable id (CurseForge-only, or no id/url) are silently skipped.
- [ ] **(b) UPDATE chip + tooltip on a newer-on-Nexus mod.** Have at least one mod where Nexus has a newer version than what's installed (or simulate by editing `metadata.json` so `version` is older than the live Nexus version). After a Refresh, EXPECT: that row shows the accent **UPDATE** chip; hovering it shows the tooltip `Nexus has {latest} — you have {installed}` with the real version strings. A mod that's current shows no chip.
- [ ] **(c) Auto-check flags a recently-updated mod within 24h of launch.** With the "Check for mod updates automatically" setting ON (default) and a mod that was updated on Nexus inside the poll window, switch to / load the game after >24h since the last poll for that game. EXPECT: the one `updated.json` call narrows to changed candidates, only those get refreshed, and the recently-updated mod gains the UPDATE chip without a manual sweep. Confirm `%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-<gameId>.txt` updates to the poll time, and the next load inside 24h does NOT re-poll (debounced).
- [ ] **(d) Offline / rate-limited degrades silently.** Disconnect the network (or hammer the API to trip a 429), then load a game (auto-check path) — EXPECT: no crash, no error toast; the auto-check swallows the failure (logged via `AppDiagnostics`, never surfaced) and the session continues with the last-known stats. Run "Refresh Nexus stats…" while rate-limited — EXPECT: the sweep stops cleanly and the status line reads `Nexus rate limit reached — try again later.` (partial progress preserved, nothing thrashed).
- [ ] **(e) `nexusLatestVersion` persists + survives a rescan.** After a Refresh that captured a latest version, confirm `metadata.json` carries `"nexusLatestVersion"` (camelCase) for the affected mods. Switch games and back (or Redetect) → EXPECT: the field round-trips intact and the UPDATE chip state is preserved (the value isn't lost on reload).

**Why these matter:** every layer below the dialogs is unit-tested, but the menu-action wiring, the fire-and-forget auto-check on game load (off the UI hot path), the setting toggle, the UPDATE chip render, and the real rate-limit behavior against a live Nexus account only exercise on a real Windows machine with a connected personal API key and a modded library.

---

## Nexus endorse — one-click endorse ⇄ abstain per row (2026-06-15)

> **STATUS — NEEDS LIVE SMOKE.** Core is fully unit-tested (POST-body client path, `EndorseRequest`/`EndorseAsync` + graceful refusals, `ModMeta.Endorsed` three-places + camelCase round-trip, bulk `GetUserEndorsementsAsync` + `MapUserEndorsements` + pure `ApplyEndorsements`, refresh-sweep integration that can't be sunk by an endorsements-call failure). The heart affordance, the `ToggleEndorseAsync` VM action, the optimistic-flip-only-on-success behavior, and the real endorse/abstain round-trip against a live Nexus account are App wiring the test project can't reach — verify against a real Nexus account with a personal API key on a modded library. See `docs/superpowers/specs/2026-06-15-nexus-endorse-design.md`.

**Shipped:** One-click endorse (and un-endorse) on each Nexus-identified row — the give-back half of the Nexus loop, never automatic. The GET-only Nexus client gained POST-body support (mirrors `CurseForgeClient`); `EndorseAsync` writes `POST /v1/games/{domain}/mods/{id}/{endorse|abstain}.json` with body `{"Version": installedVersion}` and surfaces 4xx precondition refusals as a friendly status line (the two known codes `NOT_DOWNLOADED_MOD` / `TOO_SOON_AFTER_DOWNLOAD` mapped to human text, any other code's own `message` passed through) — never a throw; 429 still raises the typed `NexusRateLimitException`. New persisted `ModMeta.Endorsed` (`bool?`, three-places, camelCase) records the user's intent and survives a rescan. The bulk `GET /v1/user/endorsements.json` list is fetched once per refresh sweep and applied via the pure `ApplyEndorsements`, so hearts stay accurate library-wide (including mods endorsed outside the launcher) without per-mod calls. The heart shows only on Nexus-identified rows when connected; the toggle flips the heart only on a successful, non-refused response.

**Smoke steps (Nexus connected with a personal API key, on a modded library — Windrose or Elden Ring with Backfilled or URL-identified mods):**

- [ ] **(a) Heart visibility.** On a connected Nexus account, a row identified via Nexus (has `nexusModId`) shows a heart affordance in the chip / icon-button area. A row with no Nexus id shows NO heart. Disconnect Nexus → the heart disappears on the next reload. (Visible only when `NexusModId` is set AND Nexus is connected.)
- [ ] **(b) Endorse fills the heart.** Click an outline heart on a mod you've downloaded → the heart fills (accent) and the status line confirms `Endorsed "{name}" on Nexus.`. Confirm on the Nexus website that the mod now reads as endorsed by your account.
- [ ] **(c) Abstain empties the heart.** Click the now-filled heart again → the heart returns to outline and the status line reads `Retracted endorsement for "{name}".`. Confirm the Nexus website reflects the retraction.
- [ ] **(d) Friendly refusal, no flip, no crash.** On a mod that's NOT been downloaded (or one downloaded too recently for Nexus's post-download wait window), click the heart → the status line shows the friendly refusal text ("You need to download this mod before you can endorse it." or the wait-window message), the heart does NOT flip, and the app does not crash. An unknown refusal code surfaces Nexus's own human `message` (still no flip, no throw).
- [ ] **(e) Bulk sync fills hearts for outside-the-launcher endorsements.** Endorse a mod directly on the Nexus website (one the launcher already lists), then run the game-options menu → "Refresh Nexus stats…" (or trigger the auto-check). EXPECT: that row's heart fills without ever clicking it in the launcher — the one `GET /v1/user/endorsements.json` call applied the real state. A failure of that single endorsements call does NOT abort the stats sweep (counts still refresh).
- [ ] **(f) `endorsed` persists + survives a rescan.** After endorsing in the launcher, confirm `metadata.json` carries `"endorsed": true` (camelCase) for the affected mod. Switch games and back (or Redetect) → EXPECT: the heart stays filled (the field round-trips intact, persisted user intent isn't lost on reload).

**Why these matter:** every layer below the button is unit-tested, but the heart render + visibility gating, the `ToggleEndorseAsync` wiring, the no-optimistic-flip-on-refusal behavior, the bulk-list application during the live refresh sweep, and the real endorse/abstain round-trip against a connected Nexus account only exercise on a real Windows machine with a personal API key and a modded library.

---

## Engine detection — 2-level Unreal probe (2026-06-15)

- [ ] **Marvel Rivals (2-level UE5) auto-detects.** Add Marvel Rivals from Steam/Epic (or point at its install). Expect: engine detected as `ue-pak`; mod path resolves to `MarvelGame/Marvel/Content/Paks/~mods`. Drop a `.pak` into `~mods` → it shows as a mod row and toggles on/off (moves to holding, not deleted).
- [ ] **Single-wrapper games still work (no regression).** A previously-working single-wrapper UE game (e.g. Palworld `Pal/...`, Hogwarts `Phoenix/...`) still detects and lists mods exactly as before.
- [ ] **Engine sibling is not mis-detected.** A UE install with an `Engine/Content/Paks` beside the project resolves to the project folder, never `Engine` (no engine paks listed as mods, mods never routed into `Engine`).
- [ ] **Big install stays fast.** Adding a game with a large/deep folder tree does not hang the add (the probe is budget-bounded).

---

## Ban-risk enable gate — profile-apply path prompts once (2026-06-15)

> **STATUS — NEEDS LIVE SMOKE.** The gate decision (`BanRiskRules.ShouldGateEnable`), the live `ByAppId` risk resolve, and the per-game ack persistence are unit-tested in Core; the dialog wiring + the profile-apply route through `MainViewModel.LoadProfileAsync` are App wiring the test project can't reach. See `docs/superpowers/specs/2026-06-15-ban-risk-safety-design.md`.

- [ ] **Profile-apply gates once (no per-row bypass).** On an un-acked high-ban-risk game, save a profile that has mods enabled, turn those mods off, then open Profiles → Load that profile. EXPECT: the ban-risk warning prompts **once** (not per mod) before anything is enabled; confirming applies the profile and enables the mods; cancelling applies nothing (no mod is enabled, the status line reads "Load cancelled.") — the profile-apply path no longer bypasses the gate.

---

## Ban-risk safety (2026-06-15)

- [ ] **Gate fires on a high-risk game.** Flag a local game `banRisk: "high"` (manual registry/manifest edit). Enabling a mod prompts the ban-risk acknowledgment naming the game. Cancel -> nothing enables, the row reverts. Enable anyway with "Don't warn me again for this game" -> enables, and the next enable does NOT re-prompt.
- [ ] **Banner persists.** The ban-risk banner shows on that game and stays visible after the ack; its copy is ban-specific, not the co-op-desync wording.
- [ ] **Medium = banner only.** A `banRisk: "medium"` game shows the banner but never prompts; a null/None game shows neither.
- [ ] **Bulk gated once.** "Enable all" / applying a profile that enables mods on the un-acked high game prompts once (no per-row bypass).
- [ ] **Disable is never gated.** Disabling a mod on a high-risk game is always immediate (reversibility — getting safer needs no friction).

---

## Plugin slice B1 — Nexus as a plugin (2026-06-15)

> **STATUS — NEEDS LIVE SMOKE.** B1 (plugin + App rewiring) is built + whole-branch-reviewed (purity + correctness both approve). The committed branch is **fail-closed** (empty `PluginSigningKey` — no plugin loads) until sub-project 5 mints the real key; these smokes were set up with a **local dev-signed** plugin (dev SPKI pinned locally + reverted; the dev-signed `ModManager.Plugin.Nexus.dll` + `.sig` deployed to `%LOCALAPPDATA%\ModManagerBuilder\plugins\`). Re-run the dev-sign if the plugin is rebuilt. Core's `Scanner`/identify still uses its own `NexusClient` (that's B2).

- [ ] **FULL loads the dev-signed Nexus plugin** and the endorse heart, "Refresh Nexus stats", and the UPDATE chip all work *through the plugin* (parity with the old in-core App-facing Nexus). The plugin assembly contains no API key (read per-call from the on-machine store).
- [ ] **Stats refresh never wipes a heart** — endorse a mod, run a stats refresh; the filled heart survives (the per-mod fetch returns `Endorsed: null`, so `SourceMetadataMapper` preserves it).
- [ ] **Endorse refusal degrades** — endorse a not-yet-downloaded mod → friendly status line, no crash, heart doesn't flip (the plugin's `SetEndorsedAsync` returns `Refused`).
- [ ] **Scan-time identify still works (B2 not done)** — Core's `Scanner` still enriches mods via its own `NexusClient` on add/rescan; md5-identify, manual-match, Vortex-identify unchanged.
- [ ] **STORE flavor is sealed** — build/run `-p:Configuration=Store` → no plugin loads (host compiled out), no user-facing Nexus surface, core fully functional; STORE dll has no `LoadFromStream`/`PluginHost`/`AssemblyLoadContext`.

---

## Plugin slice B2a — scan-time identify routes through the plugin (2026-06-18)

> **STATUS — NEEDS LIVE SMOKE.** B2a grew the `IModSource` contract (identity/credit fields + `SourceIdentifyResult`) and rewired Scanner's three identify methods (`Md5IdentifyAsync`, `Md5IdentifyArchivesAsync`, `IdentifyVortexNexusAsync`) plus `Ue4ssLuaInstaller.IdentifyMetadataAsync` from the in-Core `INexusClient` to the Abstractions `IModSource?` (null source → no-op `IdentifyResult(0)`, the zero-plugins / STORE path). Core stays NOT-yet-Nexus-free — `NexusClient`/`INexusClient` remain for the bulk endorse-state + `NexusRefresh` paths that B2b migrates. Full Core suite green (1350 passed, incl. `CorePurity` + the rewired `Md5Identify`/`VortexNexusIdentify`/`Ue4ssLuaMetadata` tests); plugin + FULL + STORE builds clean; STORE dll still carries no loader symbols. Identify is read-only — no file-op/reversibility surface. Set up the same way as B1 (local dev-signed plugin in `%LOCALAPPDATA%\ModManagerBuilder\plugins\`). See `docs/superpowers/plans/2026-06-18-plugin-nexus-B2a.md`.

- [ ] **FULL: scan-time identify flows through the dev-signed plugin.** On a FULL build with the dev-signed Nexus plugin loaded and Nexus connected, add/rescan a game with identifiable mods — md5-identify (loose + archived), manual/Vortex-recorded identify, and UE4SS Lua-mod identify all enrich rows (title/author/image/url/category/endorsements/downloads/latest-version) *through the plugin's `IModSource`*, with parity to the old in-Core `NexusClient` path. The plugin assembly still carries no API key (read per-call from the on-machine store).
- [ ] **STORE: identify no-ops cleanly.** On a STORE build (no plugin host, registry empty → `IModSource` is null), add/rescan the same game — identify is a clean no-op (`IdentifyResult(0)`): rows list and toggle normally, no Nexus enrichment, no crash, no error toast. (This is the zero-plugins path the null-source guard covers.)
- [ ] **Identify never wipes a heart.** On a row already endorsed (heart filled), a rescan that re-runs identify must not clear the heart — the grown `SourceModMetadata.Endorsed` stays null on the identify path and `SourceMetadataMapper.Apply` null-coalesces (`?? meta.Endorsed`), so the persisted intent survives.
- [ ] **Endorse (per-mod write) still works through the plugin** — the one-click heart endorse/abstain (B1) is unaffected.
- [x] ~~⚠️ **KNOWN REGRESSION (B1-introduced, caught by the B2a adversarial review — B2b fixes it).** "Refresh Nexus stats" and the 24h auto-poll were moved to a per-mod `FetchMetadataAsync` loop in B1, which silently dropped two bulk features: (1) the **library-wide endorse-heart sync**; and (2) the **rate-limit-aware windowed poll**. `NexusRefresh.RefreshAllAsync`/`SelectCandidates`/`ApplyEndorsements` were orphaned. The heart was never *wiped* (the `Endorsed: null` guard holds) — it was the *sync* and *backoff* that were gone.~~ **RESTORED in B2b-1 (2026-06-18).** The bulk endorse-state + updated-window reads now live on the `IModSource` contract (`GetUserEndorsementsAsync` / `GetRecentlyUpdatedAsync` + `SourceRateLimitException`); `NexusRefresh` was reworked onto them and both call sites rewired to the bulk path — `RefreshNexusStatsAsync` → `NexusRefresh.RefreshAllAsync` (library-wide heart sync back), `NexusUpdatePoll.MaybePollAsync` → windowed `GetRecentlyUpdatedAsync` → `SelectCandidates` → `RefreshAllAsync` with the stamp written **only on a non-rate-limited sweep** (a 429 leaves the stamp untouched so the poll retries next launch). `NexusRefresh.Overlay` keeps its *selective* refresh (stats + `NexusLatestVersion` only; identity / installed `Version` / `NexusFileId` / `Endorsed` / `IsManual` preserved) — distinct from `SourceMetadataMapper.Apply`. See the B2b-1 live-smoke checks below.

### B2b-1 live-smoke — the restored regression (verify behavior, not just compile)
- [ ] **Library-wide heart sync restored.** On a connected Nexus account, endorse a mod **on the Nexus website** (one the launcher lists, never endorsed in the launcher). Run game-options → "Refresh Nexus stats…". EXPECT: that row's heart fills — the one `GET /v1/user/endorsements.json` in `RefreshAllAsync` applied the real state. A failure of that single endorsements call does NOT abort the stats sweep (counts still refresh).
- [ ] **Rate-limited poll leaves the stamp unwritten.** Trip a 429 (hammer the API, or point at a throttled key) on game load with the auto-check ON and >24h since last poll. EXPECT: no crash; the poll does NOT write `%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-<gameId>.txt` (so the next launch retries) — confirm the stamp's mtime is unchanged. A clean (non-429) sweep DOES stamp.
- [ ] **Selective overlay never clobbers a manual match.** On a manually-URL-matched row (custom title), run "Refresh Nexus stats…". EXPECT: stats (endorsements/downloads) + UPDATE chip refresh, but the title / author / heart stay exactly as set — the selective `Overlay` doesn't touch identity or `Endorsed`.

## Plugin slice B2b-2 — Core is Nexus-client-free (2026-06-18)

> **STATUS — VERIFIED (code + gate).** B2b-2 deleted the orphaned Core Nexus client cluster — `NexusClient`, `NexusRequests`/`NexusOptions`, `INexusClient`, `NexusEndorse`, `NexusRateLimit`, and the client DTOs (`NexusMd5Match`, `NexusUser`, `NexusUpdateEntry`, `NexusEndorsement`, `EndorseOutcome`/`EndorseAction`) — and relocated the client-impl tests to two new plugin-side test projects (`tests/ModManager.Plugin.Nexus.Tests`, `tests/ModManager.App.NexusValidate.Tests`). `NexusService` (App) had its `Client` property + `NexusClient`/`NexusOptions` usage dropped; validation is now a minimal inline `GET /v1/users/validate.json`, with the credential store + connection state staying App-side. `NexusDomains` (manifest facade), `NexusRefresh`/`NexusPollStamp` (ModMeta helpers), `SourceMetadataMapper`, and the `ModMeta.Nexus*` fields STAY — those are our types, not client code. See `docs/superpowers/plans/2026-06-18-plugin-nexus-B2b.md` Tasks 7–9.

- [x] **The done-when grep is clean.** `grep -rn --include="*.cs" "Nexus" src/ModManager.Core/` returns ONLY `NexusDomains`/`NexusDomain` (manifest facade), `NexusRefresh`/`NexusRefreshResult`/`NexusPollStamp` (ModMeta helpers), `SourceMetadataMapper`, the `ModMeta.Nexus*` field names (`NexusModId`/`NexusFileId`/`NexusGameDomain`/`NexusLatestVersion`), the `INexusGate` connection/key abstraction, and the `IdentifyVortexNexusAsync` Scanner method — **no `NexusClient`/`NexusRequests`/`INexusClient`/`NexusOptions`** in any `.cs` source.
- [x] **The restored regression still holds.** `RefreshNexusStatsAsync` and `NexusUpdatePoll.MaybePollAsync` still compile and route through `NexusRefresh.RefreshAllAsync` over the loaded `IModSource` (`NexusSource`) — the cluster deletion didn't disturb the B2b-1 bulk-sync wiring.
- [x] **The gate is green.** Core suite (1280 passed, 0 failed) + the two relocated plugin test projects (`ModManager.Plugin.Nexus.Tests` 48/48, `ModManager.App.NexusValidate.Tests` 8/8) + plugin build + App FULL + App STORE all 0 errors. STORE output carries no `ModManager.Plugin.Nexus.dll` (plugin-impl-dll-free) and the entire `PluginHost` loader (`AssemblyLoadContext` + load-from-stream) compiles out under `#if FULL` (loader-free).
- [x] ~~⚠️ **Known parity gap — Nexus category labels.**~~ **RESTORED (2026-06-18, Este chose restore over accept).** `NexusModSource` now fetches the per-domain category dictionary once per game (`GET /v1/games/{domain}.json`, cached for the session) and resolves `category_id` → name on identify + fetch, so `ModMeta.Category` fills again in FULL — full parity with the pre-extraction `NexusClient`. Best-effort: a failed/offline game-info fetch leaves `Category` null without throwing (covered by `NexusCategoryTests`).
- [ ] **Live smoke — full Nexus surface still works through the plugin (FULL).** On a FULL build with the dev-signed Nexus plugin loaded and Nexus connected, the whole Nexus loop — scan-time identify, "Refresh Nexus stats" (with library-wide heart sync), the endorse heart, the UPDATE chip, manual-URL match, category labels, and the 24h auto-poll — works with parity to before the cluster deletion. Nothing regressed from removing Core's client.
- [ ] **Live smoke — STORE stays sealed.** STORE build/run: no plugin loads, no user-facing Nexus surface, core fully functional (toggling/intake unaffected). Validation/connection UI either hidden or cleanly no-ops with no Nexus client present.

---

## Plugin slice 5c-consumer — signed plugin feed fetch/install (2026-06-18)

> **STATUS — BUILT + FIXTURE-TESTED; live loop pending 5c-producer.** The consumer is fully implemented and unit-tested against a hand-signed fixture (a throwaway test keypair), proving the full trust chain without the real production key or a live GitHub release. The live loop (Nexus connect → fetch the real signed `plugins.json` from `626-mod-plugins` → verify against the pinned production key → download + verify the `ModManager.Plugin.Nexus.dll` → hot-load) CANNOT be live-smoked until 5c-producer publishes the feed. Until then, the feed URL 404s and fail-silent leaves the app exactly as it is today — no regression, no plugin removed.

**What was built:** `PluginIndex` + `PluginGate` + `PluginIntegrity` + `InstalledPluginsStore` (pure Core, camelCase, all unit-tested) + `PluginFeedInstaller` (headless orchestration over an injected download delegate — no `HttpClient` in Core) + `PluginHost.LoadOne` (hot-load seam, App-side FULL) + `PluginFeedSource` (HTTP delegate + 24h debounce + first-install bypass + connect-trigger + hot-load glue, App-side `#if FULL`) + `AppSettingsService.KeepPluginsUpdated` (default on, camelCase, round-trip tested) + Settings UI toggle + installed-plugin status line (FULL only).

**Gate (run after 5c-producer publishes):**

- [ ] **FULL: Nexus connect → plugin auto-installs.** On a FULL build, connect Nexus (Settings → enter personal API key → Connect). EXPECT: within seconds, `%LOCALAPPDATA%\ModManagerBuilder\plugins\nexus.dll` and `nexus.dll.sig` exist on disk; `installed-plugins.json` in the same folder records `{"versions":{"nexus":"X.Y.Z"}}`; the Nexus surface (endorse heart, Refresh Nexus stats, UPDATE chip) is live without a restart.
- [ ] **FULL: first-ever connect lights up the surfaces with NO rescan.** On a clean machine (no plugin installed yet), connect Nexus. EXPECT: the endorse hearts + "Refresh Nexus stats" appear the moment the plugin hot-loads — without manually rescanning or switching games. (`PluginFeedSource.PluginLoaded` → `MainViewModel.WirePluginFeed` re-notifies `NexusActionsAvailable` on the UI thread and reloads rows.)

> **NOTE — plugin UPDATES apply on next launch; fresh installs hot-load live.** A first-ever install hot-loads immediately (the registry was empty, so `ModSourceRegistry.Add` registers it live). But if a plugin was already loaded at startup, a feed UPDATE that installs a newer dll does NOT go live mid-session — `ModSourceRegistry.Add` no-ops when the id is already registered, so the new version is picked up on the next launch. A true mid-session hot-swap (ALC unload + reload of an already-registered source) is a future enhancement, intentionally out of scope here.
- [ ] **FULL: 24h debounce holds.** After a successful install, reconnect Nexus. EXPECT: the debounce fires (`last-plugin-check.txt` is recent) — no second download, no churn. After 24h (or manual delete of the stamp), the re-check runs and re-installs only if the feed carries a newer version.
- [ ] **FULL: "Keep plugins updated" toggle gates re-checks.** Turn "Keep plugins updated" OFF in Settings. Reconnect Nexus (with >24h elapsed). EXPECT: no re-check runs (the setting gates debounced updates; the first install when nothing is installed still fires regardless).
- [ ] **FULL: offline / bad index sig → fail-silent, no dll removed.** Disconnect the network (or point `FeedUrl` at a 404), then reconnect Nexus. EXPECT: no crash, no toast, no plugin removed, the installed plugin continues to load normally on the next cold launch.
- [ ] **STORE: no plugin surface.** Build/run `-p:Configuration=Store`. EXPECT: `PluginFeedSource` is absent (compiled out by `#if FULL`); no `MaybeFetchOnConnectAsync` call; no `last-plugin-check.txt`; Settings shows "Plugins are a desktop-only feature" (or the status row is hidden); the STORE dll contains no `PluginFeedSource` symbol. The STORE gate already verified this at commit time (0 errors, 0 warnings on the STORE build).

**Why the live gate is deferred:** the trust chain (index sig → sha verify → dll sig → atomic install → hot-load) is exercised end-to-end by `PluginFeedInstallerTests` against a hand-signed fixture — the path the code walks is the same. What only a live feed adds is confirmation that the production signing key `PluginSigningKey.PublicKeySpki` matches what 5c-producer actually signs with, and that the GitHub Releases download URL resolves. Those are producer-side facts, not consumer code paths.

---

## Plugin delivery UX — already-connected users, hot-load, manual button (v0.8.1)

> **STATUS — BUILT + GATE-PASSED; needs live re-smoke on a connected rig.** All three delivery gaps the 2026-06-19 live smoke of v0.8.0 exposed are fixed (Tasks 1–5 on `fix/plugin-0.8.1-delivery`). Gate passed: Core suite 1309/0, NexusValidate 10/10, FULL build 0 errors, STORE build 0 errors. STORE carries no plugin/loader symbols (confirmed). Do NOT tag v0.8.1 — the maintainer cuts it after this re-smoke.
>
> **Background:** v0.8.0 shipped: the connect-time fetch fired + downloaded + verified + installed `nexus.dll`, wrote `installed-plugins.json` — but (a) it only fired on a fresh `ConnectAsync`, so an already-connected user (key persisted) never triggered it; (b) the mid-session hot-load did not light the Nexus surfaces — full restart was needed; (c) the whole path was fail-silent, so the user couldn't tell what happened. A stale hand-dropped `ModManager.Plugin.Nexus.dll` from B1 dev-testing also sat in the plugins dir next to the feed-installed `nexus.dll`.
>
> **What shipped (all FULL-only, compile out of STORE):**
> - **Task 1 — `FetchAsync` returns an outcome** (`refactor(plugins): PluginFeedSource.FetchAsync returns an outcome`): the core fetch now returns `PluginFetchResult` (`NotApplicable`/`UpToDate`/`Installed`/`Failed`) instead of pure fire-and-forget. `MaybeFetchOnConnectAsync` delegates to it; the manual button awaits it and shows the result.
> - **Task 2 — Startup fetch for already-connected users** (`feat(plugins): fetch the Nexus plugin at startup when already connected`): `MainWindow.OnFirstActivated` now fire-and-forgets `FetchAsync(force: false)` after `LoadAsync` when Nexus is already connected — already-connected users get the plugin on first launch after updating without a reconnect.
> - **Task 3 — Hot-load re-identifies** (`fix(plugins): hot-load re-identifies so Nexus surfaces light up without a restart`): the `PluginLoaded` handler in `WirePluginFeed` now dispatches `RedetectActiveAsync()` (the game-load re-detect path: scan + Nexus identify + stats/endorse enrichment) instead of the lighter `ReloadModsAsync`. Hearts fill on hot-load — no restart needed.
> - **Task 4 — Manual button in Settings** (`feat(settings): manual 'install/refresh Nexus plugin' button with visible outcome`): "Install / refresh Nexus plugin" button beneath the "Keep plugins updated" checkbox. Awaits `FetchAsync(force: true)` and writes the outcome to `PluginStatusText`: `Installed` → version string; `UpToDate` → up-to-date + version; `NotApplicable` → "Connect Nexus first."; `Failed` → "Couldn't fetch the plugin: {reason}". Button disables while awaiting.
> - **Task 5 — Load only feed-recorded dlls** (`fix(plugins): load only feed-recorded plugin dlls, ignore stale leftovers`): `PluginHost.LoadAll` now loads only dlls whose base name matches a record in `installed-plugins.json`. A stale hand-dropped `ModManager.Plugin.Nexus.dll` beside the feed-installed `nexus.dll` is skipped (logged at debug) — the stale one can no longer shadow the canonical feed plugin.
>
> **Cleanup note:** if the rig has a stale `ModManager.Plugin.Nexus.dll` + `ModManager.Plugin.Nexus.dll.sig` in the plugins dir from B1 dev-testing, those files are now ignored by the loader. Safe to delete manually, but they won't cause harm.

**Smoke steps (FULL build, Nexus connected with a personal API key, on a modded library):**

- [ ] **(1) Already-connected startup fetch.** On a rig where the Nexus key is already persisted (no reconnect needed), delete `%LOCALAPPDATA%\ModManagerBuilder\plugins\nexus.dll` and `nexus.dll.sig` and `installed-plugins.json` so the plugins dir is clean. Launch the FULL build. EXPECT: within seconds of launch (without opening Settings or reconnecting), `nexus.dll` + `nexus.dll.sig` + `installed-plugins.json` appear in the plugins dir; the Nexus surface (endorse hearts, "Refresh Nexus stats", UPDATE chip) is live — no reconnect, no restart.
- [ ] **(2) Hot-load lights hearts without a restart.** With the FULL build running and a game loaded that has Nexus-identified mods (some with filled hearts from prior sessions), simulate or observe a mid-session plugin install: disconnect Nexus and reconnect (triggers `MaybeFetchOnConnectAsync` → `FetchAsync` → `PluginLoaded`). EXPECT: within a few seconds the rows update — hearts fill (endorsement state loaded), stats refresh — without a restart or manual redetect. If the plugin was already installed, you can also test this by manually calling "Install / refresh Nexus plugin" from Settings.
  - **Re-identify scope (honest limit):** the hot-load handler re-detects, then runs `IdentifyVortexNexusAsync` (Vortex-deployed mods carry the modId in their manifest — no archive needed) + the bulk endorsement sweep. So hearts fill for mods that already carry a Nexus id (identified in a prior session) AND for Vortex-deployed mods. A mod **raw-dropped before the plugin existed**, whose source archive is gone, cannot be md5-re-identified post-extract — for those the honest path is the manual "Fetch metadata" (CF name-search + Vortex/Nexus) or a re-drop. This is an inherent limit of post-extract identify, not a bug. Verify a Vortex-deployed Nexus mod lights up on hot-load; note any raw-dropped-pre-plugin mod that needs the manual backfill.
- [ ] **(3) Manual button — connected.** Settings → the "Install / refresh Nexus plugin" button exists beneath "Keep plugins updated". Click it while connected. EXPECT: button disables + `PluginStatusText` reads "Checking the plugin feed…" → resolves to either "Nexus plugin vX.Y.Z installed." (if a newer version was fetched) or "Nexus plugin is up to date (vX.Y.Z)." (current). Button re-enables after.
- [ ] **(4) Manual button — not connected.** Settings → click "Install / refresh Nexus plugin" with no Nexus key set. EXPECT: status reads "Connect Nexus first." — no crash, no network attempt.
- [ ] **(5) Stale dll is ignored.** If a stale `ModManager.Plugin.Nexus.dll` (from B1 dev-testing) exists alongside the feed-installed `nexus.dll`, launch the FULL build. EXPECT: only `nexus.dll` (the feed-recorded id-named file) loads — the stale one is silently skipped. Confirm by checking `AppDiagnostics` / app-errors.log for a debug-level skip entry (optional), or by verifying the Nexus surface works correctly with no duplicate-plugin symptoms.
- [ ] **(6) STORE stays sealed.** Build/run `-p:Configuration=Store`. EXPECT: no plugin loads; no startup fetch fires; the Settings row for "Install / refresh Nexus plugin" is absent or no-ops (compiled out); no `last-plugin-check.txt`; STORE dll has no `PluginFeedSource`/`PluginHost` symbols. Core fully functional (toggle/intake/save unaffected).

**Why these matter:** the delivery path (startup fetch, hot-load dispatcher, manual button wiring) is App-side FULL-only — the unit tests cover the fetch outcome shape and the plugin installer math, but the actual trigger timing (startup vs. connect-time), the `RedetectActiveAsync` visual round-trip (hearts filling mid-session), and the button's feedback loop only exercise on a real connected rig with a real feed and a real Nexus account.

---

## "Request this game" affordance in AddGameDialog (feat/request-a-game)

> **STATUS — NEEDS LIVE SMOKE.** `GameRequestUrl.Build` is fully unit-tested (Core, pure). The `HyperlinkButton` visibility toggle in `ApplyDetectedEngine` and the `OnRequestGame` handler are App wiring the test project can't reach — verify on the rig.

**Shipped:** When a user adds a game whose engine the launcher can't detect, a "Can't find the engine? Request this game" hyperlink appears below the engine picker. Clicking it builds a prefilled `https://github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new` URL — game name (title + `name` field), Steam App ID (when set), and engine defaulting to "Not sure" (the required dropdown's fallback option) — and opens it via `Windows.System.Launcher.LaunchUriAsync`. The link is hidden when engine detection succeeds. No `#if FULL` gating — both STORE and FULL flavors surface the affordance.

**Smoke steps:**

- [ ] **Undetected engine → link appears.** Add Game → "Or add one manually" → type a game name and paste a folder for a game the launcher can't classify. EXPECT: the "Can't find the engine? Request this game" link is visible below the engine picker (engine stays on the placeholder). Click it → the browser opens `github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new` with the game name pre-filled in the title + name field, the engine dropdown set to "Not sure", and the `game-request` label applied (from the template auto-label). If a Steam App ID was entered, it appears in the `steam-app-id` field.
- [ ] **Detected engine → link hidden.** Add Game → Browse to a folder whose engine the launcher CAN detect (e.g. an Unreal game or an Elden Ring install). EXPECT: the engine picker auto-selects and the "Request this game" link is NOT visible (detection succeeded; no request needed).
- [ ] **Steam Setup path → link appears.** Add Game → "Set up" on an engine-undetected Steam game in the "Set up" list. EXPECT: the manual form pre-fills (name + folder + app id), engine stays on the placeholder, and the "Request this game" link is visible. Clicking it opens the prefilled issue with the Steam App ID in the `steam-app-id` field.
- [ ] **Name required → link is a no-op without a name.** Clear the name field, then click the link. EXPECT: nothing opens (the handler returns early when name is blank).

**Why these matters:** the URL builder is unit-tested (field ids, engine-key map, escaping, `SafeUrl` guard), but the link's show/hide toggle against real engine detection, the `LaunchUriAsync` call, and the prefilled form's correctness in the browser only exercise on a real Windows machine with a real browser.

---

## feat/ban-safe-loaders Task 3 — detected loaders as launch buttons (FULL + STORE)

> **STATUS — BUILT + GATE-PASSED; needs live smoke.** Core suite 1319/0. FULL build 0 errors. STORE build 0 errors. STORE seal OK (no new forbidden symbols).

**What shipped:** `LoaderScan.Detect` (Task 2, already merged) probes the active game's play folder for known launcher exes. The ToolsPanel now renders a **LOADERS** section — collapsed when no loaders are detected, visible with one button per detected loader when they are. Each button is labeled "Launch via {DisplayName}" (e.g. "Launch via Mod Engine 2") and delegates to `MainViewModel.LaunchLoaderAsync`, which runs `Process.Start` against the detected exe and updates StatusText. Read-only launch — no file changes, no snapshot needed. No `#if FULL` anywhere: STORE and FULL both render the section (on STORE, safe loaders are the primary safe path since the EAC offline toggle is absent).

**Smoke steps:**

- [ ] **Loader present — Elden Ring with Mod Engine 2 installed** (i.e. `modengine2_launcher.exe` exists in the game's play folder): open the launcher on Elden Ring. EXPECT: a **LOADERS** section appears in the tools bar with a "Launch via Mod Engine 2" button.
- [ ] **Click the button.** EXPECT: status line reads "Launching Mod Engine 2…"; Mod Engine 2's launcher process starts (game launches via ME2 with its mod config). No crash, no error toast.
- [ ] **Loader present — Elden Ring with Seamless Co-op installed** (`launch_elden_ring_seamlesscoop.exe` or `ersc_launcher.exe` in the play folder): EXPECT: a "Launch via Seamless Co-op" button also appears in the LOADERS section alongside any ME2 button.
- [ ] **Neither loader present.** On any game without a known loader exe in the play folder: EXPECT: the LOADERS section is completely absent from the bar. No empty header visible.
- [ ] **Non-fromsoft game.** Switch to a non-fromsoft game (e.g. Windrose / R.E.P.O.): EXPECT: no LOADERS section (the catalog is scoped by engine — none currently registered for non-fromsoft engines).
- [ ] **STORE build smoke (if available).** Run the STORE build on an Elden Ring folder with ME2 installed: EXPECT identical behavior — LOADERS section present, "Launch via Mod Engine 2" button functional. Confirms no `#if FULL` gate accidentally strips the feature.

**Why these matter:** `LoaderScan.Detect` is pure-Core and unit-tested (Task 2). The XAML binding (`ViewModel.Loaders` → `ItemsRepeater`), the `HasLoaders` visibility gate (`StackPanel.Visibility="{x:Bind ViewModel.HasLoaders}"`), and the `Process.Start` launch flow only exercise on a real WinUI instance with a real game folder containing the launcher exe.

---

## feat/ban-safe-loaders — Task 4: ban-risk gate surfaces safe loaders (2026-06-26)

> **STATUS — BUILT + GATE-PASSED; needs live smoke.** Core suite 1319/0. FULL build 0 errors. STORE build 0 errors. STORE seal OK.

**What shipped:** When the ban-risk gate fires on a high-risk game (e.g. Elden Ring), `GateBanRiskEnableAsync` now resolves the game's ban-safe loaders via `LoaderScan.BanSafeFor` + `LoaderScan.Detect` and passes them to `ConfirmBanRiskEnableAsync`. The dialog now shows a "The safe way to mod this game:" section with one button per safe loader — installed loaders show "Launch {DisplayName}" (Process.Start), uninstalled loaders show "Get {DisplayName}" (opens the download URL in the browser). The "Enable anyway" / "Cancel" / "Don't warn me again" flow is **unchanged** — this only adds guidance. No `#if FULL` anywhere: on STORE, with no EAC offline toggle, the safe-loader list is the primary safe-path surface.

**Smoke steps:**

- [ ] **Ban-risk gate — loader installed.** With Elden Ring registered and `modengine2_launcher.exe` in its play folder: toggle a mod on. EXPECT: the ban-risk dialog appears (if not acked) with the warning text AND a "The safe way to mod this game:" section containing a "Launch Mod Engine 2" button.
- [ ] **Click "Launch Mod Engine 2" from inside the dialog.** EXPECT: Mod Engine 2 launches (game starts via ME2). The dialog stays open — closing it still requires "Enable anyway" or "Cancel".
- [ ] **Ban-risk gate — loader NOT installed.** Same Elden Ring setup but no ME2 exe in the play folder: EXPECT: the dialog shows "Get Mod Engine 2" and "Get Seamless Co-op" buttons (both catalog entries). Clicking either opens the respective download URL in the browser.
- [ ] **Both installed.** ME2 + Seamless Co-op both present: EXPECT two buttons in the loaders panel — "Launch Mod Engine 2" + "Launch Seamless Co-op".
- [ ] **Non-fromsoft game (no safe loaders in catalog).** Toggle a mod on a ban-risk non-fromsoft game: EXPECT: the dialog shows the warning text and the "Enable anyway" / "Cancel" / checkbox as before, with NO "The safe way to mod this game:" section (the section only renders when `safeLoaders.Count > 0`).
- [ ] **STORE build smoke.** Run the STORE build (no EAC toggle visible): trigger the ban-risk gate — EXPECT: safe-loader guidance renders identically. Confirms STORE users see the safe path (their primary option since EAC offline is absent).

**Why these matter:** `BanSafeLoaderOption` building + the `IReadOnlyList<BanSafeLoaderOption>` delegate signature change are in Core/VM — unit-tested indirectly through the existing ban-risk + loader tests. The dialog rendering (safe-loader section, button labels, Process.Start vs URL open) only exercises on a real WinUI instance with a real game context.

---

## feat/loose-root-mods — organize loose root mods (DS2/Decima)

> **STATUS — BUILT + GATE-PASSED; needs live smoke.** Tasks 1-3 (Core: detector, listing + reversible toggle, intake) are unit-tested; Task 4 wired the App surface (LooseRootBacked routing, category sections, loader-disable warning, vanilla step-aside). The row rendering, dialog, and real-root file moves only exercise on the rig against the actual Death Stranding 2 install.

**What shipped:** decima games are loose-root: mods list from the GAME ROOT via `LooseRootListing` (DirectInject catalog + by-nature ASI/addon/proxy detection), toggle through the proven `DirectInject.Disable/Enable` reversible move with `<dataDir>/loose-disabled` as the holding root, group category-then-name (PLUGINS / SHADERS / LOADERS sections), warn before disabling the ASI loader (warn-and-proceed, never a hard block), and participate in Play-vanilla step-aside/restore. No `#if FULL` anywhere.

**Setup:** re-add DS2 (or edit its `games.json` entry to `"engine": "decima"`) — pre-existing registrations keep their old engine/location config, so a stale entry will NOT pick up the loose-root form.

**Smoke steps:**

- [ ] **Categorized listing.** Open DS2. EXPECT: ReShade under SHADERS (catalog hit, kind "graphics"); Zipliner_v1.1 / DollmanMute / DeathStranding2Fix under PLUGINS with their same-stem `.ini` configs grouped into the row (not separate rows); ShaderToggler + DeathStranding2UI addons under SHADERS; dinput8 under LOADERS. Rows name-sorted within each section.
- [ ] **Game files invisible.** EXPECT NOT listed: `OptiScaler.ini`, `Chiral Clarity.ini`, `NaturalDS2.ini`, `SDR+.ini`, `DS2.exe`, `DeathStranding2Core.dll` — standalone INIs and generic DLLs are never claimed.
- [ ] **Toggle off = reversible move.** Toggle a plugin off. EXPECT: its files leave the game root into `<dataDir>\loose-disabled\<slug>\` (with a `__626mod.json` sidecar) and the game stops loading the mod. Nothing deleted.
- [ ] **Toggle on = byte-identical restore.** Toggle it back on. EXPECT: files return to the game root byte-identical (hash a file before/after if in doubt); holding folder cleared.
- [ ] **Loader warning.** Toggle dinput8 (LOADERS) off. EXPECT: "This mod is a loader" dialog — "…disabling it disables every ASI plugin." with Disable anyway / Cancel. Cancel leaves the file in place and the switch returns to on. Disable anyway moves it to holding. Never a hard block.
- [ ] **Drop install.** Drop a new `.asi` (or a zip containing one) onto the window. EXPECT: it installs to the game root through the recognition gate and lists under PLUGINS. A random readme/text drop is refused with "not a recognized loose mod" — nothing placed among game files.
- [ ] **Vanilla step-aside.** Launch dropdown → "Play vanilla (no mods)". EXPECT: every enabled loose mod (loader included) steps aside to `loose-disabled`, the game launches clean, and "Play modded (restore mods)" restores exactly the stepped-aside set afterward.

**Why these matter:** the Core detector/toggle/intake are unit-tested, but the App routing (a decima toggle used to fall through to the scanner world and silently no-op), the section headers, the loader dialog, and the vanilla stash round-trip against a real root only prove out on the rig.

---

## nexus-loose-identify — review-first Nexus identify for loose rows (App)

> **STATUS — BUILT + GATE-PASSED; needs live smoke.** Core suite green (incl. the MergeMeta apply-path pin). FULL build 0 errors. STORE build 0 errors. STORE seal OK. No `#if FULL` — the `IModTextSearch` capability check + `NexusActionsVisibility` gate IS the flavor gate.

**What shipped:** "Identify loose mods on Nexus…" in the game More menu, visible only when Nexus is connected AND the loaded Nexus plugin implements `IModTextSearch` AND the active game has loose-root rows. The command runs `LooseIdentify.Candidates` → `ProposeAsync` (search delegate self-timeouts at ~10s per call so a hung Nexus request can't stall the batch) → a review dialog: one row per proposal ("query → Title · Author · N endorsements" + trimmed summary, checkbox checked by default; unmatched rows greyed "no confident match", no checkbox). "Apply N matches" (live count, disabled at zero) is the ONLY write path — each approved hit merges over the existing metadata entry via `Scanner.MergeMeta` (existing enrichment survives; manual matches lock) and lands in one atomic `WriteManyMeta`, then rows reload with "Identified N of M loose mods."

**Smoke steps:**

- [ ] **Action visible on DS2.** Nexus connected + updated Nexus plugin (with text search) + DS2 active (loose-root rows present): EXPECT "Identify loose mods on Nexus…" in the More menu next to the other Nexus items.
- [ ] **Run it.** EXPECT: proposals for the plugin/shader stems (Zipliner, DollmanMute, ShaderToggler, …) — note a stem like "Zipliner 1" is expected (CleanModName keeps the trailing digit); NO rows for dxgi / version (loader proxies are never proposed).
- [ ] **Approve a subset.** Uncheck one matched row, hit Apply. EXPECT: button text tracked the count; approved rows gain title / author / endorsement hearts after reload; the unchecked row stays unidentified; status reads "Identified N of M loose mods."
- [ ] **Unrelated fields survive.** A row that had prior enrichment (e.g. detected description / installed date in metadata.json) keeps those fields after an approved name match lands — only identity fields change.
- [ ] **Manual match survives a re-run.** "Match to a mod…" one loose row to a URL, run identify again. EXPECT: that row is not proposed (IsManual excluded), and its entry is untouched after any Apply.
- [ ] **Re-run after identifying.** Run the action again after applying. EXPECT: previously identified rows are gone from the proposal list (NexusModId / sourceConfidence set); "No loose mods need identifying" when everything is matched.
- [ ] **Without the updated plugin.** With an older Nexus plugin (no `IModTextSearch`): EXPECT the menu item absent; every other Nexus action still works.
- [ ] **No Nexus domain.** On a loose-root game with no resolvable Nexus domain: EXPECT a clear "This game has no Nexus domain configured…" dialog, no search, nothing written.
- [ ] **Cancel writes nothing.** Open the dialog, then Cancel. EXPECT: metadata.json unchanged (checksum/timestamp if in doubt).

**Why these matter:** the Core pass (candidates / propose / ToMeta / merge) is unit-tested; the capability-gated visibility, the dialog checkbox flow, the self-timeout under a real slow Nexus call, and the merged write against a real DS2 metadata.json only prove out on the rig.
