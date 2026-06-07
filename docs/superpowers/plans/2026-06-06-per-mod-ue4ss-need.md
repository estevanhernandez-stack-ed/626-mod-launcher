# Per-mod UE4SS need Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the false "needs UE4SS" chip on plain content paks — show it only on mods that actually need UE4SS (Lua/script mods + Blueprint LogicMods paks).

**Architecture:** A pure-Core `FrameworkApplicability.ModNeedsUe4ss(mod, locationPath)` decides per-mod from signals already on `Mod` (Loader) plus the row's location path (LogicMods). The App row-builder gates the UE4SS chip on it; all other engines' chips are untouched.

**Tech Stack:** .NET 10 / C#, xUnit (headless Core tests), pure Core (no IO in the helper). WinUI App shell (row-builder, build-verified only).

**Reference reading before starting:**
- Spec: `docs/superpowers/specs/2026-06-06-per-mod-ue4ss-need-design.md`
- `src/ModManager.Core/Mod.cs:30` — `public string? Loader { get; set; }` ("ue4ss" for Lua mods, null for plain paks).
- `src/ModManager.App/ViewModels/MainViewModel.cs`:
  - `~line 376-377` — the row-builder resolves the row's location: `Scanner.LocByName(rep.Location, _ctx!).Abs`. `LocByName` returns a `ModLocationCtx` with `.Path` (relative, e.g. `R5/Content/Paks/LogicMods`) and `.Abs`.
  - `~line 423-432` — the missing-framework chip assignment. fromsoft branch picks ME2/EML by `rep.IsFolder`; the `else` (all other engines, incl. ue-pak) does `primaryMissing = MissingFrameworks.FirstOrDefault();` — this is what falsely yields UE4SS on every ue-pak row.
  - `rep` is the representative `Mod` for the row; `_ctx` is the `GameContext`.
- `src/ModManager.Core/GameContext.cs` — `ModLocationCtx` record: `(Name, Label, Abs, Mirrors, Primary)` + `Form`/`Managed`. NOTE: it has `Abs` but confirm whether it also carries the relative `Path` — if NOT, the row-builder must use the games.json `ModLocation.Path` or derive LogicMods from `.Abs`. Read `GameContext.cs` to confirm which property carries the `LogicMods`-bearing path.
- `src/ModManager.Core/NexusDomains.cs` — recently added `["3156770"] = "witchfire"` (the metadata fix on this same branch); unrelated but confirms the branch context.

**Build/test commands (Windows):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- One filter: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~<Class>"`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- NEVER bare `dotnet test`/`dotnet build` at repo root (WinUI hangs). `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on Core.

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ModManager.Core/FrameworkApplicability.cs` | NEW. `ModNeedsUe4ss(Mod mod, string locationPath)` — the per-mod rule. Pure, no IO. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | MODIFY. Gate the UE4SS chip on the helper in the row-builder (~line 432). |
| `tests/ModManager.Tests/FrameworkApplicabilityTests.cs` | NEW. |

---

## Task 1: FrameworkApplicability.ModNeedsUe4ss (Core)

**Files:**
- Create: `src/ModManager.Core/FrameworkApplicability.cs`
- Test: `tests/ModManager.Tests/FrameworkApplicabilityTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class FrameworkApplicabilityTests
{
    private static Mod Pak(string? loader = null) => new() { Name = "X", Loader = loader, IsFolder = false };
    private static Mod LuaFolder() => new() { Name = "X", Loader = "ue4ss", IsFolder = true };

    [Fact]
    public void Lua_mod_needs_ue4ss_regardless_of_location()
        => Assert.True(FrameworkApplicability.ModNeedsUe4ss(LuaFolder(), "R5/Content/Paks/~mods"));

    [Fact]
    public void LogicMods_pak_needs_ue4ss()
        => Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/LogicMods"));

    [Fact]
    public void Plain_content_pak_in_mods_does_not_need_ue4ss()
        => Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/~mods"));

    [Fact]
    public void Plain_content_pak_in_paks_root_does_not_need_ue4ss()
        => Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), "Witchfire/Content/Paks"));

    [Fact]
    public void LogicMods_match_is_case_and_separator_insensitive()
    {
        Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), @"R5\Content\Paks\logicmods"));
        Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/LogicMods/"));
    }

    [Fact]
    public void Null_or_empty_location_with_plain_pak_does_not_need_ue4ss()
    {
        Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), ""));
        Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), null!));
    }
}
```

CONFIRM `Mod`'s settable members (`Name`, `Loader`, `IsFolder`) by reading `src/ModManager.Core/Mod.cs` — they're public settable per the file (line 10, 30, 22). Adjust the `Pak`/`LuaFolder` helpers if a member differs.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkApplicabilityTests"`
Expected: FAIL — `FrameworkApplicability` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ModManager.Core;

/// <summary>
/// Per-mod framework-dependency rules — which mods actually need a given framework, so the row chip
/// is mod-aware rather than engine-wide. Today: UE4SS. A ue-pak game's UE4SS dependency applies only
/// to Lua/script mods and Blueprint LogicMods paks; a plain content pak (in ~mods or a paks-root
/// location) loads with no framework. Pure — decides from the Mod + its resolved location path.
/// </summary>
public static class FrameworkApplicability
{
    /// <summary>True when <paramref name="mod"/> needs UE4SS: it's a Lua/script mod (driven through the
    /// UE4SS manifest, <c>Loader == "ue4ss"</c>) OR a Blueprint pak in a LogicMods location (UE4SS's
    /// BPModLoader mounts that folder). A plain content pak needs nothing. <paramref name="locationPath"/>
    /// is the row's mod-location path (relative or absolute); the LogicMods check is case- and
    /// separator-insensitive.</summary>
    public static bool ModNeedsUe4ss(Mod mod, string locationPath)
    {
        if (mod is not null && string.Equals(mod.Loader, "ue4ss", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(locationPath)) return false;
        var norm = locationPath.Replace('\\', '/').TrimEnd('/');
        var leaf = norm.Length == 0 ? "" : norm[(norm.LastIndexOf('/') + 1)..];
        return string.Equals(leaf, "LogicMods", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkApplicabilityTests"`
Expected: PASS (6+ cases, 0 failed). Then full suite `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` — all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/FrameworkApplicability.cs tests/ModManager.Tests/FrameworkApplicabilityTests.cs
git commit -m "feat(frameworks): ModNeedsUe4ss — per-mod UE4SS dependency rule (Lua + LogicMods only)"
```

---

## Task 2: Gate the UE4SS chip in the row-builder (App)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (~line 423-432)
- (App VM — verified by App build, not unit tests.)

- [ ] **Step 1: Read the current chip-assignment block**

Around line 423-432:
```csharp
                FrameworkDep? primaryMissing;
                if (_ctx.Game.Engine == "fromsoft")
                {
                    primaryMissing = rep.IsFolder
                        ? MissingFrameworks.FirstOrDefault(d => d.Name == "Mod Engine 2")
                        : MissingFrameworks.FirstOrDefault(d => d.Name == "Elden Mod Loader");
                }
                else
                {
                    primaryMissing = MissingFrameworks.FirstOrDefault();
                }
```

- [ ] **Step 2: Gate the UE4SS case on the per-mod helper**

Replace the `else` branch so a UE4SS chip only attaches to a row that needs it. The row's location is resolved via `Scanner.LocByName(rep.Location, _ctx!)`; use its path for the LogicMods check. (Confirm whether `ModLocationCtx` exposes the relative `.Path` or only `.Abs` — use whichever carries the `LogicMods` tail; `.Abs` ends in `...\LogicMods` too, so it works for the leaf check either way.)

```csharp
                else
                {
                    primaryMissing = MissingFrameworks.FirstOrDefault();
                    // UE4SS is needed only by Lua/script mods + Blueprint LogicMods paks — not plain
                    // content paks (Witchfire, and ~mods/paks-root content mods generally). Drop the
                    // chip for a row that doesn't need it so we stop falsely flagging content paks.
                    if (primaryMissing?.Name == "UE4SS")
                    {
                        var locPath = Scanner.LocByName(rep.Location, _ctx!).Abs;
                        if (!FrameworkApplicability.ModNeedsUe4ss(rep, locPath))
                            primaryMissing = null;
                    }
                }
```

CONFIRM `Scanner.LocByName(rep.Location, _ctx!)` returns a `ModLocationCtx` with `.Abs` (it's used at line 377 already: `Scanner.LocByName(rep.Location, _ctx!).Abs`). Reuse the same call. `FrameworkApplicability` is in `ModManager.Core` — confirm `using ModManager.Core;` is present at the top of MainViewModel.cs (it is — the file uses `Mod`, `Scanner`, etc.).

Note: when `primaryMissing` becomes null, the existing initializer lines (`MissingFrameworkName = primaryMissing?.Name ?? ""`, etc., ~line 448-450) already null-coalesce to empty — so a null primaryMissing yields no chip, no extra change needed. Verify those lines handle null (they use `?.` — they do).

- [ ] **Step 3: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. If MSB3027/MSB3021 file-lock (running app holds the DLL), that's NOT a compile error — retry into temp output (`-p:BaseOutputPath=obj/tmpbuild/ --output bin/tmpbuild/`), confirm 0 compile errors, delete temp dirs; do NOT kill any process.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "fix(frameworks): gate the UE4SS chip per-mod — no false chip on content paks"
```

---

## Task 3: Verify + smoke entry

**Files:** `docs/smoke-tests/pending.md` (append)

- [ ] **Step 1: Full Core suite + CorePurity**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all pass.
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"` → pass (FrameworkApplicability is pure).

- [ ] **Step 2: App build** — `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` → 0 errors.

- [ ] **Step 3: Append the smoke entry to `docs/smoke-tests/pending.md`** (match the file's existing entry format):

Title: per-mod UE4SS chip (false-chip fix). Steps: on Witchfire (plain content paks, no UE4SS), the mod rows show NO "needs UE4SS" chip; on Windrose with UE4SS absent, a Lua mod row + a LogicMods pak row STILL show the chip, while a plain `~mods` content pak shows none. Why: the chip was engine-wide; now it's per-mod (Lua + LogicMods only). App-VM, not unit-testable here.

- [ ] **Step 4: Commit the smoke entry**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): add per-mod UE4SS chip smoke check"
```

---

## Self-review notes

- **Spec coverage:** the rule (Lua → `Loader==ue4ss`; LogicMods → location leaf; else false) is T1; the App gating (UE4SS-only, other frameworks untouched) is T2; tests T1; smoke T3. All spec sections map.
- **Placeholder scan:** none — both code steps show full code. One CONFIRM (does `ModLocationCtx` carry `.Path` or only `.Abs`) is resolved with a concrete default: use `.Abs` (its `LogicMods` tail satisfies the leaf check), reusing the existing line-377 call.
- **Type consistency:** `FrameworkApplicability.ModNeedsUe4ss(Mod, string)` consistent T1/T2. `Scanner.LocByName(rep.Location, _ctx!).Abs` matches the existing line-377 usage. `primaryMissing?.Name == "UE4SS"` matches the catalog `Name` ("UE4SS", per `FrameworkDeps.Catalog`).
- **Open item for executor:** confirm the catalog framework Name is exactly `"UE4SS"` (it is, per `FrameworkDeps.cs` `Name: "UE4SS"`) so the `== "UE4SS"` gate matches; if it differs, use the real string.
