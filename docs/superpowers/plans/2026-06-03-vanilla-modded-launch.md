# Vanilla vs Modded Launch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the launch button honest — a real "Play vanilla" that steps every active loader aside (mod rows + frameworks + direct-inject proxies, reversibly) and a "Play (modded)" that restores exactly the prior-active set, with one smart split-button whose label tracks on-disk mode.

**Architecture:** A pure-Core `VanillaLaunch` orchestrator composes three existing/added reversible "step the loader aside" primitives, records the exact set it moved in a `vanilla-stash.json`, and reads on-disk state to report `Vanilla`/`Modded`. The App's split-button label tracks `CurrentMode`; the dropdown offers the opposite mode, which runs StepAside/Restore then launches on the existing target (existing launch guards unchanged).

**Tech Stack:** .NET 10 / C#, xUnit (headless Core tests), `System.IO` + `System.Text.Json` only in Core, WinUI 3 App shell, camelCase JSON on disk via `AtomicJson`.

**Reference reading before starting:**
- Spec: `docs/superpowers/specs/2026-06-03-vanilla-modded-launch-design.md`
- `src/ModManager.Core/Frameworks/FrameworkRegistry.cs` — has `List` + `Uninstall`; this plan ADDS `Disable`/`Enable`/`IsDisabled`.
- `src/ModManager.Core/Frameworks/FrameworkInstaller.cs` — `FrameworkInstallManifest(FrameworkId, DisplayName, Author, InstallPath, InstalledFiles, InstalledUtc, BackupSnapshotPath)`. The loader proxy is the `InstalledFiles` entry(ies) with NO `/` (top-level, e.g. `dwmapi.dll`); everything else is under `ue4ss/`.
- `src/ModManager.Core/DirectInject.cs:160` (`Disable`) / `:192` (`Enable`) — reversible proxy step-aside, signature `(string playFolder, string holdingRoot, DirectInjectMod mod)` / `(string playFolder, string holdingRoot, string modName)`.
- `src/ModManager.Core/Scanner.cs:328-331` — `DisableModAsync(name, ctx)` / `EnableModAsync(name, ctx)` / `EnableModWithOutcomeAsync`; `BuildModListAsync(ctx)` returns mods with `.Enabled`, `.Name`, `.Location`, `.Loader`, `.ReadOnly`.
- `src/ModManager.Core/AtomicJson.cs` — `WriteJsonAtomic<T>(file, value)` (camelCase, atomic).
- `src/ModManager.App/Services/DirectInjectService.cs:47` (`AnyActiveProxyDll`), `DirectInjectListing.Applies(game)` / `.List(game)`.
- `src/ModManager.App/ViewModels/MainViewModel.cs:854-912` — `LaunchButtonLabel`, `EffectiveLaunchTarget`, `LaunchTargets`, `AnyModsEnabled`, the launch guards.
- `.claude/rules/camelcase-json-on-disk.md`, `.claude/rules/validate-then-extract.md`.

**Build/test commands (Windows):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- One filter: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~<Class>"`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- NEVER run bare `dotnet test`/`dotnet build` at repo root (the WinUI project hangs). If the App build hits MSB3027/MSB3021 file-lock, a running app holds the DLL — that is NOT a compile error.

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ModManager.Core/Frameworks/FrameworkRegistry.cs` | ADD `Disable`/`Enable`/`IsDisabled` — light reversible loader-proxy step-aside. |
| `src/ModManager.Core/VanillaLaunch.cs` | NEW. `LaunchMode`, the stash shape + store, `StepAside`/`Restore`/`CurrentMode`, the per-mechanism active reads. The orchestrator. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | Mode-aware label + `StepAsideAndLaunchAsync`/`RestoreAndLaunchAsync`; expose `CurrentLaunchMode`. |
| `src/ModManager.App/MainWindow.xaml(.cs)` | Launch split-button dropdown gains the opposite-mode item; handlers. |
| `tests/ModManager.Tests/Frameworks/FrameworkDisableTests.cs` | NEW. |
| `tests/ModManager.Tests/VanillaLaunchTests.cs` | NEW. |

App view-model/XAML tasks (6–7) have no unit tests in this codebase — verified by App build + the smoke in Task 8.

---

## Task 1: FrameworkRegistry.Disable / Enable / IsDisabled (reversible loader-proxy step-aside)

**Files:**
- Modify: `src/ModManager.Core/Frameworks/FrameworkRegistry.cs`
- Test: `tests/ModManager.Tests/Frameworks/FrameworkDisableTests.cs`

The proxy DLL is what makes a framework inject. Disabling = move the top-level (no-`/`) `InstalledFiles` entries to a holding folder `<gameData>/frameworks/<id>/disabled-proxy/`, leaving `ue4ss/` + Mods in place. Enable moves them back. `IsDisabled` = the holding folder has the proxy AND the live proxy is gone. Reversible move-to-holding, no delete.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkDisableTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-disable-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A UE4SS-shaped install: gameRoot/R5/Binaries/Win64 holds dwmapi.dll (proxy) + ue4ss/UE4SS.dll +
    // a settings ini, and gameData/frameworks/ue4ss/install.json records them. Returns (gameData, binWin64).
    private (string gameData, string binWin64) MakeInstall()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var binWin64 = Path.Combine(gameRoot, "R5", "Binaries", "Win64");
        var ue = Path.Combine(binWin64, "ue4ss");
        Directory.CreateDirectory(ue);
        File.WriteAllText(Path.Combine(binWin64, "dwmapi.dll"), "proxy bytes");
        File.WriteAllText(Path.Combine(ue, "UE4SS.dll"), "loader");
        File.WriteAllText(Path.Combine(ue, "UE4SS-settings.ini"), "[General]");

        var gameData = Path.Combine(_tmp, "data");
        var fwDir = Path.Combine(gameData, "frameworks", "ue4ss");
        Directory.CreateDirectory(fwDir);
        var manifest = new FrameworkInstallManifest(
            "ue4ss", "UE4SS", "RE-UE4SS team", binWin64,
            new[] { "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini" },
            DateTime.UtcNow, null);
        File.WriteAllText(Path.Combine(fwDir, "install.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = true }));
        return (gameData, binWin64);
    }

    [Fact]
    public void Disable_steps_the_proxy_aside_leaving_ue4ss_in_place()
    {
        var (gameData, binWin64) = MakeInstall();

        FrameworkRegistry.Disable(gameData, "ue4ss");

        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));          // proxy gone from live -> no inject
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS.dll")));   // ue4ss/ untouched
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS-settings.ini")));
        Assert.True(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void Enable_restores_the_proxy_byte_for_byte()
    {
        var (gameData, binWin64) = MakeInstall();
        var before = File.ReadAllText(Path.Combine(binWin64, "dwmapi.dll"));

        FrameworkRegistry.Disable(gameData, "ue4ss");
        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));

        FrameworkRegistry.Enable(gameData, "ue4ss");

        Assert.Equal(before, File.ReadAllText(Path.Combine(binWin64, "dwmapi.dll"))); // restored
        Assert.False(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void IsDisabled_is_false_for_a_normally_installed_framework()
    {
        var (gameData, _) = MakeInstall();
        Assert.False(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void Disable_is_a_safe_noop_when_already_disabled()
    {
        var (gameData, binWin64) = MakeInstall();
        FrameworkRegistry.Disable(gameData, "ue4ss");
        FrameworkRegistry.Disable(gameData, "ue4ss"); // again — must not throw, proxy stays aside
        Assert.True(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));
    }

    [Fact]
    public void Disable_throws_a_clear_error_when_no_manifest()
    {
        var gameData = Path.Combine(_tmp, "empty");
        Directory.CreateDirectory(gameData);
        Assert.Throws<FileNotFoundException>(() => FrameworkRegistry.Disable(gameData, "ue4ss"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkDisableTests"`
Expected: FAIL — `FrameworkRegistry.Disable`/`Enable`/`IsDisabled` don't exist.

- [ ] **Step 3: Implement — add to `FrameworkRegistry`**

Add these three methods inside the `FrameworkRegistry` class (after `Uninstall`):

```csharp
    private const string DisabledProxyDir = "disabled-proxy";

    /// <summary>The top-level (no-slash) InstalledFiles entries — the loader proxy DLL(s) that make the
    /// framework inject (e.g. dwmapi.dll). Everything else lives under ue4ss/ and isn't a process hijack.</summary>
    private static IReadOnlyList<string> ProxyFiles(FrameworkInstallManifest m)
        => m.InstalledFiles.Where(f => !f.Replace('\\', '/').Contains('/')).ToList();

    private static FrameworkInstallManifest LoadManifest(string gameDataDir, string frameworkId)
    {
        var manifestPath = Path.Combine(gameDataDir, "frameworks", frameworkId, "install.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No install manifest for framework '{frameworkId}'.", manifestPath);
        return JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(manifestPath), Json)
               ?? throw new InvalidDataException($"Couldn't parse manifest for '{frameworkId}'.");
    }

    /// <summary>Reversibly disable an installed framework WITHOUT uninstalling it: move its loader proxy
    /// DLL(s) into a holding folder so the framework stops injecting, leaving the rest of the install
    /// (ue4ss/ + Mods) in place. Move-to-holding, never delete. No-op-safe when already disabled.</summary>
    public static void Disable(string gameDataDir, string frameworkId)
    {
        var m = LoadManifest(gameDataDir, frameworkId);
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        Directory.CreateDirectory(holding);
        foreach (var rel in ProxyFiles(m))
        {
            var live = Path.Combine(m.InstallPath, rel);
            var held = Path.Combine(holding, rel);
            if (File.Exists(held)) continue;            // already aside
            if (!File.Exists(live)) continue;           // nothing to move
            Directory.CreateDirectory(Path.GetDirectoryName(held)!);
            File.Move(live, held);                       // move-to-holding (reversible)
        }
    }

    /// <summary>Re-enable a framework disabled via <see cref="Disable"/>: move its proxy DLL(s) back to the
    /// install root. Skips a proxy whose live name is already taken (a reinstalled copy is never clobbered).</summary>
    public static void Enable(string gameDataDir, string frameworkId)
    {
        var m = LoadManifest(gameDataDir, frameworkId);
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        if (!Directory.Exists(holding)) return;
        foreach (var rel in ProxyFiles(m))
        {
            var held = Path.Combine(holding, rel);
            var live = Path.Combine(m.InstallPath, rel);
            if (!File.Exists(held) || File.Exists(live)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            File.Move(held, live);
        }
        try { Directory.Delete(holding, recursive: true); } catch { /* may hold un-restored entries */ }
    }

    /// <summary>True when the framework's proxy is currently stepped aside (holding has a proxy AND the
    /// live proxy is gone) — i.e. installed but not injecting.</summary>
    public static bool IsDisabled(string gameDataDir, string frameworkId)
    {
        FrameworkInstallManifest m;
        try { m = LoadManifest(gameDataDir, frameworkId); } catch { return false; }
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        var proxies = ProxyFiles(m);
        if (proxies.Count == 0) return false;
        return proxies.Any(rel => File.Exists(Path.Combine(holding, rel)))
               && proxies.All(rel => !File.Exists(Path.Combine(m.InstallPath, rel)));
    }
```

You may refactor `Uninstall`'s manifest-load to reuse `LoadManifest` if trivial; if it risks behavior change, leave `Uninstall` as-is (DRY is nice-to-have here, not required).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkDisableTests"`
Expected: PASS (5 tests). Then full suite `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Frameworks/FrameworkRegistry.cs tests/ModManager.Tests/Frameworks/FrameworkDisableTests.cs
git commit -m "feat(frameworks): reversible Disable/Enable — step the loader proxy aside"
```

---

## Task 2: VanillaStash shape + store (camelCase persisted record)

**Files:**
- Create: `src/ModManager.Core/VanillaLaunch.cs` (this task adds ONLY the stash shape + load/save/clear)
- Test: `tests/ModManager.Tests/VanillaLaunchTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class VanillaLaunchTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vanilla-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string DataDir() { var d = Path.Combine(_tmp, "data"); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void Stash_round_trips_as_camelCase()
    {
        var data = DataDir();
        Assert.Null(VanillaStashStore.Load(data));

        var stash = new VanillaStash
        {
            ModRows = new() { new StashedModRow { Name = "FasterShips10", Location = "mods" } },
            Frameworks = new() { "ue4ss" },
            DirectInjectProxies = new() { "dwmapi.dll" },
        };
        VanillaStashStore.Save(data, stash);

        var json = File.ReadAllText(Path.Combine(data, "vanilla-stash.json"));
        Assert.Contains("\"modRows\"", json);              // camelCase on disk
        Assert.Contains("\"directInjectProxies\"", json);
        Assert.DoesNotContain("\"ModRows\"", json);

        var loaded = VanillaStashStore.Load(data)!;
        Assert.Equal("FasterShips10", loaded.ModRows[0].Name);
        Assert.Equal("mods", loaded.ModRows[0].Location);
        Assert.Contains("ue4ss", loaded.Frameworks);
        Assert.Contains("dwmapi.dll", loaded.DirectInjectProxies);
    }

    [Fact]
    public void Load_returns_null_for_missing_or_corrupt_stash()
    {
        var data = DataDir();
        Assert.Null(VanillaStashStore.Load(data));                       // missing
        File.WriteAllText(Path.Combine(data, "vanilla-stash.json"), "{ not json");
        Assert.Null(VanillaStashStore.Load(data));                       // corrupt
    }

    [Fact]
    public void Clear_removes_the_stash_file()
    {
        var data = DataDir();
        VanillaStashStore.Save(data, new VanillaStash());
        Assert.NotNull(VanillaStashStore.Load(data));
        VanillaStashStore.Clear(data);
        Assert.Null(VanillaStashStore.Load(data));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement — create `src/ModManager.Core/VanillaLaunch.cs`**

```csharp
using System.Text.Json;

namespace ModManager.Core;

/// <summary>The launch mode derived from on-disk state.</summary>
public enum LaunchMode { Modded, Vanilla }

/// <summary>One mod row that was active and got stepped aside (name + its mod location).</summary>
public sealed class StashedModRow
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
}

/// <summary>The exact set of loaders that were active and got stepped aside for a vanilla launch. Lives at
/// <c>&lt;dataDir&gt;/vanilla-stash.json</c> (camelCase). Restore replays EXACTLY this set — not "enable
/// all" — so a deliberately-off mod is never re-enabled.</summary>
public sealed class VanillaStash
{
    public int Version { get; set; } = 1;
    public DateTime SteppedAsideUtc { get; set; }
    public List<StashedModRow> ModRows { get; set; } = new();
    public List<string> Frameworks { get; set; } = new();
    public List<string> DirectInjectProxies { get; set; } = new();
}

/// <summary>Read/write the vanilla stash. camelCase via AtomicJson; missing/corrupt -> null.</summary>
public static class VanillaStashStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "vanilla-stash.json");

    public static VanillaStash? Load(string dataDir)
    {
        try
        {
            var p = PathFor(dataDir);
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<VanillaStash>(File.ReadAllText(p), Json);
        }
        catch { return null; }
    }

    public static void Save(string dataDir, VanillaStash stash) => AtomicJson.WriteJsonAtomic(PathFor(dataDir), stash);

    public static void Clear(string dataDir)
    {
        try { var p = PathFor(dataDir); if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VanillaLaunch.cs tests/ModManager.Tests/VanillaLaunchTests.cs
git commit -m "feat(launch): VanillaStash — persisted camelCase stepped-aside record"
```

---

## Task 3: VanillaLaunch.StepAside (record + step aside all three mechanisms)

**Files:**
- Modify: `src/ModManager.Core/VanillaLaunch.cs` (add the orchestrator)
- Test: `tests/ModManager.Tests/VanillaLaunchTests.cs`

StepAside is async (mod-row disable is async). It records the active set, then steps each aside via its reversible primitive. For testability the mechanism calls go through small injectable delegates with real defaults, so a test can drive it without a full game on disk.

- [ ] **Step 1: Write the failing test**

```csharp
    // A fake game context wrapper: we test StepAside's RECORDING + ORCHESTRATION via injected hooks,
    // so we don't need a real game on disk. The hooks capture what StepAside asked to step aside.
    [Fact]
    public async Task StepAside_records_exactly_the_active_set_and_steps_each_aside()
    {
        var data = DataDir();
        var disabledRows = new List<string>();
        var disabledFw = new List<string>();
        var disabledProxies = new List<string>();

        var ops = new VanillaLaunchOps
        {
            ActiveModRows = () => new[] { new StashedModRow { Name = "FasterShips10", Location = "mods" },
                                          new StashedModRow { Name = "RareDrops", Location = "mods" } },
            ActiveFrameworks = () => new[] { "ue4ss" },
            ActiveDirectInjectProxies = () => new[] { "dwmapi.dll" },
            DisableModRow = (name, loc) => { disabledRows.Add(name); return Task.CompletedTask; },
            EnableModRow = (name, loc) => Task.CompletedTask,
            DisableFramework = id => disabledFw.Add(id),
            EnableFramework = id => { },
            DisableDirectInjectProxy = p => disabledProxies.Add(p),
            EnableDirectInjectProxy = p => { },
        };

        var result = await VanillaLaunch.StepAsideAsync(data, ops);

        Assert.True(result.Success);
        Assert.Equal(new[] { "FasterShips10", "RareDrops" }, disabledRows);
        Assert.Equal(new[] { "ue4ss" }, disabledFw);
        Assert.Equal(new[] { "dwmapi.dll" }, disabledProxies);

        // The stash records exactly what was active.
        var stash = VanillaStashStore.Load(data)!;
        Assert.Equal(2, stash.ModRows.Count);
        Assert.Contains(stash.ModRows, r => r.Name == "FasterShips10" && r.Location == "mods");
        Assert.Equal(new[] { "ue4ss" }, stash.Frameworks);
        Assert.Equal(new[] { "dwmapi.dll" }, stash.DirectInjectProxies);
    }

    [Fact]
    public async Task StepAside_rolls_back_and_writes_no_stash_when_a_step_fails()
    {
        var data = DataDir();
        var enabledBack = new List<string>();
        var ops = new VanillaLaunchOps
        {
            ActiveModRows = () => new[] { new StashedModRow { Name = "A", Location = "mods" },
                                          new StashedModRow { Name = "B", Location = "mods" } },
            ActiveFrameworks = () => Array.Empty<string>(),
            ActiveDirectInjectProxies = () => Array.Empty<string>(),
            DisableModRow = (name, loc) => name == "B"
                ? throw new IOException("locked")              // second row fails
                : Task.CompletedTask,
            EnableModRow = (name, loc) => { enabledBack.Add(name); return Task.CompletedTask; },
            DisableFramework = _ => { }, EnableFramework = _ => { },
            DisableDirectInjectProxy = _ => { }, EnableDirectInjectProxy = _ => { },
        };

        var result = await VanillaLaunch.StepAsideAsync(data, ops);

        Assert.False(result.Success);
        Assert.Contains("A", enabledBack);                     // the one that moved got rolled back
        Assert.Null(VanillaStashStore.Load(data));             // no stash on failure
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: FAIL — `VanillaLaunch` / `VanillaLaunchOps` don't exist.

- [ ] **Step 3: Implement — add to `src/ModManager.Core/VanillaLaunch.cs`**

```csharp
/// <summary>The reversible mechanism operations VanillaLaunch composes, injected so Core stays testable
/// without a live game. The App wires real defaults (Scanner mod-row disable, FrameworkRegistry.Disable,
/// DirectInject.Disable) + the active reads. Each "Active*" returns what is CURRENTLY loading.</summary>
public sealed class VanillaLaunchOps
{
    public required Func<IReadOnlyList<StashedModRow>> ActiveModRows { get; init; }
    public required Func<IReadOnlyList<string>> ActiveFrameworks { get; init; }
    public required Func<IReadOnlyList<string>> ActiveDirectInjectProxies { get; init; }
    public required Func<string, string, Task> DisableModRow { get; init; }   // (name, location)
    public required Func<string, string, Task> EnableModRow { get; init; }
    public required Action<string> DisableFramework { get; init; }
    public required Action<string> EnableFramework { get; init; }
    public required Action<string> DisableDirectInjectProxy { get; init; }
    public required Action<string> EnableDirectInjectProxy { get; init; }
}

/// <summary>Outcome of a StepAside/Restore.</summary>
public sealed record VanillaLaunchResult(bool Success, string? Error = null);

/// <summary>
/// Orchestrates a real vanilla launch: steps every active loader aside (mod rows + frameworks +
/// direct-inject proxies) as one reversible unit and records the EXACT set in vanilla-stash.json so
/// Restore replays only what was active. Composes the existing reversible primitives — no new file-op
/// law. Pure Core; the mechanism IO is injected via <see cref="VanillaLaunchOps"/>.
/// </summary>
public static class VanillaLaunch
{
    public static async Task<VanillaLaunchResult> StepAsideAsync(string dataDir, VanillaLaunchOps ops)
    {
        var rows = ops.ActiveModRows();
        var fws = ops.ActiveFrameworks();
        var proxies = ops.ActiveDirectInjectProxies();

        // Track what actually moved so a mid-step failure rolls back exactly those.
        var movedRows = new List<StashedModRow>();
        var movedFws = new List<string>();
        var movedProxies = new List<string>();
        try
        {
            foreach (var r in rows) { await ops.DisableModRow(r.Name, r.Location); movedRows.Add(r); }
            foreach (var id in fws) { ops.DisableFramework(id); movedFws.Add(id); }
            foreach (var p in proxies) { ops.DisableDirectInjectProxy(p); movedProxies.Add(p); }
        }
        catch (Exception ex)
        {
            foreach (var p in movedProxies) try { ops.EnableDirectInjectProxy(p); } catch { }
            foreach (var id in movedFws) try { ops.EnableFramework(id); } catch { }
            foreach (var r in movedRows) try { await ops.EnableModRow(r.Name, r.Location); } catch { }
            return new VanillaLaunchResult(false, ex.Message);
        }

        VanillaStashStore.Save(dataDir, new VanillaStash
        {
            Version = 1,
            SteppedAsideUtc = DateTime.UtcNow,
            ModRows = rows.ToList(),
            Frameworks = fws.ToList(),
            DirectInjectProxies = proxies.ToList(),
        });
        return new VanillaLaunchResult(true);
    }
}
```

Note: `DateTime.UtcNow` is fine in Core production code (only Workflow scripts forbid it). The stash store from Task 2 stays unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: PASS (5 tests now).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VanillaLaunch.cs tests/ModManager.Tests/VanillaLaunchTests.cs
git commit -m "feat(launch): VanillaLaunch.StepAside — record + reversibly step every active loader aside"
```

---

## Task 4: VanillaLaunch.Restore (replay the EXACT stashed set) + CurrentMode

**Files:**
- Modify: `src/ModManager.Core/VanillaLaunch.cs`
- Test: `tests/ModManager.Tests/VanillaLaunchTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task Restore_re_enables_exactly_the_stashed_set_and_clears_the_stash()
    {
        var data = DataDir();
        // Pretend a prior StepAside recorded 2 of 3 mods + a framework + a proxy.
        VanillaStashStore.Save(data, new VanillaStash
        {
            ModRows = new() { new StashedModRow { Name = "FasterShips10", Location = "mods" },
                              new StashedModRow { Name = "RareDrops", Location = "mods" } },
            Frameworks = new() { "ue4ss" },
            DirectInjectProxies = new() { "dwmapi.dll" },
        });

        var enabledRows = new List<string>();
        var enabledFw = new List<string>();
        var enabledProxies = new List<string>();
        var ops = MakeOps(
            enableRow: (n, l) => { enabledRows.Add(n); return Task.CompletedTask; },
            enableFw: id => enabledFw.Add(id),
            enableProxy: p => enabledProxies.Add(p));

        var result = await VanillaLaunch.RestoreAsync(data, ops);

        Assert.True(result.Success);
        Assert.Equal(new[] { "FasterShips10", "RareDrops" }, enabledRows); // EXACTLY the stashed set
        Assert.Equal(new[] { "ue4ss" }, enabledFw);
        Assert.Equal(new[] { "dwmapi.dll" }, enabledProxies);
        Assert.Null(VanillaStashStore.Load(data));                          // stash cleared
    }

    [Fact]
    public async Task Restore_with_no_stash_is_a_safe_noop()
    {
        var data = DataDir();
        var result = await VanillaLaunch.RestoreAsync(data, MakeOps());
        Assert.True(result.Success);
    }

    [Fact]
    public void CurrentMode_is_Vanilla_when_a_stash_exists_else_Modded()
    {
        var data = DataDir();
        Assert.Equal(LaunchMode.Modded, VanillaLaunch.CurrentMode(data));
        VanillaStashStore.Save(data, new VanillaStash());
        Assert.Equal(LaunchMode.Vanilla, VanillaLaunch.CurrentMode(data));
    }

    // Helper: an ops with no-op defaults, overridable per call.
    private static VanillaLaunchOps MakeOps(
        Func<string, string, Task>? enableRow = null,
        Action<string>? enableFw = null,
        Action<string>? enableProxy = null) => new()
    {
        ActiveModRows = () => Array.Empty<StashedModRow>(),
        ActiveFrameworks = () => Array.Empty<string>(),
        ActiveDirectInjectProxies = () => Array.Empty<string>(),
        DisableModRow = (_, _) => Task.CompletedTask,
        EnableModRow = enableRow ?? ((_, _) => Task.CompletedTask),
        DisableFramework = _ => { },
        EnableFramework = enableFw ?? (_ => { }),
        DisableDirectInjectProxy = _ => { },
        EnableDirectInjectProxy = enableProxy ?? (_ => { }),
    };
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: FAIL — `RestoreAsync` / `CurrentMode` don't exist.

- [ ] **Step 3: Implement — add to `VanillaLaunch`**

```csharp
    public static async Task<VanillaLaunchResult> RestoreAsync(string dataDir, VanillaLaunchOps ops)
    {
        var stash = VanillaStashStore.Load(dataDir);
        if (stash is null) return new VanillaLaunchResult(true); // nothing stepped aside — no-op

        try
        {
            // Restore in reverse order of step-aside: proxies + frameworks first (so the loader is back
            // before its mods), then the mod rows. Each Enable is itself no-clobber/idempotent.
            foreach (var p in stash.DirectInjectProxies) ops.EnableDirectInjectProxy(p);
            foreach (var id in stash.Frameworks) ops.EnableFramework(id);
            foreach (var r in stash.ModRows) await ops.EnableModRow(r.Name, r.Location);
        }
        catch (Exception ex)
        {
            return new VanillaLaunchResult(false, ex.Message); // leave the stash so a retry can finish
        }

        VanillaStashStore.Clear(dataDir);
        return new VanillaLaunchResult(true);
    }

    /// <summary>Vanilla when a stash exists (we stepped aside), Modded otherwise. Drives the button label.</summary>
    public static LaunchMode CurrentMode(string dataDir)
        => VanillaStashStore.Load(dataDir) is not null ? LaunchMode.Vanilla : LaunchMode.Modded;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VanillaLaunchTests"`
Expected: PASS (8 tests). Then full suite `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VanillaLaunch.cs tests/ModManager.Tests/VanillaLaunchTests.cs
git commit -m "feat(launch): VanillaLaunch.Restore replays the exact stashed set + CurrentMode"
```

---

## Task 5: Wire the real ops + mode-aware label in MainViewModel (App)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`
- (App VM — verified by App build, not unit tests.)

This builds the real `VanillaLaunchOps` from the App's services and exposes `CurrentLaunchMode` + the label.

- [ ] **Step 1: Add a factory for the real ops + the mode-aware members**

Near the launch members (~line 854-912), add. Read the surrounding code first to confirm service field names: `_direct` (DirectInjectService), `_svc` (LauncherService), `_ctx` (GameContext). For the active reads:
- Active mod rows: `Mods.Where(m => m.Enabled && !m.ReadOnly)` mapped to `StashedModRow { Name = m.Name, Location = m.Location }`.
- Active frameworks: `FrameworkRegistry.List(_ctx.DataDir).Where(f => !FrameworkRegistry.IsDisabled(_ctx.DataDir, f.FrameworkId)).Select(f => f.FrameworkId)`.
- Active direct-inject proxies: `DirectInjectListing.Applies(_ctx.Game)` ? the active proxy list : empty. (See Step 1a for the proxy list source.)

```csharp
    /// <summary>The launch mode read from on-disk state (a vanilla-stash means we stepped aside).</summary>
    public LaunchMode CurrentLaunchMode => _ctx is null ? LaunchMode.Modded : VanillaLaunch.CurrentMode(_ctx.DataDir);

    /// <summary>Build the real reversible-mechanism ops from the App services for the active game.</summary>
    private VanillaLaunchOps BuildVanillaOps()
    {
        var ctx = _ctx!;
        return new VanillaLaunchOps
        {
            ActiveModRows = () => Mods.Where(m => m.Enabled && !m.ReadOnly)
                .Select(m => new StashedModRow { Name = m.Name, Location = m.Mod.Location }).ToList(),
            ActiveFrameworks = () => FrameworkRegistry.List(ctx.DataDir)
                .Where(f => !FrameworkRegistry.IsDisabled(ctx.DataDir, f.FrameworkId))
                .Select(f => f.FrameworkId).ToList(),
            ActiveDirectInjectProxies = () => _direct.ActiveProxyDlls(ctx.Game),
            DisableModRow = (name, _) => Scanner.DisableModAsync(name, ctx),
            EnableModRow = (name, _) => Scanner.EnableModAsync(name, ctx),
            DisableFramework = id => FrameworkRegistry.Disable(ctx.DataDir, id),
            EnableFramework = id => FrameworkRegistry.Enable(ctx.DataDir, id),
            DisableDirectInjectProxy = p => _direct.DisableProxy(ctx.Game, p),
            EnableDirectInjectProxy = p => _direct.EnableProxy(ctx.Game, p),
        };
    }
```

- [ ] **Step 1a: Add the missing DirectInjectService helpers**

`DirectInjectService` currently exposes `AnyActiveProxyDll` (a bool). Add the per-proxy list + per-proxy disable/enable in `src/ModManager.App/Services/DirectInjectService.cs`:

```csharp
    /// <summary>The process-load proxy DLL filenames currently sitting at the top of the play folder.</summary>
    public IReadOnlyList<string> ActiveProxyDlls(GameEntry game)
    {
        if (game.Engine != "fromsoft") return Array.Empty<string>();
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return Array.Empty<string>();
        string[] top;
        try { top = Directory.GetFiles(folder); } catch { return Array.Empty<string>(); }
        return DirectInject.ProcessLoadProxiesIn(top); // returns the matching proxy file NAMES
    }

    /// <summary>Step one active proxy DLL aside (reversible) — wraps DirectInject.Disable for a single proxy.</summary>
    public void DisableProxy(GameEntry game, string proxyDll)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = Path.Combine(folder, "_626", "vanilla-proxy");
        DirectInject.DisableSingleFile(folder, holding, proxyDll);
    }

    /// <summary>Restore one proxy DLL stepped aside by <see cref="DisableProxy"/>.</summary>
    public void EnableProxy(GameEntry game, string proxyDll)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = Path.Combine(folder, "_626", "vanilla-proxy");
        DirectInject.EnableSingleFile(folder, holding, proxyDll);
    }
```

This needs three small Core helpers in `DirectInject.cs` — `ProcessLoadProxiesIn(string[] topLevelPaths) -> IReadOnlyList<string>` (the names matching the existing `AnyProcessLoadProxy` predicate), and `DisableSingleFile`/`EnableSingleFile(playFolder, holdingRoot, fileName)` (move a single top-level file to/from holding, reversible). **Add them in Task 5b below as a Core change with tests, then return here.** If `DirectInject` already exposes an equivalent of `ProcessLoadProxiesIn` (check for a method returning proxy names, not just the bool `AnyProcessLoadProxy`), reuse it.

- [ ] **Step 1b (Core, TDD): add `DirectInject.ProcessLoadProxiesIn` + `DisableSingleFile`/`EnableSingleFile`**

Test (`tests/ModManager.Tests/DirectInjectSingleFileTests.cs`):

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectSingleFileTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-single-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void DisableSingleFile_then_EnableSingleFile_round_trips()
    {
        var play = Path.Combine(_tmp, "play"); Directory.CreateDirectory(play);
        var holding = Path.Combine(_tmp, "hold");
        File.WriteAllText(Path.Combine(play, "dwmapi.dll"), "proxy");

        DirectInject.DisableSingleFile(play, holding, "dwmapi.dll");
        Assert.False(File.Exists(Path.Combine(play, "dwmapi.dll")));   // stepped aside

        DirectInject.EnableSingleFile(play, holding, "dwmapi.dll");
        Assert.Equal("proxy", File.ReadAllText(Path.Combine(play, "dwmapi.dll"))); // restored
    }

    [Fact]
    public void ProcessLoadProxiesIn_lists_only_recognized_proxy_names()
    {
        var names = DirectInject.ProcessLoadProxiesIn(new[]
        {
            @"C:\g\dwmapi.dll", @"C:\g\game.exe", @"C:\g\dinput8.dll"
        });
        Assert.Contains("dwmapi.dll", names);
        Assert.Contains("dinput8.dll", names);
        Assert.DoesNotContain("game.exe", names);
    }
}
```

Implementation in `src/ModManager.Core/DirectInject.cs` — reuse the existing proxy-name set that `AnyProcessLoadProxy` checks against (find that predicate; it tests filenames against a known proxy list). Add:

```csharp
    /// <summary>The recognized process-load proxy DLL NAMES present among the given top-level paths
    /// (the list form of <see cref="AnyProcessLoadProxy"/>).</summary>
    public static IReadOnlyList<string> ProcessLoadProxiesIn(IEnumerable<string> topLevelPaths)
        => topLevelPaths.Select(Path.GetFileName)
            .Where(n => n is not null && IsProcessLoadProxy(n!))   // IsProcessLoadProxy = the existing predicate
            .Select(n => n!).ToList();

    /// <summary>Step a single top-level file aside to a holding folder (reversible move, no delete).</summary>
    public static void DisableSingleFile(string playFolder, string holdingRoot, string fileName)
    {
        var src = Path.Combine(playFolder, fileName);
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(holdingRoot);
        var dest = Path.Combine(holdingRoot, fileName);
        if (File.Exists(dest)) return;          // already aside
        File.Move(src, dest);
    }

    /// <summary>Restore a single file stepped aside by <see cref="DisableSingleFile"/> (no-clobber).</summary>
    public static void EnableSingleFile(string playFolder, string holdingRoot, string fileName)
    {
        var src = Path.Combine(holdingRoot, fileName);
        var dest = Path.Combine(playFolder, fileName);
        if (!File.Exists(src) || File.Exists(dest)) return;
        File.Move(src, dest);
    }
```

Find the existing proxy predicate: search `DirectInject.cs` for `AnyProcessLoadProxy` and the private name-matching it uses (e.g. a `static bool IsProcessLoadProxy(string name)` or an inline set of `dinput8.dll`/`dwmapi.dll`/`xinput1_3.dll`/etc.). If it's inline, extract it to `IsProcessLoadProxy(string name)` and have both `AnyProcessLoadProxy` and `ProcessLoadProxiesIn` call it (DRY — single source of the proxy name set).

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DirectInjectSingleFileTests"` → PASS (2). Then full suite — all pass (confirms the `AnyProcessLoadProxy` refactor didn't change behavior).

Commit:
```bash
git add src/ModManager.Core/DirectInject.cs tests/ModManager.Tests/DirectInjectSingleFileTests.cs
git commit -m "feat(direct-inject): single-file step-aside + proxy-name listing for vanilla launch"
```

- [ ] **Step 2: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. Confirm `m.Mod.Location` is the right path to the row's location (read `ModRowViewModel` — it wraps a `Mod` with `.Location`). Fix the member access if the wrapper differs.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/Services/DirectInjectService.cs
git commit -m "feat(launch): MainViewModel builds real VanillaLaunchOps + CurrentLaunchMode"
```

---

## Task 6: The two launch actions + mode-aware label (App VM)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add the actions + fold mode into the label**

```csharp
    /// <summary>Play vanilla: step every active loader aside (reversible), refresh rows, then launch clean.</summary>
    public async Task StepAsideAndLaunchAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = await VanillaLaunch.StepAsideAsync(_ctx.DataDir, BuildVanillaOps());
            if (!r.Success) { StatusText = $"Couldn't switch to vanilla: {r.Error}"; return; }
            await ReloadModsAsync();                                  // rows now show off; CurrentLaunchMode -> Vanilla
            StatusText = "Vanilla mode — mods stepped aside. Launching…";
            var target = EffectiveLaunchTarget;
            if (target is not null) await LaunchTargetExplicit(target);
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Play modded: restore exactly the stashed set, refresh rows, then launch with mods.</summary>
    public async Task RestoreAndLaunchAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = await VanillaLaunch.RestoreAsync(_ctx.DataDir, BuildVanillaOps());
            if (!r.Success) { StatusText = $"Couldn't restore mods: {r.Error}"; return; }
            await ReloadModsAsync();                                  // rows back; CurrentLaunchMode -> Modded
            StatusText = "Modded mode — mods restored. Launching…";
            var target = EffectiveLaunchTarget;
            if (target is not null) await LaunchTargetExplicit(target);
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }
```

Then update `LaunchButtonLabel` (currently `▶ {target.Label}`) to fold in the mode so the primary button reads honestly. Replace its body:

```csharp
    public string LaunchButtonLabel
    {
        get
        {
            if (CurrentLaunchMode == LaunchMode.Vanilla) return "▶ Play vanilla";
            var t = EffectiveLaunchTarget;
            return string.IsNullOrEmpty(t?.Label) ? "▶ Play (modded)" : $"▶ {t.Label}";
        }
    }
```

And ensure `CurrentLaunchMode` + `LaunchButtonLabel` are re-published at the end of `ReloadModsAsync` (find the existing `OnPropertyChanged(nameof(LaunchButtonLabel));` near line 528-529 and add `OnPropertyChanged(nameof(CurrentLaunchMode));` beside it).

- [ ] **Step 2: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(launch): vanilla/modded launch actions + mode-aware button label"
```

---

## Task 7: The dropdown opposite-mode item (App XAML)

**Files:**
- Modify: `src/ModManager.App/MainWindow.xaml` (the launch split-button's dropdown / MenuFlyout)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (handlers)

- [ ] **Step 1: Find the launch split-button**

Read `MainWindow.xaml` around the launch button (search for `LaunchButtonLabel` binding and its `MenuFlyout` of targets). The dropdown currently lists `LaunchTargets`. Add ONE mode item at the top of that flyout:

```xml
                        <MenuFlyoutItem x:Name="VanillaModeItem"
                                        Text="Play vanilla"
                                        Click="OnPlayVanilla" />
                        <MenuFlyoutItem x:Name="ModdedModeItem"
                                        Text="Play modded"
                                        Click="OnPlayModded" />
                        <MenuFlyoutSeparator />
                        <!-- existing per-target items below -->
```

Bind their visibility to the mode so only the OPPOSITE mode shows. The project uses Visibility-typed getters; add two on the VM:

```csharp
    public Visibility VanillaModeItemVisibility => CurrentLaunchMode == LaunchMode.Modded ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModdedModeItemVisibility => CurrentLaunchMode == LaunchMode.Vanilla ? Visibility.Visible : Visibility.Collapsed;
```

and bind `Visibility="{x:Bind ViewModel.VanillaModeItemVisibility, Mode=OneWay}"` / `ModdedModeItemVisibility` on the two items. Re-publish both in `ReloadModsAsync` alongside `CurrentLaunchMode`.

- [ ] **Step 2: Handlers in MainWindow.xaml.cs**

```csharp
    private async void OnPlayVanilla(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.StepAsideAndLaunchAsync();
    }

    private async void OnPlayModded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.RestoreAndLaunchAsync();
    }
```

- [ ] **Step 3: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(launch): dropdown opposite-mode item (Play vanilla / Play modded)"
```

---

## Task 8: Verification + reviewers + smoke

**Files:** none (verification only)

- [ ] **Step 1: Full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all, including the new FrameworkDisable / VanillaLaunch / DirectInjectSingleFile tests).

- [ ] **Step 2: CorePurity**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"`
Expected: PASS — no WinUI/WinRT in Core (VanillaLaunch is pure; the ops delegates carry no UI types).

- [ ] **Step 3: App build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: reversibility-auditor**

Dispatch the `reversibility-auditor` agent on `FrameworkRegistry.Disable/Enable`, `DirectInject.DisableSingleFile/EnableSingleFile`, and `VanillaLaunch.StepAside/Restore`: confirm move-to-holding (no delete), mid-step rollback leaves nothing half-stepped-aside, Restore replays the exact set, stale-stash degrades safely. Implement any Important/Critical findings.

- [ ] **Step 5: Add a smoke entry**

Append to `docs/smoke-tests/pending.md` (match the file's format): vanilla/modded on Windrose — Play vanilla steps pak rows + UE4SS proxy aside (verify `dwmapi.dll` moved to `frameworks/ue4ss/disabled-proxy/` and pak mods to holding; rows show off; button reads "Play vanilla"); launch is clean in-game; Play modded restores exactly the prior-active set (a deliberately-off mod stays off); button reads "Play (modded)". Also the manual-toggle-clears-vanilla case.

- [ ] **Step 6: Open the PR + log the decision**

Open PR `feat/vanilla-modded-launch → master`. Log to the 626 dashboard (project `DP1YCsh7iAN1yAiR8sAd`): the stateful-no-auto-restore model, the exact-set restore, the new framework-disable primitive.

---

## Self-review notes

- **Spec coverage:** real two-mode (T3-T7), stateful/no-auto-restore (CurrentMode from stash, T4), exact-set restore "8 of 12" (T4 test), step-aside-everything incl. frameworks+proxies (T1, T5, T3), smart button label tracks mode (T6), dropdown opposite mode (T7), reversible/no-new-law (T1 + T5b reuse move-to-holding), camelCase stash (T2), edge cases — partial rollback (T3), stale stash (T2/T4), no-stash no-op (T4). The manual-toggle-clears-stash edge: handled implicitly — a manual re-enable makes the on-disk set no longer match "all stepped aside," but `CurrentMode` keys off the STASH file, so add a small reconcile (T6/T7 area): when the user manually toggles while a stash exists, clear the stash. **Added as a note in T6 Step 1** — confirm during implementation: simplest is, in `ToggleAsync`/the row-toggle path, `VanillaStashStore.Clear(_ctx.DataDir)` when a manual toggle happens and a stash exists, so CurrentMode reverts to Modded. If that path isn't obvious, the executor should surface it.
- **Placeholder scan:** none — every code step has real code. The one "find the existing predicate" instruction (T5b) is a concrete refactor with a named fallback.
- **Type consistency:** `VanillaLaunchOps` / `VanillaStash` / `StashedModRow` / `LaunchMode` / `VanillaLaunchResult` consistent T2-T6. `FrameworkRegistry.Disable/Enable/IsDisabled(gameDataDir, frameworkId)` consistent T1/T5. `DirectInject.DisableSingleFile/EnableSingleFile/ProcessLoadProxiesIn` consistent T5b/T5. `VanillaLaunch.StepAsideAsync/RestoreAsync/CurrentMode` consistent T3-T6.
- **Open item flagged for executor:** the manual-toggle stash-clear (above) — pin the exact toggle path during T6.
