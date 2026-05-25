# Super Session Handoff — knock out the backlog

- **Date:** 2026-05-25
- **Mission:** Build the entire post-merge backlog for the 626 Mod Launcher (.NET 10 / WinUI 3).
  Everything is specced; this session *executes*. Work A → D, each its own branch + PR.

## Repo + environment

- **Work repo:** `C:\Users\estev\Projects\626-mod-launcher` (the .NET rewrite). NOT the Electron
  `mod-manager-builder` (that's the origin/spec repo).
- Stack: .NET 10, C#, WinUI 3 (Windows App SDK 2.x), xUnit, CommunityToolkit.Mvvm.
- **Run tests:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — the EXPLICIT project
  (a bare `dotnet test`/`dotnet build` at root hangs building WinUI).
- **Build app:** `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- **Run app:** `dotnet run --project src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
  (close any running instance first — it locks the DLL; kill `testhost.exe`/`ModManager.App.exe` on
  copy-lock).
- **Publish (zero-prereq portable):** `dotnet publish src/ModManager.App/ModManager.App.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true` (csproj has post-publish targets: copy `resources.pri`, bundle VC++ runtime, trim language packs, strip the AI/ML runtime).

## Current git state

- `master` = both shipped features merged: **PR #1** (mod-update / collision-aware intake) + **PR #2**
  (agentic game profiles). Baseline ~290 tests green.
- **PR #3 is OPEN** = the roadmap + 6 design specs (the plan). **Merge PR #3 first** (or checkout
  `docs/backlog-roadmap`) so the specs are on master.
- `origin/master` is current. Branch off master per feature (`feat/<name>`); keep PRs independent
  (no stacking). **Never push `master` directly without explicit OK** — use feature branches + PRs.

## The plan (approved build order)

Roadmap: `docs/2026-05-25-backlog-roadmap.md`. Build A → D:

**Phase A — correctness (root-caused, build-ready; short spec→plan→build):**
- **A1. Load-order mirror sync (+ SP/MP naming parity)** — `Scanner.ApplyLoadOrder` &
  `ResetLoadOrder` rename the prefix on `loc.Abs` only, never `loc.Mirrors` → SP/MP desync strands
  mods (the Windrose bug). Fix: rename mirrors too. Test-first. See memory `mp-mirror-loadorder-desync-bug`.
- **A2. Folder-drop flatten** — direct-inject (and standard) *folder* drops prefix with the dropped
  folder's name; flatten like a zip (strip one wrapper). Test-first.

**Phase B — daily-use:**
- **B3. Readme viewer** — `docs/2026-05-25-readme-viewer-design.md`.
- **B4. Launch enforcement** — `docs/2026-05-25-launch-enforcement-design.md`.

**Phase C:**
- **C5. MP-safety flags** — `docs/2026-05-25-mp-safety-flags-design.md`.

**Phase D — big levers (`L` each):**
- **D6. Nexus integration** — `docs/2026-05-25-nexus-integration-design.md`. **External prereq:**
  register the 626 app with Nexus for the SSO slug (without it, build + fake-handler-test the Core;
  the SSO + live calls need the slug + a real account).
- **D7. Save / world mods** — `docs/2026-05-25-save-world-mods-design.md` (+ requirements note
  `docs/2026-05-25-save-world-mods-note.md`). Built ON `SaveManager` (snapshot/clone/restore).

## Build method (per item)

1. **A items** (root-caused): the roadmap entry *is* the spec → `superpowers:writing-plans` → build.
2. **B/C/D**: the design spec exists → `superpowers:writing-plans` (test-first task plan) →
   `superpowers:subagent-driven-development` (implementer subagent per task + spec/quality review) →
   final review (use the `mod-safety-auditor` agent for anything touching file ops / save tree) →
   `superpowers:finishing-a-development-branch` (push + PR).
3. Branch `feat/<name>` off master. Conventional commits ending `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.
4. After each: a GUI smoke test (Este runs the app — WinUI has no UI unit tests; build success +
   the smoke test are the gates). Provide the smoke steps.

## Operating laws (non-negotiable — from CLAUDE.md)

- **Pure-core / thin-shell:** all logic in `ModManager.Core` (NO UI refs; `CorePurityTests` guards
  it); `*.App` is the thin shell.
- **Test-first:** failing xUnit test before the code.
- **File ops reversible + atomic:** *move* to holding (never delete); `AtomicJson.WriteJsonAtomic`;
  no silent overwrite on intake (the reversible replace flow handles updates).
- **Never bake API keys** (CurseForge proxy / per-user Nexus key from SSO).
- **Render mod-supplied strings as text only** (attacker-controlled — readmes too).
- **WinUI gotcha:** NO literal bool in XAML for a nullable prop (`IsChecked="True"` throws
  XamlParseException) — set defaults in code-behind. (Cost us a live debugging cycle last session.)

## Key memories (auto-loaded via MEMORY.md)

`mp-mirror-loadorder-desync-bug`, `save-world-mods-feature`, `nexus-integration-feature`,
`agentic-game-profiles`, `winui-publish-gotchas`, `dotnet-test-build-gotchas`, `dotnet-rewrite-phase1`,
`game-profile-foundation`, `ludusavi-save-locations`, `fromsoft-two-mod-worlds`, `ue-project-subfolder`,
`shared-json-camelcase`, `launch-options-feature`.

## First moves

1. **Merge PR #3** (roadmap + specs onto master) — or checkout `docs/backlog-roadmap` to read them.
2. **Build A1** (mirror-sync) — root-caused + ready; quick win that fixes the class of bug that
   stranded Windrose mods.
3. Proceed **A2 → B → C → D**, each its own `feat/` branch + PR.

## Decisions log

626 dashboard project `McSbZWG3AkLLxNAd3RJt` — dist/portable-first + agentic-profiles already logged
+ bridged. Log new architectural forks as they come (e.g. Nexus SSO key handling, save-mod structure).
