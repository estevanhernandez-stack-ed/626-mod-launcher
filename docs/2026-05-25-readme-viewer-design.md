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
| Display | **In-app, rendered markdown** (a CommunityToolkit markdown control) — render-only. |
| Safety | Content is **attacker-controlled**: render to native controls only (no raw-HTML/script), **remote images off** (privacy), link clicks routed through the existing `SafeUrl` guard + user-initiated only. |

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
mitigations above (render-only, no remote images, `SafeUrl`-gated links) are the controls; add a
note to [docs/security/threat-model.md] when this lands (a mod README is now parsed + rendered).

## File structure

- Create: `src/ModManager.Core/ReadmeCapture.cs` — `PickReadme`.
- Modify: `src/ModManager.Core/Scanner.cs` — capture the readme during zip intake; resolve a mod's readme-cache path.
- Modify: `src/ModManager.Core/DirectInject.cs` — same capture for the direct-inject zip path.
- Create: `src/ModManager.App/ReadmeDialog.xaml` + `.xaml.cs` — markdown viewer (render-only).
- Modify: `src/ModManager.App/MainWindow.xaml` + `.xaml.cs` — "Readme" affordance on the mod row.
- Modify: `src/ModManager.App/ModManager.App.csproj` — add the markdown control package.
- Tests: `tests/ModManager.Tests/ReadmeCaptureTests.cs`.
