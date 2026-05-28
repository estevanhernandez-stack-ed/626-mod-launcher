# Project agents

Repo-local agents tuned to 626 Mod Launcher conventions. Invoke via the Agent tool by `subagent_type` (the file's `name:` field), or trigger them with the natural-language phrases listed in each agent's `description:`.

| Agent | When to use |
|---|---|
| `core-purity-reviewer` | Before merging any PR that touches `src/ModManager.Core/` — verifies no WinUI / WinRT / Electron leaks. Runs `CorePurityTests` filter + greps for forbidden namespaces. |
| `catalog-entry-reviewer` | When new entries land in `KnownDirectInjectMod`, `KnownFramework`, or `ToolCatalog` — checks NOTICE coverage, fingerprint quality, camelCase JSON shape, and test coverage. |
| `reversibility-auditor` | Before merging any PR that touches a file-op site (Scanner, Intake, SaveModInstaller, FrameworkInstaller, IniEditService, etc.) — sniffs for `File.Delete` in toggle/replace paths, non-atomic writes, missing snapshots, extract-before-validate. |
| `release-notes-drafter` | After CI lands a DRAFT release, before clicking Publish — produces paste-ready release-body markdown in the project voice from the commit/PR/spec range since the previous published tag. |

See also:

- `.claude/rules/` — modular bits of guidance the agents reference (camelCase JSON on disk, validate-then-extract).
- `.claude/hooks/` — automation that fires on session events (CorePurityTests at end-of-turn, camelCase-JSON grep on Edit/Write).
- `.claude/settings.json` — wires the hooks. Committed (shared with all contributors).
- `CLAUDE.md` (repo root) — the keystone these agents operate under.
