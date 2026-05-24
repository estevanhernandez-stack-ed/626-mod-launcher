# 626 Mod Launcher (.NET 10 / WinUI 3)

Native Windows rewrite of the 626 Labs Mod Launcher — a load-order utility that enables,
disables, and organizes mods for the user's own installed games, reversibly and atomically.

This repo is **Phase 1**: the engine. `ModManager.Core` is a UI-free class library ported
test-first from the original Electron app, which serves as the **executable spec**. The
WinUI 3 shell (Phase 2) and MSIX/Store packaging (Phase 3) build on this proven core.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | `ModManager.Core` + xUnit contract | **done — 157 tests green** |
| 2 | WinUI 3 shell (Views/ViewModels, DI host) | not started |
| 3 | MSIX + Microsoft Store packaging | not started |

The ported test suite **is** the acceptance contract: a core isn't "ported" until its
xUnit test passes. The contract mirrors the Electron app's 134-test `node:test` suite
(the 3 Cloudflare Worker tests stay in JS — that logic doesn't move to C#).

## Layout

| Path | What it is |
|---|---|
| `src/ModManager.Core/` | The engine — no UI references. Scanner, Fingerprint, CurseForge client, themes, intake, metadata. |
| `tests/ModManager.Tests/` | xUnit acceptance contract. |
| `docs/spec.md` | The approved rewrite scope (from the Electron repo). |
| `docs/checklist.md` | The Phase 1 build plan. |

## Build & test

```pwsh
dotnet build
dotnet test
```

Requires the **.NET 10 SDK**.

## What's inside Core

- **Scanner** — game-context resolution, mod scanning, reversible enable/disable (phase-ordered
  with rollback), MP/SP loadouts, profiles, intake (folder-recursive + zip), data-dir migration.
- **Fingerprint** — CurseForge MurmurHash2 file identification (golden-pinned).
- **CurseForgeClient** — metadata over an injected `HttpClient`; works with a per-user key or a
  thin proxy. The API key is **never embedded** — it's passed in at runtime (operating law #2).
- **Metadata / Themes / EnginePresets / NameMatch / SteamParse** — the supporting pure cores.

## Operating laws (carried from the Electron app)

1. **Honor the builders** — surface attribution, source, donation links, downloads.
2. **Never embed the API key** — proxy or per-user, supplied at runtime.
3. **File ops stay reversible and atomic** — temp-write + rename; disable rolls back on failure.
4. **Core stays UI-free and test-first** — guarded by `CorePurityTests`.

## Spec repo

The original Electron app (working prototype + cross-referenced spec) lives separately. This
rewrite mirrors its cores and design docs; see `docs/spec.md`.
