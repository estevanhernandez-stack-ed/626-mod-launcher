# Coordination Stage 1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace today's hardcoded `Managed = read-only` with runtime ownership detection, so the launcher coexists correctly with whichever tool actually owns a folder.

**Architecture:** Two new pure-core types — `ToolOwnership.Detect` (reads on-disk markers, the only IO) and `Coordination.PostureFor` (pure detect-and-defer arbitration over an already-detected owner) — wired into `BuildModList`. A new `Mod.ReadOnly` carries the verdict to the VM, which gates toggle/uninstall on it instead of on `Managed`. The `Conductor` branch is a hook that Stage 2 (UE4SS adapter) turns on; in Stage 1 it is always off.

**Tech Stack:** .NET 10, C#, xUnit. Branch: `feat/multi-location-coordination` (off `master`, #13 already merged — do NOT stack).

Reference spec: [docs/2026-05-25-mod-tool-coordination-design.md](2026-05-25-mod-tool-coordination-design.md) §§1-3.

**Test/build commands (this repo's gotchas):**
- Tests: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (run the EXPLICIT project — bare `dotnet test` hangs building WinUI)
- App build: `dotnet build "C:\Users\estev\Projects\626-mod-launcher\src\ModManager.App\ModManager.App.csproj" -p:Platform=x64 --nologo` (kill `ModManager.App.exe` first — file lock)

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/ToolOwnership.cs` (create) | `OwnerTool` enum + `Detect(folderAbs)` — marker IO only |
| `src/ModManager.Core/Coordination.cs` (create) | `Posture` enum + `PostureFor(owner, declaredManaged, loaderCanConduct)` — pure arbitration |
| `src/ModManager.Core/Mod.cs` (modify) | add `bool ReadOnly` |
| `src/ModManager.Core/Scanner.cs` (modify) | `BuildModList` computes owner+posture per location; sets `Managed` (detected ?? declared) + `ReadOnly` |
| `src/ModManager.App/ViewModels/MainViewModel.cs` (modify) | gate `canToggle`/`canUninstall` on `!m.ReadOnly` |
| `tests/ModManager.Tests/ToolOwnershipTests.cs` (create) | marker detection |
| `tests/ModManager.Tests/CoordinationTests.cs` (create) | arbitration truth table |
| `tests/ModManager.Tests/CoordinationScanTests.cs` (create) | end-to-end: detected owner → ReadOnly mods |

---

## Task 1: `ToolOwnership.Detect` — marker detection (IO)

**Files:**
- Create: `tests/ModManager.Tests/ToolOwnershipTests.cs`
- Create: `src/ModManager.Core/ToolOwnership.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/ToolOwnershipTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// Ownership detection from on-disk markers only — the runtime truth of who owns a folder.
public class ToolOwnershipTests
{
    private static string Dir() => TestSupport.TempDir("owner-");

    [Fact]
    public void Detect_vortex_marker_file_is_vortex()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "__folder_managed_by_vortex"), "");
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_vortex_deployment_manifest_is_vortex()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "vortex.deployment.windrose-scripts.json"), "{}");
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_mo2_meta_ini_is_mo2()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "meta.ini"), "[General]");
        Assert.Equal(OwnerTool.Mo2, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_unowned_folder_is_null()
        => Assert.Null(ToolOwnership.Detect(Dir()));

    [Fact]
    public void Detect_missing_folder_is_null()
        => Assert.Null(ToolOwnership.Detect(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"))));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --filter "FullyQualifiedName~ToolOwnership" --nologo`
Expected: FAIL — `ToolOwnership` / `OwnerTool` do not exist (CS0103 / CS0246).

- [ ] **Step 3: Write the implementation**

Create `src/ModManager.Core/ToolOwnership.cs`:

```csharp
namespace ModManager.Core;

/// <summary>An external tool that owns (deploys + tracks) the files in a mod folder.</summary>
public enum OwnerTool { Vortex, Mo2 }

/// <summary>
/// Detects whether another mod manager owns a folder, from on-disk markers only. Reads the
/// filesystem but holds no state and never writes. Returns null when no tool owns the folder.
/// </summary>
public static class ToolOwnership
{
    public static OwnerTool? Detect(string folderAbs)
    {
        if (string.IsNullOrWhiteSpace(folderAbs)) return null;
        try
        {
            if (!Directory.Exists(folderAbs)) return null;
            // Vortex leaves a marker file and/or a deployment manifest where it deploys.
            if (File.Exists(Path.Combine(folderAbs, "__folder_managed_by_vortex"))) return OwnerTool.Vortex;
            if (Directory.EnumerateFiles(folderAbs, "vortex.deployment.*.json").Any()) return OwnerTool.Vortex;
            // Mod Organizer 2 writes a per-mod meta.ini in folders it stages.
            if (File.Exists(Path.Combine(folderAbs, "meta.ini"))) return OwnerTool.Mo2;
            return null;
        }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --filter "FullyQualifiedName~ToolOwnership" --nologo`
Expected: PASS (5 passed).

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/ToolOwnership.cs tests/ModManager.Tests/ToolOwnershipTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: ToolOwnership.Detect — read Vortex/MO2 ownership markers

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: `Coordination.PostureFor` — detect-and-defer arbitration (pure)

**Files:**
- Create: `tests/ModManager.Tests/CoordinationTests.cs`
- Create: `src/ModManager.Core/Coordination.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/CoordinationTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// Detect-and-defer truth table: detected owner wins; else an unowned loader conducts; else a
// declared (profile-hint) Managed value is the conservative fallback to read-only; else we own it.
public class CoordinationTests
{
    [Fact]
    public void Detected_owner_is_coexist()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(OwnerTool.Vortex, null, loaderCanConduct: false));

    [Fact]
    public void Unowned_loader_is_conductor()
        => Assert.Equal(Posture.Conductor, Coordination.PostureFor(null, null, loaderCanConduct: true));

    [Fact]
    public void Declared_managed_with_no_owner_falls_back_to_coexist()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(null, "vortex", loaderCanConduct: false));

    [Fact]
    public void Nothing_known_is_own()
        => Assert.Equal(Posture.Own, Coordination.PostureFor(null, null, loaderCanConduct: false));

    [Fact]
    public void Detected_owner_beats_a_conductable_loader()
        => Assert.Equal(Posture.Coexist, Coordination.PostureFor(OwnerTool.Vortex, null, loaderCanConduct: true));

    [Fact]
    public void Conductable_loader_beats_a_stale_declared_hint()
        => Assert.Equal(Posture.Conductor, Coordination.PostureFor(null, "vortex", loaderCanConduct: true));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --filter "FullyQualifiedName~CoordinationTests" --nologo`
Expected: FAIL — `Coordination` / `Posture` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/ModManager.Core/Coordination.cs`:

```csharp
namespace ModManager.Core;

/// <summary>How the launcher relates to a mod location.</summary>
public enum Posture
{
    Own,        // nobody else manages it — our reversible-move model
    Conductor,  // a loader with a manifest, and unowned — we drive the manifest (no file moves)
    Coexist,    // another manager owns it — read-only, never touch
}

/// <summary>
/// Detect-and-defer arbitration. A detected runtime owner always wins (defer to it). Otherwise a
/// loader that can drive its own manifest takes the folder. A declared (profile-hint) Managed value
/// is the last, conservative fallback to Coexist — never let a stale hint block a real loader. Pure:
/// takes an already-detected owner so all IO lives in <see cref="ToolOwnership.Detect"/>.
/// </summary>
public static class Coordination
{
    public static Posture PostureFor(OwnerTool? owner, string? declaredManaged, bool loaderCanConduct)
    {
        if (owner is not null) return Posture.Coexist;
        if (loaderCanConduct) return Posture.Conductor;
        if (!string.IsNullOrEmpty(declaredManaged)) return Posture.Coexist;
        return Posture.Own;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --filter "FullyQualifiedName~CoordinationTests" --nologo`
Expected: PASS (6 passed).

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Coordination.cs tests/ModManager.Tests/CoordinationTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: Coordination.PostureFor — detect-and-defer arbitration

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: `Mod.ReadOnly` + wire detection into `BuildModList`

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (add `ReadOnly`)
- Modify: `src/ModManager.Core/Scanner.cs` (`BuildModList`, the per-location loop)
- Create: `tests/ModManager.Tests/CoordinationScanTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/CoordinationScanTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// End-to-end: a folder with a Vortex marker yields read-only, vortex-tagged mods EVEN WHEN the
// profile never declared it managed; a plain folder stays ours (toggleable).
public class CoordinationScanTests
{
    [Fact]
    public async Task Detected_vortex_folder_makes_its_mods_readonly_even_if_undeclared()
    {
        var root = TestSupport.TempDir("coord-scan-");
        var scripts = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(scripts, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(scripts, "vortex.deployment.x.json"), "{}"); // marker, but NOT declared
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } }, // no Managed
        });

        var mods = await Scanner.BuildModListAsync(c);
        var pet = mods.First(m => m.Name == "PetBoarPlus");
        Assert.True(pet.ReadOnly);
        Assert.Equal("vortex", pet.Managed);
    }

    [Fact]
    public async Task Plain_location_is_not_readonly()
    {
        var root = TestSupport.TempDir("coord-scan2-");
        var paks = Path.Combine(root, "Paks", "~mods");
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "Cool_P.pak"), "x");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("mods", "~mods", "Paks/~mods") },
        });

        var cool = (await Scanner.BuildModListAsync(c)).First(m => m.Name == "Cool");
        Assert.False(cool.ReadOnly);
        Assert.Null(cool.Managed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --filter "FullyQualifiedName~CoordinationScan" --nologo`
Expected: FAIL — `Mod` has no `ReadOnly`, and detection isn't wired (PetBoarPlus.Managed would be null).

- [ ] **Step 3a: Add `ReadOnly` to `Mod`**

In `src/ModManager.Core/Mod.cs`, after the `Managed` property (the line `public string? Managed { get; set; }`), add:

```csharp
    // True when this mod's location is owned by another tool (Coexist posture): the row is read-only.
    public bool ReadOnly { get; set; }
```

- [ ] **Step 3b: Wire detection into `BuildModList`**

In `src/ModManager.Core/Scanner.cs`, replace the per-location loop opening and both `new Mod { ... }` constructions. Change the loop body from:

```csharp
        foreach (var loc in c.Locations)
        {
            if (loc.Form == "folders")
            {
                foreach (var f in ListSubfolders(loc.Abs))
                {
                    if (outMap.ContainsKey(f)) continue;
                    outMap[f] = new Mod
                    {
                        Name = f, Location = loc.Name, Enabled = true,
                        Files = new List<string> { f }, OnServer = false, IsFolder = true, Managed = loc.Managed,
                    };
                }
            }
            else
            {
                foreach (var f in ListPakFiles(loc.Abs, c))
                {
                    var k = ModKey(f, c);
                    if (!outMap.TryGetValue(k, out var mod))
                    {
                        mod = new Mod { Name = k, Location = loc.Name, Enabled = true, IsFolder = false, Managed = loc.Managed };
                        outMap[k] = mod;
                    }
                    mod.Files.Add(f);
                }
            }
        }
```

to:

```csharp
        foreach (var loc in c.Locations)
        {
            // Runtime ownership decides the posture; the profile's Managed value is only a fallback.
            // (Stage 2 will pass loaderCanConduct=true when a loader adapter claims an unowned folder.)
            var owner = ToolOwnership.Detect(loc.Abs);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: false);
            var readOnly = posture == Posture.Coexist;
            var managedLabel = owner?.ToString().ToLowerInvariant()
                ?? (readOnly ? loc.Managed : null);

            if (loc.Form == "folders")
            {
                foreach (var f in ListSubfolders(loc.Abs))
                {
                    if (outMap.ContainsKey(f)) continue;
                    outMap[f] = new Mod
                    {
                        Name = f, Location = loc.Name, Enabled = true, Files = new List<string> { f },
                        OnServer = false, IsFolder = true, Managed = managedLabel, ReadOnly = readOnly,
                    };
                }
            }
            else
            {
                foreach (var f in ListPakFiles(loc.Abs, c))
                {
                    var k = ModKey(f, c);
                    if (!outMap.TryGetValue(k, out var mod))
                    {
                        mod = new Mod
                        {
                            Name = k, Location = loc.Name, Enabled = true, IsFolder = false,
                            Managed = managedLabel, ReadOnly = readOnly,
                        };
                        outMap[k] = mod;
                    }
                    mod.Files.Add(f);
                }
            }
        }
```

- [ ] **Step 4: Run the full suite to verify pass + no regressions**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo`
Expected: PASS. The new `CoordinationScan` tests pass; existing `ScannerLocationFormTests` still pass (the `ue4ss` temp dir there has no marker, so `Managed` falls back to the declared `"vortex"` and the `mods` location stays null — unchanged).

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Mod.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/CoordinationScanTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: wire runtime ownership detection into the scan (Mod.ReadOnly)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: Gate the VM toggle/uninstall on `ReadOnly`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (the row construction, ~line 173)

This is an App-layer change (no headless test — verified by build + the Core tests above that prove `ReadOnly` is set correctly). Switching the gate from `Managed is null` to `!ReadOnly` means a row is read-only exactly when the coordination posture says Coexist, not merely when a label is present.

- [ ] **Step 1: Make the change**

In `src/ModManager.App/ViewModels/MainViewModel.cs`, change:

```csharp
                    rows.Add(new ModRowViewModel(m, canToggle: m.Managed is null, canUninstall: !directInject && m.Managed is null)
```

to:

```csharp
                    rows.Add(new ModRowViewModel(m, canToggle: !m.ReadOnly, canUninstall: !directInject && !m.ReadOnly)
```

- [ ] **Step 2: Build the App to verify it compiles**

```
Stop-Process -Name ModManager.App -Force -ErrorAction SilentlyContinue
dotnet build "C:\Users\estev\Projects\626-mod-launcher\src\ModManager.App\ModManager.App.csproj" -p:Platform=x64 --nologo
```
Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.App/ViewModels/MainViewModel.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: gate row toggle/uninstall on coordination ReadOnly, not the Managed label

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: Full verification + manual smoke

- [ ] **Step 1: Full test suite green**

Run: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo`
Expected: all pass, only the 2 known 7z/rar fixture skips.

- [ ] **Step 2: Manual smoke (live Windrose)**

Launch the app. The UE4SS script mods still show read-only with the `VORTEX` badge — but now because the folder's `vortex.deployment.*.json` marker was *detected at runtime*, not because the profile hardcoded it. Confirm: removing `"managed": "vortex"` from the `ue4ss` location in `%APPDATA%\ModManagerBuilder\games.json` still leaves those mods read-only + badged (detection covers it).

- [ ] **Step 3: Push (PR #14 already open on this branch — pushing updates it)**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" push
```

---

## Self-Review

**Spec coverage (§§1-3):** §1 ToolOwnership.Detect → Task 1. §3 Coordination.PostureFor detect-and-defer → Task 2. "Wired into the scan/VM, upgrades the hardcoded read-only" → Tasks 3-4. The `Conductor` branch (§2 loaders) is intentionally a Stage-1 hook (`loaderCanConduct: false`), filled by Stage 2 — noted in the BuildModList comment and the Coordination doc-comment. ✅

**Placeholder scan:** none — every step has full code/commands. ✅

**Type consistency:** `OwnerTool` { Vortex, Mo2 }, `Posture` { Own, Conductor, Coexist }, `PostureFor(OwnerTool?, string?, bool)`, `ToolOwnership.Detect(string)`, `Mod.ReadOnly` — names match across Tasks 1-4. `owner?.ToString().ToLowerInvariant()` yields `"vortex"` / `"mo2"`, matching the `Managed` label asserted in Task 3 and the `ManagedBadge` (uppercased in the VM). ✅
