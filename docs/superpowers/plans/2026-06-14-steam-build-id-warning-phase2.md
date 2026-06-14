# Steam build-id warning — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Warn the user when Steam has updated a game since they last modded it ("Steam updated <game> — your installed mods may need rechecking"), reading the truth automatically from Steam's `buildid` instead of asking the user to track versions by hand.

**Architecture:** Persist a per-game baseline build (`LastKnownSteamBuildId` on `GameEntry`). On game load, read the live Steam `buildId` (already parsed in Phase 1) for the active game and run a pure Core three-way comparator. First sight sets the baseline silently (no warning); a changed build raises a VM-driven, dismissible banner; dismiss re-baselines to the live build. Additive + reversible — an old registry reads the field as null = no baseline = no false warning.

**Tech Stack:** .NET 10, C#, WinUI 3 (App), xUnit (Core). Pure-Core / thin-App split (`CorePurityTests`).

**Spec:** [docs/superpowers/specs/2026-06-14-richer-steam-detection-design.md](../specs/2026-06-14-richer-steam-detection-design.md) (Phase 2).

**Design decision (baseline UX):** silent baseline on first sight → warn when the live build later differs → "dismiss" re-baselines. Chosen for: no false-warn on a never-seen game, no hook into the mod-apply flow, banner clears when the user confirms. Approved by the maintainer.

---

## File Structure

**Modify (Core):**
- `src/ModManager.Core/GameEntry.cs` — add `LastKnownSteamBuildId` (string?) to `GameEntry`.

**Create (Core):**
- `src/ModManager.Core/SteamBuildCheck.cs` — pure three-way comparator + `SteamBuildStatus` enum.

**Modify (App):**
- `src/ModManager.App/Services/LauncherService.cs` — add `SetSteamBuildBaseline(gameId, buildId)` (mutate-then-Save, mirroring `SetSaveDir`).
- `src/ModManager.App/ViewModels/MainViewModel.cs` — compute status in `ReloadModsAsync`; observable banner state + dismiss command.
- `src/ModManager.App/MainWindow.xaml` — the dismissible banner bound to the VM.

**Test:**
- `tests/ModManager.Tests/Persistence/RegistryStoreTests.cs` — round-trip the new field (camelCase).
- `tests/ModManager.Tests/SteamBuildCheckTests.cs` — the comparator's three-way decision.

---

## Task 1: Persist `LastKnownSteamBuildId` on `GameEntry`

**Files:**
- Modify: `src/ModManager.Core/GameEntry.cs` (after the existing optional props, ~line 73)
- Test: `tests/ModManager.Tests/Persistence/RegistryStoreTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

Add to `RegistryStoreTests.cs`, mirroring the existing `Save_then_Load_round_trips_as_camelCase` pattern (raw-JSON camelCase guard + load equality):

```csharp
[Fact]
public void GameEntry_lastKnownSteamBuildId_round_trips_as_camelCase()
{
    var dir = TestSupport.TempDir("regstore-buildid-");
    var reg = new GameRegistry { Games = { new GameEntry { Id = "g1", GameName = "G1", LastKnownSteamBuildId = "17556649" } } };

    RegistryStore.Save(dir, reg);

    var json = File.ReadAllText(RegistryStore.PathFor(dir));
    Assert.Contains("\"lastKnownSteamBuildId\"", json);
    Assert.DoesNotContain("\"LastKnownSteamBuildId\"", json);

    var loaded = RegistryStore.Load(dir);
    Assert.Equal("17556649", loaded.Games[0].LastKnownSteamBuildId);
}
```

(Match the existing test's `using`s / `TestSupport.TempDir` helper already used in the file.)

- [ ] **Step 2: Run it — expect FAIL**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~lastKnownSteamBuildId"`
Expected: FAIL — `GameEntry` has no `LastKnownSteamBuildId`.

- [ ] **Step 3: Add the field**

In `GameEntry.cs`, after the existing optional properties (e.g. after `NexusGameDomain`), add:

```csharp
// Steam buildid recorded the last time we saw this game (the "modded against" baseline). When the
// live Steam build later differs, the launcher warns that an update may have broken installed mods.
// Null = no baseline yet (e.g. an old registry) = no warning.
public string? LastKnownSteamBuildId { get; set; }
```

- [ ] **Step 4: Run it — expect PASS**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistryStore"`
Expected: PASS (all RegistryStore tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/GameEntry.cs tests/ModManager.Tests/Persistence/RegistryStoreTests.cs
git commit -m "feat(steam): persist lastKnownSteamBuildId baseline on GameEntry"
```

---

## Task 2: Pure build comparator (`SteamBuildCheck`)

**Files:**
- Create: `src/ModManager.Core/SteamBuildCheck.cs`
- Test: `tests/ModManager.Tests/SteamBuildCheckTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ModManager.Tests/SteamBuildCheckTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class SteamBuildCheckTests
{
    [Fact]
    public void Unknown_when_no_live_build() // not a Steam game / no manifest
    {
        Assert.Equal(SteamBuildStatus.Unknown, SteamBuildCheck.Evaluate("123", null));
        Assert.Equal(SteamBuildStatus.Unknown, SteamBuildCheck.Evaluate(null, ""));
    }

    [Fact]
    public void NoBaseline_when_baseline_missing_but_live_present()
        => Assert.Equal(SteamBuildStatus.NoBaseline, SteamBuildCheck.Evaluate(null, "17556649"));

    [Fact]
    public void Unchanged_when_equal()
        => Assert.Equal(SteamBuildStatus.Unchanged, SteamBuildCheck.Evaluate("17556649", "17556649"));

    [Fact]
    public void Updated_when_live_differs_from_baseline()
        => Assert.Equal(SteamBuildStatus.Updated, SteamBuildCheck.Evaluate("17556649", "17600000"));
}
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamBuildCheck"`
Expected: FAIL — `SteamBuildCheck` / `SteamBuildStatus` don't exist.

- [ ] **Step 3: Implement**

`src/ModManager.Core/SteamBuildCheck.cs`:

```csharp
namespace ModManager.Core;

/// <summary>Outcome of comparing a game's recorded baseline build to its current Steam build.</summary>
public enum SteamBuildStatus
{
    /// <summary>No live build to compare (not a Steam game / appmanifest had no buildid).</summary>
    Unknown,
    /// <summary>No baseline recorded yet — the caller should set it silently (no warning).</summary>
    NoBaseline,
    /// <summary>Live build matches the baseline — nothing changed.</summary>
    Unchanged,
    /// <summary>Live build differs from the baseline — Steam updated the game; warn.</summary>
    Updated,
}

/// <summary>Pure three-way decision for the "Steam updated this game under your mods" warning.</summary>
public static class SteamBuildCheck
{
    public static SteamBuildStatus Evaluate(string? baseline, string? live)
    {
        if (string.IsNullOrEmpty(live)) return SteamBuildStatus.Unknown;
        if (string.IsNullOrEmpty(baseline)) return SteamBuildStatus.NoBaseline;
        return baseline == live ? SteamBuildStatus.Unchanged : SteamBuildStatus.Updated;
    }
}
```

- [ ] **Step 4: Run it — expect PASS**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamBuildCheck"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SteamBuildCheck.cs tests/ModManager.Tests/SteamBuildCheckTests.cs
git commit -m "feat(steam): pure build-id comparator (Unknown/NoBaseline/Unchanged/Updated)"
```

---

## Task 3: `LauncherService.SetSteamBuildBaseline`

The mutate-then-Save persistence path, mirroring `SetSaveDir` / `SetAutoBackup` in the same file.

**Files:**
- Modify: `src/ModManager.App/Services/LauncherService.cs`

- [ ] **Step 1: Add the method**

Next to `SetSaveDir` / `SetAutoBackup`, add (match their exact shape — load, find by id, mutate, save):

```csharp
/// <summary>Record the current Steam build as this game's "modded against" baseline. Used to set the
/// baseline silently on first sight and to re-baseline when the user dismisses the update warning.</summary>
public void SetSteamBuildBaseline(string gameId, string? buildId)
{
    var reg = LoadRegistry();
    var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
    if (g is null) return;
    g.LastKnownSteamBuildId = buildId;
    SaveRegistry(reg);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/LauncherService.cs
git commit -m "feat(steam): LauncherService.SetSteamBuildBaseline (persist the modded-against build)"
```

---

## Task 4: Compute the warning in `MainViewModel`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

Read the file first. `ReloadModsAsync` (~line 321) sets `_ctx = _svc.ActiveContext()` (~323, early-returns if null at ~325) and computes `LaunchNeedsAttention` (~486-488) — `_ctx.Game`, `_steam` (the injected `SteamService`, which implements `IStoreLibrary`), and `_svc` (`LauncherService`) are all in scope there.

- [ ] **Step 1: Add banner state + the dismiss command**

Near the other `[ObservableProperty]` banner fields (e.g. `launchNeedsAttention` ~142), add:

```csharp
// Steam updated this game since we last recorded its build — installed mods may need rechecking.
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(SteamBuildWarningVisibility))]
private bool steamBuildChanged;

[ObservableProperty] private string steamBuildMessage = "";

// The live build to re-baseline to when the user dismisses the warning.
private string? _pendingSteamBuild;
```

Add the computed visibility next to `LaunchHintVisibility` (~146):

```csharp
public Visibility SteamBuildWarningVisibility => SteamBuildChanged ? Visibility.Visible : Visibility.Collapsed;
```

Add the dismiss command (re-baseline + clear), near the other `[RelayCommand]`s:

```csharp
[RelayCommand]
private void DismissBuildWarning()
{
    if (_ctx?.Game is null) return;
    _svc.SetSteamBuildBaseline(_ctx.Game.Id, _pendingSteamBuild);
    _ctx.Game.LastKnownSteamBuildId = _pendingSteamBuild;   // keep in-memory baseline in sync
    SteamBuildChanged = false;
}
```

- [ ] **Step 2: Compute the status during reload**

In `ReloadModsAsync`, right after the `LaunchNeedsAttention` assignment (~488, where `_ctx.Game` is in scope and non-null), add:

```csharp
// Build-id watch: warn when Steam updated this game since we last recorded its build. First sight
// records the baseline silently; the pure comparator decides. _steam.InstalledGames() is a local
// Steam scan (no network) and matches the active game by app id.
var liveBuild = InstalledGameMatch.ByAppId(_steam.InstalledGames(), _ctx.Game.SteamAppId)?.BuildId;
switch (SteamBuildCheck.Evaluate(_ctx.Game.LastKnownSteamBuildId, liveBuild))
{
    case SteamBuildStatus.NoBaseline:
        _svc.SetSteamBuildBaseline(_ctx.Game.Id, liveBuild);
        _ctx.Game.LastKnownSteamBuildId = liveBuild;
        SteamBuildChanged = false;
        break;
    case SteamBuildStatus.Updated:
        _pendingSteamBuild = liveBuild;
        SteamBuildMessage = $"Steam updated {_ctx.Game.GameName} since you last modded it — your installed mods may need rechecking.";
        SteamBuildChanged = true;
        break;
    default: // Unchanged / Unknown
        SteamBuildChanged = false;
        break;
}
```

Confirm `using ModManager.Core;` is present (it is — `InstalledGameMatch`, `SteamBuildCheck`, `SteamBuildStatus` live there). If `_steam`'s field name differs, use the actual injected `SteamService`/`IStoreLibrary` field (grounded as `_steam`).

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(steam): compute the build-update warning on game load + dismiss re-baselines"
```

---

## Task 5: The banner in `MainWindow.xaml`

**Files:**
- Modify: `src/ModManager.App/MainWindow.xaml`

Read the file first. Mirror the existing banner style (the `VortexBannerArea` Borders ~line 299, or the launch-hint banners) — a warning-toned `Border` with a message `TextBlock` and a dismiss `Button`, its `Visibility` bound to the VM. Place it in the banner stack/row where the other top-of-list banners live so it sits above the mod list.

- [ ] **Step 1: Add the banner**

Add (adapt the exact brushes/placement to match the sibling banners in the file):

```xml
<Border Visibility="{x:Bind ViewModel.SteamBuildWarningVisibility, Mode=OneWay}"
        Background="{ThemeResource SystemFillColorCautionBackgroundBrush}"
        BorderBrush="{ThemeResource SystemFillColorCautionBrush}" BorderThickness="1"
        CornerRadius="4" Padding="10,6" Margin="12,6,12,0">
    <Grid ColumnSpacing="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <FontIcon Grid.Column="0" Glyph="&#xE7BA;" FontSize="14" VerticalAlignment="Center" />
        <TextBlock Grid.Column="1" Text="{x:Bind ViewModel.SteamBuildMessage, Mode=OneWay}"
                   TextWrapping="Wrap" VerticalAlignment="Center" />
        <Button Grid.Column="2" Content="Mods rechecked" VerticalAlignment="Center"
                Command="{x:Bind ViewModel.DismissBuildWarningCommand}" />
    </Grid>
</Border>
```

(If the sibling banners use specific brush resources, match those instead of the `SystemFillColorCaution*` defaults so it's visually consistent.)

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml
git commit -m "feat(steam): build-update warning banner bound to the view-model"
```

---

## Task 6: Smoke checklist + full verification

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Add smoke entries**

Append:

```markdown
## Steam build-update warning (Phase 2)
- Switch to an installed Steam game for the first time on this build: no warning (baseline set silently). Re-switch: still no warning.
- Simulate an update: edit the game's `appmanifest_*.acf` `buildid` to a different value (or let Steam update it), then re-switch to the game in the launcher → the "Steam updated <game> — your installed mods may need rechecking" banner appears.
- Click "Mods rechecked" → banner clears and stays cleared on the next switch (baseline re-recorded). Confirm `games.json` shows the new `lastKnownSteamBuildId`.
- A non-Steam game (no app id) never shows the banner.
```

- [ ] **Step 2: Full gate**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all pass (incl. `CorePurityTests` — `SteamBuildCheck` is pure Core).
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): Steam build-update warning checklist"
```

---

## Self-Review

- **Spec coverage:** persisted baseline field (Task 1) ✓; pure comparator (Task 2) ✓; re-baseline persistence (Task 3) ✓; compute-on-load + silent-baseline + warn + dismiss-re-baselines (Task 4) ✓; banner UI (Task 5) ✓. Matches the approved baseline-UX decision.
- **Reversibility:** additive nullable field; old registries read null → `NoBaseline` → silent set, no false warning. No destructive ops.
- **camelCase:** the one persisted field ships with the string-contains round-trip test (Task 1).
- **Pure-Core:** `SteamBuildCheck` + the field are Core; IO (`InstalledGames`, registry save) and the banner are App. `CorePurityTests` covers it.
- **Type consistency:** `SteamBuildStatus` enum (Task 2) consumed in Task 4's switch; `SteamBuildCheck.Evaluate(baseline, live)` (Task 2) called in Task 4; `InstalledGameMatch.ByAppId` + `InstalledGame.BuildId` (Phase 1) used in Task 4; `LauncherService.SetSteamBuildBaseline(gameId, buildId)` (Task 3) called in Task 4's compute + dismiss; `SteamBuildWarningVisibility` / `SteamBuildMessage` / `DismissBuildWarningCommand` (Task 4) bound in Task 5.
- **Placeholders:** none — Core code is complete; App tasks carry concrete code + the one grounded insertion point (`ReloadModsAsync` ~488), with implementers confirming the exact line/field names against source.
