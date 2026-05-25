# Convert on-ramp + variant grouping + mod tools — combined design

- **Date:** 2026-05-25
- **Status:** Approved in brainstorm (shape + all forks confirmed with Este). Ready for spec review.
- **Why:** The product's adoption play. A convert — someone coming from Vortex/MO2, or no manager at
  all, with **a lot of mods** — must be able to go from "a folder full of downloads" to "a fully
  installed, identified, labeled, safe-to-play loadout" in one move. This spec is the on-ramp, plus
  the two capabilities that make it real: **variant-aware import** and **running companion mod tools
  safely**.

## The hard-won identification reality (drives everything below)

Today's live testing established exactly what can identify a mod, and it shapes the whole design:

| Source | Hashes | Identifies… | Use |
|---|---|---|---|
| **CurseForge fingerprint** | the **extracted file** (MurmurHash) | mods already installed, in place | workhorse for any install |
| **Nexus md5** | the **published archive** | a **dropped archive** only (extracted file's hash never matches) | shines on the import path |
| **CF name-search** | (name) | fuzzy fallback | fills the rest |

**Consequence:** the **pile-of-zips** import is the sweet spot — the user still has the archives, so
**all three** identifiers work (Nexus md5 included). Re-identifying already-extracted installs can't
use Nexus md5 (archive is gone); that's why the on-ramp is archive-first.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| First convert profile | **Pile-of-zips** — point at a folder of downloaded archives → install + identify each in one pass. (MO2 import is a later, separate pass; "in-place Vortex/manual" is largely covered by the existing scan + CF fingerprint.) |
| Archive formats | **zip + 7z + rar** via **SharpCompress** (pure-managed, bundles into the self-contained build → zero user prerequisites). One Core archive seam replaces the scattered `System.IO.Compression.ZipFile` calls. |
| Variant packs | **Variant grouping folded in** — an archive with `2x/3x/5x` files (pick one) installs as **one row with a selector**, mutually exclusive. Load-bearing for import; not a fast-follow. |
| Mod tools | **Combined into this spec.** A per-game **tools catalog + safe runner** that lends our save-detection + snapshot spine to external tools (save editors, etc.). |
| Tool prerequisites | The **app stays zero-prereq**; a tool's own prereqs (e.g. Python for a `.py` editor) are **detected + guided**, never bundled or silently executed (v1). |

## Operating laws this rests on

Pure-core/thin-shell · reversible + atomic file ops (snapshot-first for the save tree) · no silent
overwrite on intake · never run a command we weren't given (extends the `LaunchOptions` "never guess"
law to tools) · render mod/tool-supplied strings as text · **zero user-installed prerequisites**
(bundled deps are fine — corrected deps law).

---

## Part A — Import on-ramp (bulk install + identify from a folder of archives)

### A1. Entry + flow (App)

- **Empty-state CTA:** when a registered game has no mods, the mod list shows *"Got a folder of mods?
  Import them →"* — the convert's first moment.
- **Menu:** game-options (⚙) → **"Import mods from a folder…"**. Drag-a-folder onto the window also
  routes here.
- **Folder picker → recursive scan** for `.zip/.7z/.rar` (and loose mod files) → install + identify
  each → **summary**: *"Installed 47 · identified 41 (32 CurseForge, 9 Nexus) · 6 unmatched · 2
  skipped (unsupported)"*, with a live **N-of-M progress** (importing many mods is slow — show it).

### A2. Multi-format archive seam (Core)

- Introduce **`IArchiveReader`** (Core) — read entry names + extract entries — backed by a
  **SharpCompress** implementation handling `.zip/.7z/.rar`. Replace the raw `ZipFile` usage in
  `Scanner` (intake, `CaptureReadmes`, `ZipModKeys`, `Md5IdentifyArchivesAsync`) and `DirectInject`
  (`InstallZip`, `Plan`, `CopyIncoming`) with this seam, so **all formats flow through one place**.
- **Zip-slip / path-traversal guards** move into the seam (one safe extractor for every format),
  reusing the existing `SafeRelative`/`WrapperPrefix`/`IsUnder` logic.
- SharpCompress is pure-managed → bundles, **keeps the Tauri-portability hedge** intact, so it's
  acceptable in Core.
- **Format limits noted:** SharpCompress RAR5 / solid-7z streaming have edges; unreadable archives
  are reported as `skipped`, never crash the import.

### A3. Pipeline (reuses today's stack)

Per archive, in order:
1. **Install** via the existing collision-aware intake (`PlanIntake`/`ExecuteIntake`) — no silent
   overwrite; the reversible replace flow handles updates.
2. **Identify** (best-effort, never fails the install): **CF fingerprint** on the extracted files →
   **Nexus md5** on the archive (`Md5IdentifyArchivesAsync`) → **CF name-search** fallback. Exact
   wins over fuzzy. Honor-the-builders fields (author/donation/source) fill in via the existing
   merge + mod-row render.

### A4. Scale

- **Batch** the CF fingerprint calls (the API takes a list). **Throttle** Nexus md5 (one call per
  archive) so a 50-archive folder doesn't trip per-key rate limits; surface progress, not a freeze.

---

## Part B — Variant grouping (folded in)

### B1. Model (Core)

- A **variant group** = ≥2 mods sharing a `Variant.ParseVariant(...).Base` but with different tags
  (`2x`, `3x`, `6h_2x`, …). They are **mutually exclusive** — exactly one variant's files may be
  active (they're alternate versions of the same mod).
- **Effective state:** one variant **active** (files in the mod folder), the rest **held** (in the
  disabled holding folder) — reusing the reversible `DisableMod`/`EnableMod` moves. Switching variant
  = enable the chosen, disable the others (atomic, reversible, never deletes).

### B2. Presentation (App)

- A variant group renders as **one row** with the base name + a **variant selector** (segmented
  control / dropdown of the tags) showing which is active. A single-variant base stays a normal row
  with its variant chip (today's behavior).
- The selector drives the mutual-exclusion enable/disable through the parent VM.

### B3. Import interaction

- When an imported archive contains multiple variant files: **install all the files**, then
  **auto-activate one** (default: highest multiplier, else first) and **hold the rest** — so a pack
  never lands with every multiplier active (which would break the game). The selector lets the user
  change it after.

---

## Part C — Mod tools (catalog + safe runner)

### C1. Catalog (Core model, sibling to `LaunchOptions`)

- A per-game **tools catalog**. Each tool: `Name`, `Kind` (`exe` | `python` | `batch`), `Path`/script
  (relative, resolved like other paths), `Args`, `WorkingDir`, `Prereqs` (e.g. `python>=3.10`), and a
  **`SafetyClass`** (`none` | `editsSaves` | `editsGame`).
- Sources: the **user adds a tool** (point at the `.exe`/`.py`/`.bat`, confirm), the **agentic
  profile** declares known tools (structure-not-absolute, agent-fillable), or a curated/verified
  catalog later. **Never guessed, never auto-run** (the `LaunchOptions` law).

### C2. Runner (App)

- **Command resolution (pure, Core):** turn a tool + the machine's interpreters into a runnable
  command. For `python`: prefer the **`py -3` launcher**, else `python` on PATH.
- **Prereq gate:** if the interpreter is missing, **don't run** — surface an honest message
  (*"This tool needs Python 3.10+ — install from python.org and tick 'Add to PATH'."*) with a
  `SafeUrl`-gated link. (A bundled portable Python is a future "it just works" upgrade — out of v1.)
- **Launch:** `Process.Start` with the resolved command + working dir.

### C3. Safety — lending our spine to external tools (the differentiator)

- A tool with `SafetyClass = editsSaves` runs **inside a `SaveManager.Backup` snapshot taken first**
  (tool-agnostic — even a tool that doesn't back up itself is protected) and is **handed the
  resolved save path** (we already auto-detect it via `SaveLocator`, better than a single-game tool's
  hardcoded path). A **game-closed check** warns/blocks before running (detect the game process).
- Running external code is a trust surface: **only registered/confirmed tools**, surfaced strings as
  text, links `SafeUrl`-gated.

### C4. Intake "tool" class (the one import↔tools intersection)

- Tools often ship as downloads (this save editor is a `.py` in a Files-tab zip). Intake gains a
  **`tool` classification** so a dropped archive/file recognized as a tool is **registered in the
  tools catalog**, not mis-installed as a mod. Detection is conservative (e.g. a `.py`/`.exe` with no
  mod-class files, or a tool signature) and **confirmed by the user** before registering.

---

## Architecture (pure-core / thin-shell)

- **Core (pure, unit-tested):** `IArchiveReader` + SharpCompress impl + the moved zip-slip guards;
  variant-grouping logic (group detection, active/held resolution); the tools-catalog model + pure
  command/prereq **resolution** (no process launch); the intake `tool` classification.
- **App (build-verified + smoke):** folder picker + progress + summary UI; the variant selector on
  the row; the tools UI (list/add/run) + the actual `Process.Start` runner + Python PATH probe +
  game-process detection; the empty-state CTA.

## Dependencies

- **SharpCompress** (Core) — `.zip/.7z/.rar` reading/extraction, pure-managed, **bundled** → zero
  user prereq. (We already added `System.Security.Cryptography.ProtectedData` for the Nexus key.)
  Per the corrected law, a bundled dep that earns its place is fine; this one is the difference
  between handling a real downloads folder and skipping half of it.

## Security / safety

- **External execution:** only registered/confirmed tools; never guess a command; never auto-run.
- **Save-touching tools:** `SaveManager.Backup` snapshot first; game-closed check; resolved save path.
- **Archive extraction (all formats):** zip-slip/traversal refused in the one seam; existing files
  never silently overwritten (collision flow).
- **Tool prereqs:** detected + guided, never bundled/executed silently; the app itself stays
  zero-prereq.
- **Attacker-controlled strings** (mod + tool names/descriptions) render as text; links `SafeUrl`-gated.

## Testing (test-first, pure Core)

1. **`IArchiveReader`** — read entries + extract for `.zip` (and `.7z`/`.rar` fixtures); zip-slip /
   traversal entry refused across formats; unreadable archive → reported, no throw.
2. **Variant grouping** — group detection by `Base`; resolving active vs held; switching variant
   enables one + disables the rest (assert the reversible moves); a single-variant base stays a plain row.
3. **Import identify order** — exact (fingerprint/Nexus-md5) beats name-search; a miss never fails the install.
4. **Tools** — command resolution (`python` → `py -3`/`python`); a missing prereq yields a gate (not a
   launch); catalog round-trip via `AtomicJson`.
5. **Classification** — a dropped tool archive classifies as `tool`, a mod as `mod`, a world as
   `save-mod` (D7) — no cross-contamination.

App UI (folder import, variant selector, tools list + run) is build-verified + a live smoke test
(import a real Windrose downloads folder; switch a variant; run the Windrose save editor through the
snapshot-wrapped runner with the game closed).

## Scope / non-goals (v1)

- **No MO2 import** (virtual-FS instance parsing) — its own later pass.
- **No downloading** from CF/Nexus — point/drop only (identification, not acquisition).
- **No bundled Python** — detect + guide; portable-runtime bundling is a future upgrade.
- **Single save profile** assumed (carried from D7); multi-profile later.
- RAR5/solid-7z edge cases reported as skipped, not guaranteed.

## Build order (one spec, staged build — each test-first, its own PR off master)

1. **Archive seam** (`IArchiveReader` + SharpCompress; migrate intake/DirectInject off raw `ZipFile`;
   move zip-slip guards in). Foundational; unlocks 7z/rar everywhere.
2. **Folder import** (bulk install + identify pipeline + progress/summary + empty-state CTA).
3. **Variant grouping** (Core model + row selector + mutual-exclusion + import auto-activate-one).
4. **Mod tools** (catalog model + command/prereq resolution + runner + save-snapshot safety +
   intake `tool` class).

## File structure (indicative)

- Create: `src/ModManager.Core/ArchiveReader.cs` (`IArchiveReader` + SharpCompress impl);
  `src/ModManager.Core/VariantGroup.cs` (grouping/active-held logic);
  `src/ModManager.Core/ModTools.cs` (catalog model + command/prereq resolution);
  App: an import service/flow, a tools service + runner, the variant selector + tools UI.
- Modify: `src/ModManager.Core/Scanner.cs` + `DirectInject.cs` (use the archive seam; tool/variant
  hooks); `Intake.cs` (`tool` class); `GameEntry.cs` + `GameProfileImport.cs` (tools catalog on the
  game + agentic profile); `ModManager.App.csproj` (SharpCompress); the mod-row VM/XAML; DI host.
- Tests: `ArchiveReaderTests`, `VariantGroupTests`, `ModToolsTests`, classification + import-identify tests.

## Open questions (for the build, not blockers)

- Variant default-active heuristic (highest multiplier vs first) — confirm the rule per engine.
- Tool detection signatures (how confidently we auto-classify a dropped `.py`/`.exe` as a tool vs ask).
- Whether the verified/curated tools catalog (like verified `LaunchOptions`) lands in v1 or later.
