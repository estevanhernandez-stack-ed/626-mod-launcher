# Safe Clear, Restore, and New-User Onboarding — Master Design

**Date:** 2026-05-28
**Status:** Drafted from in-chat brainstorm with Este (+ an 8-agent grounding/adversarial workflow) — awaiting review
**Project:** 626 Mod Launcher (unbound on the dashboard — log decisions with `projectId: null`, tagged `626-mod-launcher`)
**Phase specs:**
- [`2026-05-28-phase0-reversibility-prerequisites-design.md`](2026-05-28-phase0-reversibility-prerequisites-design.md) — fixes + primitives the engine stands on
- [`2026-05-28-phase1-safe-clear-restore-design.md`](2026-05-28-phase1-safe-clear-restore-design.md) — the reset engine
- Phase 2 (Onboarding) — specced once Phase 1 lands; shape captured below

## Why

We want a painless new-user onboarding flow. But before a returning or resetting user can land in a clean first-run, we need a way to **clear a user who already has data** without:

- **erasing any mods** — enabled mods live in the game folder, disabled mods live in `<dataDir>/disabled/<mod>/` as real files that were *moved* out of the game ([`Scanner.cs:403-460`](../../../src/ModManager.Core/Scanner.cs#L403-L460));
- **leaving a game unplayable** — if a game needs a specific launcher (Seamless Co-op, Mod Engine 2), the user has to be told;
- **forcing a Nexus re-auth** — resetting the interface shouldn't nuke the Nexus connection unless asked.

The user reframed "clear" into something better than a delete: a **Safe Clear** that archives the whole launcher state into a restorable point, hands the user an off-boarding sheet describing exactly what state they're left in, and returns the launcher to first-run. A returning user — or one prepping for a major game update — restores from the point. That archive-and-restore is the safety net the rest of the feature hangs on.

## What the grounding workflow found (and reshaped)

An 8-agent read-only workflow grounded every assumption against the code and then adversarially stress-tested the design. Two findings reshaped the work; both verified directly against source:

1. **`FrameworkRegistry.Uninstall` resolves installed files against `gameRoot`, not the install root** ([`FrameworkRegistry.cs:58,69`](../../../src/ModManager.Core/Frameworks/FrameworkRegistry.cs#L58)) — but `FrameworkInstaller` records them relative to `installRoot` and stores the correct path in `m.InstallPath`, which Uninstall ignores ([`FrameworkInstaller.cs:82,144,152`](../../../src/ModManager.Core/Frameworks/FrameworkInstaller.cs#L82)). For FromSoft games (`PlayFolder` → `<gameRoot>/Game/`) uninstall deletes the wrong path and leaves framework files behind. **"Return to vanilla" cannot reverse a FromSoft framework until this is fixed.**
2. **`EnableMod` has no rollback** ([`Scanner.cs:462-519`](../../../src/ModManager.Core/Scanner.cs#L462-L519)) — its mirror `DisableEntry` rolls back a half-move cleanly ([`Scanner.cs:429-442`](../../../src/ModManager.Core/Scanner.cs#L429-L442)) but the inverse copy-loop has no try/catch and no `moved` list, and `File.Copy` (line 511) has no overwrite flag. The "Leave mods active" path runs through it; a failure after games.json is reset strands the game in a state no manifest describes.

Both are pre-existing — the feature would merely be the first thing to lean on them hard. They are fixed in **Phase 0**.

The review also confirmed: `MoveAny` swallows everything in a bare `catch` and never verifies a cross-volume copy ([`Scanner.cs:300-312`](../../../src/ModManager.Core/Scanner.cs#L300-L312)); there is **zero free-space checking** anywhere in the codebase; `IsBusy` is a UI bool, not a lock; loader mods (UE4SS/BepInEx) toggle via a manifest flip, not file moves ([`Scanner.cs:408-419,471-482`](../../../src/ModManager.Core/Scanner.cs#L408-L419)); owned/Vortex mods are skipped by both enable and disable ([`Scanner.cs:407,470`](../../../src/ModManager.Core/Scanner.cs#L407)); and most sideloaded mods have **no recorded source URL** (only Seamless Co-op has one hardcoded — [`KnownDirectInjectMod.cs:46`](../../../src/ModManager.Core/Catalog/KnownDirectInjectMod.cs#L46)).

## Operating laws (these outrank convenience)

Safe Clear is the single most destructive operation the launcher will ever run. It earns its own laws, in the spirit of the README's existing four. Every phase spec enforces these.

- **Law A — Snapshot, verify, seal, *then* destroy.** When archiving is on, nothing is moved, deleted, reset, or reversed until a **complete, checksum-verified, sentinel-sealed** restore point exists on disk. Never uninstall-then-discover-you-can't-archive. An interrupted Safe Clear must always leave a *complete* restore point or a *clean* original — never a half-written archive.
- **Law B — Restore replays untrusted input.** A restore point is user-reachable files on disk; the manifest can be hand-edited or pre-seeded. Every path the replay is about to write runs the **same forbidden-path / containment gate** the install path uses ([`FrameworkInstaller.cs:98-119`](../../../src/ModManager.Core/Frameworks/FrameworkInstaller.cs#L98-L119)), extracted into a reusable `PathGate` (Phase 0). A `..` or drive-rooted entry is refused; the game folder is untouched.
- **Law C — Byte-for-byte means verified, not hoped.** The manifest records per-file size (and hash for cross-volume payloads). Restore *verifies* against it and refuses on mismatch. Cross-volume moves copy → verify → delete; an unverified copy is never deleted.
- **Law D — `nexus.json` never enters a restore point.** The Nexus key is DPAPI-encrypted, bound to the current Windows account, App-layer only ([`NexusService.cs:21-22`](../../../src/ModManager.App/Services/NexusService.cs#L21)). "Keep Nexus" is purely an App-side *skip-delete* branch. DPAPI never reaches Core, and the key never lands in an archive that could be copied to another machine.
- **Law E — Pre-flight before any mutation.** Before the first move: free space on the restore-point volume (payload + 10% + a 1 GB floor via `DriveInfo`), game-not-running (match against `GameEntry.LaunchTargets` process), and every target drive reachable. Short on any → refuse with a precise message, change nothing.
- **Law F — One writer.** A real cross-operation gate (a `SemaphoreSlim(1,1)` in the App) blocks Safe Clear / Restore against in-flight intake, toggles, and async identify writes. An on-disk `safe-clear.lock` under `%APPDATA%\ModManagerBuilder` enables crash recovery on next launch.
- **Law G — Snapshot-all, then mutate-all.** Across multiple games, the whole capture phase completes and seals before the reset/reverse phase begins. A failure partway through reset still leaves a complete restore point.
- **Law H — The off-boarding sheet is a convenience, the restore point is the truth.** The in-game-folder sheet is best-effort: atomic temp+rename, snapshot any colliding file first (ReplacedStore), written at the **game root** (never the exe/`Game/` subfolder — anti-cheat), and a write failure (read-only / locked) never aborts the clear. The authoritative copy lives in the restore point. The sheet leads with "your mods are preserved" and is honest where a source URL is unknown.

## Architecture — Core / App split

`RestorePointService` is **split**, not a single class, to keep Core pure and keep DPAPI + `%APPDATA%` + disk-discovery + dialogs in the App.

```
ModManager.Core (pure, headless, tested)            ModManager.App (Windows shell)
┌──────────────────────────────────────┐           ┌────────────────────────────────────┐
│ RestorePoint (record + schemaVersion) │           │ RestorePointService                  │
│ RestorePointPlanner                   │  ← DTO →   │  - owns %APPDATA% restore-points/    │
│   capture-plan / replay-plan          │           │  - free-space + game-running checks  │
│ PathGate.ValidateRelative()           │           │  - the SemaphoreSlim writer gate     │
│ OffBoardingReport + OffBoardingSheet  │           │  - keep/skip nexus.json (DPAPI)      │
│   .Render(report) → string (no IO)    │           │  - drives the move/copy/verify IO    │
│ RestoreReconcile (id/GameRoot diff)   │           │  - Safe Clear + Restore dialogs      │
│ SpaceCheck.Require(volume, bytes...)  │           │  - hydrates OffBoardingReport from    │
└──────────────────────────────────────┘           │    LaunchScan + DirectInject.Detect + │
                                                    │    FrameworkRegistry + metadata       │
CorePurityTests guards UI-namespace leaks, but      └────────────────────────────────────┘
NOT System.IO leaks — so the Core renderer ships
with an explicit "touches no filesystem" test.
```

The off-boarding sheet is the clearest trap: launch discovery lives in App (`LaunchScan` does disk walks — [`LaunchScan.cs`](../../../src/ModManager.App/Services/LaunchScan.cs)), and `DirectInject.Detect` needs an App-fed file enumeration. So the **App hydrates a fully-populated `OffBoardingReport` DTO and Core renders the string**. A "renderer touches no filesystem" unit test backs the boundary, because `CorePurityTests` ([`CorePurityTests.cs:10-25`](../../../tests/ModManager.Tests/CorePurityTests.cs#L10-L25)) only catches UI-namespace assembly references, not a `System.IO` leak.

## Restore-point storage (laid-out directory)

A restore point is a plain timestamped directory mirroring the live layout — **not** a zip. Byte-for-byte verification is trivial, the `PathGate` runs naturally on copy-back, and there's no zip-bomb surface on the most destructive op in the app.

```
%APPDATA%\ModManagerBuilder\restore-points\<yyyyMMdd-HHmmss>\
  manifest.json                 ← written LAST, atomically, with complete:true + checksums
  games.json                    ← verbatim copy of the live registry (IDs intact)
  app-settings.json
  themes\…                      ← copied
  profile\…                     ← avatar copied
  games\<game-id>\
    data\                       ← the whole _626mods/<id>/ payload (disabled\, profiles\,
                                    classification.json, metadata.json, loadorder.json,
                                    config-backups\, readmes\, frameworks\ + backup trees)
    vanilla-moved\              ← files pulled OUT of the game folder under "return to vanilla"
    offboarding.txt             ← the authoritative off-boarding sheet for this game
  (nexus.json is NEVER here — Law D)
```

The live `_626mods/<id>/` directory is **not deleted** on Safe Clear — its *contents* are archived and a `RESTORE-AVAILABLE.json` breadcrumb (camelCase, names the restore-point timestamp) is left in place so a fresh re-add detects the orphan and offers restore (see game-id determinism below).

## Manifest schema (the new persisted shape)

`manifest.json` is a new on-disk shape, so it follows the camelCase rule with a string-asserting round-trip test ([`.claude/rules/camelcase-json-on-disk.md`](../../../.claude/rules/camelcase-json-on-disk.md)) and routes through `AtomicJson` ([`AtomicJson.cs:16-20`](../../../src/ModManager.Core/AtomicJson.cs#L16)) — **not** a hand-rolled `JsonSerializerOptions` (the trap `FrameworkInstaller.cs:159-163` fell into).

```jsonc
{
  "schemaVersion": 1,              // refuse-on-newer; migrate older
  "launcherVersion": "0.4.0",      // diagnostics
  "createdUtc": "2026-05-28T…Z",
  "complete": true,                // the SEAL — written last; Restore refuses without it
  "keepNexus": true,               // record of the toggle (nexus.json itself never archived)
  "totalBytes": 12345678,
  "fileCount": 421,
  "games": [
    {
      "id": "elden-ring",          // verbatim from games.json — Restore upserts, never re-mints
      "gameName": "ELDEN RING",
      "gameRoot": "D:\\…\\ELDEN RING\\Game",
      "endState": "vanilla",       // "vanilla" | "modsActive"
      "launchTargets": [ { "label": "Play (Seamless Co-op)", "kind": "exe",
                           "target": "Game\\seamlesscoop\\launch_….exe", "isDefault": true } ],
      "requiredLauncher": "…",     // GameEntry.RequiredLauncher, if set
      "frameworks": [ { "frameworkId": "eldenmodloader", "installPath": "…\\Game",
                        "installedFiles": [ … ], "backupSnapshotPath": "…",
                        "capturedStatePath": "games\\elden-ring\\data\\frameworks\\…" } ],
      "loaderMods": [ { "name": "…", "loader": "ue4ss", "enabled": true } ],  // manifest-state
      "ownedMods": [ { "name": "…", "managedBy": "vortex" } ],                // noted, not moved
      "movedFiles": [ { "rel": "Game\\dinput8.dll", "bytes": 1234, "sha256": "…" } ],
      "mods": [ { "name": "…", "enabled": false, "sourceUrl": "https://…",
                  "sourceConfidence": "fingerprint", "installedUtc": "…" } ],
      "offboardingSheetGameFolderPath": "D:\\…\\ELDEN RING\\626-launcher-how-to-launch.txt"
    }
  ]
}
```

## Game-id determinism — footgun turned into the restore hook

Game ids are deterministic slugs, uniqued only against the *currently registered* set ([`EnginePresets.cs:47-59`](../../../src/ModManager.Core/EnginePresets.cs#L47)), and the per-game data dir is keyed on the id ([`Scanner.cs:29-37`](../../../src/ModManager.Core/Scanner.cs#L29-L37)). After Safe Clear empties games.json, re-adding "Elden Ring" re-derives the *same* `elden-ring` id and the *same* data-dir path.

- **The footgun:** two Elden Ring installs (`elden-ring`, `elden-ring-2`) re-added in a different order silently swap data dirs.
- **The fix that turns it into a feature:**
  - **Restore replays games.json verbatim** via `Registry.UpsertGame` ([`Registry.cs:25-27`](../../../src/ModManager.Core/Registry.cs#L25)) — it **never** routes through the onboarding add-game step that mints new ids. Restore and add-game are mutually exclusive on a given game.
  - **Reconcile, don't clobber:** if a live game shares an id but points at a different `GameRoot`, Restore surfaces a conflict (App dialog) rather than overwriting — pure-Core `RestoreReconcile` returns the conflict list.
  - **Onboarding is id-aware:** when the user adds a game post-clear, compute its prospective id and check `restore-points/` for a matching archived data dir *before* creating an empty one; if found, offer restore. The `RESTORE-AVAILABLE.json` breadcrumb makes even a cold re-add detect it.

## First-run vs just-cleared

Both produce an empty games.json ([`Registry.EmptyRegistry`](../../../src/ModManager.Core/Registry.cs#L9)), so onboarding can't tell "fresh install" from "just Safe-Cleared, offer Restore" without a marker. Safe Clear writes a small `%APPDATA%\ModManagerBuilder\last-clear.json` (camelCase: `clearedUtc`, `restorePoint`) consumed once by onboarding to surface the Restore step, then cleared.

## Retention

Restore points hold full mod payloads (tens of GB possible). Following the `IniEditService` retention precedent (`MaxBackupsPerFile = 10`), restore points get a surfaced management surface in Settings (list with timestamp / games / size, delete), a default keep-all with a low-disk warning, and a soft cap that prompts (never silently prunes) when exceeded.

## Phase map

| Phase | Spec | Ships |
|---|---|---|
| **0 — Prerequisites** | [phase0](2026-05-28-phase0-reversibility-prerequisites-design.md) | FrameworkRegistry.Uninstall fix, EnableMod rollback, MoveAny IOException-scope + verified cross-volume move, `PathGate` extraction, `SpaceCheck`, DisableEntry mirror snapshot-first ordering, ModMeta `installedUtc` + source-URL capture at intake + opt-in backfill sweep. All test-first. |
| **1 — Safe Clear + Restore** | [phase1](2026-05-28-phase1-safe-clear-restore-design.md) | `RestorePoint*` Core types, App `RestorePointService`, the Safe Clear dialog + orchestration (Laws A–H), capture set, return-to-vanilla / leave-active, off-boarding sheet, Restore replay (gated + verified + reconciled), restore-point management, interrupted-clear recovery. |
| **2 — Onboarding** | (later) | Wizard: welcome + add-first-game (Steam detect), suggested-Nexus (skip if `IsConnected`), personalize (theme + avatar), drop-a-mod tutorial, Restore-from-previous-setup offer. Triggers first-run AND after Safe Clear (via `last-clear.json`). Re-runnable from Settings. Modeled on `AddGameDialog` ([`AddGameDialog.xaml.cs:37-100`](../../../src/ModManager.App/AddGameDialog.xaml.cs#L37)). |

## Decisions to log (after approval)

- Safe Clear reframed from a destructive wipe into an archive-and-restore safety net (also serves "prep for major update").
- Two pre-existing reversibility bugs (FrameworkRegistry.Uninstall path, EnableMod no-rollback) found and folded into Phase 0 — the feature exposed that the bar was higher than "build a clear button."
- Restore points stored as laid-out directories, not zips.
- Source URLs persisted at install time (honoring-the-builders adjacent — attribution-preserving, not bundling).

## Out of scope (whole feature)

- Cloud / cross-machine restore points (the DPAPI-bound Nexus key alone makes a restore point non-portable by design).
- Unbundling third-party mod packs (Wabbajack-style) during clear.
- Migrating existing FrameworkInstaller/Registry off their current manifest shape beyond the Uninstall path fix.
