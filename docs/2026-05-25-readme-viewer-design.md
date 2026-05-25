# Readme viewer — design

- **Date:** 2026-05-25
- **Status:** Approved (shape confirmed with Este)
- **Roadmap:** Phase B3 in [docs/2026-05-25-backlog-roadmap.md](2026-05-25-backlog-roadmap.md).
- **Why:** Mods ship READMEs that carry the stuff a mod manager can't infer — install rules,
  **MP-compatibility notes**, and required settings (the Seamless co-op password; the save-mod
  "choose LOCAL save / never touch RocksDB_v2" rules). The launcher never surfaces them today. This
  feeds the MP-safety work (C5) and the save/world-mods work (D7), which both lean on readme content.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Source | **Capture at intake** (keep the dropped zip's README with the mod) **+ the CurseForge description** the app already fetches as fallback. Works for any mod type incl. bare paks; Nexus-ready. |
| Display | **In-app, rendered markdown**, render-only. **Implemented with an in-house render-only renderer** (chosen 2026-05-25, Este) — NOT the CommunityToolkit Labs/preview markdown control: it adds a runtime UI dependency (supply-chain + binary size, against the "no deps casually" law) and carries build risk on net10/WinAppSDK 2.1.3. A pure `Markdown.Parse` (Core, tested) + a `ReadmeRenderer` that maps the parsed model to native `RichTextBlock` controls honors the same intent, is safer (no third-party markdown parser surface), and guarantees the build. |
| Safety | Content is **attacker-controlled**: render to native controls only (no raw-HTML/script), **remote images off** (the model has no image span — images aren't rendered), link clicks routed through the existing `SafeUrl` guard (http(s) only; other schemes degrade to plain text). |

## Scope shipped (v1)

- **Standard Scanner intake path** (the dominant case — paks, CF mods, the Windrose mods): readme captured at intake, viewer wired on the mod row. DirectInject (FromSoft loose-file mods) readme capture/viewer is a fast-follow — the mechanism (`PickReadme`, the cache, `Markdown`, `ReadmeRenderer`) is all reusable; only the direct-inject row keying needs wiring.
- **Renderer:** in-house. Pure parser `ModManager.Core.Markdown.Parse` (block/span model, flat, no nesting) + `ModManager.App.ReadmeRenderer` (blocks -> `RichTextBlock`). Links: real `Hyperlink` only for `SafeUrl.IsHttpUrl`, else plain text.

## Architecture (pure-core / thin-shell)

### Core (pure, unit-tested)

- **`ReadmeCapture.PickReadme(IEnumerable<string> entryNames) : string?`** — choose the best readme
  from a set of zip-entry / file names: prefer `README.md` then `README.txt` (case-insensitive),
  else the first `.md`, else the first `.txt`; none → null. Pure + tested. (Path-safety: callers use
  the basename / the existing zip-slip guards when extracting — `PickReadme` only selects a name.)

### Capture at intake (Scanner + DirectInject — IO)

- During **zip intake** (both the standard `Scanner` path and `DirectInject`), after the mod files
  are placed, if `PickReadme` finds one, extract it to the readme cache keyed by each mod added from
  that zip: `<dataDir>\readmes\<modKey>.<ext>` (`modKey` via the existing `Scanner.ModKey`). It's a
  **derived cache** — re-captured on re-intake, so no atomic-write/reversibility ceremony.
- Multi-mod zip → the readme is written for each mod key added from it (best-effort; one shared
  readme is the common real case).

### App (thin shell, build-verified + smoke-tested)

- **`ReadmeDialog`** — a `ContentDialog` hosting a CommunityToolkit `MarkdownTextBlock`. Resolution
  order: captured readme file → else the mod's CurseForge description (from metadata) → else
  "No readme available." Configure the control **render-only**: no raw-HTML passthrough, remote
  image loading disabled, and a `LinkClicked` handler that opens only `SafeUrl.IsHttpUrl` links via
  the existing safe-open path.
- **Mod row** gains a **"Readme"** affordance, enabled when a captured readme or a CF description
  exists for that mod.

## Source priority (view time)

```
captured readme (<dataDir>\readmes\<modKey>.<ext>)
  -> else the mod's CurseForge description (already in metadata)
  -> else "No readme available."
```

## Dependency

Adds a CommunityToolkit markdown control (e.g. `CommunityToolkit.WinUI.Controls.MarkdownTextBlock`;
exact package verified at build). Deliberate UI dependency, render-only — flagged per the
"don't add runtime deps casually" rule. Core stays dependency-free.

## Error handling

- Unreadable / missing readme file → fall through to the CF description, then the empty state.
- Markdown that fails to render → show the raw text rather than crash the dialog.
- A `LinkClicked` to a non-http(s) URL → ignored (the `SafeUrl` guard).

## Scope / limits (v1)

- **Forward-only capture:** mods added after this ships get their shipped readme; already-installed
  mods fall back to the CF description (no retroactive capture — the source zip is gone).
- Render-only markdown; **no remote images** in v1 (revisit with a privacy toggle later, alongside
  the deferred mod-images work).
- One readme per mod (the best-pick); no multi-document browser.

## Testing (test-first, pure Core)

`tests/ModManager.Tests/ReadmeCaptureTests.cs`:
1. `PickReadme` returns `README.md` when present alongside other `.md`/`.txt`.
2. Prefers `README.txt` over a non-readme `.md` when no `README.md`.
3. Falls back to the first `.md`, then the first `.txt`, when no `README*`.
4. Case-insensitive (`readme.MD`).
5. None present (only `.pak`/`.ucas`) → null.

App capture (Scanner/DirectInject) + `ReadmeDialog` are build-verified + a live smoke test (drop a
mod with a README, open it, confirm it renders, links gated, no remote fetch).

## Threat model

Rendering mod-supplied markdown is a **new attack surface** (READMEs are attacker-controlled). The
mitigations above (render-only, no remote images, `SafeUrl`-gated links) are the controls.

**As shipped (2026-05-25):** the in-house renderer *reduces* this surface vs. a third-party markdown
control — there is no external parser to trust. Controls in place:
- **Parse, don't execute:** `Markdown.Parse` (pure, tested) produces a typed block/span model; the
  renderer only ever constructs native `Run`/`Hyperlink` controls — no HTML, no `NavigateToString`,
  no script path.
- **No remote fetch:** the model has no image span, so readme images are never loaded (privacy).
- **Link scheme gate:** the parser captures URLs verbatim; `ReadmeRenderer` makes a clickable
  `Hyperlink` only when `SafeUrl.IsHttpUrl` (http(s)), else renders the link as plain text — a
  `javascript:`/`file:`/`steam:` readme link can't become clickable.
- **Cache-write traversal:** readmes are extracted to `<dataDir>\readmes\<modKey>.<ext>`; the key is
  basename-derived (`Path.GetFileName`) and guarded by `IsSafeKey` (rejects invalid filename chars),
  with a negative test pinning that a zip-slip entry name can't escape the cache dir.

(This repo has no standalone `docs/security/threat-model.md`; the threat note lives here with the design.)

## File structure (as shipped)

- Create: `src/ModManager.Core/ReadmeCapture.cs` — `PickReadme` (pure selection).
- Create: `src/ModManager.Core/Markdown.cs` — pure markdown parser (block/span model).
- Modify: `src/ModManager.Core/Scanner.cs` — `CaptureReadmes` at intake (wired into `AddMods` + `ExecuteIntake`), `ReadmePathFor`, `IsSafeKey`.
- Create: `src/ModManager.App/ReadmeRenderer.cs` — in-house render-only renderer (parsed model -> `RichTextBlock`); no XAML dialog, no package.
- Modify: `src/ModManager.App/MainWindow.xaml` + `.xaml.cs` — "Readme" affordance on the mod row + `OnShowReadme` (inline `ContentDialog`).
- Modify: `src/ModManager.App/ViewModels/{ModRowViewModel,MainViewModel}.cs` — `ReadmeFilePath`/`ReadmeVisibility`/`GetReadmeMarkdown`, set from `ReadmePathFor`.
- Tests: `tests/ModManager.Tests/{ReadmeCaptureTests, MarkdownTests, ReadmeIntakeTests}.cs`.
- **Deferred:** `DirectInject` readme capture/viewer (mechanism reusable; only direct-inject row keying remains).
