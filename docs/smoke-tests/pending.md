# Pending smoke tests

Running log of post-merge smoke needs the orchestrator can't verify automatically. Each entry: what shipped, what to test, why it matters. Strike entries through (or move to a "Cleared" section) once smoked.

---

## PR #49 — BND4 file-table walk (merged 2026-05-26)

**Shipped:** ER save editor's reader + writer now locate save sections by BND4 entry NAME (`USER_DATA011` for the save header, `USER_DATA000`..`USER_DATA009` for the 10 character slots) instead of hardcoded byte offsets like `0x019003B0`. Future ER patches that reshape the file layout still work; patches that rename the save-header entry fail loud with `InvalidDataException` listing the names that WERE found — never silently corrupt a save.

**Synthesized-fixture tests cover** the read/write contract, the resilience claim (12-entry layout + relocated section + renamed header), and the per-entry bounds check at the parser seam. **What they can't cover:** real bytes shipped by FromSoft. The read path is the higher-regression-risk class.

**Smoke steps:**
- [ ] Open a real Elden Ring save in the Saves dialog → Characters section populates with correct name / level / runes / stats matching in-game values.
- [ ] Open a Seamless Co-op save (`.co2`) → Characters section populates correctly.
- [ ] Open a real save with NO active characters (every slot empty) → empty list, no exception toast.
- [ ] (Optional, higher value) Edit a stat on a real save → confirm the edit lands, a snapshot appears in the Snapshots list, and the in-game character reflects the change after launch.

---

## PR #51 — Mod-dependency detection (merged 2026-05-26)

**Shipped:** Every mod row in a framework-gated game (UE4SS, BepInEx, SMAPI, ME2, DLL proxy, Forge/Fabric) gets a red `NEEDS X` chip with a clickable get-link when the framework isn't installed. Post-drop status line names the missing framework and host (`". Heads up: this mod needs UE4SS — get it at github.com."`). Pure-core probe covered by 13 unit tests; App wiring verified by build only.

**Smoke steps:**
- [ ] Switch to Windrose on a machine where `R5/Binaries/Win64/ue4ss/UE4SS.dll` does NOT exist → every mod row shows a red `NEEDS UE4SS` chip; clicking opens the UE4SS releases page in the browser.
- [ ] Restore the UE4SS folder + click Redetect → chips disappear (Redetect funnels through `ReloadModsAsync`, which triggers the framework refresh).
- [ ] With UE4SS still missing, drop a `.pak` mod onto the window → post-drop status line ends with `. Heads up: this mod needs UE4SS — get it at github.com.`
- [ ] Switch to Elden Ring with neither `dinput8.dll` nor `modengine2_launcher.exe` present → direct-inject rows show `NEEDS DLL PROXY...`; folder rows (if any) show `NEEDS MOD ENGINE 2`. Drop a direct-inject mod → drop-line says "needs DLL proxy" (not ME2 — the chip-vs-drop-line agreement fix in commit `534c507`).
- [ ] Install `dinput8.dll` → DLL proxy chip disappears on direct-inject rows; ME2 chip remains on folder rows.

---

## PR #?? — Mod dashboard (Windrose-first tools + INI editor) (merged 2026-05-27)

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
