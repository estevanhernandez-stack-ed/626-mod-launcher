# Mod Dashboard — Windrose-first (Tools + INI Editor) Design

> **Status:** Brainstormed 2026-05-27 with Este. Approved for implementation planning.
>
> **Goal:** Turn the launcher into the central place for managing a game's mods AND its third-party tools. v1 ships drop-zip-installable tools (with auto-snapshot for save editors) and an inline INI editor, focused on Windrose as the day-one target game.

## Why this exists

The launcher today is a mod installer. Users still leave the app to launch third-party tools (save editors, save fixers, INI editors, etc.) from the file system. The "mod dashboard" framing reframes the launcher as the central console for everything mod-adjacent — including community-built tools we don't author.

Per [honor-the-builders](../../../CLAUDE.md) and the [Windrose integration pivot](../handoffs/2026-05-27-windrose-integration-pivot.md): we do NOT bundle third-party tools. The user drops them in, we surface and orchestrate.

## Scope

### In v1

- **Tools panel** — slim row at the top of the main view, above the mod list. Per-game. Surfaces installed tools as launch buttons.
- **Drop-zip tool install** — the existing drop pipeline classifies dropped archives as tools vs mods, routes accordingly.
- **Smart runnable surfacing** — installer picks the launch target (`.exe` / `.bat` / `.ps1` / `.cmd`) automatically; asks once if ambiguous.
- **Save-snapshot before save-editor launch** — tools tagged `EditsSaves` trigger a save-folder snapshot before the tool process starts.
- **Day-one tool catalog** — WSE Save Editor + WSE Save Fix (both Windrose, both Nexus-distributed). Known to the launcher; surfaced as `[Get …]` chips when not installed (same pattern as PR #51's `NEEDS UE4SS`).
- **Inline INI editor** — pencil icon on a mod row when the mod folder contains `.ini` files. Click → in-app text editor with snapshot-before-save.
- **Honor-the-builders surfaces** — button tooltips, Settings → About section, NOTICE entries that explicitly say "never bundled."

### Out of v1 (deferred to v2+)

- Cross-game tools (a tool installed for Windrose stays Windrose-only)
- Tool version tracking / update detection
- Folder shortcuts ("Open game folder," "Open save folder," etc.)
- INI structured-form view (just plain text edit for v1)
- Tools for non-Windrose games in the day-one catalog (the architecture scales — content stays minimal)
- Game-running detection at tool launch (snapshot covers most corruption risk)

## Architecture

Pure-core / thin-shell split per the [project laws](../../../CLAUDE.md):

```
ModManager.Core.Tools          (pure, headless-testable)
├── ToolEntry                  record — one installed tool
├── ToolCatalog                static class — known tools, fingerprints
├── ToolDetector               static class — classify archive as tool|mod|ambiguous
├── ToolRegistry               per-game JSON persistence
└── ToolIntake                 extract zip → register → return ToolEntry

ModManager.App.Tools           (WinUI 3)
├── ToolsPanel                 slim-row XAML control above mod list
├── ToolsPanelViewModel        bound to MainViewModel.Tools
├── IniEditorDialog            inline INI editor with snapshot
└── ToolConfigureDialog        change runnable / toggle EditsSaves / rename
```

State on disk:

- `_626mods/<game-id>/tools/<tool-id>/` — extracted tool folders
- `_626mods/<game-id>/tools.json` — per-game registry (camelCase per shared-json convention)
- `_626mods/<game-id>/.ini-history/<mod-id>/<ini-rel-path>.<timestamp>.bak` — INI undo history

## Data shapes

### `ToolEntry` (Core record)

```csharp
public sealed record ToolEntry(
    string ToolId,        // stable id from extracted folder name (kebab-case)
    string DisplayName,   // button label
    string InstallDir,    // absolute path under _626mods/<game>/tools/<id>/
    string Runnable,      // relative path to launch target
    bool EditsSaves,      // controls pre-launch save snapshot
    string? GetUrl,       // for "Get it here" link if uninstalled but catalog-known
    string Source);       // "catalog" | "user"
```

### `ToolCatalog` (static class)

Mirrors `FrameworkDeps.Catalog`. One entry per known tool, keyed by zip-filename pattern + content fingerprint.

Day-one entries (author + project: **RimmyCode / WSE Project** — `https://github.com/RimmyCode/Windrose-Save-Editor`):

| Tool | Engine | Steam App ID | `EditsSaves` | GetUrl |
|---|---|---|---|---|
| WSE Save Editor | `ue-pak` | `3041230` | `true` | `https://www.nexusmods.com/windrose/mods/153` |
| WSE Save Fix | `ue-pak` | `3041230` | `true` | (pinned by implementer during catalog-seed task — same author, separate Nexus listing) |

Each catalog entry includes: `DisplayName`, `EditsSaves`, `GetUrl`, expected runnable filename pattern, applicable engine + steamAppId, attribution metadata.

### `tools.json` schema (per-game)

```json
{
  "tools": [
    {
      "toolId": "wse-save-editor",
      "displayName": "WSE Save Editor",
      "installDir": "C:\\...\\_626mods\\windrose\\tools\\wse-save-editor",
      "runnable": "WSE_Save_Editor.exe",
      "editsSaves": true,
      "getUrl": "https://www.nexusmods.com/...",
      "source": "catalog"
    }
  ]
}
```

camelCase per `[[shared-json-camelcase]]`.

## Drop pipeline integration

`ToolDetector.Classify(archive, gameContext)` runs FIRST in the existing intake:

1. **Catalog match (highest confidence):** zip filename matches a catalog pattern OR archive contents fingerprint to a catalog entry → return `Classification.Tool(catalogEntry)`. Apply catalog metadata directly.
2. **Heuristic match (medium confidence):** archive contains at least one executable (`.exe` / `.bat` / `.ps1` / `.cmd`) AND no recognized mod signatures (`.pak` in engine-specific path, `Scripts/*.lua`, `manifest.json` with mod-shape) → return `Classification.Tool(null)`.
3. **Mod signature wins** if both heuristic-tool AND mod signatures present → return `Classification.Mod`.
4. **Ambiguous:** neither catalog match nor heuristic-tool but executables present → return `Classification.Ambiguous`.

**Routing:**

- `Classification.Tool` → `ToolIntake.Install(archive, gameContext, catalogMatch?)` → extract → register → toast: *"Installed WSE Save Editor as a tool for Windrose."*
- `Classification.Mod` → existing `Scanner.ExecuteIntake` (unchanged path).
- `Classification.Ambiguous` → dialog asks "Mod or Tool?" → route per user pick.

## Runnable surfacing

`ToolIntake` picks the launch target on install:

1. Catalog entry specifies expected runnable filename pattern → use it.
2. Single `.exe` in the extracted folder → use it.
3. Multiple `.exe`s, one matches the zip name or catalog `DisplayName` (case-insensitive) → use it.
4. Filter out `*install*`, `*setup*`, `*update*`, `*dep*` filename patterns.
5. Multiple legitimate runnables remain → drop dialog asks: *"Which one is the main launcher?"*

The picked path stores in `ToolEntry.Runnable`. User can change it later via the configure dialog (small "..." menu on the tool button).

## Tools panel UI

A slim row above the mod list when a game is active. Three states:

**Tools installed:**
```
┌─ Windrose ───────────────────────────────────────────┐
│  [WSE Save Editor]  [WSE Save Fix]  [+ Add Tool]     │
├──────────────────────────────────────────────────────┤
│  Mod List...                                         │
```

**Empty:**
```
┌─ Windrose ───────────────────────────────────────────┐
│  No tools yet. Drop a zip to install, or [+ Add].    │
├──────────────────────────────────────────────────────┤
```

**Known-but-uninstalled** (mirrors the `[NEEDS UE4SS]` chip from PR #51):
```
┌─ Windrose ───────────────────────────────────────────┐
│  [Get WSE Save Editor ↗]  [Get WSE Save Fix ↗]  [+]  │
├──────────────────────────────────────────────────────┤
```
"Get" buttons open the Nexus page in the browser.

### Tool button behavior

**Click — save-editing tool (`EditsSaves: true`):**
1. Toast: *"Snapshotting save before launch…"*
2. Snapshot the save folder via the existing `SaveManager.Backup(...)` pipeline. Label: `before-<tool-display-name>-<timestamp>`.
3. If snapshot fails → abort, toast: *"Couldn't snapshot the save. Tool launch cancelled. Your save is untouched."*
4. Launch the tool process async (fire and continue — user keeps using the launcher).
5. On tool exit, toast: *"WSE Save Editor closed. Snapshot saved as 'before-WSE-Save-Editor-2026-05-27 09:34'."* (auto-refresh the Saves dialog if open).

**Click — non-save tool:**
1. Just launch the tool process async. No snapshot, no exit toast.

**Click — broken runnable** (file moved, locked, missing):
1. Error toast + an "Open install folder" fallback button.

### Configure dialog (right-click on button or `…` menu)

- Change runnable (re-pick from detected executables in `InstallDir`)
- Toggle "Edits saves" checkbox
- Rename display label
- Open install folder
- Uninstall (deletes folder + registry entry — irreversible, toast confirmation)

### Add Tool button (`+`)

Opens a file picker for a zip; same intake flow as drop.

## INI editor

### Access point

A pencil icon appears on a mod row when the mod folder contains `.ini` files (detected at scan time, cached on the row VM).

Click → small picker if multiple INIs, else direct to the editor dialog.

### Editor dialog

- In-app `ContentDialog` with a `TextBox` (multi-line, monospace font) showing the file contents.
- Title bar shows the relative path (`Settings.ini` or `Mods/Foo/config.ini` etc.).
- "Save" + "Cancel" buttons.

### Save flow (snapshot-before-write)

1. Compute the backup path: `_626mods/<game>/.ini-history/<mod-id>/<ini-rel-path>.<timestamp>.bak`
2. Copy the current INI contents to the `.bak` path (atomic — temp + rename).
3. If the `.bak` write fails → abort, toast: *"Couldn't snapshot the INI. Edit not applied."* (matches the snapshot-first safety law.)
4. Write the new contents to the actual INI via `fs-atomic.writeAtomic`.
5. Toast: *"Saved Settings.ini. Previous version kept in INI history."*

### Restore previous

A "Restore previous" button on the editor dialog reads the most recent `.bak`, swaps the contents in the editor (does NOT auto-save — user reviews and clicks Save).

### Out of scope (v2+)

- Syntax highlighting
- Structured key=value form view
- Multi-file diff
- Find / replace

## Honor-the-builders

Three surfaces, all required for v1:

### 1. Button tooltip

Hover on a tool button shows:
> *"WSE Save Editor by RimmyCode (WSE Project). Catalog metadata only — never bundled. Click to launch."*

### 2. Settings → About

A new "Installed tools" section listing each installed tool with author + Nexus link + license note.

### 3. NOTICE file entry

Each catalog tool gets an attribution block in the repo's `NOTICE` file. Required language:

```
WSE Save Editor and WSE Save Fix — by RimmyCode (WSE Project).
Surfaced by launching the user's own install. Never bundled or
redistributed. The launcher knows of these tools via a fingerprint
catalog only; their source code, binaries, and item-ID database are
not included with this product.

WSE Save Editor: https://www.nexusmods.com/windrose/mods/153
Source: https://github.com/RimmyCode/Windrose-Save-Editor
License: Personal use, per the Nexus Mods page terms.
```

The "never bundled" language is load-bearing — it's the line that keeps us clean on the personal-use license that gates these tools.

## Safety + error handling

| Failure mode | Behavior |
|---|---|
| Snapshot fails before save-editor launch | Abort launch. Toast: *"Couldn't snapshot the save. Tool launch cancelled. Your save is untouched."* |
| Runnable missing or locked at launch | Error toast + "Open install folder" fallback. |
| Malformed / corrupt zip on drop | Reject before any disk write. Toast: *"This zip didn't extract cleanly — try re-downloading."* |
| Uninstall while tool process is running | Block. Toast: *"WSE Save Editor is open. Close it and try again."* |
| INI save fails mid-write | `.bak` preserved, original file untouched. Toast with retry. |
| Drop classifier picks wrong category | Recovery: tool installed as mod can be uninstalled + re-dropped; the drop dialog's "Mod or Tool?" branch handles ambiguous cases up front. |
| Two tools dropped at once | Process sequentially with a progress toast per tool. |
| Game running during save-editor launch | No auto-check in v1. Snapshot covers most corruption risk. v2 polish. |

## Testing posture

| Layer | Tests |
|---|---|
| `ToolEntry` record | Pure data — pinned by one shape test. |
| `ToolCatalog` | Pinned: 2 entries (WSE Save Editor + WSE Save Fix), each has valid `EditsSaves`, `GetUrl`, fingerprint pattern. |
| `ToolDetector.Classify` | ~10 unit tests: catalog match, heuristic-tool, mod signature wins, ambiguous, malformed archive. |
| `ToolRegistry` JSON round-trip | Save + load + camelCase check. |
| `ToolIntake.Install` | Round-trip: dropped zip → extracted folder → registry entry → expected runnable. |
| `ToolsPanel` + `ToolConfigureDialog` | Smoke-tested via Task 7 (no automated WinUI tests; matches existing pattern). |
| `IniEditorDialog` | Snapshot-before-write contract tested via a Core-level helper (`IniEditService.SaveWithBackup`) that the dialog wraps; the helper is unit-tested. Dialog itself is smoke-tested. |

Expect ~25 new unit tests, all green.

## Dependencies

No new NuGets. We reuse:

- `SharpCompress` (already in Core) for zip extraction
- `System.Text.Json` for `tools.json`
- `Microsoft.UI.Xaml` for the panel + dialogs

## Composes with

- **PR #51 (mod-dep chip)** — the `[Get …]` chip pattern is reused for known-but-uninstalled catalog tools.
- **PR #49 (FromSoft save editor)** — `SaveManager.Backup(...)` is reused as the snapshot primitive for save-editing tools.
- **PR #44 (snapshot-first safety law)** — extended to cover third-party save editors via the `EditsSaves` flag.
- **Existing drop pipeline** — `ToolDetector.Classify` adds a branch before `Scanner.ExecuteIntake`; the mod-side flow is unchanged.

## Open questions

None blocking. Implementation choices we'll lock during the plan:

- Exact heuristic for "executable at root vs nested" — refine during Task 3.
- Tool icon: do we ship a default tool icon, or use a generic one? (v1: generic monospace text button, no icon.)
- INI detection during scan: glob all `*.ini` recursively in the mod folder, or only at root? (Recommend: recursive, capped at first 20 hits per mod to avoid pathological cases.)
- INI `.bak` retention policy: keep all forever, keep last N per file, or expire after M days? (Recommend v1: keep last 10 per INI file, expire none — simple, predictable, low disk cost.)
- WSE Save Fix Nexus URL: implementer pins it during the catalog-seed task by web-searching `"WSE Save Fix" site:nexusmods.com` or asking RimmyCode directly.
- Tool process exit detection: `System.Diagnostics.Process.Exited` event with `EnableRaisingEvents = true`, fired off the UI thread. Standard .NET pattern.

## Done definition

- ✅ Tools row visible in the main view when a game is active.
- ✅ Drop a tool zip → smart-classified as tool → installed → button appears.
- ✅ Click WSE Save Editor button → save snapshot lands first → tool launches → tool closes → toast confirms.
- ✅ Mod row with `.ini` files shows pencil icon → click → editor dialog → save creates `.bak` → restore previous works.
- ✅ Known-but-uninstalled WSE tools show `[Get … ↗]` chips with working Nexus links.
- ✅ NOTICE has the attribution block. Settings → About lists installed tools.
- ✅ ~25 new unit tests green. Full suite green.

## Next step

Implementation plan via `superpowers:writing-plans`. Expect 7-10 tasks across Core + App layers.
