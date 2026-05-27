# Next steps after the mod dashboard ships

**Status:** As of 2026-05-27, the mod dashboard lane is merged (PR #56). Master is at `061c95e`. The portable smoke build is at `dist/626-Mod-Launcher-portable-win-x64.zip` (67.3 MB). Smoke list with 17 items is in [`docs/smoke-tests/pending.md`](../../smoke-tests/pending.md).

Two lanes are queued — both can run in fresh-context sessions.

---

## Smoke findings (2026-05-27 — fix these before or during lane work)

Three issues surfaced during smoke. Triage and fix order is up to the next session.

### F1 — ER Characters section shows empty (regression)

**Symptom:** Saves dialog → Characters section says "No editable characters detected in this folder" on a real ER save folder containing `ESxxxx.sl2` / `.co2` / `.err`. **Worked yesterday on the same save** (the "before-edit Celestia 2026-05-26 2040" snapshot in the dialog proves a prior successful read+edit).

**Diagnosis status:** Code in the save-editor read path is byte-identical to PR #49's working version (`git diff 7d5a035..061c95e -- src/ModManager.App/SavesDialog.xaml.cs src/ModManager.Core/SaveEditor/` is empty). The regression must be either (a) something in the published build that behaves differently from the dev build, OR (b) a non-source-code change (config, save-format drift after gameplay, file lock).

**Immediate diagnostic gap:** [`SavesDialog.xaml.cs:160`](../../../src/ModManager.App/SavesDialog.xaml.cs#L160) silently swallows ALL exceptions from `ReadCharacters`:

```csharp
try { slots = svc.ReadCharacters(savePath); }
catch { continue; }   // any parse failure — skip the file, don't fail the dialog
```

No visibility into what's actually throwing. **First fix:** narrow this catch — log the exception type + message to `StatusText` (or a `Debug.WriteLine`) so the next smoke immediately shows the real failure.

**Hypotheses to test once the exception is visible:**
- BND4 parse failure on the `.err` or `.co2` files (different file structure than `.sl2`?)
- Save file locked because the game wrote after the launcher cached file metadata
- An AOT-trim issue in the published build that doesn't affect the dev build (try `dotnet run --project src/ModManager.App` to compare)

### F2 — ER DLL-proxy chip text confuses ModEngine 2 users

**Symptom:** Every ER mod row shows `NEEDS DLL PROXY (DINPUT8/VERSION/WINHTTP)`. User dropped a Mod Engine 2 zip expecting it to clear; the launcher's intake replaced a file inside the zip but the chip persisted.

**Root cause:** Two issues compounding:

1. **Naming.** "DLL PROXY (DINPUT8/VERSION/WINHTTP)" is technically accurate but unparseable to a non-modder. The thing it actually wants is **ELDEN MOD LOADER** (`https://www.nexusmods.com/eldenring/mods/117`). Mod Engine 2 is a different thing entirely — folder-based, no DLL proxy.
2. **Workflow.** Dropping a Mod Engine 2 zip into the launcher routes through mod intake; intake extracts to the mods folder, not the game root. ELDEN MOD LOADER's `dinput8.dll` has to land at the game root next to `eldenring.exe` — the launcher's intake doesn't do that by design (it'd violate the file-ops-stay-reversible invariant).

**Proposed fixes (pick one or both):**
- Rename the chip to `NEEDS ELDEN MOD LOADER` and the catalog `Name` field to "ELDEN MOD LOADER" — keep the dinput8/version/winhttp detect paths internal. Clearer call to action.
- Improve the "Get it here" link surface so it's the obvious next step instead of just a clickable chip.
- (Bigger) Teach the launcher to install root-level DLL proxies via a dedicated "Add framework" flow that drops the file at the game root with reversibility tracked separately from mods.

### F3 — INI editor misses Seamless Co-op's INI (and any direct-inject mod's INI)

**Symptom:** No pencil icon appears on the Seamless Co-op row. User expects to edit `seamlesscoopsettings.ini` (password, etc.) from inside the launcher.

**Root cause:** The pencil-icon detection in [`MainViewModel.ReloadModsAsync`](../../../src/ModManager.App/ViewModels/MainViewModel.cs) globs `*.ini` recursively under `rep.ModFolderAbs` — the mod's tracked folder. Seamless Co-op is a **direct-inject mod**: its files (including the INI) land at `<gameRoot>/SeamlessCoop/seamlesscoopsettings.ini`, NOT inside a tracked mod folder. Direct-inject mods have no "mod folder" in the launcher's model — files spray into the game folder. So the INI is invisible to the current detector.

**Proposed fix:** Add a per-mod **known-config-paths catalog** (parallel to `ToolCatalog`) that maps recognized direct-inject mods to their config files. Seamless Co-op → `SeamlessCoop/seamlesscoopsettings.ini` relative to game root. The pencil icon then appears for both regular-folder-tracked mods AND catalog-recognized direct-inject mods.

This is a small new pure-core surface plus an extension to the row-build INI discovery. ~2-3 hour scope.

### Additional smoke-list clarification

The original BND4 smoke step said "Open a real Elden Ring save in the Saves dialog → Characters section populates" — the user interpreted "open" as a per-file action. Update wording: "Open the Saves dialog with a registered ER game — the Characters section auto-populates from every save type."

---

---

## Lane 1: Elden Ring inventory editing

**Effort:** 17-24h, 12 tasks. **Spec + plan already on master** from PR #48.

**What it ships:** Phase 2 of the FromSoft save editor — adds inventory edit (add item, remove item, set quantity / +N level / infusion). New tabbed `CharacterEditDialog` (identity / attributes / inventory tabs, all commit through one Save edit). Embedded ClayAmore item catalog (Apache-2.0) + alfizari quest-locked items list (MIT) covering ~1,500 ER items.

**Why this lane is ready:**
- Plan: [`docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-inventory.md`](../plans/2026-05-26-saves-editor-fromsoft-inventory.md)
- Spec: [`docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-inventory-design.md`](../specs/2026-05-26-saves-editor-fromsoft-inventory-design.md)
- Licenses clean: ClayAmore Apache-2.0 + alfizari MIT. Honor-the-builders surfaces planned (NOTICE + Settings → About + in-dialog attribution).
- BND4 walk (PR #49) is the load-bearing foundation; the inventory walker hangs off `DiscoverMagicOffset` in [`src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`](../../../src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs).

**Concerns from the plan:**
- Task 0 (format research + offset lock) is load-bearing. Wrong inventory offsets = bricked saves. Snapshot-first + post-write verify catch this; goal is to never trigger.
- Item catalog source: ClayAmore primary + alfizari for ER 1.13+ diff. License-bundle check happens in Task 1.
- Quest-locked items: ~90 items, finalized in Task 0 by cross-referencing wiki questline trackers.

---

## Lane 2: Save-editor pipeline as a reusable skill

**Effort:** brainstorm-first, then spec, then plan, then execute. Not yet scoped.

**What it is:** Meta/process work — turn the FromSoft save editor pattern into a **reusable skill** so adding save editors for new games (DS3, Sekiro, AC6, future FromSoft titles, or other engines entirely) is mechanical instead of bespoke. Use Elden Ring's pattern (BND4 walk + slot offsets + snapshot-first writes + neutral DTOs) as the canonical template; the skill drives an agent through pinning format facts → generating model types → wiring the dialog.

**Why now:** Este flagged this earlier today as "we could probably make it a skill" alongside the ER inventory ask. Knowledge captured during ER inventory editing becomes immediately applicable to the next FromSoft game. The skill turns one-off save-editor builds into a 2-3 day per-game lane.

**Status:** No spec, no plan, no scope yet. Needs `superpowers:brainstorming` from scratch.

**Memory references:**
- [[saves-editor-fromsoft]] — existing brainstorm history
- [[fromsoft-two-mod-worlds]] — engine context (ME2 vs direct-inject)

---

## Recommended order

**Lane 1 (ER inventory) first.** Rationale:
- Spec + plan exist; lane 2 is brainstorm-only
- ER inventory's execution surfaces real-world friction with the format → that friction informs lane 2's skill design
- Lane 2 (the meta-skill) lands stronger if the second concrete editor (DS3 or Sekiro) is in the pipeline OR if ER inventory's lessons are fresh

**Alternative:** if you want the meta-thinking first, run lane 2's brainstorm now while ER inventory is still queued. Both are valid.

---

## Short prompt for the fresh session

Paste this into a fresh Claude Code session after compacting:

```
Picking up where the prior session left off. Master is at 061c95e (mod dashboard PR #56 merged). Local portable build at dist/626-Mod-Launcher-portable-win-x64.zip; smoke list in docs/smoke-tests/pending.md.

First — read docs/superpowers/handoffs/2026-05-27-post-dashboard-next-steps.md. Three smoke findings are captured there:
  F1: ER Characters section shows empty (regression from yesterday's working build)
  F2: ER DLL-proxy chip text confuses ModEngine 2 users
  F3: INI editor misses Seamless Co-op's INI (and any direct-inject mod's INI)

Then wait for me to pick what's next. Options:
  - "F1" → investigate the ER characters regression (narrow the silent catch in SavesDialog.xaml.cs:160 first, then diagnose)
  - "F2" → rename the chip / improve the get-link UX
  - "F3" → add per-mod known-config-paths catalog
  - "lane 1" or "ER inventory" → execute docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-inventory.md via superpowers:subagent-driven-development (cut feat/saves-editor-fromsoft-inventory off master; ~17-24h, 12 tasks; ClayAmore Apache-2.0 + alfizari MIT)
  - "lane 2" or "save-editor skill" → invoke superpowers:brainstorming for the save-editor-as-skill meta work

Recommendation: F1 first (it's a real regression, not a UX issue), then F2+F3 quick fixes, then the lanes. But your call.

Do not start any of these without my pick.
```
