# Agent-access for the launcher — Design Sketch

**Status:** Sketch only. Este will spin up a fresh instance to scaffold the real plan; this doc captures the framing + the surface + the safety laws so that session has a running start.

**Goal:** Let an outside agent (Claude Code, Claude Desktop, an MCP-aware editor, a custom Cowork orchestrator) drive the launcher safely — register games, scan, intake mods, toggle, apply loadouts, install save mods, read state — through a stable programmatic surface. The user keeps the steering wheel; the agent gets a passenger seat with clearly-labeled controls.

---

## The frame: not "another MCP" — pick the smallest right surface

We don't have to invent something exotic; we just have to pick the right shape for who's driving.

| Shape | What it is | Best for | Tradeoff |
| --- | --- | --- | --- |
| **MCP server (stdio)** | Standalone process exposing tools over JSON-RPC stdio. Claude Code / Desktop already speak MCP. | Local desktop agents that already speak MCP and want to drive the launcher without it running. | New process to maintain; some launcher state has to be shared with the running app (or run a single MCP that uses the launcher cores directly without booting WinUI). |
| **MCP server (HTTP/SSE)** | Same, but over loopback HTTP. Discoverable, multi-agent. | Multiple agents at once, or agents on a different machine on LAN. | Auth surface is bigger (more attack surface, even on loopback). |
| **Local HTTP API on the running app** | The launcher itself binds `127.0.0.1:<port>` while running and exposes JSON endpoints. | Agents that want to drive the LIVE launcher (see your live mod list, apply a loadout you can see happen). | The launcher has to be running; agents need a stable port discovery. |
| **CLI surface** | `626-launcher.exe --json scan --game windrose` etc. | Scripts, CI, batch jobs. Trivial to call from anywhere. | Each call boots and reads state from disk; no live notifications. |
| **File-watched intent queue** | Agents drop JSON intent files into a known dir; the launcher picks them up. | Hands-off async automation. | Latency, ergonomics. |

**Recommended primary surface: an MCP server that imports `ModManager.Core` directly + a tiny "live-running" bridge.**

- Core operations (scan, intake-plan, apply-loadout, register-game, list-themes, read-save-mods, read-mod-metadata, get-game-context) run against the same pure cores the app uses. No second source of truth. They work whether the app is running or not.
- For ops that NEED the live app (e.g. "apply this theme NOW so I can see it", "open the Settings dialog"), the MCP can talk to the running app via a tiny named-pipe / loopback channel — same machine only. If the app isn't running, those tools return `{ "error": "launcher not running" }`.

This is the smallest shape that lets a remote agent (Claude Code in a sibling repo, Claude Desktop, Cowork) USEFULLY drive the launcher without inventing a brand-new protocol. MCP is already the lingua franca for desktop-agent ↔ tool, and our pure-core architecture means the MCP server is mostly a thin marshaler.

---

## The tool surface — what agents can actually do

These are the verbs the cores already expose, just renamed for a JSON tool catalog. Every one of them ALREADY has a Core method or Scanner static; the MCP wraps + serializes.

**Read (no side effects):**

- `list_games` — registered games + their engine / paths / metadata
- `get_active_game` — which game is currently active in the shell
- `list_mods <gameId>` — every mod for a game with enabled/class/loader/variant/metadata
- `get_mod_context <gameId>` — the full GameContext (locations, exts, profiles dir, save dir)
- `list_themes` — installed themes + which is active
- `list_save_mods <gameId>` — installed world mods + their source archives
- `list_profiles <gameId>` — saved loadouts
- `get_app_settings` — backdrop, avatar state, Nexus connection state
- `dry_run_intake <gameId> <paths...>` — what would happen if these zips were dropped (uses `Scanner.PlanIntake`, returns the plan WITHOUT writing)

**Write (side effects — gated):**

- `register_game <profile-json>` — same shape as the agentic-profile flow today
- `apply_loadout <gameId> <profileId>` — switch loadout
- `set_mod_enabled <gameId> <modName> <on>` — toggle a single mod
- `set_all <gameId> <on>` — bulk enable/disable
- `intake <gameId> <paths...> <replace?>` — actually drop files; returns the same shape as the UI's drop status
- `apply_theme <themeId>` — switch theme
- `import_theme <theme-json>` — install a user theme (validates first; doesn't auto-apply)
- `install_save_mod <gameId> <zipPath>` — runs `SaveModFlow.TryHandleDrops`
- `uninstall_mod <gameId> <modName>` — gated by an explicit `confirm: true` argument

**Live ops (only when the app is running — talk to the live instance):**

- `redetect <gameId>` — force a rescan
- `set_active_game <gameId>` — switch the shell's active game
- `open_settings` — pop the Settings dialog

The catalog can grow; the read/write split is the load-bearing invariant.

---

## Safety laws — the agent passenger seat doesn't get to override the human

These are the laws the keystone already encodes; the MCP surface honors them mechanically:

1. **Reversibility.** Anything the agent does that touches files goes through the same reversible paths the UI does. Toggle = move-to-disabled (not delete). Intake = no-clobber. Uninstall = explicit `confirm: true` or it refuses.

2. **No silent destructive ops.** `uninstall_mod`, `remove_game`, `reset_save_mod` all require `confirm: true`. Bulk versions of these don't exist by default; an agent that needs to remove 20 mods loops `uninstall_mod` with confirm each time. The friction is deliberate.

3. **Honor owned folders.** `set_mod_enabled` on a Vortex-managed mod fires the same owned-folder guard the UI does — refused unless the agent passes `acknowledge_managed: true`, mirroring the "don't warn again" checkbox in the UI.

4. **No API keys ever leave the machine.** The MCP exposes `nexus_status` (connected? premium?) but NEVER `nexus_key`. The user pasted their personal key into the launcher; the launcher's job is to hold it, not relay it.

5. **No CurseForge key in the MCP surface either.** The same per-user proxy + bundled-but-not-exfiltrated discipline applies. Read endpoints fetch through the existing services; the agent never gets a raw key.

6. **Local-only by default.** The MCP binds to stdio (no network) OR loopback `127.0.0.1` with a per-session token written to a file in the user's home dir. Agents read the token; nothing on the LAN can hit the surface without the token.

7. **Consent surface.** First time an agent connects, the launcher shows a one-time consent toast naming the agent (or its identifier) and what tools it's calling. Approval lasts the session; denial blocks until manually re-approved. (For headless use without a running launcher, a `--auto-approve` flag is documented and the user opts in by setting it.)

8. **Audit trail.** Every WRITE op writes to `%APPDATA%\ModManagerBuilder\agent-log.jsonl` with timestamp + tool + args + result. The user can see exactly what an agent did. (Read ops don't log by default — too noisy — but `--audit-reads` flag enables it.)

9. **Idempotency.** Tools that can be safely retried are explicitly marked idempotent in their schema. Tools that aren't (intake, save-mod install) document the cost of retry. Agents are dumb sometimes; the surface protects them from themselves.

10. **The launcher's UI is the canonical view.** If an MCP write op succeeds while the launcher is running, the launcher refreshes (the same paths the UI takes after drag-drop or click-to-toggle). The user always sees what the agent did, in real time.

---

## Architectural shape (preview — Este's fresh-instance plan will harden this)

```
                +-------------------------------+
                |  Agent (Claude Code, Desktop, |
                |  Cowork, other MCP client)    |
                +---------------+---------------+
                                | MCP (stdio or loopback)
                                v
            +-------------------+-------------------+
            |   ModManager.Mcp (new project)        |
            |   - tool catalog                      |
            |   - request/response marshaling       |
            |   - audit logger                      |
            |   - consent dialog (when app running) |
            +-------+---------------------------+---+
                    |                           |
                    | direct API                | named pipe / loopback
                    v                           v
            +-------+---------------+   +-------+----------------+
            |  ModManager.Core      |   |  ModManager.App        |
            |  (pure data + logic)  |   |  (live shell, optional)|
            +-----------------------+   +------------------------+
```

The MCP project is in this repo (sibling to `ModManager.App`). It depends on `ModManager.Core` for the cores it can run headlessly, and reaches the running app via a documented local channel (named pipe `\\.\pipe\ModManagerBuilder-mcp` or loopback HTTP — pick at scaffold time) for live-shell ops.

**The MCP can run without the app.** Agent calls `list_mods` → MCP reads the registry from `%APPDATA%`, builds a `GameContext` via the same Scanner pure helpers, returns. The app doesn't need to be open.

**When the app IS running**, the MCP additionally exposes live-only tools (open_settings, redetect, set_active_game) AND every write op pushes a "refresh" message to the app over the same channel so the UI reflects reality.

---

## What stays out of scope (for now)

- **Multi-machine.** Local agents only. No internet exposure, no remote-control. If you want that later it's a separate auth design.
- **Voice / chat-style agent embedding inside the launcher.** Out of scope. The agent stays in its own app and drives ours.
- **Streaming / SSE.** First cut is request/response. Streaming for long-running ops (mass intake, fingerprint identify) is a v2.
- **Direct curseforge / nexus search.** Agent can call `find_mods <gameId>` which returns Nexus / CF URLs (same as the toolbar Find mods); the actual browsing happens out-of-band.
- **Theme generation from a prompt.** The agent has the prompt at hand already (it IS an agent); it can just call `import_theme` with the JSON it generates. No need to re-implement the prompt flow.

---

## Open questions for the fresh-instance plan

1. **Auth model details.** Token in a file? OS-level capability? Both? — Pick at scaffold time, but lean on local-only.
2. **Consent UI in the launcher.** A toast? A persistent panel? A first-time-only dialog with a "trust this agent always" checkbox?
3. **MCP discovery.** Stdio (Claude Desktop / Code call us directly) is the simplest first cut. Loopback HTTP comes later if needed.
4. **Where the MCP binary lives in the distributed portable.** Bundled next to `ModManager.App.exe`? Separate `626-mcp.exe`?
5. **Versioning.** Tool catalog needs a `version` field so agents can detect a launcher upgrade. Semver on the tool surface.
6. **Test strategy.** The MCP marshaling is testable end-to-end against an in-memory registry. Mirror the existing Core test pattern.
7. **Error shape.** Standardize error responses (`{ "error": { "code": "owned_folder", "message": "...", "hint": "set acknowledge_managed: true" } }`) so agents can branch on `code`.

---

## Why this is a fit for the operating laws

- **Honor the builders.** Read-only metadata + attribution is preserved. Agents see `Author`, `ModUrl`, `Donate` for every mod — they can surface those when they recommend changes to the user.
- **Reversibility + safety.** Every write op goes through the same Scanner / DirectInject / SaveModInstaller paths the UI uses. The agent inherits the laws by construction.
- **Honest about gaps.** Tools that aren't safe to call without a confirm refuse cleanly. The catalog is small and growable; we don't ship a tool until its safety model is clear.
- **Honor user agency.** Consent + audit + per-tool refusal codes mean the agent is a clearly-labeled passenger. The user can revoke, audit, and override at any time.

---

## Concrete next step

When you spin up the fresh instance, the entry point is:

> "Build an MCP server for the 626 Mod Launcher per the sketch in `docs/superpowers/specs/2026-05-26-agent-access-design-sketch.md`. Start with the read-only tool catalog (10 tools), the consent dialog, and the audit log. Defer the write tools and the live-app channel to a second PR after read works end-to-end."

That's the smallest first PR that makes the rest of the work possible.
