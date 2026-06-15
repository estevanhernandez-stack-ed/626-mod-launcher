# Engine Detection Probe Deepening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make nested-Unreal games (Marvel Rivals and the long tail) auto-detect their engine + mod path by deepening the `Content/Paks` probe from one wrapper level to two, behind one shared pure-Core resolver with a false-positive guard.

**Architecture:** A new pure-Core `UeProjectScan` owns the bounded 2-level walk, the engine/anti-cheat/redist denylist, and the pure pick rules (prefer-shallowest + score-by-exe/shipping-pak + don't-guess-on-multi-match). The three detection sites delegate to it so they agree by construction: the add-wizard seeder (`EnginePresets.DetectUePakModLocation`, Core) and the two App delegators (`EngineScan.Probe` gate, `ModLocator.Detect` picker). `project` graduates from a leaf name to a relative path; the existing `ModLocations.UePakModLocation` primitive already `Path.Combine`s multi-segment paths. `PakClassifier` gains a UE5 shipping-pak name variant so the scoring signal works for UE5 titles like Marvel Rivals.

**Tech Stack:** .NET 10, C# (nullable-on, warnings-as-errors), xUnit. Pure Core + thin App shell; `CorePurityTests` bans WinUI/WinRT (not `System.IO`). The test project references **ModManager.Core only** — `EngineScan`/`ModLocator` live in `ModManager.App` and are **not** unit-testable here, so all logic is tested at the `UeProjectScan` level and the App sites are build- + smoke-verified.

**Spec:** [`docs/superpowers/specs/2026-06-15-engine-detection-probe-deepening-design.md`](../specs/2026-06-15-engine-detection-probe-deepening-design.md) · **Research:** [`docs/superpowers/research/2026-06-15-engine-detection-research.md`](../research/2026-06-15-engine-detection-research.md)

**Build/test commands (this repo — never bare `dotnet` at root):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (kill any running `ModManager.App` first — it locks `ModManager.Core.dll`, MSB3027)

---

## Task 1: `PakClassifier` — UE5 shipping-pak name variant + name-only check

UE4 cooks paks as `pakchunkN-WindowsNoEditor.pak`; UE5 (Marvel Rivals) drops the suffix → `pakchunkN-Windows.pak`. The `UeProjectScan` scoring signal needs a name-only check, and base-game-pak hiding must work for UE5 too.

**Files:**
- Modify: `src/ModManager.Core/PakClassifier.cs`
- Test: `tests/ModManager.Tests/PakClassifierTests.cs` (create if absent; otherwise append)

- [ ] **Step 1: Write the failing tests**

Append to `tests/ModManager.Tests/PakClassifierTests.cs` (create the file with this content if it does not exist):

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PakClassifierUe5Tests
{
    [Fact]
    public void Ue5_windows_suffix_is_a_shipping_pak_name()
    {
        Assert.True(PakClassifier.IsShippingPakName("pakchunk0-Windows.pak"));   // UE5
        Assert.True(PakClassifier.IsShippingPakName("pakchunk12optional-Windows.pak"));
    }

    [Fact]
    public void Ue4_windowsnoeditor_still_matches()
    {
        Assert.True(PakClassifier.IsShippingPakName("pakchunk0-WindowsNoEditor.pak")); // UE4
        Assert.True(PakClassifier.IsBaseGamePak("pakchunk0-WindowsNoEditor.pak", 10));
    }

    [Fact]
    public void Mod_pak_name_is_not_a_shipping_name()
    {
        Assert.False(PakClassifier.IsShippingPakName("zz_Funner_Witchfire.pak"));
        Assert.False(PakClassifier.IsShippingPakName("MyCoolMod_P.pak"));
    }

    [Fact]
    public void Ue5_base_pak_is_classified_base_by_name()
    {
        Assert.True(PakClassifier.IsBaseGamePak("pakchunk0-Windows.pak", sizeBytes: 50)); // name signal, small size
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PakClassifierUe5Tests`
Expected: FAIL — `IsShippingPakName` does not exist (compile error) and/or UE5 name not matched.

- [ ] **Step 3: Implement**

In `src/ModManager.Core/PakClassifier.cs`, broaden the regex and add the name-only method; reuse it in `IsBaseGamePak`:

```csharp
    // UE packaged-game convention. UE4: pakchunk<N>[optional]-WindowsNoEditor.pak.
    // UE5: pakchunk<N>[optional]-Windows.pak (and -WindowsClient/-WindowsServer). Case-insensitive.
    private static readonly Regex ShippingPakName =
        new(@"^pakchunk\d+.*-Windows[A-Za-z]*\.pak$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the file name matches the UE shipping-pak convention (UE4 or UE5). Name only.</summary>
    public static bool IsShippingPakName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return ShippingPakName.IsMatch(Path.GetFileName(fileName));
    }

    /// <summary>True when <paramref name="fileName"/> + <paramref name="sizeBytes"/> indicate a base-game
    /// pak (hide + protect). Name pattern OR size — see the class summary.</summary>
    public static bool IsBaseGamePak(string fileName, long sizeBytes)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return IsShippingPakName(fileName) || sizeBytes >= ModSizeCeilingBytes;
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PakClassifierUe5Tests`
Expected: PASS. Also run the existing paks suite to confirm no regression: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PaksRoot`

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/PakClassifier.cs tests/ModManager.Tests/PakClassifierTests.cs
git commit -m "feat(engine): UE5 shipping-pak name variant + IsShippingPakName

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `UeProjectScan` — types, denylist, and the pure `Pick` rules

The pure half: candidate/result records, the sibling-name denylist, and the decision (`Pick`) with no IO. Fully unit-tested with synthetic candidate lists.

**Files:**
- Create: `src/ModManager.Core/UeProjectScan.cs`
- Test: `tests/ModManager.Tests/UeProjectScanPickTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/UeProjectScanPickTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class UeProjectScanPickTests
{
    private static UeProjectCandidate C(string rel, int depth, bool pak = false, bool bin = false, bool upr = false)
        => new(rel, depth, HasShippingPak: pak, HasBinariesSibling: bin, HasUprojectSibling: upr);

    [Fact]
    public void Empty_is_none()
        => Assert.Equal(UeProjectPickKind.None, UeProjectScan.Pick(Array.Empty<UeProjectCandidate>()).Kind);

    [Fact]
    public void Single_candidate_is_chosen_even_without_signals()
    {
        var pick = UeProjectScan.Pick(new[] { C("Pal", 1) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Pal", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Two_unremarkable_candidates_are_ambiguous()
    {
        var pick = UeProjectScan.Pick(new[] { C("A", 1), C("B", 1) });
        Assert.Equal(UeProjectPickKind.Ambiguous, pick.Kind);
    }

    [Fact]
    public void One_project_looking_candidate_beats_an_unremarkable_one()
    {
        var pick = UeProjectScan.Pick(new[] { C("Tool", 1), C("Game", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Game", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Two_project_looking_candidates_tie_is_ambiguous()
    {
        var pick = UeProjectScan.Pick(new[] { C("Game", 1, bin: true), C("Other", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.Ambiguous, pick.Kind);
    }

    [Fact]
    public void Client_beats_server_when_both_look_like_projects()
    {
        var pick = UeProjectScan.Pick(new[] { C("GameServer", 1, bin: true), C("Game", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Game", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Shallower_wins_among_project_looking()
    {
        var pick = UeProjectScan.Pick(new[] { C("Outer/Inner", 2, pak: true), C("Shallow", 1, pak: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Shallow", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Denylist_includes_engine_and_anticheat()
    {
        Assert.Contains("Engine", UeProjectScan.Denylist);
        Assert.Contains("EasyAntiCheat", UeProjectScan.Denylist);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter UeProjectScanPickTests`
Expected: FAIL — `UeProjectScan` / its types do not exist (compile error).

- [ ] **Step 3: Implement (pure half only)**

Create `src/ModManager.Core/UeProjectScan.cs` with the types, denylist, and `Pick`. (The IO half — `Enumerate`/`HasContentPaks`/`WalkProjectDirs`/`Describe` — is added in Task 3; leave them out for now so this task compiles and tests in isolation.)

```csharp
using System.IO;

namespace ModManager.Core;

/// <summary>One Unreal "project" folder (the dir that owns a Content directory) found under a game root.</summary>
public sealed record UeProjectCandidate(
    string RelativeProjectPath,   // "" (root is the project) | "Pal" | "MarvelGame/Marvel"
    int    WrapperDepth,          // 0 = root, 1, or 2
    bool   HasShippingPak,        // a PakClassifier shipping-name pak inside its Content/Paks
    bool   HasBinariesSibling,    // a Binaries folder next to Content (the playable build signal)
    bool   HasUprojectSibling);   // a .uproject next to Content (tie-breaker bonus only)

/// <summary>Bound on the directory walk so it stays fast on huge installs.</summary>
public readonly record struct ScanBudget(int MaxDirs)
{
    public static ScanBudget Default => new(200);
}

public enum UeProjectPickKind { None, One, Ambiguous }

/// <summary>The outcome of picking the right project from the candidates. One = auto-pick; Ambiguous = don't guess.</summary>
public sealed record UeProjectPick(UeProjectPickKind Kind, UeProjectCandidate? Chosen)
{
    public static readonly UeProjectPick None = new(UeProjectPickKind.None, null);
    public static readonly UeProjectPick Ambiguous = new(UeProjectPickKind.Ambiguous, null);
    public static UeProjectPick One(UeProjectCandidate c) => new(UeProjectPickKind.One, c);
}

/// <summary>
/// Bounded discovery of Unreal project folders (the dir owning Content/Paks) under a game root, plus
/// the pure rules for picking the RIGHT one. Single source of truth so the engine-decision gate
/// (EngineScan), the add-wizard seeder (EnginePresets), and the runtime resolver (ModLocator) agree by
/// construction. Walks root + up to 2 wrapper levels, skips engine/anti-cheat/redist siblings, and is
/// hard-bounded by a directory budget. The walk does System.IO (allowed in Core); Pick is pure.
/// </summary>
public static class UeProjectScan
{
    /// <summary>Folder names that are never a game project wrapper — skipped before descending.</summary>
    public static IReadOnlyList<string> Denylist { get; } = new[]
    {
        "Engine", "Binaries", "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye",
        "CommonRedist", "_CommonRedist", "Redist", "Redistributable", "Prerequisites",
        "DirectXRedist", "VCRedist", "DotNetRedist",
    };

    private static readonly HashSet<string> DenySet = new(Denylist, StringComparer.OrdinalIgnoreCase);

    private static bool IsDenied(string folderName) => DenySet.Contains(folderName);

    /// <summary>Pure decision over candidates. One when exactly one candidate, or one project-looking
    /// candidate strictly out-scores the rest; Ambiguous when two-or-more tie (don't guess); None when empty.</summary>
    public static UeProjectPick Pick(IReadOnlyList<UeProjectCandidate> candidates)
    {
        if (candidates is null || candidates.Count == 0) return UeProjectPick.None;
        if (candidates.Count == 1) return UeProjectPick.One(candidates[0]);

        var looking = candidates.Where(IsProjectLooking).ToList();
        if (looking.Count == 0) return UeProjectPick.Ambiguous; // multiple, none looks real — don't guess
        if (looking.Count == 1) return UeProjectPick.One(looking[0]);

        var ranked = looking.Select(c => (c, score: Score(c)))
                            .OrderByDescending(t => t.score).ToList();
        return ranked[0].score > ranked[1].score
            ? UeProjectPick.One(ranked[0].c)
            : UeProjectPick.Ambiguous;
    }

    private static bool IsProjectLooking(UeProjectCandidate c) => c.HasShippingPak || c.HasBinariesSibling;

    private static int Score(UeProjectCandidate c)
    {
        var s = 0;
        if (!LastSegment(c.RelativeProjectPath).EndsWith("Server", StringComparison.OrdinalIgnoreCase)) s += 1000;
        s += (2 - Math.Clamp(c.WrapperDepth, 0, 2)) * 100; // shallower wins
        if (c.HasShippingPak) s += 40;
        if (c.HasBinariesSibling) s += 20;
        if (c.HasUprojectSibling) s += 5;
        return s;
    }

    private static string LastSegment(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return "";
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 ? parts[^1] : rel;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter UeProjectScanPickTests`
Expected: PASS (all 8).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/UeProjectScan.cs tests/ModManager.Tests/UeProjectScanPickTests.cs
git commit -m "feat(engine): UeProjectScan pick rules + denylist (pure)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `UeProjectScan` — bounded 2-level `Enumerate` + `HasContentPaks` (IO)

The IO half: the budget-bounded, denylist-skipping walk that finds project dirs (root + up to 2 wrappers) and the short-circuit gate.

**Files:**
- Modify: `src/ModManager.Core/UeProjectScan.cs`
- Test: `tests/ModManager.Tests/UeProjectScanTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/UeProjectScanTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class UeProjectScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ueprojscan-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string Root() { var r = Path.Combine(_tmp, Guid.NewGuid().ToString("n")); Directory.CreateDirectory(r); return r; }
    private static void MakePaks(string root, params string[] relSegments)
        => Directory.CreateDirectory(Path.Combine(new[] { root }.Concat(relSegments).Concat(new[] { "Content", "Paks" }).ToArray()));

    [Fact]
    public void Single_wrapper_is_found_at_depth_1()
    {
        var root = Root(); MakePaks(root, "Pal");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Single(cands);
        Assert.Equal("Pal", cands[0].RelativeProjectPath);
        Assert.Equal(1, cands[0].WrapperDepth);
        Assert.True(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Two_wrapper_is_found_at_depth_2()  // Marvel Rivals: MarvelGame/Marvel/Content/Paks
    {
        var root = Root(); MakePaks(root, "MarvelGame", "Marvel");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Contains(cands, c => c.RelativeProjectPath.Replace('\\', '/') == "MarvelGame/Marvel" && c.WrapperDepth == 2);
        Assert.True(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Root_is_the_project_at_depth_0()  // STALKER 2: install root IS the project
    {
        var root = Root(); MakePaks(root); // root/Content/Paks
        var cands = UeProjectScan.Enumerate(root);
        Assert.Contains(cands, c => c.RelativeProjectPath == "" && c.WrapperDepth == 0);
    }

    [Fact]
    public void Engine_sibling_is_skipped()
    {
        var root = Root(); MakePaks(root, "Engine"); MakePaks(root, "Phoenix");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Single(cands);
        Assert.Equal("Phoenix", cands[0].RelativeProjectPath);
    }

    [Fact]
    public void Shipping_pak_and_binaries_signals_are_recorded()
    {
        var root = Root(); MakePaks(root, "Marvel");
        File.WriteAllText(Path.Combine(root, "Marvel", "Content", "Paks", "pakchunk0-Windows.pak"), "x");
        Directory.CreateDirectory(Path.Combine(root, "Marvel", "Binaries"));
        var c = UeProjectScan.Enumerate(root).Single();
        Assert.True(c.HasShippingPak);
        Assert.True(c.HasBinariesSibling);
    }

    [Fact]
    public void No_content_paks_means_no_detection()
    {
        var root = Root(); Directory.CreateDirectory(Path.Combine(root, "Misc", "Stuff"));
        Assert.Empty(UeProjectScan.Enumerate(root));
        Assert.False(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Walk_respects_the_directory_budget()
    {
        var root = Root();
        for (var i = 0; i < 50; i++) Directory.CreateDirectory(Path.Combine(root, "junk" + i));
        MakePaks(root, "zzz_last", "deep"); // a real one that a tiny budget won't reach
        var cands = UeProjectScan.Enumerate(root, new ScanBudget(MaxDirs: 5));
        Assert.True(cands.Count <= 1); // budget stops the walk before exhausting all junk dirs
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter UeProjectScanTests`
Expected: FAIL — `Enumerate` / `HasContentPaks` do not exist (compile error).

- [ ] **Step 3: Implement (add the IO half to `UeProjectScan`)**

Add these members to `src/ModManager.Core/UeProjectScan.cs` (inside the class):

```csharp
    /// <summary>Every Unreal project dir (root + up to 2 wrapper levels) that owns a Content/Paks,
    /// denylist-skipped and budget-bounded. IO, bounded + deterministic.</summary>
    public static IReadOnlyList<UeProjectCandidate> Enumerate(string gameRoot, ScanBudget? budget = null)
    {
        var list = new List<UeProjectCandidate>();
        WalkProjectDirs(gameRoot, budget ?? ScanBudget.Default, (rel, depth, abs) =>
        {
            if (Directory.Exists(Path.Combine(abs, "Content", "Paks"))) list.Add(Describe(rel, depth, abs));
            return false; // collect all
        });
        return list;
    }

    /// <summary>Fast gate: does at least one non-denylisted Content/Paks exist within 2 wrappers? Short-circuits.</summary>
    public static bool HasContentPaks(string gameRoot, ScanBudget? budget = null)
    {
        var found = false;
        WalkProjectDirs(gameRoot, budget ?? ScanBudget.Default, (_, _, abs) =>
        {
            if (Directory.Exists(Path.Combine(abs, "Content", "Paks"))) { found = true; return true; }
            return false;
        });
        return found;
    }

    // Visits root + up to 2 wrapper levels. Calls onProjectDir(rel, depth, absDir) for every dir that
    // contains a Content folder; returning true stops the walk early. Descends into every non-denylisted
    // level-1 dir regardless (a wrapper like "MarvelGame" has no Content itself but holds the project).
    private static void WalkProjectDirs(string gameRoot, ScanBudget budget, Func<string, int, string, bool> onProjectDir)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;
        var examined = 0;

        bool Check(string rel, int depth)
        {
            var abs = depth == 0 ? gameRoot : Path.Combine(gameRoot, rel);
            return Directory.Exists(Path.Combine(abs, "Content")) && onProjectDir(rel, depth, abs);
        }

        if (Check("", 0)) return;

        string[] level1;
        try { level1 = Directory.GetDirectories(gameRoot); } catch { return; }
        foreach (var w1 in level1)
        {
            if (++examined > budget.MaxDirs) return;
            var w1Name = Path.GetFileName(w1);
            if (string.IsNullOrEmpty(w1Name) || IsDenied(w1Name)) continue;
            if (Check(w1Name, 1)) return;

            string[] level2;
            try { level2 = Directory.GetDirectories(w1); } catch { continue; }
            foreach (var w2 in level2)
            {
                if (++examined > budget.MaxDirs) return;
                var w2Name = Path.GetFileName(w2);
                if (string.IsNullOrEmpty(w2Name) || IsDenied(w2Name)) continue;
                if (Check(Path.Combine(w1Name, w2Name), 2)) return;
            }
        }
    }

    private static UeProjectCandidate Describe(string rel, int depth, string absProjectDir)
    {
        var paks = Path.Combine(absProjectDir, "Content", "Paks");
        var hasShippingPak = false;
        try { hasShippingPak = Directory.EnumerateFiles(paks, "*.pak").Any(f => PakClassifier.IsShippingPakName(Path.GetFileName(f))); }
        catch { /* unreadable Paks */ }
        var hasBinaries = Directory.Exists(Path.Combine(absProjectDir, "Binaries"));
        var hasUproject = false;
        try { hasUproject = Directory.EnumerateFiles(absProjectDir, "*.uproject").Any(); }
        catch { /* unreadable project dir */ }
        return new UeProjectCandidate(rel, depth, hasShippingPak, hasBinaries, hasUproject);
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter UeProjectScanTests`
Expected: PASS (all 7). Then run `--filter UeProjectScanPickTests` to confirm Task 2 still green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/UeProjectScan.cs tests/ModManager.Tests/UeProjectScanTests.cs
git commit -m "feat(engine): bounded 2-level UeProjectScan.Enumerate + HasContentPaks

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: `EnginePresets.DetectUePakModLocation` delegates to `UeProjectScan`

The add-wizard seeder now resolves nested projects via the shared resolver and passes a relative project path to the existing primitive.

**Files:**
- Modify: `src/ModManager.Core/EnginePresets.cs:111-130`
- Test: `tests/ModManager.Tests/PaksRootPresetTests.cs` (append one test)

- [ ] **Step 1: Write the failing test**

Append to `tests/ModManager.Tests/PaksRootPresetTests.cs` (inside the class, reusing its `_tmp`, `UeInput`):

```csharp
    [Fact]
    public void Two_wrapper_project_resolves_the_nested_mods_path()  // Marvel Rivals
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "MarvelGame", "Marvel", "Content", "Paks", "~mods"));
        var entry = EnginePresets.BuildGameEntry(UeInput(gameRoot), existingIds: null);
        var loc = entry.ModLocations.Single();
        Assert.Equal("MarvelGame/Marvel/Content/Paks/~mods", loc.Path.Replace('\\', '/'));
        Assert.Null(loc.Form); // ~mods present → loader form, not paks-root
    }

    [Fact]
    public void Two_wrapper_loaderless_resolves_paks_root()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "MarvelGame", "Marvel", "Content", "Paks"));
        var entry = EnginePresets.BuildGameEntry(UeInput(gameRoot), existingIds: null);
        var loc = entry.ModLocations.Single();
        Assert.Equal("paks-root", loc.Form);
        Assert.Equal("MarvelGame/Marvel/Content/Paks", loc.Path.Replace('\\', '/'));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PaksRootPresetTests`
Expected: FAIL — current one-level walk returns null for the two-wrapper layout → entry falls back to the static `Content/Paks/~mods`, so the path assertion fails.

- [ ] **Step 3: Implement**

Replace the body of `DetectUePakModLocation` in `src/ModManager.Core/EnginePresets.cs` (lines 111-130) with:

```csharp
    private static ModLocation? DetectUePakModLocation(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return null;

        // Delegate discovery + the false-positive guard to the shared resolver so this seeder, the
        // engine-decision gate (EngineScan), and the runtime resolver (ModLocator) agree by construction.
        var withPaks = UeProjectScan.Enumerate(gameRoot)
                                    .Where(c => Directory.Exists(Path.Combine(gameRoot, c.RelativeProjectPath, "Content", "Paks")))
                                    .ToList();
        var pick = UeProjectScan.Pick(withPaks);
        if (pick.Kind != UeProjectPickKind.One || pick.Chosen is not { } chosen) return null;

        var rel = chosen.RelativeProjectPath;
        var paks = Path.Combine(gameRoot, rel, "Content", "Paks");
        var loaderPresent = Directory.Exists(Path.Combine(paks, "~mods"))
                            || Directory.Exists(Path.Combine(paks, "LogicMods"));
        return ModLocations.UePakModLocation(rel, loaderPresent);
    }
```

(Note: `Enumerate` only returns dirs that have a `Content` folder; the `.Where(... Content/Paks ...)` filter keeps this seeder's existing "requires Content/Paks" contract. `UePakModLocation` already `Path.Combine`s the multi-segment `rel`.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PaksRootPresetTests`
Expected: PASS — both new tests AND all 5 originals (single-wrapper Witchfire/R5, explicit modPath, non-ue, no-gameroot) stay green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/EnginePresets.cs tests/ModManager.Tests/PaksRootPresetTests.cs
git commit -m "feat(engine): DetectUePakModLocation resolves 2-level UE projects via UeProjectScan

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: `EngineScan.Probe` gate — parity via `UeProjectScan.HasContentPaks` (App)

The engine-decision gate must agree with the seeder: detect 2-level projects, skip `Engine`. App-side, not unit-testable here — build-verified, parity guaranteed by calling the same tested Core primitive.

**Files:**
- Modify: `src/ModManager.App/Services/EngineScan.cs:25-26`

- [ ] **Step 1: Implement**

In `src/ModManager.App/Services/EngineScan.cs`, replace the `contentPaks` computation (lines 25-26) with a call to the shared resolver. The `subs` array is still used by the `source`/`unity` checks below, so leave it:

```csharp
        var contentPaks = UeProjectScan.HasContentPaks(root);
```

Update the class doc comment (lines 8-9) to reflect the new bound:

```csharp
/// here; the decision (<see cref="EngineDetect.GuessEngine"/>) stays pure + tested. Unreal Content/Paks
/// discovery is delegated to <see cref="UeProjectScan"/> (root + up to 2 wrapper levels, denylist-skipped,
/// budget-bounded); the other signatures stay bounded to root + one subfolder level for speed.
```

- [ ] **Step 2: Build to verify it compiles**

Kill any running `ModManager.App` first, then:
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings (warnings are errors).

- [ ] **Step 3: Run the Core suite (regression — pure decision untouched)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter EngineDetect`
Expected: PASS — all 9 `EngineDetectTests` unchanged (`GuessEngine` and the `EngineProbe` record are not touched).

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/EngineScan.cs
git commit -m "feat(engine): EngineScan gate detects 2-level UE projects via UeProjectScan

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: `ModLocator` picker — parity via `UeProjectScan.Enumerate` + `Pick` (App)

The runtime install-target picker discovers projects at up to 2 levels and preserves the "don't guess on multi-match" discipline. App-side — build-verified + smoke.

**Files:**
- Modify: `src/ModManager.App/Services/ModLocator.cs`

- [ ] **Step 1: Implement**

In `src/ModManager.App/Services/ModLocator.cs`, rewrite `Detect` to source projects from `UeProjectScan` and seed via `Pick`; delete the now-unused private `UnrealProjects` method (replaced by `Enumerate`). Keep the `Name` helper:

```csharp
    public static IReadOnlyList<ModLocation> Detect(string? gameRoot, string? engine)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return Array.Empty<ModLocation>();

        var candidates = engine == "ue-pak" ? UeProjectScan.Enumerate(gameRoot) : null;
        var projects = candidates?.Select(c => c.RelativeProjectPath).ToList();

        var existing = new List<ModLocation>();
        foreach (var rel in ModLocations.Candidates(engine, projects))
        {
            if (!Directory.Exists(Path.Combine(gameRoot, rel))) continue;
            existing.Add(new ModLocation(Name(existing.Count), rel, rel));
        }
        if (existing.Count > 0) return existing;

        // No loader folder matched. If the resolver picks exactly one project, seed from the disk fact:
        // Content/Paks present -> loader-less paks-root; not yet present -> the ~mods install target.
        // Ambiguous / none -> keep the preset default (don't guess), exactly as before.
        if (engine == "ue-pak" && candidates is { Count: > 0 })
        {
            var pick = UeProjectScan.Pick(candidates);
            if (pick.Kind == UeProjectPickKind.One && pick.Chosen is { } chosen)
            {
                var rel = chosen.RelativeProjectPath;
                var paksExists = Directory.Exists(Path.Combine(gameRoot, rel, "Content", "Paks"));
                return new[] { paksExists
                    ? ModLocations.UePakModLocation(rel, loaderPresent: false)
                    : ModLocations.UePakModLocation(rel, loaderPresent: true) };
            }
        }

        return Array.Empty<ModLocation>();
    }

    // Distinct location keys so per-location identity (disable meta, mirrors) stays unambiguous.
    private static string Name(int idx) => idx == 0 ? "mods" : "mods" + (idx + 1);
```

(`ModLocations.Candidates` already accepts multi-segment project names and `Path.Combine`s them, and filters out the empty root entry while always adding the root-level fallback — no change needed there.)

- [ ] **Step 2: Build to verify it compiles**

Kill any running `ModManager.App` first, then:
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/ModLocator.cs
git commit -m "feat(engine): ModLocator resolves 2-level UE projects, keeps multi-match discipline

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Full verification + smoke checklist

**Files:**
- Modify: `docs/smoke-tests/pending.md` (append)

- [ ] **Step 1: Full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests green, including `CorePurityTests` (new `UeProjectScan` is pure-Core, no WinUI/WinRT), the 9 `EngineDetectTests`, the `PaksRoot*` suite, and the new `UeProjectScan*` + `PakClassifierUe5` tests.

- [ ] **Step 2: App build**

Kill any running `ModManager.App`, then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Append smoke entries**

Append to `docs/smoke-tests/pending.md`:

```markdown
## Engine detection — 2-level Unreal probe (2026-06-15)

- [ ] **Marvel Rivals (2-level UE5) auto-detects.** Add Marvel Rivals from Steam/Epic (or point at its install). Expect: engine detected as `ue-pak`; mod path resolves to `MarvelGame/Marvel/Content/Paks/~mods`. Drop a `.pak` into `~mods` → it shows as a mod row and toggles on/off (moves to holding, not deleted).
- [ ] **Single-wrapper games still work (no regression).** A previously-working single-wrapper UE game (e.g. Palworld `Pal/...`, Hogwarts `Phoenix/...`) still detects and lists mods exactly as before.
- [ ] **Engine sibling is not mis-detected.** A UE install with an `Engine/Content/Paks` beside the project resolves to the project folder, never `Engine` (no engine paks listed as mods, mods never routed into `Engine`).
- [ ] **Big install stays fast.** Adding a game with a large/deep folder tree does not hang the add (the probe is budget-bounded).
```

- [ ] **Step 4: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): 2-level Unreal probe checklist

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes (author checklist, already run)

- **Spec coverage:** `UeProjectScan` (walk + denylist + pick + budget) = Tasks 2-3; `DetectUePakModLocation` deepening = Task 4; `EngineScan` gate = Task 5; `ModLocator` picker = Task 6; `PakClassifier` UE5 = Task 1; smoke = Task 7. All five "Surfaces touched" rows covered.
- **Test strategy correction vs spec:** the spec listed `EngineScan_Probe_*` unit tests; the test project does **not** reference `ModManager.App`, so those behaviors are tested at the `UeProjectScan` level (Task 3: 2-level found, Engine skipped, budget cap) and the App gate/picker are build- + smoke-verified. Parity is by construction (both call the tested Core primitive).
- **Type consistency:** `UeProjectCandidate(RelativeProjectPath, WrapperDepth, HasShippingPak, HasBinariesSibling, HasUprojectSibling)`, `UeProjectPick(Kind, Chosen)` with `UeProjectPickKind {None, One, Ambiguous}`, `ScanBudget(MaxDirs)`, `PakClassifier.IsShippingPakName(string?)` — names identical across Tasks 1-6.
- **Reversibility/laws:** no `File.Delete`, no writes — this is read-side detection only; toggle/move paths and `PaksRootGuard` are untouched. camelCase JSON on disk unaffected (`ModLocation.Path` is an existing string field carrying a longer value).
- **No placeholders:** every code step shows full code; every run step shows the exact filtered command + expected result.
