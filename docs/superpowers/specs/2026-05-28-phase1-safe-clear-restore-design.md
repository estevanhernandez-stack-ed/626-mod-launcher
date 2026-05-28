# Phase 1 — Safe Clear + Restore engine — Design Spec

**Date:** 2026-05-28
**Status:** Drafted — awaiting review
**Branch (proposed):** `feat/safe-clear-restore`
**Master:** [`2026-05-28-safe-clear-restore-onboarding-design.md`](2026-05-28-safe-clear-restore-onboarding-design.md)
**Depends on:** [Phase 0](2026-05-28-phase0-reversibility-prerequisites-design.md) (must merge first)

## Why

The reset engine. Archive the whole launcher state into a restorable point, hand the user an off-boarding sheet describing the exact state they're left in, return the launcher to first-run — and let a returning user restore byte-for-byte. Governed by Laws A–H (master spec). The onboarding wizard (Phase 2) consumes this engine's outputs; it is **not** built here.

## Components

### Core (pure, tested)

```csharp
namespace ModManager.Core.RestorePoints;

public sealed record RestorePointManifest(            // the on-disk shape (master spec §Manifest)
    int SchemaVersion, string LauncherVersion, string CreatedUtc, bool Complete,
    bool KeepNexus, long TotalBytes, int FileCount, IReadOnlyList<GameArchive> Games);

public sealed record GameArchive(
    string Id, string GameName, string GameRoot, string EndState,           // "vanilla" | "modsActive"
    IReadOnlyList<LaunchTarget> LaunchTargets, string? RequiredLauncher,
    IReadOnlyList<FrameworkArchive> Frameworks, IReadOnlyList<LoaderModState> LoaderMods,
    IReadOnlyList<OwnedModNote> OwnedMods, IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<ArchivedMod> Mods, string? OffboardingSheetGameFolderPath);

public static class RestorePointPlanner
{
    // What to capture for a game (pure: takes hydrated inputs, returns a plan of file ops).
    public static CapturePlan PlanCapture(GameCaptureInput input);
    // How to replay a manifest (pure: returns ordered, PathGate-validated write ops + verify list).
    public static ReplayPlan PlanReplay(RestorePointManifest m, IReadOnlyList<GameEntry> liveGames);
}

public static class RestoreReconcile   // id/GameRoot conflict detection — returns conflicts, writes nothing
{ public static IReadOnlyList<RestoreConflict> Check(RestorePointManifest m, IReadOnlyList<GameEntry> live); }

public sealed record OffBoardingReport( /* fully-hydrated DTO — no IO */ … );
public static class OffBoardingSheet { public static string Render(OffBoardingReport r); }  // string only
```

Every write path in `PlanReplay` runs `PathGate.IsContained` (Phase 0). `OffBoardingSheet.Render` ships with a unit test asserting it touches no filesystem (the `CorePurityTests` assembly guard can't see a `System.IO` leak — master spec).

### App (Windows shell)

```csharp
namespace ModManager.App.Services;

public sealed class RestorePointService     // singleton in App.xaml.cs ConfigureServices
{
    private readonly SemaphoreSlim _gate = new(1, 1);   // Law F — one writer
    // Owns %APPDATA%\ModManagerBuilder\restore-points\, free-space + game-running checks,
    // the keep/skip nexus.json branch (DPAPI never reaches Core), all move/copy/verify IO,
    // and hydrates OffBoardingReport from LaunchScan + DirectInject.Detect + FrameworkRegistry + metadata.
    public Task<SafeClearResult> SafeClearAsync(SafeClearOptions opts, IProgress<string> progress, CancellationToken ct);
    public IReadOnlyList<RestorePointInfo> ListRestorePoints();
    public Task<RestoreResult> RestoreAsync(string timestamp, IProgress<string> progress, CancellationToken ct);
    public void DeleteRestorePoint(string timestamp);
}
```

## The Safe Clear dialog

Modeled on the Uninstall confirm dialog ([`MainWindow.xaml.cs:591-612`](../../../src/ModManager.App/MainWindow.xaml.cs#L591)), using the owned-folder warning Border+TextBlock for the reassurance copy.

- **Scope line:** "Reset launcher — all N games" (full reset is the default; per-game clear is a later nicety, not v1).
- **End-state** (radio): **Return to vanilla (restorable)** vs **Leave mods active**. The choice applies to all games by default; an expander lets the user override per game when they have a mix (e.g. vanilla one game for an update, keep another modded).
- **Create restore point** (checkbox, default ON, skippable). When OFF, the no-archive hard reset still **moves** mod payloads to holding (never `File.Delete`) — "skip archive" means "don't write a restore point," not "delete mods."
- **Keep Nexus API connection** (toggle, default ON) — skips deleting `nexus.json` (Law D).
- **Provenance summary** before confirm: "M of K mods have a recorded source; the rest are preserved in the restore point but you'll need to find them again." Honest counts, not a blank promise.
- **Pre-flight results** surfaced inline: free space, game-running, offline drives. Any blocker disables Confirm with the precise reason.

## Safe Clear orchestration (Laws A, E, F, G in order)

```
SafeClearAsync:
  await _gate.WaitAsync(ct)                         // Law F — block intake/toggle/identify
  write %APPDATA%\…\safe-clear.lock {startedUtc, timestamp}   // crash-recovery breadcrumb

  PRE-FLIGHT (Law E) — change nothing yet:
    - every registered game's GameRoot + dataDir reachable? (offline/USB drive → list, require
      reconnect or explicit exclude; excluded games noted in their sheet, not cleared)
    - any target game process running? (match GameEntry.LaunchTargets exe) → refuse "close <game>"
    - sum payload bytes; SpaceCheck.Require(restore-point volume) → refuse if short

  CAPTURE-ALL then SEAL (Law A + G) — still no destructive move:
    for each game:
      - copy games.json verbatim into the restore point (IDs intact)
      - copy the whole _626mods/<id>/ data dir → restore-points/<ts>/games/<id>/data/
        (disabled\, profiles\, classification.json, metadata.json, loadorder.json,
         config-backups\, readmes\, frameworks\ + backup trees)
      - snapshot FrameworkRegistry.List() current installed files (with live config edits)
        BEFORE any uninstall → games/<id>/data/frameworks/<fwId>/captured-state\
      - record DirectInject.Detect() output, ReplacedStore __626replaced.json provenance,
        loader-mod manifest enable-state (UE4SS/BepInEx), owned-mod inventory (noted, not moved)
      - copy themes\, profile\ (avatar), app-settings.json
      - hydrate OffBoardingReport; render sheet → games/<id>/offboarding.txt (the authoritative copy)
    - compute per-file size/sha (cross-volume payloads) → manifest
    - VERIFY the capture (file count + sizes) against the plan
    - write manifest.json LAST, atomically, complete:true + totalBytes + fileCount  ← THE SEAL

  MUTATE-ALL (only after seal):
    for each game, apply end-state:
      vanilla    → reverse direct-inject (DirectInject.Disable), uninstall frameworks
                   (Phase-0-fixed Uninstall, against InstallPath), move active mod payloads to
                   holding; ReplacedStore-backed files restored to true original; loader manifests
                   flipped off; OWNED mods left in place (can't move) and flagged in the sheet
      modsActive → re-enable everything from holding first (Phase-0-fixed EnableMod with rollback +
                   structured skip outcomes), then reconcile live enabled set vs pre-clear set and
                   surface any skip; copy (not move) state into archive already done in capture
    - best-effort write offboarding.txt INTO the game ROOT (Law H): atomic temp+rename, snapshot a
      colliding file via ReplacedStore first, name "626-launcher-how-to-launch.txt", at game root
      (never Game\); on read-only/locked failure → log + tell user it's in the restore point, DON'T abort
    - leave RESTORE-AVAILABLE.json breadcrumb in each _626mods/<id>/ (do NOT delete the data dir)

  RESET:
    - delete launcher registry/index that defines "has data": games.json, app-settings.json,
      themes\, profile\ (their content is archived). nexus.json deleted ONLY if KeepNexus == false.
    - write last-clear.json {clearedUtc, restorePoint} for onboarding's Restore step
    - delete safe-clear.lock
  finally: _gate.Release()
```

A failure during MUTATE/RESET leaves a **complete, sealed** restore point (capture finished first) — recovery is "restore from it." A failure during CAPTURE leaves the original fully intact (nothing destructive ran) and a partial, **unsealed** restore point that Restore refuses and startup recovery offers to delete.

## Return-to-vanilla honesty

"Return to vanilla" is only as clean as the catalog + provenance coverage. The sheet states plainly when it can't guarantee a pristine folder:

- Files with a ReplacedStore backup → restored to true original.
- Pure additive mod entries (catalog-known direct-inject, frameworks) → moved/uninstalled cleanly.
- Loose files matching no catalog signature and no ReplacedStore record → archived, but the sheet says: "these files were added by mods and moved to your restore point; your game folder may differ from a fresh install if a mod overwrote a game file we didn't snapshot."
- Owned/Vortex mods → not touched; sheet says "managed by Vortex — clean up there."

## The off-boarding sheet (`OffBoardingReport` → `OffBoardingSheet.Render`)

App hydrates from: `GameEntry.LaunchTargets` (+ `RequiredLauncher`), `LaunchScan.Detect` for Seamless/ME2 launchers ([`LaunchScan.cs:20-103`](../../../src/ModManager.App/Services/LaunchScan.cs#L20)), `DirectInject.Detect`, `FrameworkRegistry.List`, and per-mod metadata (`DisplayName`, `Url`, `sourceConfidence`, `installedUtc`). Core renders a plain-text sheet:

```
How to launch ELDEN RING after resetting 626 Mod Launcher
==========================================================
Your mods are preserved. The full setup is saved in your restore point:
  C:\Users\you\AppData\Roaming\ModManagerBuilder\restore-points\20260528-141233\

HOW TO START THE GAME
  Seamless Co-op is still installed. Launch with:
    D:\…\ELDEN RING\Game\seamlesscoop\launch_elden_ring_seamlesscoop.exe
  Do NOT launch from Steam directly while Seamless Co-op is installed.

WHAT'S STILL INSTALLED
  Frameworks:  Elden Mod Loader (by …)
  Mods (12):   <name> — source: https://nexusmods.com/…   (installed 2026-04-02)
               <name> — likely source: https://…          (low-confidence match)
               <name> — source not recorded — sideloaded; you'll need to find it again
  Managed by Vortex (2): <name>, <name> — clean these up in Vortex.

TO RESTORE THIS SETUP
  Open 626 Mod Launcher → onboarding → "Restore a previous setup", or
  Settings → Restore points → 20260528-141233.
```

## Restore replay (`RestoreAsync`)

```
- acquire _gate; refuse if a Safe Clear lock is present
- load manifest; REFUSE if !complete, schemaVersion > supported, or checksum/fileCount mismatch
- RestoreReconcile.Check vs live games:
    same id + same GameRoot   → upsert verbatim (Registry.UpsertGame — never re-mint ids)
    same id + different root   → CONFLICT → surface dialog, do nothing for that game until resolved
    new id                     → upsert
- PlanReplay → ordered write ops; PathGate.IsContained on EVERY destination (Law B)
- copy data dirs back, move vanilla-moved files back into game folders (verified per Law C)
- re-apply themes/avatar/app-settings; restore nexus.json is N/A (never archived)
- remove the launcher-authored offboarding.txt from game folders (it's recorded in the manifest)
- clear last-clear.json
```

Restore models nothing on `SaveManager.Restore`'s clear-then-extract ([`SaveManager.cs:101-104`](../../../src/ModManager.Core/SaveManager.cs#L101)) — no `File.Delete` loop in the game folder; restore is verified copy-back over a known layout.

## Restore-point management + interrupted-clear recovery

- **Settings → Restore points:** list (timestamp, games, total size), Restore, Delete. Low-disk warning; soft cap prompts (never silently prunes) — `IniEditService.MaxBackupsPerFile` precedent.
- **Startup recovery:** if `safe-clear.lock` exists on launch, a Safe Clear was interrupted. If the restore point is sealed (`complete:true`) → offer "resume reset" or "restore (undo)". If unsealed → the original is intact; offer to discard the partial archive and continue. Either way, never silently proceed.

## Testing (the reversibility battery)

Core, headless, test-first:

- **Round-trip byte-for-byte:** capture → restore → game folders + data dirs identical (single-volume and forced cross-volume).
- **End-states:** vanilla leaves game launchable + mods in archive; modsActive re-enables all, reconciles, surfaces skips.
- **Keep-Nexus:** ON leaves `nexus.json` untouched and absent from the archive; OFF deletes it and still never archives it.
- **Skip-archive:** mods are moved (never `File.Delete`), no manifest written.
- **Law A ordering:** a forced failure during MUTATE leaves a sealed, complete restore point; a forced failure during CAPTURE leaves the original intact + an unsealed point Restore refuses.
- **Law B:** a hand-edited manifest with `..\..\x.dll` is refused on replay; game folder unchanged.
- **Law C:** checksum mismatch on restore is refused; cross-volume copy verifies before source delete.
- **Game-id:** two same-named installs (`-2` suffix) survive clear+restore with ids intact and data dirs un-swapped; reconcile returns a conflict for same-id-different-GameRoot and writes nothing.
- **Manifest:** camelCase round-trip (string-assert `"schemaVersion"`, `"gameId"`); refuse-on-newer-schema; refuse-without-sentinel.
- **Off-boarding renderer:** touches no filesystem; lists every launch target; renders known / low-confidence / unknown source lines; never emits the Nexus account name or key.
- **Loader + owned mods:** UE4SS/BepInEx enable-state captured + replayed via manifest flip (not moves); Vortex mods noted, never moved.

App-side smoke (`docs/smoke-tests/pending.md`):
- Two-drive machine (game on D:, `%APPDATA%` on C:), large mod set → Safe Clear archives, verifies, resets; sheet written to game root; game launches via Seamless; Restore brings everything back.
- Kill the app mid-clear → relaunch detects the lock and offers resume/restore.

## Out of scope (Phase 1)

- The onboarding wizard and its Restore-offer UI — Phase 2 (consumes `last-clear.json`, `ListRestorePoints`, and the id-aware add-game check).
- Per-game (single-game) Safe Clear — v1 is full-launcher reset; per-game is a later add.
- Cloud/portable restore points (DPAPI-bound by design).
