# Agent-Access MCP — Phase 1 (read-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.
>
> **GATE:** This plan is for review/approval BEFORE any code is written. It firms the design sketch (`docs/superpowers/specs/2026-05-26-agent-access-design-sketch.md`) into a buildable Phase-1. Decisions that need Este's sign-off are marked **[CONFIRM]**.

**Goal:** A local MCP server (`ModManager.Mcp`) that lets an outside agent READ the launcher's state — games, mods, contexts, themes, profiles, save mods, tools, an intake dry-run — through a stable JSON tool surface, running headless against `ModManager.Core` (app need not be open). Read-only first; write tools + the live-app channel are later phases.

**Architecture:** New console project `src/ModManager.Mcp/` depending only on `ModManager.Core`. It speaks MCP over stdio. Every tool is a thin marshaler over an existing Core static — no second source of truth. The one enabling refactor: extract the games.json / app-settings file load out of `LauncherService` (App) into a Core store so the MCP and the app read the same bytes.

**Tech Stack:** .NET 10 console, `ModManager.Core`, the official C# MCP SDK (`ModelContextProtocol` nuget) **[CONFIRM]** vs hand-rolled JSON-RPC over stdio, xUnit.

---

## Decisions (resolving the sketch's 7 open questions)

| # | Question | Recommended Phase-1 decision |
|---|---|---|
| 1 | Auth model | Local-only. Per-session token written to `%APPDATA%\ModManagerBuilder\mcp-token` (0600-ish); agent reads it. Stdio transport needs no network. **[CONFIRM]** |
| 2 | Consent UI | Deferred to the live-app-channel phase (it needs the running app). Phase 1 is headless: a documented `--auto-approve` flag the user opts into, plus the audit log. |
| 3 | Discovery | **Stdio only** for Phase 1 (Claude Code/Desktop spawn the server directly). Loopback HTTP is a later phase. |
| 4 | Binary location | Ship `626-mcp.exe` next to `ModManager.App.exe` in the portable. **[CONFIRM]** (alt: separate download). |
| 5 | Versioning | Tool catalog carries a `serverVersion` + `catalogVersion`; `get_server_info` tool returns them so agents detect upgrades. Semver on the tool surface. |
| 6 | Test strategy | End-to-end against an in-memory `%APPDATA%` (temp dir + seeded games.json), mirroring the Core test pattern. Each tool: seed → call → assert JSON shape. |
| 7 | Error shape | `{ "error": { "code": "...", "message": "...", "hint": "..." } }`. Phase-1 codes: `unknown_game`, `not_found`, `bad_request`, `launcher_data_missing`. |

**Scope (Phase 1):** read-only catalog (below) + token auth + audit log + `--auto-approve`. **Out:** all write tools, the live-app named-pipe channel, the in-app consent toast, loopback HTTP, multi-machine, streaming.

---

## File structure

- Create: `src/ModManager.Mcp/ModManager.Mcp.csproj` (console, refs `ModManager.Core`)
- Create: `src/ModManager.Mcp/Program.cs` (host + transport wiring)
- Create: `src/ModManager.Mcp/Tools/ReadTools.cs` (the 10 read tools)
- Create: `src/ModManager.Mcp/AuditLog.cs` + `src/ModManager.Mcp/AuthToken.cs`
- Create: `src/ModManager.Core/Persistence/RegistryStore.cs` + `AppSettingsStore.cs` (EXTRACTED file load/save — see Task 1)
- Modify: `src/ModManager.App/Services/LauncherService.cs` (delegate load/save to the new Core store — no behavior change)
- Test: `tests/ModManager.Tests/Persistence/RegistryStoreTests.cs`, `tests/ModManager.Mcp.Tests/` (new test project) **[CONFIRM]** (or fold MCP tests into ModManager.Tests)

---

## The read tool catalog (each → a real Core API)

| Tool | Backing Core API (grounded) |
|---|---|
| `get_server_info` | new — version + catalog version |
| `list_games` | `RegistryStore.Load(dataRoot).Games` (Task 1) |
| `get_active_game` | `Registry.GetActiveGame(RegistryStore.Load(...))` |
| `get_mod_context <gameId>` | `Scanner.GameContext(game)` |
| `list_mods <gameId>` | `Scanner.BuildModListAsync(Scanner.GameContext(game))` |
| `list_themes` | `Themes` (Core) |
| `list_profiles <gameId>` | `GameProfiles` (Core) |
| `list_save_mods <gameId>` | `SaveModInstaller` (Core, read side) |
| `list_tools <gameId>` | `ToolRegistry` (Core) |
| `dry_run_intake <gameId> <paths...>` | `Scanner.PlanIntake(paths, ctx)` — returns the plan, writes nothing |
| `get_app_settings` | `AppSettingsStore.Load(dataRoot)` (Task 1) — backdrop/avatar/Nexus-connected bool; **never the key** |

`nexus_status` (connected/premium) is DEFERRED — it reads through the App-side `NexusService` (DPAPI); exposing it headless needs a Core seam. Note it for the write/live phase.

---

## Task 1: Extract registry + app-settings file I/O into Core (enabling refactor)

**Why:** `list_games` / `get_active_game` / `get_app_settings` must read `%APPDATA%\ModManagerBuilder\games.json` + `app-settings.json` headlessly. Today that load lives in `LauncherService.LoadRegistry` (App). Extract it to Core so the MCP and the app share one reader — no second source of truth, no drift.

**Files:** Create `src/ModManager.Core/Persistence/RegistryStore.cs` + `AppSettingsStore.cs`; modify `LauncherService` to delegate; test `tests/ModManager.Tests/Persistence/RegistryStoreTests.cs`.

- [ ] **Step 1:** failing test `RegistryStore_roundtrips_games_json_as_camelCase` — write a `GameRegistry`, `RegistryStore.Save(dir, reg)`, assert the file contains `"activeGameId"` (camelCase) and `RegistryStore.Load(dir)` round-trips. Run → fails (type doesn't exist).
- [ ] **Step 2:** implement `RegistryStore.Load(dataRoot)` / `Save` using `AtomicJson` + camelCase `JsonSerializerOptions` (the on-disk-camelCase rule). Move the exact serialize logic out of `LauncherService`. Run → green.
- [ ] **Step 3:** failing test `RegistryStore_missing_file_returns_empty_registry`. Implement the empty/missing path. Green.
- [ ] **Step 4:** same pair for `AppSettingsStore` (load/save app-settings.json, camelCase round-trip).
- [ ] **Step 5:** modify `LauncherService.LoadRegistry`/`SaveRegistry` to delegate to `RegistryStore` — assert no behavior change by running the full App-touching test surface that exists. Build App x64.
- [ ] **Step 6:** commit `refactor(core): extract RegistryStore + AppSettingsStore (shared by App + MCP)`.

**Risk:** low — pure extraction, camelCase round-trip locked by test, `CorePurityTests` guards the new Core files. Reversible.

---

## Task 2: Scaffold `ModManager.Mcp` + stdio transport

**Files:** `src/ModManager.Mcp/ModManager.Mcp.csproj`, `Program.cs`, add to the solution.

- [ ] **Step 1:** create the console project referencing `ModManager.Core`; add to the `.sln`. **[CONFIRM]** transport: official `ModelContextProtocol` SDK (recommended — handles JSON-RPC + the MCP handshake) vs hand-rolled stdio JSON-RPC.
- [ ] **Step 2:** `Program.cs` wires the MCP host over stdio, reads `--data-root` (default `%APPDATA%\ModManagerBuilder`), `--auto-approve`, `--audit-reads`.
- [ ] **Step 3:** `get_server_info` tool returns `{ serverVersion, catalogVersion }`. Smoke: launch the server, send the MCP `initialize` + `tools/list`, confirm `get_server_info` is listed and callable.
- [ ] **Step 4:** commit `feat(mcp): scaffold ModManager.Mcp stdio server + get_server_info`.

**Risk:** new project, no existing-code change. The transport choice is the one real fork — **[CONFIRM]**.

---

## Task 3: The read tool catalog

**Files:** `src/ModManager.Mcp/Tools/ReadTools.cs`.

- [ ] For each tool in the catalog table: write it as a thin marshaler over its backing Core API, returning the documented JSON shape; on a bad `gameId` return the `unknown_game` error shape. One commit per small group (games/context, mods, themes/profiles/tools, save-mods, dry-run-intake).
- [ ] Each tool gets an end-to-end test (Task 5 pattern): seed a temp data-root + game folder, call the tool, assert the JSON.
- [ ] `dry_run_intake` MUST write nothing — assert the game folder is byte-identical after the call.

**Risk:** low — read-only, no file writes (except `dry_run_intake` which is proven write-free by `PlanIntake` + the assertion). Each tool is a few lines.

---

## Task 4: Auth token + audit log

**Files:** `src/ModManager.Mcp/AuthToken.cs`, `AuditLog.cs`.

- [ ] **Step 1:** `AuthToken` writes a per-session random token to `%APPDATA%\ModManagerBuilder\mcp-token` on startup; the server rejects calls without it unless `--auto-approve`. **[CONFIRM]** the exact gate (Phase 1 may accept `--auto-approve` as the only mode since stdio is already local + spawned by the agent).
- [ ] **Step 2:** `AuditLog` appends `{ ts, tool, args, ok }` to `%APPDATA%\ModManagerBuilder\agent-log.jsonl` (camelCase). Reads are logged only under `--audit-reads`; the catalog marks each tool read/write so the later write phase logs by default.
- [ ] **Step 3:** tests for token gen/verify + audit append. Commit.

**Risk:** low. No destructive surface (read-only phase).

---

## Task 5: Test project + end-to-end harness

- [ ] **[CONFIRM]** new `tests/ModManager.Mcp.Tests/` project vs folding into `ModManager.Tests`. Recommend a dedicated project (keeps the Core suite fast + pure).
- [ ] Harness: seed a temp `%APPDATA%`-style dir with a `games.json` (one or two games) + game folders with a couple mods; spin the tool layer in-process; assert each tool's JSON. Mirror the existing Core fixture pattern (`TestSupport.TempDir`).

---

## Self-review

- Every read tool maps to a grounded, existing Core static (verified 2026-05-29). The only new Core code is the `RegistryStore`/`AppSettingsStore` extraction (Task 1) — additive + camelCase-tested.
- `CorePurityTests` is unaffected (the MCP project is a sibling, not in Core; Task 1's new Core files are pure JSON I/O).
- Safety laws from the sketch are inherited by construction (read-only = no reversibility surface; no keys exposed; local-only stdio; audit log present; `dry_run_intake` proven write-free).
- **Decisions flagged [CONFIRM]:** MCP SDK vs hand-rolled; binary location; token gate vs `--auto-approve`-only; dedicated test project. None block the shape; all are scaffold-time calls.

## Concrete first PR (per the sketch's "smallest first PR")

Tasks 1 + 2 + the first three read tools (`get_server_info`, `list_games`, `get_active_game`, `get_mod_context`, `list_mods`) + the test harness. That proves the headless-Core-over-MCP path end-to-end; the rest of the catalog + auth/audit follow in PR 2, write tools + the live-app channel in a later phase.
