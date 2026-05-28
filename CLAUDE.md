# 626 Mod Launcher

> **Persona:** Inherits The Architect from `~/.claude/CLAUDE.md`. No need to re-establish identity — this file adds project context only.

A native Windows mod manager for PC games — .NET 10 / WinUI 3, pure-core + thin-shell, reversible by default. Shipped via tag-driven Velopack with auto-update from GitHub Releases.

## Tech Stack & Voice

- **Language / framework:** .NET 10, C# (`<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — global via `Directory.Build.props`).
- **UI:** WinUI 3 (`Microsoft.WindowsAppSDK`), x64 / arm64 Windows-only.
- **Tests:** xUnit, headless. `tests/ModManager.Tests/` is the contract surface — 635+ tests at v0.3.0.
- **Packaging:** Velopack (`vpk`) → installer + delta nupkg + `releases.win.json` under `dist/release/`.
- **Release:** tag-triggered GitHub Actions (`v*.*.*` or `v*.*.*.*` → DRAFT release with assets attached).
- **Voice (README + user-facing UI copy):** Builder-to-builder, second person, sentence case. *"Honest about what it does, polite about your files, decent to the modders."* No corporate speak — no "empower / leverage / seamlessly / unlock / robust solution / delightful experience." Em-dashes welcome. Periods at the end of microcopy. No emoji in code, commits, or UI strings.

## What's where

| Path | What it is |
|---|---|
| `src/ModManager.Core/` | Pure data + logic + headless services. No WinRT, no WinUI, no Electron. Every file-touching invariant lives here behind a test. |
| `src/ModManager.Core/Catalog/` | Unified-catalog Phase 1 — `KnownDirectInjectMod` + `DirectInjectConfigOverrides` + resolver. Future phases fold Tools and Frameworks in. |
| `src/ModManager.Core/Frameworks/` | Framework intake — `KnownFramework`, `FrameworkInstaller` (validate-then-extract), `FrameworkRegistry`. |
| `src/ModManager.Core/Tools/` | Third-party tool catalog (WSE Save Editor, etc.) — detection + intake + registry. |
| `src/ModManager.Core/SaveEditor/FromSoft/` | BND4-walking save editor (Elden Ring + adjacents). |
| `src/ModManager.Core/IniEdit/` | INI editor service (snapshot-first, atomic write, Restore Previous). |
| `src/ModManager.Core/Scanner.cs` | Mod discovery — engine-aware, ~61KB, the heart of the read path. |
| `src/ModManager.App/` | WinUI 3 shell — views, view-models, DI host, Windows-only services. Thin layer over Core. |
| `src/ModManager.App/Services/` | App-side adapters (Steam detection, theme, palette, launcher, update-check, etc.). |
| `src/ModManager.App/ViewModels/` | `MainViewModel` is the row-builder + intake orchestrator. |
| `src/ModManager.App/Frameworks/` | Framework install + unrecognized-nudge dialogs. |
| `tests/ModManager.Tests/` | xUnit. Includes `CorePurityTests` — the guard against WinUI / WinRT leaks into Core. |
| `docs/` | Specs, plans, designs, reviews — dated `YYYY-MM-DD-*.md` files. |
| `docs/superpowers/{specs,plans,research,handoffs}/` | Where the brainstorm → spec → plan → handoff workflow lands. |
| `docs/reviews/` | Critical-eye code reviews per release. |
| `docs/smoke-tests/pending.md` | Manual smoke checklist for features that aren't covered (or aren't fully coverable) by unit tests. |
| `docs/RELEASE.md` | Maintainer-side release flow (the public-facing user docs are `README.md` + `GETTING-STARTED.md`). |
| `scripts/build-velopack-release.ps1` | Self-contained publish → `vpk pack` → installer + nupkgs + manifest. |
| `.github/workflows/release.yml` | Tag-triggered CI. Restore → test (Core) → fetch prior release → build Velopack → upload artifacts → push DRAFT release. |
| `NOTICE` | Third-party tool + framework attribution. Catalog-metadata-only; never bundled. |
| `THIRD_PARTY_NOTICES.md` | Dependency licenses (auto-aggregated). |
| `.mcp.json` | 626Labs Dashboard MCP wiring (`mcp__626labs-cloud__*`). |

## How it works at runtime

**Core / App split.** `ModManager.Core` is pure functions + data + xUnit tests — runs headless on any platform. `ModManager.App` is the WinUI 3 shell that wraps it. Every behavior change starts with a failing test in Core; the App is allowed to be the messy edges (dialogs, file pickers, Steam library probing, Mica/Acrylic, AppWindow.SetIcon). `CorePurityTests` enforces the split — if you accidentally `using Microsoft.UI.*` in a Core file, the test suite fails.

**The intake path.** User drops a `.zip` / `.7z` / `.rar` / loose folder onto the window. `ArchiveReader` cracks it. `Scanner` + `EngineDetect` + `KnownDirectInjectMod.Catalog` + `KnownFramework.Catalog` + `ToolCatalog` classify what it is. `Fingerprint` does the CurseForge match (proxy fingerprint → Nexus md5 → name search). The row builder in `MainViewModel.ReloadModsAsync` produces a `ModRowViewModel` per detected unit. Toggling a row delegates to the right per-engine enable/disable mechanism — the one the loader actually expects, not a one-size hack.

**The file-op laws.** Every state change writes through atomic temp-file + rename (`AtomicJson`, etc.). Disabling *moves* files to a holding folder; nothing is deleted by a toggle. Replacing snapshots the original first. INI/Lua edits use Restore Previous. Save-tree writes are snapshot-guarded.

**Updates.** `UpdateChecker` (in `src/ModManager.App/Services/UpdateChecker.cs`) pings the GitHub Releases `releases.win.json`, debounced once per 24h via a stamp at `%LOCALAPPDATA%\ModManagerBuilder\last-update-check.txt`. Dev / portable builds skip cleanly (`UpdateManager.IsInstalled` is false).

**MCP wiring.** `.mcp.json` connects this repo to the 626Labs Dashboard MCP (`mcp__626labs-cloud__*`). Use it for project binding + decision logging — see *Decisions log* below.

## Common tasks

| You want to… | Path / command |
|---|---|
| Run the Core test suite | `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` |
| Build + run the app | `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` then run the produced `bin/x64/Debug/.../ModManager.App.exe` |
| Add a new test | Drop a `*Tests.cs` in `tests/ModManager.Tests/` (or a sub-folder matching the Core sub-namespace). xUnit auto-discovers. |
| Add a known direct-inject mod | Append to `KnownDirectInjectMod.Catalog` in `src/ModManager.Core/Catalog/KnownDirectInjectMod.cs`. Cover with a test in `tests/ModManager.Tests/Catalog/`. |
| Add a known framework | `src/ModManager.Core/Frameworks/KnownFramework.cs` + test + update `NOTICE`. |
| Add a third-party tool | `src/ModManager.Core/Tools/ToolCatalog.cs` + test + `NOTICE`. |
| Pack a Velopack release locally | `pwsh scripts/build-velopack-release.ps1 -Version 0.3.0` (requires `dotnet tool install -g vpk` once). |
| Ship a release | `git tag v0.3.0 && git push origin v0.3.0` — CI builds the DRAFT release. See `docs/RELEASE.md` for the rest. |
| Write a spec / plan | `docs/superpowers/specs/YYYY-MM-DD-<slug>-design.md` and `docs/superpowers/plans/YYYY-MM-DD-<slug>.md`. |
| Add a smoke entry | Append to `docs/smoke-tests/pending.md` with steps + expected outcome. |
| Review a release | `docs/reviews/YYYY-MM-DD-vX.Y.Z-review.md` — use existing reviews as the tone + severity template. |

## Conventions

- **Commits:** Conventional commits — `feat(area)`, `fix(area)`, `docs(area)`, `refactor(area)`, `chore(area)`. Area is usually the Core sub-namespace (`catalog`, `frameworks`, `direct-inject`, `save-editor`, `intake`, `frameworks`) or a UI surface (`settings`, `dialog`, `viewmodel`).
- **Branches:** `feat/<slug>`, `fix/<slug>`, `docs/<slug>` off `master`. PRs into `master`; merges happen via PR, not direct push.
- **Style:** Nullable-on everywhere; warnings are errors. Prefer records + immutable data in Core. Side-effecting code at the App boundary.
- **Tests:** Every Core behavior change starts with a failing xUnit test. `CorePurityTests` runs in the suite — it fails if Core takes a WinUI / WinRT dep.
- **On-disk JSON shape:** camelCase, always. The launcher historically shared JSON with an Electron predecessor; the convention stayed.
- **File rules:** `bin/` and `obj/` are gitignored. `dist/` is gitignored — release artifacts only. `*.secret`, `.env`, `appsettings.*.local.json` never get committed (the CurseForge key lives only in the Worker proxy).

## Decisions log

Significant decisions log to the 626Labs Dashboard MCP via `mcp__626labs-cloud__manage_decisions` action `log`. The `.mcp.json` at the repo root wires this up — bind the project on session start via `mcp__626labs-cloud__manage_projects` action `findByRepo` and tag every decision with the bound `projectId`.

The bar: *would future-you (or someone asking "why this approach?") want to know this in 3–6 months?*

Especially log:

- **Architectural choices** — picked X over Y because Z (e.g., unified-catalog kind-tagged shape over separate registries).
- **Operating-law tradeoffs** — anywhere reversibility or pure-core was relaxed and why (almost never).
- **Catalog growth decisions** — adding a known direct-inject mod / framework / tool, especially when the detection signature is fragile.
- **Engine-specific gotchas** — discovering that loader X silently expects Y (the BND4 walk fix in F1 is a textbook example).
- **Release / packaging shifts** — Velopack version bumps, signing strategy, channel changes (Store vs GitHub).
- **Honoring-the-builders edits** — NOTICE additions, attribution corrections, takedown requests honored.

Skip the routine: ran tests, fixed a typo, renamed a variable, bumped a patch dep with no behavior change.

If session-start binding finds no match: tag the decision description with the repo name and set `projectId: null` so the dashboard can surface it later.

## Knowledge & taste

The repo is the system of record — if it isn't written here, the agent can't see it.

- **Conventions / taste:** this file's *Conventions* + *What NOT to do* sections, plus per-surface specs in `docs/superpowers/specs/`.
- **Why-decisions:** logged to the 626Labs Dashboard MCP (see above). Spec/plan files in `docs/superpowers/` carry the design rationale.
- **Operating laws:** README's *Operating laws* block is canonical — four rules that outrank convenience. This file's *What NOT to do* enforces them.
- **Things the agent keeps getting wrong:** capture the correction as a short note in this file (or a `docs/superpowers/specs/` entry) the moment it surfaces, instead of re-explaining it every session.

## What NOT to do

- **Don't run bare `dotnet test` or `dotnet build` at the repo root.** The WinUI App project hangs the build. Always target the explicit project: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` or `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. The README and `docs/RELEASE.md` both call this out; CI sidesteps it the same way.
- **Don't treat "Electron" references as live.** The launcher is .NET 10 / WinUI 3. References to Electron in older docs or comments are historical (predecessor app). Verify before acting on any "Electron" claim — the only place the legacy still bites is the on-disk JSON shape (camelCase), which is intentional and stays.
- **Don't break the camelCase JSON-on-disk convention.** Every file the launcher writes (`config-overrides.json`, framework install manifests, profile files, theme files, registry files) uses camelCase keys. Snake_case or PascalCase on-disk JSON will silently break round-trips with existing user data. Set `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and cover round-trip with a test.
- **Don't leak WinUI / WinRT / UI types into `src/ModManager.Core/`.** `CorePurityTests` catches it in CI but please don't waste cycles. If you need a platform primitive in Core, abstract it behind an interface; implement the adapter in `src/ModManager.App/Services/`.
- **Don't break reversibility.** Every file op must be undoable — temp-write + rename, snapshot-first on replace, move-to-holding on disable. No `File.Delete` in toggle / replace paths. No partial-extract that leaves the game folder mid-state (see `FrameworkInstaller.Install` for the validate-then-extract pattern).
- **Don't bundle third-party tool or framework binaries.** Catalog metadata + fingerprints + URLs only — never the binaries or assets themselves. `NOTICE` carries the "never bundled" language for a reason; honor it for every new catalog entry. If you find yourself wanting to ship a `.dll`, stop and re-read the NOTICE.
- **Don't embed an API key.** CurseForge access goes through a server-side proxy. Nexus uses the user's personal API key, supplied at runtime, kept on-machine. No secret ever lands in `appsettings.json` or source.
- **Don't force-push to `master`** (and don't commit directly — merge via PR). Tags trigger production releases; never re-tag a published version.
- **Don't write a snapshot list** (current sprint / recent decisions / "what's in flight") into this file. Snapshots rot. Describe how to *find* state — point at `git log`, `docs/superpowers/`, the dashboard — never enumerate.

## References

- **Public-facing user docs:** `README.md`, `GETTING-STARTED.md`
- **Maintainer-side release flow:** `docs/RELEASE.md`
- **Specs / plans / handoffs:** `docs/superpowers/{specs,plans,handoffs,research}/`
- **Code reviews:** `docs/reviews/`
- **Manual smoke checklist:** `docs/smoke-tests/pending.md`
- **Operating laws (canonical):** `README.md` → *Operating laws*
- **Third-party attribution:** `NOTICE` (catalog entries) + `THIRD_PARTY_NOTICES.md` (dependency licenses)
- **Specialized agents:** `.claude/agents/` — `core-purity-reviewer`, `catalog-entry-reviewer`, `reversibility-auditor`, `release-notes-drafter` (see `.claude/agents/README.md`)
- **Modular rules:** `.claude/rules/` — camelCase JSON on disk, validate-then-extract pattern
- **Session hooks:** `.claude/hooks/` — CorePurityTests at end-of-turn, camelCase JSON warn on Edit/Write (wired in `.claude/settings.json`)
- **CI:** `.github/workflows/release.yml`
- **MCP config:** `.mcp.json` (626Labs Dashboard)
