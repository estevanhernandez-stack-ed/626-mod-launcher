# Phase 1B-2 — Safe Clear + Restore WinUI Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Wire the merged Phase 1B-1 orchestrator into the WinUI app — the `RestorePointService` adapter, a `GameProcessProbe`, the off-boarding-sheet write, the Safe Clear dialog, Settings restore-point management, and startup interrupted-clear recovery — so a user can reset and restore from the UI.

**Architecture:** The App layer is WinUI (`net10.0-windows`, `UseWinUI`) and is **not headless-unit-testable**. Verification is therefore: (1) the one genuinely-pure piece (the process-name matcher) is TDD'd in Core; (2) the **App project compiles** (`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`); (3) a **manual smoke checklist** in `docs/smoke-tests/pending.md` covers the end-to-end flow. Everything composes the merged `ModManager.Core.RestorePoints` engine + orchestrator (which take explicit paths — the App supplies `%APPDATA%`, DPAPI, `Process`, dialogs).

**Tech Stack:** .NET 10, C#, WinUI 3, xUnit (only for the Core matcher). Build the App via the explicit csproj + `-p:Platform=x64` — NEVER bare `dotnet build`/`dotnet test` at the repo root (the WinUI project hangs it).

**Spec/plan lineage:** Master `docs/superpowers/specs/2026-05-28-safe-clear-restore-onboarding-design.md`; Phase 1 spec `.../2026-05-28-phase1-safe-clear-restore-design.md`; orchestrator plan `.../plans/2026-05-28-phase1b1-safe-clear-restore-orchestrator.md` (merged as #78). **Depends on:** the orchestrator in `master`.

## What's deferred to Phase 2 (NOT here)

The onboarding wizard (welcome + add-first-game + suggested-Nexus + personalize + drop-a-mod tutorial + restore offer) and routing the post-clear empty state into it. 1B-2 only needs the empty state to *appear* after a clear (it does, naturally, via `HasGame=false`) and the restore-from-a-point UI to exist in Settings. `last-clear.json` + `RESTORE-AVAILABLE.json` markers are already written by the orchestrator; Phase 2 consumes them.

## Grounded conventions (from the WinUI surface investigation)

- **Dialogs:** XAML `ContentDialog` files (`x:Class`, `Title`/buttons in markup) + code-behind constructor taking services. Template: `AddGameDialog.xaml`/`.xaml.cs:12-57`. Simple confirms are built in code (`MainWindow.xaml.cs:591-612`). `XamlRoot = Content.XamlRoot`; `hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this)`.
- **Settings sections:** `ScrollViewer > StackPanel`; header `TextBlock` + `Border` divider; lists are `ItemsRepeater` + `DataTemplate` (`x:DataType`) with a `Grid` row (name+detail / buttons). Exact template: the "Installed frameworks" section, `SettingsDialog.xaml:124-152`, seeded by a `RefreshX()` in `SettingsDialog.xaml.cs`.
- **Mutually-exclusive choice:** house style is **ComboBox** (`BackdropBox`, `SettingsDialog.xaml:49-54`); there is no `RadioButtons` usage. Checkboxes + `ToggleSwitch` are used.
- **DI:** `services.AddSingleton<…>()` in `App.xaml.cs:28-48`; resolve via `App.AppHost.Services.GetRequiredService<…>()`.
- **Startup:** `MainWindow.OnFirstActivated` (`MainWindow.xaml.cs:66-71`) runs once → `ViewModel.LoadAsync()`. Recovery hooks here.
- **Refresh after clear/restore:** `IGameProvider.Reload()` must drive `MainViewModel.ReloadModsAsync()` (`MainViewModel.cs:260-279`): `_ctx = _svc.ActiveContext()` → `HasGame = _ctx is not null` → empty-state (`EmptyVisibility`, line 142-143).

---

## Task 1: `ProcessNameMatch` (pure, TDD) + `GameProcessProbe` (App glue)

The only headless-testable piece. The pure matcher decides whether a game's launch-target exes are in a running-process-name set; `GameProcessProbe` supplies the real running set via `Process`.

**Files:** Create `src/ModManager.Core/RestorePoints/ProcessNameMatch.cs`; `src/ModManager.App/Services/GameProcessProbe.cs`; test `tests/ModManager.Tests/RestorePoints/ProcessNameMatchTests.cs`.

- [ ] **Step 1: failing test**

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class ProcessNameMatchTests
{
    private static GameEntry Game(params (string kind, string target)[] targets) => new()
    {
        Id = "t", GameName = "T",
        LaunchTargets = targets.Select(t => new LaunchTarget("L", t.kind, t.target)).ToList(),
    };

    [Fact]
    public void Matches_when_an_exe_launch_target_name_is_running()
    {
        var g = Game(("exe", @"D:\ELDEN RING\Game\eldenring.exe"), ("steam", "steam://rungameid/1245620"));
        // running process names are WITHOUT extension, case-insensitive (Process.GetProcessesByName convention)
        Assert.True(ProcessNameMatch.AnyRunning(g, new[] { "explorer", "eldenring" }));
    }

    [Fact]
    public void No_match_when_no_exe_target_is_running()
    {
        var g = Game(("exe", @"D:\x\game.exe"), ("steam", "steam://x"));
        Assert.False(ProcessNameMatch.AnyRunning(g, new[] { "explorer", "discord" }));
    }

    [Fact]
    public void Ignores_non_exe_targets_and_handles_no_targets()
    {
        Assert.False(ProcessNameMatch.AnyRunning(Game(("steam", "steam://x")), new[] { "anything" }));
        Assert.False(ProcessNameMatch.AnyRunning(Game(), new[] { "anything" }));
    }
}
```

- [ ] **Step 2: run; FAIL.**
- [ ] **Step 3: `ProcessNameMatch.cs`** (Core, pure)

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>Pure decision: is any of a game's exe launch targets present in a set of running process
/// names? The App's GameProcessProbe supplies the running set from System.Diagnostics.Process; this
/// keeps the comparison logic headless-testable. Process names are extension-less, case-insensitive
/// (the Process.GetProcessesByName convention).</summary>
public static class ProcessNameMatch
{
    public static bool AnyRunning(GameEntry game, IReadOnlyCollection<string> runningProcessNames)
    {
        if (runningProcessNames.Count == 0) return false;
        var running = new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase);
        foreach (var t in game.LaunchTargets)
        {
            if (!string.Equals(t.Kind, "exe", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(t.Target);
            if (!string.IsNullOrEmpty(name) && running.Contains(name)) return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: run; PASS. Full Core suite green. Commit** `feat(restore): ProcessNameMatch (pure game-running matcher)`.

- [ ] **Step 5: `GameProcessProbe.cs`** (App — the `Process` glue, smoke only)

```csharp
using System.Diagnostics;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.App.Services;

/// <summary>App-side IGameRunningProbe: feeds the live running-process names into the pure matcher.</summary>
public sealed class GameProcessProbe : IGameRunningProbe
{
    public bool AnyRunning(GameEntry game)
    {
        // Process.GetProcesses() once; names are extension-less. (No per-target GetProcessesByName loop —
        // one snapshot is cheaper and the matcher is pure.)
        string[] names;
        try { names = Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } }).ToArray(); }
        catch { return false; }   // never let a probe failure block the user; pre-flight degrades to "not running"
        return ProcessNameMatch.AnyRunning(game, names);
    }
}
```
(No test — `Process` is the untestable glue; the matcher is covered. Build-verified in Task 6.)

---

## Task 2: App seam adapters + `RestorePointService` + DI

**Files:** Create `src/ModManager.App/Services/RestorePointService.cs` (contains the service + the `INexusGate`/`IGameProvider` adapters). Modify `src/ModManager.App/App.xaml.cs` (DI) and `src/ModManager.App/Services/LauncherService.cs` (add a `Reload` notification hook).

- [ ] **Step 1: `LauncherService.Reload` hook.** `LauncherService` has no reload/notify today. Add a lightweight notification the App can subscribe to (an `event Action? RegistryChanged;` raised by a public `void NotifyRegistryChanged() => RegistryChanged?.Invoke();`). `MainViewModel` (or MainWindow) subscribes and calls `ReloadModsAsync` on the UI thread. (Keep it minimal — `LauncherService` already exposes `LoadRegistry()`/`ActiveContext()`; the orchestrator re-reads via the provider, this event just repaints the UI.)

- [ ] **Step 2: the seam adapters + service** (`RestorePointService.cs`):

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.App.Services;

/// <summary>App adapter over the headless RestorePointOrchestrator: supplies %APPDATA% paths, the
/// DPAPI nexus gate, the Process probe, and the LauncherService-backed game provider; renders + writes
/// the off-boarding sheet into each game folder after a clear. The orchestrator does the law-bound work.</summary>
public sealed class RestorePointService
{
    private readonly RestorePointOrchestrator _orch;
    private readonly LauncherService _launcher;

    public RestorePointService(LauncherService launcher, NexusService nexus)
    {
        _launcher = launcher;
        var dataRoot = LauncherService.DataRoot;                     // %APPDATA%\ModManagerBuilder
        var rpRoot = Path.Combine(dataRoot, "restore-points");
        var version = typeof(RestorePointService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _orch = new RestorePointOrchestrator(dataRoot, rpRoot, version,
            new LauncherGameProvider(launcher), new NexusGate(nexus), new GameProcessProbe());
    }

    private static string Ts() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");   // the App owns the clock

    public async Task<SafeClearResult> SafeClearAsync(SafeClearOptions opts, CancellationToken ct = default)
    {
        var result = await _orch.SafeClearAsync(opts, Ts(), ct);
        if (result.Ok && opts.CreateRestorePoint && result.RestorePointTimestamp is not null)
            WriteOffBoardingSheets(result.RestorePointTimestamp);     // best-effort, after the clear
        return result;
    }

    public Task<RestoreResult> RestoreAsync(string timestamp, CancellationToken ct = default) => _orch.RestoreAsync(timestamp, ct);
    public IReadOnlyList<RestorePointInfo> ListRestorePoints() => _orch.ListRestorePoints();
    public void DeleteRestorePoint(string ts) => _orch.DeleteRestorePoint(ts);
    public InterruptedClear? DetectInterruptedClear() => _orch.DetectInterruptedClear();
    public void DiscardPartial(string ts) => _orch.DiscardPartial(ts);

    // Render each game's off-boarding sheet and write it into the game root at the SEALED path.
    // Snapshot a colliding file via ReplacedStore first (reversible), then atomic temp+rename. Best-effort:
    // a read-only/locked game folder must NOT fail the (already-sealed) clear — log + skip.
    private void WriteOffBoardingSheets(string timestamp)
    {
        var rpDir = Path.Combine(LauncherService.DataRoot, "restore-points", timestamp);
        var manifest = RestorePointManifestStore.Read(rpDir);
        if (manifest is null) return;
        foreach (var ga in manifest.Games)
        {
            if (ga.OffboardingSheetGameFolderPath is null) continue;
            try
            {
                var launchLines = LaunchScan.Detect(ga.GameRoot, /*engine*/ null, /*steamAppId*/ null)
                    .Targets.Select(t => t.Label).ToList();          // see Task 5 note: thread engine/appId if available
                var report = OffBoardingHydrator.Hydrate(ga, rpDir, launchLines);
                var text = OffBoardingSheet.Render(report);
                var sheetPath = ga.OffboardingSheetGameFolderPath;
                if (File.Exists(sheetPath))
                {
                    var batch = ReplacedStore.NewBatch(Path.Combine(rpDir, "games", ga.Id, "replaced-sheet"));
                    var rel = Path.GetFileName(sheetPath);
                    ReplacedStore.Backup(sheetPath, rel, batch);
                    ReplacedStore.WriteManifest(batch, new[] { new ReplacedEntry(sheetPath, rel, DateTime.UtcNow) });
                }
                AtomicJson.WriteTextAtomic(sheetPath, text);
            }
            catch { /* best-effort: the authoritative sheet is already in the restore point */ }
        }
    }

    private sealed class NexusGate(NexusService nexus) : INexusGate
    {
        public bool IsConnected => nexus.IsConnected;
        public void DeleteStoredKey() => nexus.Disconnect();   // deletes nexus.json
    }

    private sealed class LauncherGameProvider(LauncherService launcher) : IGameProvider
    {
        public IReadOnlyList<GameEntry> Games => launcher.LoadRegistry().Games;
        public GameContext ContextFor(GameEntry game) => Scanner.GameContext(game);
        public void Reload() => launcher.NotifyRegistryChanged();   // re-read happens via LoadRegistry; this repaints the UI
    }
}
```

- [ ] **Step 3: DI** — in `App.xaml.cs` ConfigureServices, add `services.AddSingleton<RestorePointService>();` (after the other service singletons, before `Build()`).

- [ ] **Step 4: build the App project** — `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Expect: builds clean. Fix any signature mismatch against the real `LaunchScan.Detect` / `ReplacedStore` / `AtomicJson` (the grounded signatures are above; verify on build). **Note:** `LaunchScan.Detect(gameRoot, engine, steamAppId)` needs the engine + steamAppId — thread them from the `GameEntry` (the App can look the game up by `ga.Id` in the registry to get `Engine`/`SteamAppId`). Adjust the launch-line hydration accordingly.

- [ ] **Step 5: Commit** `feat(restore): App RestorePointService adapter + seams + off-boarding sheet write`.

---

## Task 3: Safe Clear dialog

**Files:** Create `src/ModManager.App/SafeClearDialog.xaml` + `.xaml.cs` (model on `AddGameDialog`). Modify `SettingsDialog` (or `MainWindow` toolbar) to add the entry point.

- [ ] **Step 1: `SafeClearDialog.xaml`** — a `ContentDialog` (Title "Reset launcher", PrimaryButtonText "Clear", CloseButtonText "Cancel", `DefaultButton="Close"`). Content: `StackPanel Spacing=12 Width=440`:
  - A reassurance `TextBlock`: "Your mods are kept. This archives your setup to a restore point you can return to, then resets the launcher to first-run."
  - **End-state** `ComboBox` (house style — no RadioButtons): items "Return to vanilla (restorable)" / "Leave mods active". (Per-game override is a later nicety; v1 applies the choice to all games.)
  - **Create restore point** `CheckBox` (`IsChecked=True`).
  - **Keep Nexus connection** `ToggleSwitch` (`IsOn=True`) — show it only if Nexus `IsConnected` (pass that in).
  - **Pre-flight area:** a `TextBlock`/`InfoBar` populated on load (run the pre-flight: free space, game-running, drives) — if blocked, disable the Clear button + show the reason. (Reuse the orchestrator's refusal: simplest is to *attempt* and surface `SafeClearResult.RefusedReason`, but better UX is a pre-check; for v1, run `SafeClearAsync` on confirm and show the refusal in an `InfoBar` if `!Ok`, keeping the dialog open.)
- [ ] **Step 2: `SafeClearDialog.xaml.cs`** — constructor `(IntPtr hwnd, RestorePointService svc, bool nexusConnected)`. `BuildOptions()` reads the ComboBox/CheckBox/ToggleSwitch into a `SafeClearOptions`. The OnPrimary handler (or the caller) calls `svc.SafeClearAsync(options)`; on `!Ok`, set `args.Cancel = true` and show `RefusedReason` in the InfoBar (keep dialog open); on Ok, close + trigger the UI refresh (the orchestrator's `Reload` already fired).
- [ ] **Step 3: entry point** — add a "Reset launcher…" button in `SettingsDialog` (a new section, or near the bottom) that opens `SafeClearDialog`; or a toolbar overflow item in `MainWindow`. Follow the `OnSettings` open pattern (`MainWindow.xaml.cs:405-424`): `hwnd` + `XamlRoot = Content.XamlRoot`.
- [ ] **Step 4: build** the App project (`-p:Platform=x64`). **Step 5: Commit** `feat(restore): Safe Clear dialog + entry point`.

---

## Task 4: Settings → Restore points management

**Files:** Modify `src/ModManager.App/SettingsDialog.xaml` + `.xaml.cs`.

- [ ] Add a "Restore points" section modeled exactly on the "Installed frameworks" section (`SettingsDialog.xaml:124-152`): a header `TextBlock`, an empty-state `TextBlock`, and an `ItemsRepeater` bound to a `RestorePointRow` record (`record RestorePointRow(string Timestamp, string Games, string Size, string Id)`), `DataTemplate` = a `Grid` with name/detail (timestamp + comma-joined games + size) and two buttons: **Restore** (`Tag={x:Bind Id}`, Click → confirm → `svc.RestoreAsync(id)` → on conflicts, show them; on Ok, refresh) and **Delete** (`Tag={x:Bind Id}`, Click → confirm → `svc.DeleteRestorePoint(id)` → re-seed the list).
- [ ] Seed it: `SettingsDialog` constructor takes `RestorePointService` (or resolve from `App.AppHost`); a `RefreshRestorePoints()` sets `RestorePointsList.ItemsSource = svc.ListRestorePoints().Select(i => new RestorePointRow(i.Timestamp, string.Join(", ", i.GameNames), FormatSize(i.TotalBytes), i.Timestamp)).ToList();` Call it in the constructor + after a delete.
- [ ] build (`-p:Platform=x64`). **Commit** `feat(restore): Settings restore-point list (restore/delete)`.

---

## Task 5: Startup interrupted-clear recovery

**Files:** Modify `src/ModManager.App/MainWindow.xaml.cs` (`OnFirstActivated`).

- [ ] In `OnFirstActivated` (before `ViewModel.LoadAsync()`), resolve `RestorePointService`, call `DetectInterruptedClear()`. If non-null:
  - `Sealed == true`: a clear completed capture but crashed during mutate/reset → show a `ContentDialog`: "A reset was interrupted, but your setup was safely archived. Restore it?" → Restore (`RestoreAsync(ts)`) / Keep cleared (dismiss). **Copy must be honest** — "restore your saved setup," not "undo" (the final review's note: restore recovers the saved state, it doesn't surgically reverse a half-finished mutate).
  - `Sealed == false`: died before the seal → original is intact → "A reset didn't finish. Discard the incomplete archive?" → Discard (`DiscardPartial(ts)`) / Keep.
  - Then proceed to `LoadAsync()`.
- [ ] build (`-p:Platform=x64`). **Commit** `feat(restore): startup interrupted-clear recovery prompt`.

---

## Task 6: Build gate + smoke checklist

- [ ] **Full App build:** `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` — clean. And the Core suite stays green (`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — the `ProcessNameMatch` tests + everything else).
- [ ] **Append to `docs/smoke-tests/pending.md`** the manual end-to-end checklist (the parts no unit test covers):
  - Two-drive machine (game on D:, `%APPDATA%` on C:): Settings → Reset launcher → Return to vanilla, keep restore point, keep Nexus → Clear. Verify: game folder is vanilla + launchable; `626-launcher-how-to-launch.txt` written at the game root with the correct launch instructions + source URLs; launcher shows empty-state; Nexus still connected.
  - Settings → Restore points → the new point is listed → Restore → game folder + registry come back; mods present; the in-folder sheet removed.
  - Keep-Nexus OFF → after clear, Nexus disconnected (`nexus.json` gone), and never in the archive.
  - Game running → Clear refused with "close <game> first."
  - Kill the app mid-clear (or simulate a leftover `safe-clear.lock`) → relaunch → recovery prompt (sealed → restore offered; unsealed → discard offered).
  - Leave-mods-active end-state → game stays modded + launchable; restore brings the launcher view back.
- [ ] **Commit** `test(smoke): Safe Clear + Restore manual checklist`.

---

## Self-Review

**Spec coverage:** RestorePointService adapter + seams (Task 2), game-running probe (Task 1), off-boarding sheet write with snapshot-on-collision (Task 2), Safe Clear dialog with end-state/create-RP/keep-Nexus + pre-flight (Task 3), Settings restore-point list + restore/delete (Task 4), startup interrupted-clear recovery (Task 5), build gate + smoke (Task 6). Onboarding wizard = Phase 2.

**Testability honesty:** only `ProcessNameMatch` is unit-tested (Core, pure). Everything else is WinUI — verified by **compilation** (`dotnet build` the App csproj) + **manual smoke**. This is the inherent ceiling of the WinUI layer; the law-bound logic was all proven headless in Phase 1A/1B-1.

**Type consistency:** `RestorePointService` calls the merged orchestrator (`SafeClearAsync`/`RestoreAsync`/`ListRestorePoints`/`DeleteRestorePoint`/`DetectInterruptedClear`/`DiscardPartial`) and engine (`RestorePointManifestStore.Read`, `OffBoardingHydrator.Hydrate`, `OffBoardingSheet.Render`) + App primitives (`LaunchScan.Detect`, `ReplacedStore`, `AtomicJson.WriteTextAtomic`, `NexusService.Disconnect`, `LauncherService.LoadRegistry`) — all real signatures from the grounded surface. The seams (`INexusGate`/`IGameRunningProbe`/`IGameProvider`) match the orchestrator contracts.

**Open implementation note (resolve on build):** `LaunchScan.Detect` needs `engine`+`steamAppId` — the off-boarding hydration in Task 2 must look the game up in the registry by `ga.Id` to pass them (or thread the `GameEntry` through). Verify on the Task 2 build.

## Next: Phase 2 (onboarding)

The wizard (welcome + add-first-game via Steam detect + suggested-Nexus skipping if `IsConnected` + personalize + drop-a-mod tutorial + "Restore a previous setup") triggered first-run AND after a clear (via `last-clear.json`), id-aware restore offer (via `RESTORE-AVAILABLE.json` + the deterministic game-id), re-runnable from Settings. That closes the original ask: the painless new-user onboarding, on top of a reset engine that's reversible by law.
