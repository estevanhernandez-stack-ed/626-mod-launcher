# Ban-safe loaders + loaders-in-the-bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Per-task implement → review → fix gated, then a whole-branch review. Steps use `- [ ]`.

**Goal:** Surface launchable, anti-cheat-safe mod loaders (Mod Engine 2, Seamless Co-op) as one-click buttons in the bar when detected in the game folder, and make the ban-risk warning point at those safe loaders instead of dead-ending.

**Architecture:** One focused new Core unit — `KnownLoader` (a small catalog of loaders that have a distinct launcher exe + a `BanSafe` flag) + `LoaderScan` (detect the launcher in the game's play folder) + `BanSafeLoaders` (resolve a game's safe loaders). The App surfaces detected loaders as launch buttons (reusing the `ToolLauncher` Process.Start path) and the ban-risk gate dialog lists the game's safe loaders. No new launch plumbing; centralizing loader knowledge in `KnownLoader` avoids sprinkling `BanSafe` across the four existing catalogs.

**Tech Stack:** .NET 10 / C# (Core: pure + xUnit-tested; App: WinUI 3). Both flavors (no `#if FULL`).

## Global Constraints

- **Both STORE and FULL** — no `#if FULL` on any of this. On STORE the safe-loader guidance is the *primary* safe path (the EAC offline toggle is FULL-only, stripped from STORE). Verify the STORE build still seals (`scripts/check-store-seal.ps1`).
- **No bundled loader binaries** — `KnownLoader` carries detection hints + a `GetUrl` only. The user installs the loader; we detect + launch it. Never ship the binary (the NOTICE law).
- **No silent anti-cheat disabling** — this adds *guidance*; the ban-risk warning + acknowledgment (`BanRiskRules.ShouldGateEnable`, `BanRiskAckStore`) stay exactly as they are. Never auto-act.
- **Reversibility untouched** — launching a loader is read-only (Process.Start); snapshot saves first only where the loader is known to touch saves (mirror `ToolLauncher`'s `EditsSaves` pattern).
- **camelCase JSON on disk** if any new persisted shape (none expected — `KnownLoader` is a compiled catalog, not persisted).
- **Never bare `dotnet` at the repo root.** Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (FULL) and add `-p:Configuration=Store` (STORE); kill `ModManager.App` first. Conventional commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Branch `feat/ban-safe-loaders`.

---

## Task 1: `KnownLoader` catalog (Core)

**Files:**
- Create: `src/ModManager.Core/Loaders/KnownLoader.cs`
- Test: `tests/ModManager.Tests/Loaders/KnownLoaderCatalogTests.cs`

**Interfaces:**
- Produces: `record KnownLoader(string LoaderId, string DisplayName, string Engine, string? SteamAppId, IReadOnlyList<string> LauncherExeNames, string GetUrl, string Author, bool BanSafe, bool EditsSaves = false)` and `static class KnownLoaderCatalog { static IReadOnlyList<KnownLoader> Catalog { get; } }`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.Loaders;

namespace ModManager.Tests.Loaders;

public class KnownLoaderCatalogTests
{
    [Fact]
    public void Catalog_has_modengine2_and_seamless_both_ban_safe_for_elden_ring()
    {
        var c = KnownLoaderCatalog.Catalog;
        var me2 = Assert.Single(c, x => x.LoaderId == "mod-engine-2");
        Assert.True(me2.BanSafe);
        Assert.Equal("fromsoft", me2.Engine);
        Assert.Contains("modengine2_launcher.exe", me2.LauncherExeNames);

        var sc = Assert.Single(c, x => x.LoaderId == "seamless-coop");
        Assert.True(sc.BanSafe);
        Assert.Contains("launch_elden_ring_seamlesscoop.exe", sc.LauncherExeNames);
    }

    [Fact]
    public void Every_loader_has_a_get_url_and_at_least_one_launcher_exe()
    {
        Assert.All(KnownLoaderCatalog.Catalog, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.GetUrl));
            Assert.NotEmpty(l.LauncherExeNames);
        });
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter Loaders` → FAIL (KnownLoader not defined).
- [ ] **Step 3: Implement `src/ModManager.Core/Loaders/KnownLoader.cs`:**

```csharp
namespace ModManager.Core.Loaders;

/// <summary>A mod loader with a DISTINCT launcher exe the launcher can detect in the game's play
/// folder and surface as a one-click "Launch via X" button. <see cref="BanSafe"/> marks loaders whose
/// modding path avoids the game's anti-cheat (Mod Engine 2 loads mods without touching the EAC surface;
/// Seamless Co-op runs its own multiplayer). Metadata + a Get-it-here URL only — the binary is never
/// bundled.</summary>
public sealed record KnownLoader(
    string LoaderId,
    string DisplayName,
    string Engine,
    string? SteamAppId,
    IReadOnlyList<string> LauncherExeNames,
    string GetUrl,
    string Author,
    bool BanSafe,
    bool EditsSaves = false);

public static class KnownLoaderCatalog
{
    public static IReadOnlyList<KnownLoader> Catalog { get; } = new[]
    {
        new KnownLoader(
            LoaderId: "mod-engine-2",
            DisplayName: "Mod Engine 2",
            Engine: "fromsoft",
            SteamAppId: "1245620",                          // Elden Ring; the canonical ME2 target
            LauncherExeNames: new[] { "modengine2_launcher.exe" },
            GetUrl: "https://github.com/soulsmods/ModEngine2/releases",
            Author: "soulsmods (ModEngine2)",
            BanSafe: true),                                  // loads mods without touching EAC
        new KnownLoader(
            LoaderId: "seamless-coop",
            DisplayName: "Seamless Co-op",
            Engine: "fromsoft",
            SteamAppId: "1245620",
            LauncherExeNames: new[] { "launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe" },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/510",
            Author: "LukeYui",
            BanSafe: true),                                  // ships its own MP, bypasses EAC
    };
}
```

- [ ] **Step 4: Run the test, verify it passes** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter Loaders` → PASS.
- [ ] **Step 5: Commit** — `feat(loaders): KnownLoader catalog (Mod Engine 2 + Seamless Co-op, ban-safe)`.

## Task 2: `LoaderScan` + `BanSafeLoaders` resolver (Core)

**Files:**
- Create: `src/ModManager.Core/Loaders/LoaderScan.cs`
- Test: `tests/ModManager.Tests/Loaders/LoaderScanTests.cs`

**Interfaces:**
- Consumes: `KnownLoader`, `KnownLoaderCatalog` (Task 1).
- Produces: `record DetectedLoader(KnownLoader Loader, string LauncherPath)` and
  `static class LoaderScan { IReadOnlyList<DetectedLoader> Detect(string? playFolder, string engine, string? steamAppId); IReadOnlyList<KnownLoader> BanSafeFor(string engine, string? steamAppId); }`.
  `Detect` returns the catalog loaders whose launcher exe exists in `playFolder` (scoped by engine; steamAppId match OR the loader's SteamAppId is null), with the resolved absolute `LauncherPath`. `BanSafeFor` returns the catalog's `BanSafe` loaders scoped to the game (regardless of install state — for the gate's "get it here" path).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ModManager.Core.Loaders;

namespace ModManager.Tests.Loaders;

public class LoaderScanTests
{
    private static string TempPlayFolder(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-loaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x");
        return d;
    }

    [Fact]
    public void Detect_finds_modengine2_when_its_launcher_is_present()
    {
        var dir = TempPlayFolder("modengine2_launcher.exe");
        try
        {
            var found = LoaderScan.Detect(dir, "fromsoft", "1245620");
            var d = Assert.Single(found);
            Assert.Equal("mod-engine-2", d.Loader.LoaderId);
            Assert.Equal(Path.Combine(dir, "modengine2_launcher.exe"), d.LauncherPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Detect_returns_empty_for_wrong_engine_or_missing_launcher()
    {
        var dir = TempPlayFolder("eldenring.exe");
        try
        {
            Assert.Empty(LoaderScan.Detect(dir, "fromsoft", "1245620")); // no loader exe present
            Assert.Empty(LoaderScan.Detect(dir, "bethesda", "1245620")); // wrong engine
            Assert.Empty(LoaderScan.Detect(null, "fromsoft", "1245620")); // null play folder
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BanSafeFor_lists_the_games_ban_safe_loaders_regardless_of_install()
    {
        var safe = LoaderScan.BanSafeFor("fromsoft", "1245620");
        Assert.Contains(safe, l => l.LoaderId == "mod-engine-2");
        Assert.Contains(safe, l => l.LoaderId == "seamless-coop");
        Assert.All(safe, l => Assert.True(l.BanSafe));
        Assert.Empty(LoaderScan.BanSafeFor("bethesda", "377160")); // none scoped to Fallout 4
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter Loaders` → FAIL (LoaderScan not defined).
- [ ] **Step 3: Implement `src/ModManager.Core/Loaders/LoaderScan.cs`:**

```csharp
using System.IO;

namespace ModManager.Core.Loaders;

/// <summary>A catalog loader whose launcher exe was found in the play folder.</summary>
public sealed record DetectedLoader(KnownLoader Loader, string LauncherPath);

/// <summary>Pure detection: which KnownLoaders are installed in a game's play folder, and which
/// ban-safe loaders apply to a game. No I/O beyond File.Exists.</summary>
public static class LoaderScan
{
    private static bool Applies(KnownLoader l, string engine, string? steamAppId) =>
        string.Equals(l.Engine, engine, StringComparison.Ordinal)
        && (l.SteamAppId is null || string.Equals(l.SteamAppId, steamAppId, StringComparison.Ordinal));

    public static IReadOnlyList<DetectedLoader> Detect(string? playFolder, string engine, string? steamAppId)
    {
        if (string.IsNullOrWhiteSpace(playFolder) || !Directory.Exists(playFolder))
            return Array.Empty<DetectedLoader>();
        var found = new List<DetectedLoader>();
        foreach (var l in KnownLoaderCatalog.Catalog)
        {
            if (!Applies(l, engine, steamAppId)) continue;
            foreach (var exe in l.LauncherExeNames)
            {
                var p = Path.Combine(playFolder, exe);
                if (File.Exists(p)) { found.Add(new DetectedLoader(l, p)); break; }
            }
        }
        return found;
    }

    public static IReadOnlyList<KnownLoader> BanSafeFor(string engine, string? steamAppId) =>
        KnownLoaderCatalog.Catalog.Where(l => l.BanSafe && Applies(l, engine, steamAppId)).ToList();
}
```

- [ ] **Step 4: Run the test, verify it passes** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter Loaders` → PASS.
- [ ] **Step 5: Run the full Core suite** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all green (no regression).
- [ ] **Step 6: Commit** — `feat(loaders): LoaderScan detection + ban-safe resolver`.

## Task 3: Surface detected loaders as launch buttons in the bar (App)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (add a `Loaders` collection + populate it in `ReloadModsAsync`, near the Tools population ~line 577-622; add a `LaunchLoaderAsync(DetectedLoader)` near `LaunchToolAsync` ~line 1917)
- Modify: `src/ModManager.App/Tools/ToolsPanel.xaml` + `ToolsPanel.xaml.cs` (a loaders group binding `ViewModel.Loaders`, a click handler → `LaunchLoaderAsync`)
- Modify: `docs/smoke-tests/pending.md` (a smoke entry)

**Interfaces:**
- Consumes: `LoaderScan.Detect` (Task 2), `DirectInjectService.PlayFolder(gameRoot)` (existing — resolves the play folder; used in `AntiCheatStateOf`), `ToolLauncher` Process.Start pattern.
- Produces: `ObservableCollection<DetectedLoaderRow> Loaders`, `Task LaunchLoaderAsync(DetectedLoaderRow)`.

- [ ] **Step 1:** Add a tiny view row record (App-side) `sealed record DetectedLoaderRow(string DisplayName, string LauncherPath, bool BanSafe)` (in MainViewModel.cs or a small file) and `public ObservableCollection<DetectedLoaderRow> Loaders { get; } = new();` next to `Tools` (~line 112). Add `OnPropertyChanged`/visibility helpers mirroring `HasTools` (e.g. `HasLoaders`).
- [ ] **Step 2:** In `ReloadModsAsync` (where Tools/MissingTools populate, ~577-622): clear `Loaders`, then `var pf = DirectInjectService.PlayFolder(_ctx.Game.GameRoot); foreach (var d in LoaderScan.Detect(pf, _ctx.Game.Engine, _ctx.Game.SteamAppId)) Loaders.Add(new DetectedLoaderRow(d.Loader.DisplayName, d.LauncherPath, d.Loader.BanSafe));` then `OnPropertyChanged(nameof(HasLoaders))`.
- [ ] **Step 3:** Add `LaunchLoaderAsync` near `LaunchToolAsync` (~1917):

```csharp
public async Task LaunchLoaderAsync(DetectedLoaderRow row)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = row.LauncherPath,
            UseShellExecute = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(row.LauncherPath) ?? "",
        };
        System.Diagnostics.Process.Start(psi);
        StatusText = $"Launching {row.DisplayName}…";
    }
    catch (Exception ex) { StatusText = $"Couldn't launch {row.DisplayName}: {ex.Message}"; }
    await Task.CompletedTask;
}
```

- [ ] **Step 4:** In `ToolsPanel.xaml`, add a loaders group above/below the Tools group (mirror the Tools `ItemsRepeater`/`ItemsControl` at line 29): bind `ViewModel.Loaders`, each item a button labeled `Launch via {DisplayName}` (Tag = the row), with the existing tools-row button style. In `ToolsPanel.xaml.cs` add `OnLoaderClick` mirroring `OnToolClick` (line 33): `if (sender is FrameworkElement el && el.Tag is DetectedLoaderRow r && ViewModel is not null) await ViewModel.LaunchLoaderAsync(r);`. No `#if FULL`.
- [ ] **Step 5: Build both flavors** — `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (FULL) then `-p:Configuration=Store` (STORE). Both 0 errors. Run `pwsh scripts/check-store-seal.ps1` → seal still OK (no new forbidden symbols).
- [ ] **Step 6: Smoke entry** — append to `docs/smoke-tests/pending.md`: with Mod Engine 2 / Seamless installed in an Elden Ring folder, a "Launch via Mod Engine 2" / "Launch via Seamless Co-op" button appears in the bar and launches the loader; absent → no button.
- [ ] **Step 7: Commit** — `feat(loaders): surface detected loaders as launch buttons in the bar`.

## Task 4: Ban-risk gate points at the safe loaders (App)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`GateBanRiskEnableAsync` ~line 736 — resolve the game's ban-safe loaders, change the `ConfirmBanRiskEnable` delegate signature to take them)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (`ConfirmBanRiskEnableAsync` ~line 232 — render the safe-loader list)
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: `LoaderScan.Detect` + `LoaderScan.BanSafeFor` (Task 2), the existing `ConfirmBanRiskEnable` delegate.
- Produces: an updated `ConfirmBanRiskEnable` delegate type `Func<string, IReadOnlyList<BanSafeLoaderOption>, Task<(bool, bool)>>` where `record BanSafeLoaderOption(string DisplayName, string? LauncherPath, string GetUrl)` (LauncherPath non-null when installed → launch; null → Get-it-here).

- [ ] **Step 1:** Define `sealed record BanSafeLoaderOption(string DisplayName, string? LauncherPath, string GetUrl)` (Core `Loaders/` or App — App is fine since it's a view-model carrier). Change the `ConfirmBanRiskEnable` field type (MainViewModel) from `Func<string, Task<(bool,bool)>>?` to `Func<string, IReadOnlyList<BanSafeLoaderOption>, Task<(bool,bool)>>?`.
- [ ] **Step 2:** In `GateBanRiskEnableAsync` (~736), after `ShouldGateEnable` is true, build the options: for each `KnownLoader` in `LoaderScan.BanSafeFor(_ctx.Game.Engine, _ctx.Game.SteamAppId)`, check if it's in `LoaderScan.Detect(pf, …)` (installed → `LauncherPath`, else null + `GetUrl`). Pass that list to `ConfirmBanRiskEnable(_ctx.Game.GameName, options)`. Behavior otherwise unchanged (proceed/ack).
- [ ] **Step 3:** In `ConfirmBanRiskEnableAsync` (MainWindow ~232), accept the `IReadOnlyList<BanSafeLoaderOption>` param; after the existing warning text, if the list is non-empty add a line "The safe way to mod this game:" + a button per option — installed → "Launch {DisplayName}" (Process.Start the LauncherPath), not installed → "Get {DisplayName}" (open GetUrl via the existing `SafeUrl` + Process.Start pattern). Keep the Enable-anyway / Cancel buttons + the "don't warn again" checkbox exactly as-is. Update the ctor wiring (line 42) for the new signature.
- [ ] **Step 4: Build both flavors** + `scripts/check-store-seal.ps1`. 0 errors, seal OK. (On STORE, with no EAC offline toggle, this safe-loader list is the only safe-path guidance — confirm it renders.)
- [ ] **Step 5: Smoke entry** — enabling a mod on Elden Ring (banRisk high) shows the warning AND the safe loaders (Mod Engine 2 / Seamless) with Launch (if installed) or Get-it-here; the warning + ack still gate exactly as before.
- [ ] **Step 6: Run the full Core suite** (no regression) + commit — `feat(loaders): ban-risk gate surfaces the game's ban-safe loaders`.

## Self-review

- **Spec coverage:** Component A (loaders in the bar) → Tasks 1-3. Component B (ban-safe self-ID) → `BanSafe` on `KnownLoader` (Task 1). Component C (gate points at safety) → Task 4. Both-flavors + STORE-primary → Global Constraints + Tasks 3/4 Step "build both + seal". No-bundle / no-silent-AC / reversibility → Global Constraints.
- **Centralization decision noted:** loader knowledge lives in `KnownLoader` (not sprinkled across KnownTool/KnownFramework/FrameworkDep/KnownDirectInjectMod) — one source for the new feature; the existing catalogs are untouched (zero breakage).
- **Type consistency:** `KnownLoader`, `DetectedLoader`, `DetectedLoaderRow`, `BanSafeLoaderOption`, `LoaderScan.Detect`/`BanSafeFor`, `LaunchLoaderAsync` names match across tasks.
- **No placeholders:** Tasks 1-2 carry full TDD code; Tasks 3-4 carry the exact methods + wiring against grounded line numbers (App UI verified by build + seal + smoke, since WinUI dialog/row rendering isn't unit-testable).
- **Future-proofing note (not a task):** more loaders (e.g. other games' launcher exes) are one-record additions to `KnownLoaderCatalog`; bespoke DLL-injector loaders with no separate launcher exe (UE4SS, BepInEx, REFramework) are out of scope here — they have nothing to "launch" (the game launches normally).
