# Paks-root + base-game filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make loader-less UE-pak games (Witchfire) first-class — manage mods that live directly in `Content/Paks` alongside the base game, showing the user's mod paks and never listing or moving the base-game paks.

**Architecture:** A pure-Core `PakClassifier` is the single source of truth for base-vs-mod (name pattern OR size). A new `"paks-root"` location form makes `Scanner.BuildModList` filter base paks out of a shared Paks folder; a hard guard in the disable/move path refuses to ever move a base-classified pak. The `ue-pak` engine preset auto-detects `~mods` (loader) vs `paks-root` (loader-less) at add-time.

**Tech Stack:** .NET 10 / C#, xUnit (headless Core tests), `System.IO` + `System.Text.RegularExpressions` in Core. camelCase JSON on disk (the `Form` value persists as a plain string — no schema change).

**Reference reading before starting:**
- Spec: `docs/superpowers/specs/2026-06-06-paks-root-base-game-filter-design.md`
- `src/ModManager.Core/Scanner.cs` — key seams:
  - `SafeReadFiles(dir)` (~line 149) returns file NAMES (drops `Directory.GetFiles` full paths to names).
  - `ListPakFiles(dir, c)` (~line 161) = `SafeReadFiles(dir).Where(n => c.FileRe.IsMatch(n))` — names only, no size.
  - `BuildModList(c)` (~line 169), the pak branch is the `else` at ~line 220-235: `foreach (var f in ListPakFiles(loc.Abs, c)) { var k = ModKey(f, c); ... }`. `loc.Form` is `"files"`/`"folders"` today; `loc.Abs` is the absolute folder.
  - `ModKey(filename, c)` (~line 137) strips ext + applies `groupingRule` (`strip_underscore_p_suffix` → `Regex.Replace(baseName, "_[Pp]$", "")`).
  - `DisableEntry(Mod m, GameContext c)` (~line 405) and `DisableMod(name, c)` (~line 370) — the pak move sites. `LocByName(m.Location, c).Abs` resolves the folder; files move via `MoveAny(Path.Combine(loc.Abs, f), ...)`.
- `src/ModManager.Core/GameContext.cs` — `ModLocationCtx` record has `Form { get; init; }` (string) + `Abs`.
- `src/ModManager.Core/EnginePresets.cs:14-15` — the `ue-pak` preset row: `new("Unreal Engine 4/5 (.pak)", new[]{"pak","ucas","utoc"}, "strip_underscore_p_suffix", "Content/Paks/~mods", "...")`. This is the static default; the add-time location resolver is where on-disk detection goes (find where the preset's mod-path becomes a game's ModLocations — search for where AddGame / detection builds `ModLocation` from the preset).
- `.claude/rules/camelcase-json-on-disk.md`.

**Build/test commands (Windows):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- One filter: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~<Class>"`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- NEVER run bare `dotnet test`/`dotnet build` at the repo root (the WinUI project hangs). `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on Core — use analyzer-preferred xUnit assertions (`Assert.Contains(item, coll)`, not `Assert.True(coll.Contains())`).

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ModManager.Core/PakClassifier.cs` | NEW. `IsBaseGamePak(name, size)` — the base-vs-mod rule (name pattern OR size). Pure, no IO. |
| `src/ModManager.Core/Scanner.cs` | MODIFY. A size-carrying pak list; the `paks-root` branch in `BuildModList`; the base-pak guard in the disable/move path. |
| `src/ModManager.Core/EnginePresets.cs` (+ the add-time location resolver) | MODIFY. `ue-pak` preset/detection picks `paks-root` vs `~mods` by on-disk check. |
| `tests/ModManager.Tests/PakClassifierTests.cs` | NEW. |
| `tests/ModManager.Tests/PaksRootScanTests.cs` | NEW. |
| `tests/ModManager.Tests/PaksRootPresetTests.cs` | NEW (or fold into an existing preset test if one exists). |

No App code — `paks-root` yields the same `Mod` rows the existing pak path renders.

---

## Task 1: PakClassifier — the base-vs-mod rule

**Files:**
- Create: `src/ModManager.Core/PakClassifier.cs`
- Test: `tests/ModManager.Tests/PakClassifierTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PakClassifierTests
{
    private const long GB = 1024L * 1024 * 1024;
    private const long MB = 1024L * 1024;

    [Theory]
    // Witchfire's real paks (name + size).
    [InlineData("pakchunk0-WindowsNoEditor.pak", 4L * 1024 * 1024 * 1024, true)]   // base: name + size
    [InlineData("pakchunk0optional-WindowsNoEditor.pak", 591L * 1024 * 1024, true)] // base: name only (modest size)
    [InlineData("pakchunk30-2x-witchfire_P.pak", 6 * 1024, false)]                 // mod
    [InlineData("zz_Funner_Witchfire.pak", 22L * 1024 * 1024, false)]              // mod
    public void Classifies_Witchfire_paks(string name, long size, bool expectedBase)
        => Assert.Equal(expectedBase, PakClassifier.IsBaseGamePak(name, size));

    [Fact]
    public void Size_alone_flags_an_unconventionally_named_huge_pak_as_base()
        // No pakchunk/WindowsNoEditor in the name, but multi-GB → base (the size safety net).
        => Assert.True(PakClassifier.IsBaseGamePak("Witchfire-WindowsClient.pak", 3L * 1024 * 1024 * 1024));

    [Fact]
    public void Name_alone_flags_a_modestly_sized_base_chunk_as_base()
        // Conventional name, small size → still base (name match wins).
        => Assert.True(PakClassifier.IsBaseGamePak("pakchunk12-WindowsNoEditor.pak", 2 * 1024 * 1024));

    [Fact]
    public void A_normal_mod_pak_is_not_base()
        => Assert.False(PakClassifier.IsBaseGamePak("CoolWeapon_P.pak", 5 * 1024 * 1024));

    [Fact]
    public void Accepted_edge_a_mod_named_like_a_base_pak_is_treated_as_base()
        // Documented tradeoff: a mod aping the base name is hidden. Pinned so it's intentional.
        => Assert.True(PakClassifier.IsBaseGamePak("pakchunk0-WindowsNoEditor.pak", 3 * 1024));

    [Fact]
    public void Case_insensitive_on_the_name_pattern()
        => Assert.True(PakClassifier.IsBaseGamePak("PakChunk5-WindowsNoEditor.PAK", 1 * 1024 * 1024));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PakClassifierTests"`
Expected: FAIL — `PakClassifier` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Single source of truth for "is this pak the base game or a mod" when both share one folder (a
/// loader-less UE-pak game like Witchfire drops mods straight into Content/Paks alongside the game's
/// own paks). Base-game paks must never be listed as mods or moved by a toggle. Pure — name + size only,
/// no IO. The two signals are OR'd: a conventional shipping name OR an implausibly large size means base.
/// </summary>
public static class PakClassifier
{
    // UE packaged-game convention: pakchunk<N>[optional]-WindowsNoEditor.pak. Case-insensitive.
    private static readonly Regex ShippingPakName =
        new(@"^pakchunk\d+.*-WindowsNoEditor\.pak$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A pak no real mod reaches — above this it's treated as base game even if the name doesn't
    /// match the shipping convention. Well above any real mod pak, below the multi-GB base chunks.</summary>
    public const long ModSizeCeilingBytes = 1536L * 1024 * 1024; // 1.5 GB

    /// <summary>True when <paramref name="fileName"/> + <paramref name="sizeBytes"/> indicate a base-game
    /// pak (hide + protect). Name pattern OR size — see the class summary.</summary>
    public static bool IsBaseGamePak(string fileName, long sizeBytes)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = System.IO.Path.GetFileName(fileName);
        return ShippingPakName.IsMatch(name) || sizeBytes >= ModSizeCeilingBytes;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PakClassifierTests"`
Expected: PASS (8 cases — 4 Theory rows + 4 facts... count is the xUnit total; just confirm 0 failed).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/PakClassifier.cs tests/ModManager.Tests/PakClassifierTests.cs
git commit -m "feat(scanner): PakClassifier — base-game-vs-mod pak rule (name OR size)"
```

---

## Task 2: A size-carrying pak list in the scanner

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs`
- Test: covered indirectly by Task 3; no standalone test (it's a private helper). Confirm via the full suite staying green.

The classifier needs size, but `ListPakFiles` returns names only. Add a private helper that returns `(name, size)` pairs, WITHOUT changing the existing `ListPakFiles` (other callers rely on its name-only shape).

- [ ] **Step 1: Add the helper near `ListPakFiles` (~line 162)**

```csharp
    // Pak files in a dir as (name, sizeBytes) — used by the paks-root branch to classify base vs mod.
    // Separate from ListPakFiles (name-only, relied on by other callers) so neither changes the other.
    private static IReadOnlyList<(string Name, long Size)> ListPakFilesWithSize(string dir, GameContext c)
    {
        var outList = new List<(string, long)>();
        try
        {
            foreach (var full in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(full);
                if (name is null || !c.FileRe.IsMatch(name)) continue;
                long size; try { size = new FileInfo(full).Length; } catch { size = 0; }
                outList.Add((name, size));
            }
        }
        catch { /* unreadable dir -> empty, same as SafeReadFiles */ }
        return outList;
    }
```

- [ ] **Step 2: Build to confirm it compiles (no behavior change yet)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj 2>&1 | findstr /C:"Passed!" /C:"Failed!"`
Expected: full suite still passes (the helper is unused so far — confirms it compiles under warnings-as-errors; an unused private method is a CS0169/IDE warning, NOT an error, but if the build treats it as error, it'll be consumed in Task 3 — proceed to Task 3 before worrying, or temporarily it's fine because methods don't trip CS0169 (that's fields). Private unused METHODS do not error.)

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.Core/Scanner.cs
git commit -m "feat(scanner): ListPakFilesWithSize helper for base-game classification"
```

---

## Task 3: The `paks-root` branch in BuildModList

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs`
- Test: `tests/ModManager.Tests/PaksRootScanTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PaksRootScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksroot-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A Witchfire-shaped game: Content/Paks holds 2 base paks + 2 mod paks, location form = paks-root.
    private GameEntry Setup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var paks = Path.Combine(gameRoot, "Witchfire", "Content", "Paks");
        Directory.CreateDirectory(paks);
        // Base game (must be hidden). Write a small file but force the name pattern to classify it base.
        File.WriteAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"), "base");
        File.WriteAllText(Path.Combine(paks, "pakchunk0optional-WindowsNoEditor.pak"), "baseopt");
        // Mods (must show).
        File.WriteAllText(Path.Combine(paks, "pakchunk30-2x-witchfire_P.pak"), "mod1");
        File.WriteAllText(Path.Combine(paks, "zz_Funner_Witchfire.pak"), "mod2");

        return new GameEntry
        {
            Id = "witchfire", GameName = "Witchfire", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "strip_underscore_p_suffix",
            FileExtensions = new[] { "pak", "ucas", "utoc" },
            DataDir = Path.Combine(_tmp, "data"),
            ModLocations = new[] { new ModLocation("mods", "Paks", "Witchfire/Content/Paks") { Form = "paks-root" } },
        };
    }

    [Fact]
    public async Task PaksRoot_lists_the_mods_and_never_the_base_game()
    {
        var game = Setup();
        Directory.CreateDirectory(game.DataDir!);
        var ctx = Scanner.GameContext(game);

        var mods = await Scanner.BuildModListAsync(ctx);
        var names = mods.Select(m => m.Name).ToList();

        // The two mods are present (grouped key: _P stripped on the first).
        Assert.Contains("pakchunk30-2x-witchfire", names);
        Assert.Contains("zz_Funner_Witchfire", names);
        // The base game is NEVER listed.
        Assert.DoesNotContain(names, n => n.Contains("WindowsNoEditor"));
        Assert.DoesNotContain(names, n => n.StartsWith("pakchunk0"));
        Assert.Equal(2, mods.Count);
    }
}
```

IMPORTANT: verify `ModLocation` has a settable `Form` (it's `public string? Form` on the `ModLocation` record in `GameEntry.cs` — confirm; the games.json shape shows `"form": null`). And confirm `GameEntry` member names (`Id`, `GameName`, `Engine`, `GameRoot`, `GroupingRule`, `FileExtensions`, `DataDir`, `ModLocations`) by reading `src/ModManager.Core/GameEntry.cs` — match the real shape (the VanillaLaunchScanTests / other scan tests in the suite are a working reference for constructing a GameEntry).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaksRootScanTests"`
Expected: FAIL — today the `paks-root` form falls through to the default pak branch (form isn't `"folders"`), which lists ALL paks including the base game, so `mods.Count` is 4 and the base-game assertions fail.

- [ ] **Step 3: Add the paks-root branch in `BuildModList`**

Find the pak `else` branch (~line 220-235):

```csharp
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
```

Replace the `foreach` line so a `paks-root` location filters base paks. Insert this just before that `foreach`, and change the loop source:

```csharp
            else
            {
                // paks-root (loader-less UE games like Witchfire): mods live in Content/Paks alongside
                // the base game, so filter base-game paks out by classifier. Other pak forms ("files")
                // are unchanged — they list every pak (no base game is mixed into a dedicated mod folder).
                var pakFiles = loc.Form == "paks-root"
                    ? ListPakFilesWithSize(loc.Abs, c)
                        .Where(p => !PakClassifier.IsBaseGamePak(p.Name, p.Size))
                        .Select(p => p.Name)
                    : ListPakFiles(loc.Abs, c);

                foreach (var f in pakFiles)
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
```

Note: the branch dispatch above this `else` decides "folders" vs the pak path. Confirm a `paks-root` location reaches THIS `else` (it should — only `loc.Form == "folders"` takes the folders branch; `paks-root` is not `"folders"`, so it falls here). If the dispatch is `if (loc.Form == "folders") {...} else {...}`, paks-root correctly lands in the else. Read ~line 178-220 to confirm the exact dispatch and that `isUe4ss`/`isBepInEx` (which key off `Form == "folders"` / engine) don't misroute a paks-root location. paks-root is `files`-like, so `isBepInEx` (`loc.Form != "folders" && engine=="bepinex"`) would be false for a ue-pak game — fine.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaksRootScanTests"`
Expected: PASS. Then full suite `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — ALL pass (confirms the `"files"`/`"folders"` paths are unchanged; the new branch only engages for `paks-root`).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/PaksRootScanTests.cs
git commit -m "feat(scanner): paks-root form filters base-game paks from the mod list"
```

---

## Task 4: The hard base-pak disable guard

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs`
- Test: `tests/ModManager.Tests/PaksRootScanTests.cs` (add a case)

Even though base paks never become rows, a misjudgment or a stale `Mod` object must never let a base pak move. Guard the disable/move path: refuse to move any pak file that classifies as base.

- [ ] **Step 1: Write the failing test (add to `PaksRootScanTests`)**

```csharp
    [Fact]
    public async Task Disabling_a_mod_in_paks_root_never_moves_a_base_game_pak()
    {
        // Construct a (hostile/buggy) Mod whose Files include a base-game pak, and assert the disable
        // path refuses to move it — the game file stays put even if something asks to disable it.
        var game = Setup();
        Directory.CreateDirectory(game.DataDir!);
        var ctx = Scanner.GameContext(game);
        var paks = Path.Combine(ctx.GameRoot, "Witchfire", "Content", "Paks");

        // A mod row that (wrongly) claims a base pak as one of its files.
        var hostile = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scanner.UninstallModAsync(hostile.Name, ctx));   // or the disable entrypoint that takes a name

        Assert.Contains("base game", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"))); // never moved
    }
```

IMPORTANT: the exact disable entrypoint matters. `DisableEntry(Mod, ctx)` is private; the public name-based path is `DisableModAsync(name, ctx)` / `UninstallModAsync(name, ctx)` (read ~line 328-372 to confirm signatures). `DisableModAsync` rebuilds the mod list to find the mod by name — but a base pak is NOT in the list (Task 3 filters it), so a name lookup won't find it and the guard wouldn't be reached via that path. THEREFORE: put the guard INSIDE the lowest-level pak move helper so it protects EVERY path. The right site is `DisableEntry` (~line 405) and any other place that does `MoveAny(Path.Combine(loc.Abs, f), ...)` for a pak in a paks-root location. Adjust the test to call whatever public entrypoint actually reaches a file move; if no public path can construct this hostile case (because the filter prevents it), make the guard a small testable helper and test THAT directly (see Step 3 alt).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Disabling_a_mod_in_paks_root"`
Expected: FAIL — no guard yet (either the move succeeds, or the entrypoint doesn't throw the expected message).

- [ ] **Step 3: Add the guard**

Add a small guard helper and call it in `DisableEntry` before the move loop (and in `DisableMod` if it has a separate move path). Place the helper near `PakClassifier` usage in Scanner:

```csharp
    // Refuse to move a pak that classifies as base game — the operating-law backstop: even a wrong
    // classification or a stale/hostile Mod can never strand the game's own files. Only enforced for
    // paks-root locations (where base + mods share a folder); other forms never mix the two.
    private static void GuardNoBasePakMove(Mod m, ModLocationCtx loc)
    {
        if (loc.Form != "paks-root") return;
        foreach (var f in m.Files)
        {
            long size; try { size = new FileInfo(Path.Combine(loc.Abs, f)).Length; } catch { size = 0; }
            if (PakClassifier.IsBaseGamePak(Path.GetFileName(f), size))
                throw new InvalidOperationException(
                    $"\"{f}\" is a base-game file, not a mod — refusing to move it. Nothing was changed.");
        }
    }
```

In `DisableEntry` (~line 405), right after resolving `var loc = LocByName(m.Location, c);` and BEFORE the move loop (before `Directory.CreateDirectory(dest);`), add:

```csharp
        GuardNoBasePakMove(m, loc);
```

If the hostile test can't reach `DisableEntry` through a public API (because `DisableModAsync` won't find an unlisted mod by name), test `GuardNoBasePakMove` indirectly: instead, write the test to exercise the guard through the real flow — OR make the test assert via a public method that does accept a Mod. Simplest robust approach: change the test to call the guard's effect through `DisableEntry` by making `DisableEntry` reachable, OR (cleaner) keep `GuardNoBasePakMove` `internal` and add `[assembly: InternalsVisibleTo("ModManager.Tests")]` if the project already exposes internals to tests (check `src/ModManager.Core/ModManager.Core.csproj` or an AssemblyInfo for an existing `InternalsVisibleTo` — if present, test the guard directly; if not, do NOT add one just for this — instead test through the public disable path with a real mod row that the scanner DID list, and separately unit-test that `PakClassifier.IsBaseGamePak` returns true for the base pak, which is the guard's decision input).

DECISION for the implementer: if `InternalsVisibleTo("ModManager.Tests")` already exists, make `GuardNoBasePakMove` internal and test it directly with a hostile `Mod`. If it does NOT exist, drop the hostile-Mod test (it can't be constructed through a public path because the filter prevents a base pak from ever becoming a listed mod) and instead keep ONLY: (a) the Task-3 test proving base paks are never listed, and (b) a `PakClassifier` test proving the base pak classifies true — together those prove the guard's input is correct and the row never exists. The guard remains as defense-in-depth even if not directly unit-tested. Report which path you took.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaksRootScanTests"`
Expected: PASS. Then full suite — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/PaksRootScanTests.cs
git commit -m "feat(scanner): hard guard — never move a base-game pak in a paks-root location"
```

---

## Task 5: ue-pak preset auto-detects paks-root vs ~mods

**Files:**
- Modify: `src/ModManager.Core/EnginePresets.cs` and/or the add-time location resolver
- Test: `tests/ModManager.Tests/PaksRootPresetTests.cs`

- [ ] **Step 1: Find the add-time location resolver**

Read `src/ModManager.Core/EnginePresets.cs` fully and search the codebase for where a preset's mod-path string (`"Content/Paks/~mods"`) becomes a game's `ModLocations` when a game is added (grep: `ModLocation(`, `ModPath`, `EnginePresets`, `Detect`, `ResolveModLocations`). That resolver is where on-disk detection belongs. Identify the exact method + signature before writing the test (it likely takes a gameRoot + engine and returns `ModLocation[]` or sets them on a `GameInput`/`GameEntry`).

- [ ] **Step 2: Write the failing test (`tests/ModManager.Tests/PaksRootPresetTests.cs`)**

Adapt the call to the real resolver signature found in Step 1. Shape:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PaksRootPresetTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksroot-preset-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string MakeUeGame(bool withModsSubfolder)
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var paks = Path.Combine(gameRoot, "Witchfire", "Content", "Paks");
        Directory.CreateDirectory(paks);
        if (withModsSubfolder) Directory.CreateDirectory(Path.Combine(paks, "~mods"));
        return gameRoot;
    }

    [Fact]
    public void Loaderless_game_gets_a_paks_root_location_at_Content_Paks()
    {
        var gameRoot = MakeUeGame(withModsSubfolder: false);
        // CALL THE REAL RESOLVER found in Step 1, e.g.:
        var locs = EnginePresets.ResolveModLocations("ue-pak", gameRoot);
        var loc = locs.Single();
        Assert.Equal("paks-root", loc.Form);
        Assert.EndsWith(Path.Combine("Content", "Paks"), loc.Path.Replace('/', Path.DirectorySeparatorChar));
        Assert.DoesNotContain("~mods", loc.Path);
    }

    [Fact]
    public void Loader_game_with_a_mods_subfolder_keeps_the_existing_mods_location()
    {
        var gameRoot = MakeUeGame(withModsSubfolder: true);
        var locs = EnginePresets.ResolveModLocations("ue-pak", gameRoot);
        Assert.Contains(locs, l => l.Path.Replace('\\', '/').EndsWith("Content/Paks/~mods"));
    }
}
```

If the resolver doesn't exist as a clean pure function (detection is currently inlined in AddGame), the implementer should EXTRACT a pure `ResolveModLocations(engine, gameRoot)` (or similar) into `EnginePresets` / a small resolver class, move the existing static-preset behavior into it, then add the ue-pak on-disk branch. This keeps it testable and is a reasonable in-scope improvement (the spec calls for it). Match whatever the existing add-flow expects back.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaksRootPresetTests"`
Expected: FAIL — today ue-pak always returns the static `Content/Paks/~mods` (Form null), so the loaderless test fails (Form != "paks-root").

- [ ] **Step 4: Implement the detection**

In the resolver for `ue-pak`: resolve the project subfolder (the existing logic that turns `Content/Paks/~mods` into `<Project>/Content/Paks/~mods` — reuse `FrameworkDeps.ProjectSubfolder` or whatever the codebase already uses to find the `<Project>` dir; the spec + FrameworkInstaller reference it). Then:

```csharp
// ue-pak: loader (UE4SS) games mount a ~mods subfolder; loader-less games take mods directly in
// Content/Paks alongside the base game. Detect which by what's on disk.
var paksDir = /* <gameRoot>/<project>/Content/Paks resolved absolute */;
var modsSub = Path.Combine(paksDir, "~mods");
var logicSub = Path.Combine(paksDir, "LogicMods");
if (Directory.Exists(modsSub) || Directory.Exists(logicSub))
{
    // existing behavior: a folders/~mods-style location (loader present)
    // return the current ModLocation(s) as today
}
else
{
    // loader-less: manage Content/Paks directly, filtering base-game paks
    return new[] { new ModLocation("mods", "Paks", "<project>/Content/Paks") { Form = "paks-root" } };
}
```

Use the SAME project-subfolder resolution the rest of the codebase uses so the path matches what the scanner expects (relative to gameRoot, e.g. `Witchfire/Content/Paks`). The exact `ModLocation` name/label/path shape must match what the scanner + games.json expect (read an existing ue-pak game's entry — Windrose — for the shape).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PaksRootPresetTests"`
Expected: PASS (both). Then full suite — all pass (existing ue-pak add behavior for loader games unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/EnginePresets.cs tests/ModManager.Tests/PaksRootPresetTests.cs
git commit -m "feat(presets): ue-pak auto-detects paks-root (loader-less) vs ~mods (loader)"
```

---

## Task 6: Form camelCase round-trip + verification

**Files:**
- Test: `tests/ModManager.Tests/PaksRootScanTests.cs` (add a round-trip case) — or wherever GameEntry/ModLocation serialization is tested.

- [ ] **Step 1: Add the round-trip test**

Confirm the persisted `Form` value survives camelCase serialization (the `ModLocation`/games.json shape). Find the existing games.json serialization test (grep `RegistryStore`, `games.json`, `JsonSerializer` in tests). Add a case:

```csharp
    [Fact]
    public void ModLocation_form_paks_root_round_trips_camelCase()
    {
        var loc = new ModLocation("mods", "Paks", "Witchfire/Content/Paks") { Form = "paks-root" };
        var opts = new System.Text.Json.JsonSerializerOptions
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var json = System.Text.Json.JsonSerializer.Serialize(loc, opts);
        Assert.Contains("\"form\"", json);
        Assert.Contains("paks-root", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ModLocation>(json, opts)!;
        Assert.Equal("paks-root", back.Form);
    }
```

If `ModLocation` round-trip is already covered by a registry test, fold this assertion in there instead of a new test. Match how the repo serializes games (it uses `AtomicJson`/camelCase — confirm the casing of `Form` on disk is `"form"`).

- [ ] **Step 2: Run + full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: ALL pass.

- [ ] **Step 3: Commit**

```bash
git add tests/ModManager.Tests/PaksRootScanTests.cs
git commit -m "test(scanner): paks-root Form round-trips as camelCase"
```

---

## Task 7: Verification + reviewers + Witchfire smoke

**Files:** none (verification only)

- [ ] **Step 1: Full Core suite** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all pass.
- [ ] **Step 2: CorePurity** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"` → pass (PakClassifier is pure; no UI/IO leak).
- [ ] **Step 3: App build** — `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` → 0 errors (no App change, but confirm nothing broke).
- [ ] **Step 4: reversibility-auditor** — dispatch on `Scanner.cs` (the `GuardNoBasePakMove` + paks-root branch): confirm a base pak can never be moved, the guard fires before any file op, and the paks-root scan adds no delete. Implement any Important/Critical finding.
- [ ] **Step 5: Witchfire smoke entry** — append to `docs/smoke-tests/pending.md` (match format): re-add Witchfire from Steam → the preset configures `Content/Paks` as `paks-root` → the 2 mods (`pakchunk30-2x-witchfire`, `zz_Funner_Witchfire`) show, the base paks (`pakchunk0-WindowsNoEditor`, `pakchunk0optional`) never appear → toggle a mod works (its pak moves to holding) → the base game pak never appears and can't be disabled.
- [ ] **Step 6: PR + decision log** — open PR `feat/paks-root-base-game-filter → master`. Log to the 626 dashboard (project `DP1YCsh7iAN1yAiR8sAd`): the paks-root form, the name-OR-size classifier + accepted edge, the hard no-move guard, the preset auto-detect.

---

## Self-review notes

- **Spec coverage:** PakClassifier name-OR-size + accepted edge (T1); paks-root scan filters base (T3); hard no-move guard (T4); preset auto-detect (T5); Form persists camelCase (T6); reversibility + smoke (T7). All spec sections map to a task.
- **Placeholder scan:** none. Two implementer DECISIONS are explicitly framed (T4 guard-test path depends on whether `InternalsVisibleTo` exists; T5 may require extracting a `ResolveModLocations` resolver) — both give a concrete default + how to choose, not a vague "figure it out."
- **Type consistency:** `PakClassifier.IsBaseGamePak(string, long)` consistent T1/T3/T4. `ListPakFilesWithSize` consistent T2/T3. `ModLocation.Form == "paks-root"` consistent T3/T4/T5/T6. `GuardNoBasePakMove(Mod, ModLocationCtx)` T4.
- **Open items flagged for executor:** (1) confirm the `BuildModList` dispatch routes paks-root to the pak `else` (T3 Step 3); (2) confirm `ModLocation.Form` is settable + the games.json casing (T3/T6); (3) the disable entrypoint that actually reaches a file move (T4) — guard goes at the lowest move site; (4) the real add-time resolver signature (T5 Step 1) — extract a pure resolver if detection is inlined.
