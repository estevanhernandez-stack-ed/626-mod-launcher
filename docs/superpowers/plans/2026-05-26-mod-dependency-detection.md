# Mod-Dependency Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build (App): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL.

**Goal:** Surface missing-framework dependencies at game-load + drop time so a Windrose UE4SS mod (or any framework-gated mod) never silently fails — the user sees "needs UE4SS" with a "Get it here" link instead of a quiet no-op.

**Architecture:** A new pure `FrameworkDeps` core ships a static per-engine catalog (UE4SS, BepInEx, SMAPI, EML/dinput8 proxy, ME2, Forge/Fabric) and a `CheckPresent(GameContext)` probe that returns the missing entries. The App layer wires the probe into `MainViewModel.ReloadModsAsync` (status-line callout + persistent `MissingFrameworks` collection bound to a status banner) and into `AddModsAsync` (drop-time callout: when intake landed a mod whose engine has a missing framework, the post-drop status line names the gap + the get-link). A row-level `NEEDS X` chip on every relevant mod row mirrors the existing `MANAGED` / `BUILT-IN` chip pattern verbatim.

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. No new NuGets.

**Spec:** `docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md`

---

## Task 1: Core — `FrameworkDep` record model

**Files:**
- Create: `src/ModManager.Core/FrameworkDeps.cs`
- Create: `tests/ModManager.Tests/FrameworkDepsModelTests.cs`

Pure data shape: what a known framework is, where to detect it on disk, where to get it. No I/O in this task — the catalog and probe come in Tasks 2 + 3.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/FrameworkDepsModelTests.cs
using ModManager.Core;

namespace ModManager.Tests;

public class FrameworkDepsModelTests
{
    [Fact]
    public void FrameworkDep_carries_name_engine_detect_paths_and_url()
    {
        var dep = new FrameworkDep(
            Engine: "ue-pak",
            Name: "UE4SS",
            DetectRelativePaths: new[] { "Binaries/Win64/ue4ss/UE4SS.dll", "Binaries/Win64/dwmapi.dll" },
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Note: "Required for Lua mods and LogicMods paks.");
        Assert.Equal("ue-pak", dep.Engine);
        Assert.Equal("UE4SS", dep.Name);
        Assert.Equal(2, dep.DetectRelativePaths.Count);
        Assert.Equal("https://github.com/UE4SS-RE/RE-UE4SS/releases", dep.GetUrl);
        Assert.Equal("Required for Lua mods and LogicMods paks.", dep.Note);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter FullyQualifiedName~FrameworkDepsModel` — RED (no `FrameworkDep` type).

- [ ] **Step 2: Implement the model**

```csharp
// src/ModManager.Core/FrameworkDeps.cs
namespace ModManager.Core;

/// <summary>
/// One framework dependency the launcher knows about: what engine it belongs to, where it
/// installs on disk (relative to the game root or a UE project subfolder), and where the
/// user can get it. Pure data — no probing logic here, see <see cref="FrameworkDeps"/>.
/// </summary>
/// <param name="Engine">Engine key from <c>EnginePresets.Presets</c> (e.g. "ue-pak", "bepinex").</param>
/// <param name="Name">Display name as the user knows it (UE4SS, BepInEx, SMAPI, ME2, Forge/Fabric, dinput8 proxy).</param>
/// <param name="DetectRelativePaths">One or more relative file paths; if ANY exists under the resolved
/// candidate roots, the framework is considered present. Multiple paths cover loader variants
/// (e.g. UE4SS ships its loader as <c>dwmapi.dll</c> next to <c>UE4SS.dll</c>).</param>
/// <param name="GetUrl">https URL where the user can get the framework. Single canonical link
/// per framework — vendor releases page, not a wiki tree.</param>
/// <param name="Note">One-sentence why-it-matters, surfaced in the status banner tooltip.</param>
public sealed record FrameworkDep(
    string Engine,
    string Name,
    IReadOnlyList<string> DetectRelativePaths,
    string GetUrl,
    string Note);
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter FullyQualifiedName~FrameworkDepsModel` — GREEN.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.Core/FrameworkDeps.cs tests/ModManager.Tests/FrameworkDepsModelTests.cs
git commit -m "feat(core): FrameworkDep record — framework dependency model"
```

---

## Task 2: Core — `FrameworkDeps.Catalog` (6 entries)

**Files:**
- Edit: `src/ModManager.Core/FrameworkDeps.cs`
- Create: `tests/ModManager.Tests/FrameworkDepsCatalogTests.cs`

The catalog: one `FrameworkDep` per known framework, keyed by engine. Test-first.

Catalog scope per the spec:
- `ue-pak` → **UE4SS** (covers Lua mods + LogicMods paks)
- `bepinex` → **BepInEx** (Unity loader; `winhttp.dll` + `BepInEx/core/`)
- `smapi` → **SMAPI** (Stardew)
- `fromsoft` → **EML / dinput8 proxy** (loose-DLL loader for direct-inject) **AND** **Mod Engine 2** (folder-based)
- `minecraft` → **Forge or Fabric** (the user picks; we name both)

That's 6 entries.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/FrameworkDepsCatalogTests.cs
using ModManager.Core;

namespace ModManager.Tests;

public class FrameworkDepsCatalogTests
{
    [Fact]
    public void Catalog_has_six_entries_across_known_engines()
    {
        Assert.Equal(6, FrameworkDeps.Catalog.Count);
    }

    [Fact]
    public void Catalog_includes_ue4ss_for_ue_pak()
    {
        var ue4ss = FrameworkDeps.Catalog.Single(d => d.Name == "UE4SS");
        Assert.Equal("ue-pak", ue4ss.Engine);
        Assert.Contains("ue4ss", ue4ss.DetectRelativePaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", ue4ss.GetUrl);
    }

    [Fact]
    public void Catalog_includes_bepinex_smapi_me2_eml_forge_fabric()
    {
        var names = FrameworkDeps.Catalog.Select(d => d.Name).ToList();
        Assert.Contains("BepInEx", names);
        Assert.Contains("SMAPI", names);
        Assert.Contains("Mod Engine 2", names);
        Assert.Contains("DLL proxy (dinput8/version/winhttp)", names);
        Assert.Contains("Forge or Fabric", names);
    }

    [Fact]
    public void Every_entry_has_https_get_url_and_at_least_one_detect_path()
    {
        foreach (var dep in FrameworkDeps.Catalog)
        {
            Assert.StartsWith("https://", dep.GetUrl);
            Assert.NotEmpty(dep.DetectRelativePaths);
            Assert.False(string.IsNullOrWhiteSpace(dep.Note));
        }
    }
}
```

Run filter: `--filter FullyQualifiedName~FrameworkDepsCatalog` — RED (no `Catalog` member).

- [ ] **Step 2: Implement the catalog**

Edit `src/ModManager.Core/FrameworkDeps.cs`, append:

```csharp
/// <summary>
/// Static catalog of framework dependencies the launcher knows about. One entry per
/// (engine, framework) pair. Mirrors the spec table at
/// <c>docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md</c>. Add an entry
/// here when adding a new engine + framework; the probe and the UI pick it up automatically.
/// </summary>
public static class FrameworkDeps
{
    public static IReadOnlyList<FrameworkDep> Catalog { get; } = new[]
    {
        new FrameworkDep(
            Engine: "ue-pak",
            Name: "UE4SS",
            // UE4SS ships its loader as dwmapi.dll next to the ue4ss/ runtime under
            // <Project>/Binaries/Win64. EITHER path existing means the framework is present.
            DetectRelativePaths: new[]
            {
                "Binaries/Win64/ue4ss/UE4SS.dll",
                "Binaries/Win64/dwmapi.dll",
            },
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Note: "Required for Lua mods and Blueprint LogicMods paks. Plain content paks don't need it."),

        new FrameworkDep(
            Engine: "bepinex",
            Name: "BepInEx",
            DetectRelativePaths: new[]
            {
                "BepInEx/core/BepInEx.dll",
                "winhttp.dll",
            },
            GetUrl: "https://github.com/BepInEx/BepInEx/releases",
            Note: "Unity plugin loader. Required for any .dll mod under BepInEx/plugins/."),

        new FrameworkDep(
            Engine: "smapi",
            Name: "SMAPI",
            DetectRelativePaths: new[]
            {
                "StardewModdingAPI.exe",
            },
            GetUrl: "https://smapi.io/",
            Note: "Stardew Valley mod loader. Required for any folder mod under Mods/ with a manifest.json."),

        new FrameworkDep(
            Engine: "fromsoft",
            Name: "Mod Engine 2",
            DetectRelativePaths: new[]
            {
                "modengine2_launcher.exe",
                "mod/config_eldenring.toml",
            },
            GetUrl: "https://github.com/soulsmods/ModEngine2/releases",
            Note: "FromSoft folder-based mod loader. Required for /mod folder mods; not needed for direct-inject loose files."),

        new FrameworkDep(
            Engine: "fromsoft",
            Name: "DLL proxy (dinput8/version/winhttp)",
            // Direct-inject mods chain off whichever DLL proxy is already installed. We check all three.
            DetectRelativePaths: new[]
            {
                "dinput8.dll",
                "version.dll",
                "winhttp.dll",
            },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Note: "DLL proxy chain-loader for direct-inject mods (ELDEN MOD LOADER and similar). Most ER mods chain off dinput8.dll."),

        new FrameworkDep(
            Engine: "minecraft",
            Name: "Forge or Fabric",
            DetectRelativePaths: new[]
            {
                "libraries/net/minecraftforge",
                "libraries/net/fabricmc",
            },
            GetUrl: "https://files.minecraftforge.net/",
            Note: "Minecraft mod loader. Forge OR Fabric — install whichever matches your modpack."),
    };
}
```

Run: `--filter FullyQualifiedName~FrameworkDepsCatalog` — GREEN.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.Core/FrameworkDeps.cs tests/ModManager.Tests/FrameworkDepsCatalogTests.cs
git commit -m "feat(core): FrameworkDeps.Catalog — 6 known framework dependencies"
```

---

## Task 3: Core — `FrameworkDeps.CheckPresent(GameContext)` probe

**Files:**
- Edit: `src/ModManager.Core/FrameworkDeps.cs`
- Create: `tests/ModManager.Tests/FrameworkDepsCheckPresentTests.cs`

The probe: given a `GameContext`, return the catalog entries for the active engine that are NOT present on disk. Pure — takes the game root + engine + the resolved mod-location paths (the UE project subfolders are encoded there per `Scanner.GameContext`); returns missing entries only. Empty list = nothing missing.

Key design call: **UE4SS detection probes under each resolved mod-location's project subfolder**, NOT just the bare game root. A Windrose `ModLocation.Path` is `R5/Content/Paks/~mods`, so we extract `R5` as the project subfolder and look for `R5/Binaries/Win64/ue4ss/UE4SS.dll`. For non-UE engines we just check from the game root.

- [ ] **Step 1: Write the failing test — happy paths first**

```csharp
// tests/ModManager.Tests/FrameworkDepsCheckPresentTests.cs
using ModManager.Core;

namespace ModManager.Tests;

public class FrameworkDepsCheckPresentTests
{
    private static GameContext Ctx(string root, string engine, string modPath)
    {
        var game = new GameEntry
        {
            Id = "g",
            GameName = "Test",
            Engine = engine,
            GameRoot = root,
            ModLocations = new[] { new ModLocation("mods", "mods", modPath) },
            GroupingRule = "filename_no_ext",
            FileExtensions = new[] { "pak" },
        };
        return Scanner.GameContext(game);
    }

    [Fact]
    public void Ue_pak_with_ue4ss_dll_under_project_subfolder_is_present()
    {
        var root = TestSupport.TempDir("fwdep-");
        var bin = Path.Combine(root, "R5", "Binaries", "Win64", "ue4ss");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "UE4SS.dll"), "x");
        var ctx = Ctx(root, "ue-pak", "R5/Content/Paks/~mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "UE4SS");
    }

    [Fact]
    public void Ue_pak_without_ue4ss_returns_ue4ss_as_missing()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "ue-pak", "R5/Content/Paks/~mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "UE4SS");
    }

    [Fact]
    public void Bepinex_present_via_winhttp_at_root()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "winhttp.dll"), "x");
        var ctx = Ctx(root, "bepinex", "BepInEx/plugins");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "BepInEx");
    }

    [Fact]
    public void Bepinex_absent_returns_bepinex_as_missing()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "bepinex", "BepInEx/plugins");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "BepInEx");
    }

    [Fact]
    public void Smapi_present_via_exe_at_root()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "StardewModdingAPI.exe"), "x");
        var ctx = Ctx(root, "smapi", "Mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "SMAPI");
    }

    [Fact]
    public void Fromsoft_returns_both_me2_and_dll_proxy_when_neither_present()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "fromsoft", "mod");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "Mod Engine 2");
        Assert.Contains(missing, d => d.Name.StartsWith("DLL proxy"));
    }

    [Fact]
    public void Fromsoft_with_dinput8_satisfies_dll_proxy_only()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "dinput8.dll"), "x");
        var ctx = Ctx(root, "fromsoft", "mod");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name.StartsWith("DLL proxy"));
        Assert.Contains(missing, d => d.Name == "Mod Engine 2"); // still missing
    }

    [Fact]
    public void Unknown_engine_returns_empty()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "custom", "mods");

        Assert.Empty(FrameworkDeps.CheckPresent(ctx));
    }
}
```

Run: `--filter FullyQualifiedName~FrameworkDepsCheckPresent` — RED.

- [ ] **Step 2: Implement the probe**

Append to `src/ModManager.Core/FrameworkDeps.cs` (inside the `FrameworkDeps` static class):

```csharp
/// <summary>
/// Return the catalog entries that are NOT present on disk for the active engine.
/// Empty = nothing missing. For UE-pak games, detect paths are probed under each
/// mod-location's project subfolder (e.g. <c>R5/Binaries/Win64/ue4ss/UE4SS.dll</c>),
/// then under the bare game root as a fallback. For non-UE engines, detect paths are
/// resolved relative to the game root only.
/// </summary>
public static IReadOnlyList<FrameworkDep> CheckPresent(GameContext ctx)
{
    var engine = ctx.Game.Engine ?? "";
    var entries = Catalog.Where(d => d.Engine == engine).ToList();
    if (entries.Count == 0) return Array.Empty<FrameworkDep>();

    var roots = ResolveProbeRoots(ctx);
    var missing = new List<FrameworkDep>();
    foreach (var dep in entries)
    {
        if (!IsAnyPathPresent(dep.DetectRelativePaths, roots))
            missing.Add(dep);
    }
    return missing;
}

// For UE-pak: the project subfolders extracted from each resolved primary mod-location path
// (the first path segment of the relative form: "R5/Content/Paks/~mods" -> "R5"), plus the
// bare game root as a fallback. For everything else: just the game root.
private static IReadOnlyList<string> ResolveProbeRoots(GameContext ctx)
{
    var roots = new List<string>();
    if (ctx.Game.Engine == "ue-pak")
    {
        foreach (var loc in ctx.Game.ModLocations)
        {
            var sub = ProjectSubfolder(loc.Path);
            if (sub is null) continue;
            var abs = System.IO.Path.Combine(ctx.GameRoot, sub);
            if (!roots.Contains(abs)) roots.Add(abs);
        }
    }
    if (!roots.Contains(ctx.GameRoot)) roots.Add(ctx.GameRoot);
    return roots;
}

// Pull the project subfolder from a UE mod-location path: "R5/Content/Paks/~mods" -> "R5".
// A path that starts with "Content/" (root-level fallback) has no project subfolder.
private static string? ProjectSubfolder(string relPath)
{
    if (string.IsNullOrWhiteSpace(relPath)) return null;
    var norm = relPath.Replace('\\', '/').TrimStart('/');
    var first = norm.Split('/')[0];
    if (string.IsNullOrEmpty(first)) return null;
    if (string.Equals(first, "Content", StringComparison.OrdinalIgnoreCase)) return null;
    return first;
}

private static bool IsAnyPathPresent(IReadOnlyList<string> relPaths, IReadOnlyList<string> roots)
{
    foreach (var root in roots)
        foreach (var rel in relPaths)
            if (PathExists(System.IO.Path.Combine(root, rel)))
                return true;
    return false;
}

// File.Exists for files; Directory.Exists for directories (Forge/Fabric libraries/ subtrees).
private static bool PathExists(string p)
{
    try { return System.IO.File.Exists(p) || System.IO.Directory.Exists(p); }
    catch { return false; }
}
```

Run: `--filter FullyQualifiedName~FrameworkDepsCheckPresent` — GREEN.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.Core/FrameworkDeps.cs tests/ModManager.Tests/FrameworkDepsCheckPresentTests.cs
git commit -m "feat(core): FrameworkDeps.CheckPresent — probe missing frameworks per game"
```

---

## Task 4: ViewModel — `MainViewModel.MissingFrameworks` + status banner wiring

**Files:**
- Edit: `src/ModManager.App/ViewModels/MainViewModel.cs`

Expose the probe through the VM as an `ObservableCollection<FrameworkDep>` so the UI can bind a banner under the toolbar. Refresh inside `ReloadModsAsync` (one canonical refresh point — game switch, post-drop reload, and Redetect all funnel through it).

- [ ] **Step 1: Add the observable property**

Near the other `[ObservableProperty]` declarations at the top of `MainViewModel` (search for `private bool hasGame`):

```csharp
/// <summary>Framework dependencies the active game is missing — surfaced as a status banner.
/// Refreshed at every <see cref="ReloadModsAsync"/>. Empty = nothing missing (banner hidden).</summary>
public ObservableCollection<FrameworkDep> MissingFrameworks { get; } = new();

/// <summary>Bound to the banner's Visibility — true when at least one framework is missing.</summary>
public bool HasMissingFrameworks => MissingFrameworks.Count > 0;

/// <summary>One-line summary for the banner ("Missing: UE4SS"). Multiple frameworks comma-joined.</summary>
public string MissingFrameworksSummary => MissingFrameworks.Count == 0
    ? ""
    : "Missing: " + string.Join(", ", MissingFrameworks.Select(d => d.Name));
```

(Add `using ModManager.Core;` if missing — it's almost certainly already there.)

- [ ] **Step 2: Refresh inside `ReloadModsAsync`**

Find the end of the `try { ... }` body in `ReloadModsAsync` (just before the lines `OnPropertyChanged(nameof(EffectiveLaunchTarget));` and `OnPropertyChanged(nameof(LaunchButtonLabel));`). Append:

```csharp
            // Refresh missing-framework state every reload — game switch, post-drop, Redetect
            // all funnel through here. Pure probe + a small collection diff so the banner binding
            // updates without re-creating items needlessly.
            MissingFrameworks.Clear();
            foreach (var dep in FrameworkDeps.CheckPresent(_ctx))
                MissingFrameworks.Add(dep);
            OnPropertyChanged(nameof(HasMissingFrameworks));
            OnPropertyChanged(nameof(MissingFrameworksSummary));
```

- [ ] **Step 3: Also clear on the no-game branch**

Find the `if (_ctx is null)` block at the top of `ReloadModsAsync` (it sets `StatusText = "No game registered..."`). Just before the `return;`, add:

```csharp
            MissingFrameworks.Clear();
            OnPropertyChanged(nameof(HasMissingFrameworks));
            OnPropertyChanged(nameof(MissingFrameworksSummary));
```

- [ ] **Step 4: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

If a copy-lock error mentions a locked `ModManager.Core.dll`, kill `ModManager.App.exe` (and `testhost.exe` if present) and rebuild.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): MainViewModel.MissingFrameworks — refresh in ReloadModsAsync"
```

---

## Task 5: ViewModel — `ModRowViewModel.MissingFrameworkChip` + visibility

**Files:**
- Edit: `src/ModManager.App/ViewModels/ModRowViewModel.cs`

Each row gets a `NEEDS X` chip when its engine's framework is missing. The row VM doesn't know the game context, so the parent VM passes the missing-framework name in at row construction (via `init` property) — mirrors how `ModFolderAbs` is wired in `ReloadModsAsync`.

- [ ] **Step 1: Add the row-level fields**

Near the other `init` properties on `ModRowViewModel` (the cluster around `public string ModFolderAbs { get; init; } = "";` and `public string? ReadmeFilePath`), add:

```csharp
/// <summary>Display name of the framework this mod's engine is missing ("UE4SS"); empty when
/// nothing's missing. Set by the parent VM from <see cref="MainViewModel.MissingFrameworks"/>.</summary>
public string MissingFrameworkName { get; init; } = "";

/// <summary>Get-link URL for the missing framework. Opened via HyperlinkButton.NavigateUri
/// (SafeUrl guards to https only).</summary>
public string? MissingFrameworkUrl { get; init; }

/// <summary>One-sentence why-it-matters for the tooltip.</summary>
public string MissingFrameworkNote { get; init; } = "";

public bool HasMissingFramework => !string.IsNullOrEmpty(MissingFrameworkName);
public Visibility MissingFrameworkVisibility => HasMissingFramework ? Visibility.Visible : Visibility.Collapsed;

/// <summary>Chip label: "NEEDS UE4SS". Uppercased to match the existing chip convention.</summary>
public string MissingFrameworkChip => HasMissingFramework
    ? "NEEDS " + MissingFrameworkName.ToUpperInvariant()
    : "";

public Uri? MissingFrameworkUri => SafeUrl.IsHttpUrl(MissingFrameworkUrl) ? new Uri(MissingFrameworkUrl!) : null;
```

- [ ] **Step 2: Wire row construction in `MainViewModel.ReloadModsAsync`**

In `MainViewModel.ReloadModsAsync`, inside the `foreach (var fam in VariantGroups.Group(list))` loop, before the existing `rows.Add(new ModRowViewModel(...))` call, derive the chip data from the engine. For v1, we attach the chip to **every row** when the engine has a single missing framework — the spec narrows by drop verdict, but for the persistent row chip the simple posture is "engine X is missing framework Y, every row in the active game gets the chip until it's installed." For FromSoft (two frameworks possible), pick the more-impactful one: ME2 if missing AND any folder mod exists, else the DLL proxy.

Add right above the `rows.Add(...)` call:

```csharp
                // Row-level missing-framework chip. v1 attaches the chip to every row when the
                // engine has a missing framework — keeps the model simple. FromSoft has two
                // candidates; prefer ME2 for folder-mod rows, DLL proxy for direct-inject rows.
                var primaryMissing = MissingFrameworks.FirstOrDefault();
                if (_ctx.Game.Engine == "fromsoft" && MissingFrameworks.Count > 1)
                {
                    primaryMissing = rep.IsFolder
                        ? MissingFrameworks.FirstOrDefault(d => d.Name == "Mod Engine 2") ?? primaryMissing
                        : MissingFrameworks.FirstOrDefault(d => d.Name.StartsWith("DLL proxy")) ?? primaryMissing;
                }
```

Then extend the `new ModRowViewModel(...) { ... }` initializer with three more `init` assignments:

```csharp
                    MissingFrameworkName = primaryMissing?.Name ?? "",
                    MissingFrameworkUrl = primaryMissing?.GetUrl,
                    MissingFrameworkNote = primaryMissing?.Note ?? "",
```

- [ ] **Step 3: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

- [ ] **Step 4: Add the chip in `MainWindow.xaml`**

Find the `<StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">` chip strip in the mod-row `DataTemplate` (around line 365 in master). The chips currently start with the `MANAGED` badge. Insert the new chip as the FIRST child of that StackPanel (stakes-first — the missing-framework chip is red and lives left of every other chip):

```xml
                            <HyperlinkButton NavigateUri="{x:Bind MissingFrameworkUri, Mode=OneWay}"
                                             Padding="0" BorderThickness="0" Background="Transparent"
                                             Visibility="{x:Bind MissingFrameworkVisibility, Mode=OneWay}"
                                             ToolTipService.ToolTip="{x:Bind MissingFrameworkNote, Mode=OneWay}">
                                <Border CornerRadius="3" Padding="6,2"
                                        BorderThickness="1" BorderBrush="{StaticResource ThemeDanger}">
                                    <TextBlock Text="{x:Bind MissingFrameworkChip, Mode=OneWay}"
                                               FontFamily="Consolas" FontSize="11"
                                               Foreground="{StaticResource ThemeDanger}" />
                                </Border>
                            </HyperlinkButton>
```

Rebuild: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/ModRowViewModel.cs src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml
git commit -m "feat(app): NEEDS-framework chip on every mod row when framework is missing"
```

---

## Task 6: Drop-time callout — extend `AddModsAsync` status line

**Files:**
- Edit: `src/ModManager.App/ViewModels/MainViewModel.cs`

When `AddModsAsync` lands a mod via the regular intake (the `Scanner.ExecuteIntake` branch — NOT direct-inject, which has its own status line; the spec example is the Windrose UE-pak case which lands here), and the active game has a missing framework, append a "needs X" line to the status. Direct-inject's branch also benefits — we add the same append there.

Approach: a small private helper that returns the suffix string for the current missing-framework state, called once per branch right before each branch's `StatusText = ...` assignment. The helper returns `""` when nothing's missing or when the engine isn't one where the drop would have triggered a need.

- [ ] **Step 1: Add the helper at the bottom of the `MainViewModel` class**

Find a quiet spot near `UpdateStatus()` (around line 321 in master). Insert:

```csharp
/// <summary>Suffix for the post-drop status line when the active game has a missing framework.
/// Empty string when nothing's missing. The drop status line gets ". Heads up: this mod needs X
/// — get it at <url>." appended so the user sees the gap the moment they drop.</summary>
private string MissingFrameworkDropSuffix()
{
    if (MissingFrameworks.Count == 0) return "";
    var dep = MissingFrameworks[0];
    // Trim the URL to a host-ish form so the status line stays readable. The persistent chip
    // carries the full clickable link; this is the just-dropped callout.
    var host = "";
    try { host = new Uri(dep.GetUrl).Host; } catch { host = dep.GetUrl; }
    return $". Heads up: this mod needs {dep.Name} — get it at {host}.";
}
```

- [ ] **Step 2: Append the suffix in the regular intake branch**

In `AddModsAsync`, find the final `StatusText = string.Join(". ", statusParts) + ...` assignment in the non-direct-inject branch (the one that ends with the `Identified {identified} on CurseForge` clause, around line 1014 in master). Append `+ MissingFrameworkDropSuffix()`:

```csharp
            StatusText = string.Join(". ", statusParts)
                + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
                + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
                + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "")
                + MissingFrameworkDropSuffix();
            await ReloadModsAsync();
```

Note: `await ReloadModsAsync()` runs AFTER the status-line assignment, but `MissingFrameworks` was last refreshed at the previous reload. That's the right read — the framework state did not change because of a mod drop (frameworks are installed by the user separately), so the pre-drop state is the correct read for the just-finished drop.

- [ ] **Step 3: Append the suffix in the direct-inject branch**

Earlier in `AddModsAsync`, find the direct-inject branch's `StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}..."` (around line 925 in master). Append the same suffix:

```csharp
                StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
                    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : ".")
                    + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
                    + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "")
                    + MissingFrameworkDropSuffix();
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): post-drop status line names the missing framework + get-link"
```

---

## Task 7: Smoke + PR

**Files:** none (smoke + PR only)

Lay hands on the running app to confirm the user-facing behavior matches the spec. Then push and open a PR off `master` (DO NOT stack — independent branch per the keystone law).

- [ ] **Step 1: Full test suite green**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

All previously-green tests still green; the 11+ new framework-dep tests green.

- [ ] **Step 2: App build clean**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

No warnings beyond the existing baseline.

- [ ] **Step 3: Manual smoke — UE4SS missing case**

Launch the app. Switch to Windrose (or any `ue-pak` game) on a machine where `R5/Binaries/Win64/ue4ss/UE4SS.dll` does NOT exist (rename the folder temporarily if it does).

Verify:
- The mod-row chip strip shows a red `NEEDS UE4SS` chip on every mod row.
- Clicking the chip opens `https://github.com/UE4SS-RE/RE-UE4SS/releases` in the browser.
- Restore the folder, click Redetect; the chip disappears.

- [ ] **Step 4: Manual smoke — drop callout**

With UE4SS still missing, drop a `.pak` mod onto the window. The post-drop status line should end with `. Heads up: this mod needs UE4SS — get it at github.com.`

- [ ] **Step 5: Manual smoke — FromSoft case**

Switch to Elden Ring (or another `fromsoft` game). With neither `dinput8.dll` nor `modengine2_launcher.exe` present, every direct-inject row shows `NEEDS DLL PROXY (DINPUT8/VERSION/WINHTTP)`; every folder row (if any) shows `NEEDS MOD ENGINE 2`. With `dinput8.dll` present, only the ME2 chip remains on folder rows.

- [ ] **Step 6: Commit the plan + push branch + open PR**

```bash
git status
git push -u origin <current-branch>
gh pr create --base master --title "feat: mod-dependency detection — chip + drop callout" --body "$(cat <<'EOF'
## Summary

Surface missing-framework dependencies so a Windrose UE4SS mod (or any framework-gated mod) never silently fails.

- New pure `FrameworkDeps.Catalog` + `CheckPresent(GameContext)` probe in Core (6 frameworks: UE4SS, BepInEx, SMAPI, ME2, DLL proxy, Forge/Fabric)
- Red `NEEDS X` chip on every mod row when the engine's framework is missing; clicking opens the get-link
- Post-drop status line names the missing framework + host

Spec: `docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md`
Plan: `docs/superpowers/plans/2026-05-26-mod-dependency-detection.md`

## Test plan

- [x] `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — all green
- [x] Manual smoke: UE4SS missing → chip + drop-line appears, click opens release page
- [x] Manual smoke: UE4SS present → no chip, no drop-line
- [x] Manual smoke: FromSoft two-framework case (DLL proxy + ME2) — correct chip per row type

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

Do NOT push if any test failed or the manual smoke missed.

---

## Plan-commit step (run FIRST, before any task work)

```bash
git add docs/superpowers/plans/2026-05-26-mod-dependency-detection.md
git commit -m "docs: implementation plan for mod-dependency detection"
```
