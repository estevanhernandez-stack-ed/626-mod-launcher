# Phase 1B-1 — Safe Clear + Restore Orchestrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the App-layer `RestorePointService` that drives the Phase 1A Core engine through the full Law-A sequence (pre-flight → capture-all → seal → mutate-all → reset), plus Restore, restore-point management, off-boarding-sheet hydration, a game-running probe, and interrupted-clear recovery — all unit-testable against a temp data root.

**Architecture:** `RestorePointService` (App singleton) takes its data root as a constructor parameter (defaults to `%APPDATA%\ModManagerBuilder`, overridden in tests) so the orchestration runs headless against temp dirs. It composes the merged `ModManager.Core.RestorePoints` engine (which already takes explicit paths) + App services (`LauncherService`, `NexusService`, `LaunchScan`). The DPAPI `nexus.json` decision is a path-level `File.Delete`/skip (no DPAPI in the orchestrator's hot path). A `SemaphoreSlim(1,1)` is the one-writer gate (Law F). DTOs/results are returned for the WinUI layer (Phase 1B-2) to render.

**Tech Stack:** .NET 10, C#, xUnit. The service lives in `src/ModManager.App/Services/` (App layer — it owns `%APPDATA%` paths + DPAPI + process checks). Tests live in `tests/ModManager.Tests/` and exercise it against temp roots (the App test project already references Core; confirm it can see App types — see Task 0).

**Spec:** [`../specs/2026-05-28-phase1-safe-clear-restore-design.md`](../specs/2026-05-28-phase1-safe-clear-restore-design.md) (App orchestration section). **Master:** [`../specs/2026-05-28-safe-clear-restore-onboarding-design.md`](../specs/2026-05-28-safe-clear-restore-onboarding-design.md) (Laws A–H). **Builds on:** Phase 1A engine (merged: `RestorePointEngine`, `RestorePointManifestStore`, `RestoreReconcile`, `OffBoardingSheet`, `RestoreMarkers`, `FileTally`) + Phase 0 (`SpaceCheck`).

**Test command:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (single: `--filter "FullyQualifiedName~<Name>"`). NEVER bare `dotnet test`/`dotnet build` at the repo root.

## Scope note — Phase 1B-2 (the WinUI shell, NOT here)

The Safe Clear `ContentDialog` (end-state radio + create-restore-point + keep-Nexus toggles + pre-flight display), the Settings → Restore-points management UI, the "Replay onboarding"/Reset entry, and the startup wiring (call recovery on launch, route post-clear to onboarding) are Phase 1B-2 — smoke-tested, built with the app running. This plan exposes everything those surfaces call: `RestorePointService` with `SafeClearAsync`, `RestoreAsync`, `ListRestorePoints`, `DeleteRestorePoint`, `DetectInterruptedClear`, and the option/result DTOs.

## CRITICAL — Task 0 prerequisite: can the test project see App types?

`RestorePointService` is in `ModManager.App`. Phase 0/1A tests are all Core. **Before Task 1**, confirm `tests/ModManager.Tests/ModManager.Tests.csproj` references `ModManager.App` (or can). If it does NOT (likely — the App is WinUI and may not be test-referenceable cleanly), then the orchestrator's testable logic must live in a **test-referenceable place**. Two options, decide in Task 0:
- **(A)** If `ModManager.Tests` already references `ModManager.App` and App types are reachable headless → put `RestorePointService` in `ModManager.App/Services/` and test it directly.
- **(B)** If not (WinUI App isn't headless-test-friendly) → put the orchestration LOGIC in a new pure class `RestorePointOrchestrator` in **`ModManager.Core/RestorePoints/`** (it only needs paths + the Core engine + interfaces for the App bits: an `INexusGate { bool IsConnected; void Delete(); }` and `IGameRunningProbe { bool AnyRunning(GameEntry) }`), and make the App's `RestorePointService` a thin adapter that implements those interfaces with DPAPI/Process and delegates. Tests target the Core `RestorePointOrchestrator` with fakes.

**Task 0 is a spike:** check the csproj references and the `CorePurityTests` constraints, then pick A or B and record the decision at the top of the implementation. The tasks below are written for **option B** (the safer default — keeps the logic pure + testable and respects the App's WinUI-only nature); if the spike shows A is clean, collapse the interfaces and put it in App. Do NOT guess — verify the csproj first.

---

## File Structure (option B — adjust if Task 0 picks A)

| File | Responsibility |
|---|---|
| `src/ModManager.Core/RestorePoints/RestorePointOrchestrator.cs` | Pure Law-A sequence: pre-flight → capture-all → seal → mutate-all → reset; Restore; recovery. Takes paths + `INexusGate`/`IGameRunningProbe`. |
| `src/ModManager.Core/RestorePoints/OrchestratorContracts.cs` | `SafeClearOptions`, `SafeClearResult`, `RestorePointInfo`, `RestoreResult`, `InterruptedClear`, `INexusGate`, `IGameRunningProbe`, `IGameProvider` |
| `src/ModManager.Core/RestorePoints/OffBoardingHydrator.cs` | Build `OffBoardingReport` from a `GameContext` + launch lines (pure; the App passes LaunchScan output in) |
| `src/ModManager.App/Services/RestorePointService.cs` | App adapter: `%APPDATA%` paths, DPAPI nexus gate, `Process`-based game probe, `LauncherService` game provider; delegates to the orchestrator; renders+writes the in-game-folder sheet |
| `src/ModManager.App/Services/GameProcessProbe.cs` | `Process.GetProcessesByName` check against a game's launch-target exe |
| `tests/ModManager.Tests/RestorePoints/RestorePointOrchestrator*Tests.cs` | the orchestration battery against temp roots with fakes |

---

## Task 0: Spike — test-referenceability + pick A/B

- [ ] **Step 1:** Read `tests/ModManager.Tests/ModManager.Tests.csproj` — does it `<ProjectReference>` `ModManager.App`? Read `src/ModManager.App/ModManager.App.csproj` — is it `net10.0-windows`/WinUI (not headless-test-friendly)? Read `tests/ModManager.Tests/CorePurityTests.cs` to reconfirm Core's forbidden namespaces.
- [ ] **Step 2:** Decide A or B (default B). Record the decision as a comment at the top of `RestorePointOrchestrator.cs` (or, for A, `RestorePointService.cs`). **No code/test yet — this is a read-only spike.** Report the decision + evidence (the csproj lines) before proceeding to Task 1.

> The remaining tasks assume **B**. If A: drop the interfaces, put the class in App, and have tests reference App directly — the logic + tests are otherwise identical.

---

## Task 1: Orchestrator contracts + interfaces

**Files:** Create `src/ModManager.Core/RestorePoints/OrchestratorContracts.cs`; test `tests/ModManager.Tests/RestorePoints/OrchestratorContractsTests.cs`.

- [ ] **Step 1: failing test** — a camelCase round-trip is not needed (these aren't persisted); instead assert the records construct + the option defaults. Write:

```csharp
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OrchestratorContractsTests
{
    [Fact]
    public void SafeClearOptions_defaults_archive_on_and_keep_nexus_on()
    {
        var o = new SafeClearOptions();
        Assert.True(o.CreateRestorePoint);
        Assert.True(o.KeepNexus);
        Assert.Equal("vanilla", o.DefaultEndState);
        Assert.NotNull(o.PerGameEndState);
    }
}
```

- [ ] **Step 2: run; FAIL.**
- [ ] **Step 3: create `OrchestratorContracts.cs`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>What the user chose in the Safe Clear dialog (Phase 1B-2 builds this).</summary>
public sealed record SafeClearOptions
{
    public bool CreateRestorePoint { get; init; } = true;          // Law: archive on by default, skippable
    public bool KeepNexus { get; init; } = true;                    // keep nexus.json (Law D)
    public string DefaultEndState { get; init; } = "vanilla";       // applied to games without an override
    public IReadOnlyDictionary<string, string> PerGameEndState { get; init; }
        = new Dictionary<string, string>();                         // gameId -> "vanilla"|"modsActive"
}

/// <summary>Result of a Safe Clear (for the dialog to report).</summary>
public sealed record SafeClearResult(
    bool Ok, string? RefusedReason, string? RestorePointTimestamp,
    IReadOnlyList<string> PerGameSheetPaths, IReadOnlyList<string> Warnings);

/// <summary>A pre-flight blocker (free space, game running, offline drive).</summary>
public sealed record PreflightBlocker(string Kind, string Detail);

public sealed record RestorePointInfo(string Timestamp, IReadOnlyList<string> GameNames, long TotalBytes, bool Complete);

public sealed record RestoreResult(bool Ok, string? RefusedReason, IReadOnlyList<RestoreConflict> Conflicts, IReadOnlyList<string> Warnings);

public sealed record InterruptedClear(string Timestamp, bool Sealed);

/// <summary>App-side seam: the nexus.json keep/skip decision (DPAPI lives in the App impl).</summary>
public interface INexusGate { bool IsConnected { get; } void DeleteStoredKey(); }

/// <summary>App-side seam: is any of a game's launch-target processes running?</summary>
public interface IGameRunningProbe { bool AnyRunning(GameEntry game); }

/// <summary>App-side seam: the registered games + their live contexts (LauncherService in the App).</summary>
public interface IGameProvider
{
    IReadOnlyList<GameEntry> Games { get; }
    GameContext ContextFor(GameEntry game);
    void ReplaceRegistry(IReadOnlyList<GameEntry> games);   // restore upserts; reset clears
}
```

- [ ] **Step 4: run; PASS.** Full suite green. Commit `feat(restore): orchestrator contracts + App-seam interfaces`.

---

## Task 2: `OffBoardingHydrator` (pure report builder)

**Files:** Create `src/ModManager.Core/RestorePoints/OffBoardingHydrator.cs`; test `OffBoardingHydratorTests.cs`.

The App passes in the launch lines (from `LaunchScan` — App-only) + the restore-point path; the hydrator builds the rest (`Frameworks`, `Mods` with provenance, `OwnedMods`) from the `GameArchive` already produced by `CaptureGame`. This keeps it pure + testable; the App just supplies launch strings.

- [ ] **Step 1: failing test**

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OffBoardingHydratorTests
{
    [Fact]
    public void Hydrate_maps_archive_to_report()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla",
            Array.Empty<LaunchTarget>(), null,
            new[] { new FrameworkArchive("elm", "Elden Mod Loader", "TechieW", @"D:\T", new[] { "dinput8.dll" }, "frameworks-state/elm") },
            Array.Empty<LoaderModState>(),
            new[] { new OwnedModNote("VortexMod", "Vortex") },
            Array.Empty<MovedFile>(),
            new[] { new ArchivedMod("CoolMod", true, "https://nexusmods.com/x", "fingerprint", "2026-04-02T00:00:00Z") },
            null);

        var report = OffBoardingHydrator.Hydrate(ga, @"C:\…\restore-points\20260528-141233",
            launchLines: new[] { "Launch with X" });

        Assert.Equal("T", report.GameName);
        Assert.Contains("Launch with X", report.LaunchLines);
        Assert.Contains("Elden Mod Loader (by TechieW)", report.Frameworks);
        Assert.Contains(report.Mods, m => m.Name == "CoolMod" && m.SourceUrl == "https://nexusmods.com/x" && m.InstalledDate == "2026-04-02");
        Assert.Contains(report.OwnedMods, o => o.Name == "VortexMod" && o.ManagedBy == "Vortex");
    }
}
```

- [ ] **Step 2: FAIL.**
- [ ] **Step 3: create `OffBoardingHydrator.cs`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>Builds the off-boarding report from a captured <see cref="GameArchive"/>. Pure — the App
/// supplies the launch lines (from LaunchScan, which is App-only) + the restore-point path.</summary>
public static class OffBoardingHydrator
{
    public static OffBoardingReport Hydrate(GameArchive ga, string restorePointPath, IReadOnlyList<string> launchLines)
        => new(
            GameName: ga.GameName,
            RestorePointPath: restorePointPath,
            LaunchLines: launchLines,
            Frameworks: ga.Frameworks.Select(f => $"{f.DisplayName} (by {f.Author})").ToList(),
            Mods: ga.Mods.Select(m => new OffBoardingModLine(
                m.Name, m.SourceUrl, m.SourceConfidence, FormatDate(m.InstalledUtc))).ToList(),
            OwnedMods: ga.OwnedMods.Select(o => new OffBoardingOwnedMod(o.Name, o.ManagedBy)).ToList());

    // ISO-8601 → yyyy-MM-dd, or null. Uses RoundtripKind so a "Z" timestamp parses correctly.
    private static string? FormatDate(string? iso)
        => string.IsNullOrEmpty(iso) ? null
           : (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
               ? d.ToString("yyyy-MM-dd") : null);
}
```

- [ ] **Step 4: PASS.** Full suite green. Commit `feat(restore): OffBoardingHydrator (archive -> report)`.

---

## Task 3: `RestorePointOrchestrator.SafeClear` — the Law-A sequence

**Files:** Create `src/ModManager.Core/RestorePoints/RestorePointOrchestrator.cs`; test `RestorePointOrchestratorSafeClearTests.cs`.

The orchestrator owns: pre-flight (free space via `SpaceCheck`, game-running via `IGameRunningProbe`, drive reachability), the `safe-clear.lock` lifecycle, capture-all (per game via `CaptureGame`), seal (fill `TotalBytes`/`FileCount` via `FileTally`, write manifest LAST), mutate-all (`ApplyEndState`), the off-boarding sheet (render + return paths for the App to write into game folders), markers (`RestoreMarkers`), and reset (delete top-level state honoring `KeepNexus` via `INexusGate`). Takes the data root + restore-points root as constructor params (temp in tests) + the three seams + a clock (timestamp passed in — no `DateTime.Now` in Core).

- [ ] **Step 1: failing tests** (the headline: full clear produces a sealed restore point + leaves a playable, reset launcher; skip-archive moves nothing destructive)

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointOrchestratorSafeClearTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-orch-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // ... a FakeNexusGate (IsConnected=true, records DeleteStoredKey calls),
    //     FakeProbe (AnyRunning=false), and an in-memory IGameProvider over a temp game with one disabled mod.
    //     (Build these as private nested fakes; model the temp game on RestorePointEngineCaptureTests.MakeGame.)

    [Fact]
    public async Task SafeClear_archives_seals_and_resets_keeping_nexus()
    {
        // arrange a game with a disabled mod + a games.json-equivalent via the fake provider;
        // dataRoot = <_root>\appdata, restorePointsRoot = <_root>\appdata\restore-points
        // act: orchestrator.SafeClearAsync(new SafeClearOptions{ CreateRestorePoint=true, KeepNexus=true }, ts:"20260528-141233", ...)
        // assert:
        //  - result.Ok, result.RestorePointTimestamp == "20260528-141233"
        //  - manifest.json exists under restore-points\20260528-141233 and Validate(...).Ok (sealed, complete:true, TotalBytes>0)
        //  - nexus gate DeleteStoredKey was NOT called (KeepNexus)
        //  - safe-clear.lock is gone (cleared at the end)
        //  - last-clear.json written; RESTORE-AVAILABLE.json left in the game data dir
    }

    [Fact]
    public async Task SafeClear_refuses_when_game_running()
    {
        // FakeProbe.AnyRunning => true ; assert result.Ok == false, RefusedReason mentions the game, nothing archived/moved.
    }

    [Fact]
    public async Task SafeClear_skip_archive_moves_mods_but_writes_no_manifest()
    {
        // CreateRestorePoint=false ; assert no manifest written, but the per-game end-state still applied (mods MOVED not deleted).
    }

    [Fact]
    public async Task SafeClear_keepNexus_false_deletes_the_key()
    {
        // KeepNexus=false ; assert FakeNexusGate.DeleteStoredKey WAS called and nexus.json never entered the archive.
    }
}
```

Flesh these out with the fakes + temp game. The four behaviors (seal + keep-nexus, refuse-on-running, skip-archive-still-moves, keep-nexus-false-deletes) are the Law A/D/E proofs at the orchestration level.

- [ ] **Step 2: FAIL.**
- [ ] **Step 3: implement `RestorePointOrchestrator`** — the SafeClear sequence:

```
SafeClearAsync(SafeClearOptions opts, string timestamp, CancellationToken ct):
  acquire the SemaphoreSlim (Law F)
  try:
    games = provider.Games
    PRE-FLIGHT (Law E) — change nothing:
      - for each game: ctx = provider.ContextFor(game); check ctx.GameRoot + ctx.DataDir reachable (Directory.Exists on the drive root) → unreachable => collect blocker
      - if probe.AnyRunning(game) for any => return SafeClearResult(Ok:false, "Close <game> first")
      - if CreateRestorePoint: sum FileTally.ByteSize over each game's data dir + game-folder direct-inject payload; SpaceCheck.Require(restorePointsRoot, sum) => if !Ok return refusal with the GB message
      - any reachability blocker => refusal
    write safe-clear.lock {startedUtc:timestamp, timestamp}
    rpDir = <restorePointsRoot>\<timestamp>
    CAPTURE-ALL (only if CreateRestorePoint):
      gameArchives = []
      for each game: ga = CaptureGame(new GameCaptureInput(game, ctx, endStateFor(game, opts)), <rpDir>\games\<id>); gameArchives.Add(ga)
      copy top-level state (games.json equivalent via provider snapshot, themes/, profile/, app-settings.json) into rpDir   // NOT nexus.json (Law D)
      // hydrate + render the off-boarding sheet per game; write into rpDir\games\<id>\offboarding.txt (authoritative);
      // record the intended in-game-folder sheet path on the GameArchive (with{}) — the APP writes the game-folder copy.
      manifest = new RestorePointManifest(SchemaVersion, launcherVersion, timestamp, Complete:false, opts.KeepNexus,
                   TotalBytes: FileTally.ByteSize(rpDir), FileCount: FileTally.FileCount(rpDir), gameArchives)
      VERIFY (recount) then RestorePointManifestStore.WriteSealed(rpDir, manifest with { Complete = true })   // THE SEAL, last
    MUTATE-ALL (after seal):
      for each game: ApplyEndState(ctx, endStateFor(game,opts), <rpDir>\games\<id>)
      leave RestoreMarkers.WriteRestoreAvailable(ctx.DataDir, timestamp) per game   // do NOT delete the data dir
    RESET:
      provider.ReplaceRegistry(empty)   // games.json cleared
      delete themes/, profile/, app-settings.json (archived); nexus: if !KeepNexus => nexusGate.DeleteStoredKey()
      RestoreMarkers.WriteLastClear(dataRoot, clearedUtc:timestamp, timestamp)
      delete safe-clear.lock
    return SafeClearResult(Ok:true, timestamp, perGameSheetPaths, warnings)
  finally: release the semaphore
```

Write the full C# from this. Key points the tests pin: seal written LAST with `Complete=true`; refuse-before-any-write when a game is running; skip-archive still calls `ApplyEndState` (moves, never deletes); `KeepNexus=false` calls `nexusGate.DeleteStoredKey()`, ON never does; `nexus.json` is never copied into `rpDir`. Use the passed-in `timestamp` (no `DateTime.Now` in Core). The off-boarding sheet's game-folder write is the App's job (returned in `perGameSheetPaths` as the intended path) — the orchestrator writes only the authoritative `rpDir` copy.

- [ ] **Step 4: PASS** (all four). Full suite green. Commit `feat(restore): SafeClear orchestration (Law A pre-flight/seal/mutate/reset)`.

---

## Task 4: `RestorePointOrchestrator.Restore` + list/delete

**Files:** Modify the orchestrator; test `RestorePointOrchestratorRestoreTests.cs`.

- [ ] **Steps:** TDD `RestoreAsync(timestamp)`: `Read` → `Validate` (refuse unsealed/newer) → `RestoreReconcile.Check` vs `provider.Games` (return conflicts, write nothing if any) → `provider.ReplaceRegistry(manifest games upserted verbatim — never re-mint ids)` → `ReplayGame` per game → `RestoreMarkers.ClearLastClear`. `ListRestorePoints()` enumerates `restorePointsRoot` (each dir's manifest → `RestorePointInfo`, skipping unsealed). `DeleteRestorePoint(timestamp)` removes the dir. **Headline test:** SafeClear(vanilla) then Restore → the game's registry entry + data dir + moved game-folder files come back (compose with the engine's verified replay). **Conflict test:** a live game with the same id + different GameRoot → Restore returns a conflict and writes nothing. Commit `feat(restore): Restore replay + restore-point list/delete`.

---

## Task 5: interrupted-clear recovery (`DetectInterruptedClear`)

**Files:** Modify the orchestrator; test `RestorePointOrchestratorRecoveryTests.cs`.

- [ ] **Steps:** TDD `DetectInterruptedClear() → InterruptedClear?`: if `safe-clear.lock` exists, read its timestamp, check whether `<restorePointsRoot>\<ts>\manifest.json` is sealed (`Validate(...).Ok`) → return `InterruptedClear(ts, Sealed)`; null if no lock. The App (1B-2) shows: sealed → "resume reset / restore (undo)"; unsealed → "the original is intact; discard the partial archive?". Add `DiscardPartial(timestamp)` (delete the unsealed dir + the lock) and confirm `DetectInterruptedClear` returns null after. **Tests:** lock + sealed manifest → `Sealed=true`; lock + no/unsealed manifest → `Sealed=false`; no lock → null; DiscardPartial clears it. Commit `feat(restore): interrupted-clear detection + discard`.

---

## Task 6: App adapter `RestorePointService` + `GameProcessProbe`

**Files:** Create `src/ModManager.App/Services/RestorePointService.cs` + `GameProcessProbe.cs`. (App layer — smoke + the one testable probe method.)

- [ ] **Steps:**
  - `GameProcessProbe : IGameRunningProbe` — `AnyRunning(GameEntry)` resolves each `LaunchTarget` of `Kind=="exe"` to its exe filename and checks `Process.GetProcessesByName(nameWithoutExt)`. Extract the pure matching (`exe path → process name`) into a static helper and unit-test THAT (the `Process` call itself is smoke). 
  - `RestorePointService` (App singleton, registered in `App.xaml.cs` ConfigureServices) — constructs `RestorePointOrchestrator` with: data root = `%APPDATA%\ModManagerBuilder`, restore-points root under it, a `NexusGate` adapter (`IsConnected` ← `NexusService.IsConnected`; `DeleteStoredKey` ← `NexusService.Disconnect`), `GameProcessProbe`, and a `LauncherGameProvider` adapter over `LauncherService` (`Games` ← `LoadRegistry().Games`; `ContextFor` ← `Scanner.GameContext`; `ReplaceRegistry` ← build + `SaveRegistry`). Public async methods delegate to the orchestrator, passing `DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")` as the timestamp (the App owns the clock). After a successful capture, for each returned sheet path: render via `OffBoardingHydrator` + `LaunchScan.Detect` launch lines, then write the sheet into the game ROOT atomically (temp+rename), snapshotting a colliding file via `ReplacedStore` first; on read-only/locked failure, log + add a warning (best-effort — never abort the clear). Provide a smoke entry in `docs/smoke-tests/pending.md`.
  - Register in DI; this task is mostly wiring + the one probe unit test. Commit `feat(restore): App RestorePointService adapter + GameProcessProbe`.

---

## Final verification

- [ ] Full Core suite green incl. `CorePurityTests` (the orchestrator + hydrator + contracts are pure Core under option B — only `System.IO`; the App adapter holds `Process`/DPAPI/WinRT). Run `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`.

## Self-Review

**Spec coverage:** pre-flight free-space/running/reachable (Task 3), capture-all→seal-last→mutate-all→reset ordering (Task 3), keep-Nexus + skip-archive (Task 3), Restore validate+reconcile+replay (Task 4), list/delete management (Task 4), interrupted-clear recovery (Task 5), off-boarding hydration + in-game-folder write (Tasks 2 + 6), game-running probe (Task 6). The Safe Clear **dialog**, Settings **management UI**, and **startup wiring** are Phase 1B-2 (smoke).

**Placeholder note:** Task 3's test bodies are described (fakes + temp game + the four assertions) rather than fully written, because the fakes depend on the Task 0 A/B decision and the exact `IGameProvider` shape — the implementer writes them against the contracts from Task 1. Every other task has complete code. The Task-3 implementation pseudocode is precise enough to write the method directly; the executor fills the test fakes from the contracts.

**Type consistency:** `SafeClearOptions`/`SafeClearResult`/`RestorePointInfo`/`RestoreResult`/`InterruptedClear`/`INexusGate`/`IGameRunningProbe`/`IGameProvider` (Task 1) are consumed by the orchestrator (Tasks 3–5) and implemented by the App adapter (Task 6). `OffBoardingHydrator.Hydrate` (Task 2) feeds `OffBoardingSheet.Render` (Phase 1A). All engine calls (`CaptureGame`/`ApplyEndState`/`ReplayGame`/`RestorePointManifestStore`/`RestoreReconcile`/`RestoreMarkers`/`FileTally`/`SpaceCheck`) use the merged Phase 1A/0 signatures.

## Next: Phase 1B-2 (WinUI) then Phase 2 (onboarding)

1B-2: Safe Clear `ContentDialog` (model on the Uninstall confirm at `MainWindow.xaml.cs:591` + `AddGameDialog` multi-step), Settings → Restore points (model on `SettingsDialog` sections), "Reset / Replay onboarding" entry, and startup wiring (`App.OnLaunched`/`MainWindow.OnFirstActivated` → `DetectInterruptedClear` + post-clear onboarding via `last-clear.json`). Phase 2: the onboarding wizard consuming `last-clear.json` + `RESTORE-AVAILABLE.json`.
