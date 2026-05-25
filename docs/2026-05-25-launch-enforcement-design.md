# Launch enforcement (required launcher) — design

- **Date:** 2026-05-25
- **Status:** Approved (shape confirmed with Este)
- **Roadmap:** Phase B4 in [docs/2026-05-25-backlog-roadmap.md](2026-05-25-backlog-roadmap.md).
- **Why:** Some mods only work through their own launcher (Seamless Co-op → `ersc_launcher.exe`);
  launching vanilla with them enabled is a silently-broken state ("don't let the mod be picked and
  the launcher not used"). The pieces exist — `GameEntry.RequiredLauncher` (from the agentic
  profile), the detected `LaunchTargets`, the `SeamlessNeedsLauncher` hint — but nothing *enforces*
  using the launcher. This adds the enforcement.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Where the requirement lives | **Game-level** (`GameEntry.RequiredLauncher`) for v1. Per-mod requirements are a later refinement. |
| Trigger | Active when `RequiredLauncher` is set **and** ≥1 mod is enabled. Mods off → no friction. |
| Strength | **Confirm-on-vanilla** (not a hard gate): required launcher becomes the default Play; vanilla stays selectable but confirms first. Steers hard, keeps the escape hatch. |

## Architecture (pure-core / thin-shell)

### Core — `LaunchGuard` (pure, unit-tested)

```csharp
public static class LaunchGuard
{
    /// <summary>True when the game declares a required launcher and at least one mod is enabled —
    /// the required launcher should be the default Play target.</summary>
    public static bool RequiresLauncher(GameEntry game, bool anyModsEnabled)
        => anyModsEnabled && !string.IsNullOrEmpty(game.RequiredLauncher);

    /// <summary>True when launching <paramref name="target"/> should prompt a confirm first —
    /// i.e. enforcement is active and the target is a vanilla/steam launch (not the launcher).</summary>
    public static bool NeedsVanillaConfirm(GameEntry game, bool anyModsEnabled, LaunchTarget target)
        => RequiresLauncher(game, anyModsEnabled) && target.Kind != "exe"; // steam/vanilla
}
```

The decision logic stays headless + tested; the App acts on the verdict. (`target.Kind` is "steam"
for vanilla and "exe" for a launcher target — matches the existing `LaunchTarget` shape.)

### App — enforcement in the launch flow

- **Default Play** (the main Launch button / `LaunchCommand`): when `LaunchGuard.RequiresLauncher`
  is true, launch via the **required launcher** — resolve `Path.Combine(GameRoot, RequiredLauncher)`
  (relative path; reuse the resolver/`PlayFolder` logic), run as an exe with its own folder as the
  working dir (the way the existing Seamless exe target launches). If the launcher exe is **missing**,
  show the existing needs-launcher hint instead of attempting to launch a non-existent file.
- **Picking a vanilla/steam target** (from the Launch dropdown or the default) while enforcement is
  active → `LaunchGuard.NeedsVanillaConfirm` is true → show a `ContentDialog`:
  > "Your enabled mods/co-op won't load through a vanilla launch.
  > **[Use <launcher name>]** · **[Launch vanilla anyway]** · **[Cancel]**"

  "Use launcher" → launch the required launcher; "vanilla anyway" → proceed with the vanilla target.
  The dropdown still lists every target — enforcement is on **execution** (confirm), not by hiding
  options.

## Data flow

```
Launch action (button or dropdown target)
  -> anyModsEnabled = Mods.Any(m => m.Enabled)
  -> LaunchGuard.RequiresLauncher? -> default Play = required launcher (resolved)
  -> target is vanilla AND NeedsVanillaConfirm? -> confirm dialog
       Use launcher -> run <GameRoot>\<RequiredLauncher>
       Vanilla anyway -> run the vanilla/steam target
  -> else -> launch as today
```

## Composes with existing pieces

Builds on `LaunchTargets`, `MainViewModel.LaunchTargetExplicit` / `LaunchCommand`, the Launch
dropdown (`OnLaunchMenuOpening`/`OnLaunchTargetClick`), and the `DirectInjectService.SeamlessNeedsLauncher`
hint. `RequiredLauncher` (the explicit field) drives enforcement; when it's null, launch behavior is
exactly as today — no regression for games without a required launcher.

## Error handling

- Required launcher exe missing → the needs-launcher hint; never launch a non-existent exe.
- Launch failure → surfaced via `StatusText` (existing pattern).
- `RequiredLauncher` set but resolves outside `GameRoot` (bad/manual value) → treat as not-present
  (don't launch it); the relative-path validation from the agentic profile already guards new ones.

## Scope / limits (v1)

- **Game-level** required launcher; the trigger is "any mod enabled" (crude but correct for the
  Seamless case). Per-mod "this mod needs launcher X" is a later refinement.
- **Confirm**, not hard-gate — vanilla stays reachable behind one confirm.
- One required launcher per game.

## Testing (test-first, pure Core)

`tests/ModManager.Tests/LaunchGuardTests.cs`:
1. `RequiresLauncher` true when `RequiredLauncher` set + mods enabled; false when mods off; false
   when `RequiredLauncher` null.
2. `NeedsVanillaConfirm` true for a steam/vanilla target when enforcement active; false for an exe
   (launcher) target; false when enforcement inactive.

App default-target swap + the confirm dialog are build-verified + a live smoke test (enable a mod on
a game with a required launcher → Launch defaults to the launcher; pick vanilla → confirm fires).

## File structure

- Create: `src/ModManager.Core/LaunchGuard.cs` — the pure verdict helpers.
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — default Play uses the required launcher
  when active; expose what the launch handlers need (the resolved launcher target + `anyModsEnabled`).
- Modify: `src/ModManager.App/MainWindow.xaml.cs` — vanilla-target confirm dialog in the launch path.
- Tests: `tests/ModManager.Tests/LaunchGuardTests.cs`.
