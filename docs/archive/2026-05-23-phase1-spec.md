# .NET 10 / WinUI 3 Rewrite — Scope

- **Date:** 2026-05-23
- **Status:** Approved (shape confirmed with Este)
- **Why:** A native Windows rewrite to Este's quality standard. The Electron app is the working prototype and the **executable spec** — its pure cores + 137-test suite + design docs are the contract this rewrite mirrors. (.NET 10 is the committed v2 destination — logged in the 626 dashboard decision log.)

## Decisions (locked)

| Question | Decision |
|---|---|
| UI framework | **WinUI 3** (Windows App SDK), .NET 10. Windows-only — matches Steam games + Store. |
| Repo | **New dedicated repo** (own solution + CI). This Electron repo is the cross-referenced spec. |
| MVVM | **CommunityToolkit.Mvvm**; DI via `Microsoft.Extensions.Hosting`. |
| Distribution | Self-contained unsigned build for dev/friends → **MSIX + Microsoft Store** (Store signs it). |
| Proxy | **Reuse the existing Cloudflare Worker** unchanged; the C# client points at the same URL. |

## Architecture — Core library + thin WinUI shell

Three projects (mirrors today's pure-core / thin-shell split):

- **`ModManager.Core`** — class library, **no UI, no WinUI references**. Every pure core ports here. This is where the test-contract logic lives.
- **`ModManager.App`** — WinUI 3 shell: Views + ViewModels (CommunityToolkit.Mvvm), DI host, file dialogs, window. Thin, like `main.js`.
- **`ModManager.Tests`** — xUnit. The **ported test suite is the acceptance contract**: a core isn't "ported" until its test passes in C#.

Operating laws carry over verbatim: honor the builders (attribution/consent), never embed the API key (proxy or per-user), file ops stay reversible/atomic, core stays UI-free + test-first.

## Port map (Electron → C#)

| Today (JS) | Rewrite (C#) | Notes |
|---|---|---|
| `scanner.js` (gameContext, buildModList, enable/disable, profiles, dataDir, migrate) | `Scanner`, `Registry`, `GameContext` | `System.IO`; keep phase-ordered disable + rollback |
| `fingerprint-core.js` (murmur2 + whitespace strip) | `Fingerprint.cs` | exact port; golden test `JEI = 3089143260` carries over |
| `name-match-core.js` (cleanModName, pickBestMatch) | `NameMatch.cs` | token-Jaccard, threshold 0.5 |
| `curseforge-core.js` + `curseforge.js` | `CurseForgeClient` | `HttpClient`; proxy baseUrl; getMod(s)/search/fingerprints/resolveGameId |
| `steam.js` / `steam-core.js` | `Steam.cs` | registry read (no shell), appmanifest + libraryfolders parse |
| `engine-presets.js`, `popular-games.js` | data classes (or embedded JSON) | port as-is |
| `metadata-core.js` (mergeMetadata, prettify) | `Metadata.cs` | curated-wins merge |
| `intake-core.js`, folder expand, smart-intake | `Intake.cs` | `System.IO.Compression` for zip; fingerprint-at-drop |
| `fs-atomic.js` | `AtomicJson.cs` | temp-write + rename |
| `url-core.js` (isHttpUrl) | `SafeUrl.cs` | http/https guard for open-external |
| themes (`themes.js` + JSON) | `Themes` + JSON resources | 7 built-ins + user themes |

## UI/UX direction — sleek but informative (626-grounded)

**Identity split:** the 626 brand (deep navy, cyan `#17d4fa` + magenta `#f22f89` paired, Space Grotesk / Inter / JetBrains Mono, hairline borders, neon-glow-not-shadow) owns **app chrome + brand surfaces** (title bar, About, icon, Store listing, empty states). The **in-app palette stays themeable** — the 7 themes port; add a default **"626 Lab"** theme (navy + cyan/magenta) so it's on-brand out of the box, amber "Obsidian" as an alternate.

**Window:** WinUI **Mica** backdrop, custom title bar.

**Layout:**

```text
title bar (Mica, 56px):   ◆ 626 Mod Launcher   [Game ▾]   [+ Game]   [▶ Launch]
command bar:              ↻  All On  All Off │ All [MP] SP │ Profiles ▾ │ + Add  Fetch  ⌕
mod list (canvas):        ListView / ItemsRepeater of mod rows
status bar (36px):        <active game path>            N of M enabled
```

**Mod row (hierarchy over density):**

- Mod **name** — prominent (Inter 600).
- **Description** — quiet, secondary.
- **Author credit line** — first-class but calm: `by <author> · CurseForge · Source · N downloads` (honor the builders, visibly). Links open via the safe-URL guard.
- **Capsule chips** — location / variant / MP-SP (JetBrains Mono, uppercase, tracked).
- **ToggleSwitch** on the right; the active theme accent drives toggle + chips.
- Hairline dividers; neon-glow on hover/selected (no drop shadows).

**Icons:** Segoe Fluent Icons for native chrome + the 626 mark for identity. No emoji.

## Phasing

1. **Core + tests** — port `ModManager.Core` and the 137 tests to xUnit. Green = the engine is proven headless, no UI. (Highest-leverage; the contract.)
2. **WinUI shell** — Views/ViewModels over the proven Core: game switcher, mod list + toggle, MP/SP, profiles, intake + smart-intake, fetch-metadata, themes, launch, popular-games picker.
3. **Packaging** — self-contained build, then MSIX + Store (signing, capabilities, listing framed as a load-order utility for the user's own files).

## v1 scope

Parity with today's core: games + registry, reversible toggle, MP/SP loadouts, profiles, intake + smart-intake (fingerprint), fetch-metadata (name search), themes, Steam launch, popular-games catalog, honor-the-builders display.

**Deferred (roadmap):** Nexus SSO + md5-at-intake, cover-art themes (game-specific palettes derived from box art), engine auto-detect for off-catalog games, mod images (CSP/privacy was the Electron blocker; revisit natively).

## Build method

Build with **vibe-cartographer** ("cart"), feeding this spec as the blueprint. The 137-test contract drives a test-first port: port a test, port the core until it's green, repeat; then the WinUI shell.
