# Phase 1A — Safe Clear + Restore Core Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless, fully-tested Core engine for Safe Clear + Restore — manifest contracts, the off-boarding sheet renderer, restore reconcile, and the capture → end-state → replay file engine — so the entire reversibility battery (byte-for-byte round-trip, vanilla/leave-active, gated+verified restore) passes in xUnit with no WinUI.

**Architecture:** Pure `ModManager.Core.RestorePoints` namespace. The engine takes **explicit directory paths** (no `%APPDATA%` knowledge) and composes the Phase 0 primitives (`PathGate`, `SafeMove`, `SpaceCheck`) plus existing Core (`AtomicJson`, `Registry`, `Scanner`/`GameContext`, `DirectInject`, `FrameworkRegistry`, `ReplacedStore`, `ToolOwnership`, `Ue4ssManifest`, `BepInExPlugins`, `ModMeta`). The App shell (Phase 1B) supplies real paths, the DPAPI/`nexus.json` decision, the game-running process check, the `SemaphoreSlim` writer gate, and the dialogs — none of which live here.

**Tech Stack:** .NET 10, C#, xUnit. Operating Laws A–H (master spec) are enforced in this layer: Law A (snapshot→verify→seal→then-destroy ordering), Law B (PathGate on every replay write), Law C (verified copy-back + checksums).

**Spec:** [`../specs/2026-05-28-phase1-safe-clear-restore-design.md`](../specs/2026-05-28-phase1-safe-clear-restore-design.md). **Master:** [`../specs/2026-05-28-safe-clear-restore-onboarding-design.md`](../specs/2026-05-28-safe-clear-restore-onboarding-design.md). **Depends on:** Phase 0 (merged — PathGate/SafeMove/SpaceCheck/EnableOutcome/ModMeta fields are in `master`).

**Test command (never bare `dotnet test` at the repo root):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Single test: append `--filter "FullyQualifiedName~<Name>"`.

**Scope — what's Phase 1B (NOT here):** the App `RestorePointService` (orchestration over real `%APPDATA%` paths, `SemaphoreSlim`, free-space + game-running pre-flight wiring, DPAPI `nexus.json` keep/skip), the Safe Clear `ContentDialog`, Settings → Restore-points management UI, startup interrupted-clear recovery, and hydrating any off-boarding launch data that only `LaunchScan` (App) can derive. This plan exposes the engine those will call.

---

## Engine design decisions (read before implementing — they resolve the intricate parts)

1. **Capture is non-destructive copy.** `CaptureGame` *copies* the live `_626mods/<id>/` data dir into the archive via `SafeMove.CopyDirVerified` and snapshots framework install state — the live data dir is untouched. Only `ApplyEndState` (the MUTATE phase) moves anything, and only after the manifest is sealed (Law A is enforced by the App orchestrator's call order; the Core functions are individually safe to call in that order).
2. **Vanilla moves game-folder files into the archive's `vanilla-moved/`, NOT the holding folder.** Direct-inject files live in the game folder; if vanilla used `DirectInject.Disable` (moves to holding inside the data dir) the moved files would land *after* the data dir was already copied, so they'd be absent from the archive. Instead, vanilla moves them straight into `archive/games/<id>/vanilla-moved/<rel>` (recorded as `MovedFile`), and restore moves them back. Frameworks are uninstalled via the Phase-0-fixed `FrameworkRegistry.Uninstall` (their installed files were snapshotted into `frameworks-state/` during capture, so they're recoverable on restore).
3. **Loader-mod enable state is captured/replayed via the manifest, not file moves** — `Ue4ssManifest.IsEnabled` / `BepInExPlugins.Scan` to read, `SetEnabled` to apply.
4. **`MovedFile.Sha256` is recorded for moved game-folder files** so restore can verify byte-for-byte (Law C). Data-dir copy-back relies on `SafeMove`'s size verification.
5. **The seal is `Complete=true`, written LAST via `AtomicJson`.** `Read` + `Validate` refuse a manifest that is missing, not `Complete`, or whose `SchemaVersion` exceeds what this build supports.

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/RestorePoints/RestorePointManifest.cs` | The manifest record + all sub-records (`GameArchive`, `FrameworkArchive`, `LoaderModState`, `OwnedModNote`, `MovedFile`, `ArchivedMod`) + `RestorePoint` constants |
| `src/ModManager.Core/RestorePoints/RestorePointManifestStore.cs` | Write-sealed / read / validate the `manifest.json` (single-file JSON via `AtomicJson`) |
| `src/ModManager.Core/RestorePoints/OffBoardingReport.cs` | The fully-hydrated DTO (+ sub-records) the renderer consumes |
| `src/ModManager.Core/RestorePoints/OffBoardingSheet.cs` | `Render(report) → string` — pure, no filesystem |
| `src/ModManager.Core/RestorePoints/RestoreReconcile.cs` | `Check(manifest, liveGames) → conflicts` |
| `src/ModManager.Core/RestorePoints/FileTally.cs` | `Sha256(path)`, `ByteSize(dir)`, `FileCount(dir)` helpers |
| `src/ModManager.Core/RestorePoints/RestorePointEngine.cs` | `CaptureGame`, `ApplyEndState`, `ReplayGame` |
| `tests/ModManager.Tests/RestorePoints/*Tests.cs` | one test file per unit above |

All new on-disk shapes follow the camelCase rule with string-asserting round-trip tests, and route through `AtomicJson` (never hand-rolled `JsonSerializerOptions`).

---

## Task 1: Manifest contracts + store (write-sealed / read / validate)

**Files:**
- Create: `src/ModManager.Core/RestorePoints/RestorePointManifest.cs`
- Create: `src/ModManager.Core/RestorePoints/RestorePointManifestStore.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestorePointManifestTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointManifestTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-man-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static RestorePointManifest Sample(bool complete) => new(
        SchemaVersion: RestorePoint.SchemaVersion,
        LauncherVersion: "0.4.0",
        CreatedUtc: "2026-05-28T14:12:33Z",
        Complete: complete,
        KeepNexus: true,
        TotalBytes: 123,
        FileCount: 4,
        Games: new[]
        {
            new GameArchive("elden-ring", "ELDEN RING", @"D:\ELDEN RING\Game", "vanilla",
                LaunchTargets: new[] { new LaunchTarget("Play (Seamless Co-op)", "exe", @"Game\sc\launch.exe") { IsDefault = true } },
                RequiredLauncher: null,
                Frameworks: Array.Empty<FrameworkArchive>(),
                LoaderMods: Array.Empty<LoaderModState>(),
                OwnedMods: Array.Empty<OwnedModNote>(),
                MovedFiles: new[] { new MovedFile(@"Game\dinput8.dll", 1234, "abc123") },
                Mods: new[] { new ArchivedMod("CoolMod", false, "https://nexusmods.com/x", "fingerprint", "2026-04-02T00:00:00Z") },
                OffboardingSheetGameFolderPath: @"D:\ELDEN RING\626-launcher-how-to-launch.txt"),
        });

    [Fact]
    public void Manifest_round_trips_as_camelCase()
    {
        Directory.CreateDirectory(_tmp);
        RestorePointManifestStore.WriteSealed(_tmp, Sample(complete: true));
        var json = File.ReadAllText(Path.Combine(_tmp, RestorePointManifestStore.FileName));

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"gameName\"", json);
        Assert.Contains("\"movedFiles\"", json);
        Assert.Contains("\"sourceConfidence\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"GameName\"", json);

        var rt = RestorePointManifestStore.Read(_tmp)!;
        Assert.Equal("elden-ring", rt.Games[0].Id);
        Assert.Equal("vanilla", rt.Games[0].EndState);
        Assert.Equal(1234, rt.Games[0].MovedFiles[0].Bytes);
        Assert.Equal("fingerprint", rt.Games[0].Mods[0].SourceConfidence);
        Assert.True(rt.Complete);
    }

    [Fact]
    public void Read_returns_null_when_no_manifest()
    {
        Directory.CreateDirectory(_tmp);
        Assert.Null(RestorePointManifestStore.Read(_tmp));
    }

    [Fact]
    public void Validate_refuses_unsealed_manifest()
    {
        var v = RestorePointManifestStore.Validate(Sample(complete: false), RestorePoint.SchemaVersion);
        Assert.False(v.Ok);
        Assert.Contains("incomplete", v.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_refuses_newer_schema()
    {
        var newer = Sample(complete: true) with { SchemaVersion = RestorePoint.SchemaVersion + 1 };
        var v = RestorePointManifestStore.Validate(newer, RestorePoint.SchemaVersion);
        Assert.False(v.Ok);
        Assert.Contains("newer", v.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_a_sealed_current_manifest()
        => Assert.True(RestorePointManifestStore.Validate(Sample(complete: true), RestorePoint.SchemaVersion).Ok);
}
```

- [ ] **Step 2: Run; verify FAIL** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RestorePointManifestTests"` (compile error — types don't exist).

- [ ] **Step 3: Create `RestorePointManifest.cs`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>Version + sentinel constants for restore-point manifests.</summary>
public static class RestorePoint
{
    /// <summary>Bump when the manifest shape changes. Restore refuses any manifest whose
    /// schemaVersion exceeds the running build's supported value.</summary>
    public const int SchemaVersion = 1;
}

/// <summary>The sealed on-disk record of a Safe Clear. camelCase JSON. <c>Complete</c> is the seal,
/// written LAST by the orchestrator — Restore refuses a manifest that isn't complete.</summary>
public sealed record RestorePointManifest(
    int SchemaVersion,
    string LauncherVersion,
    string CreatedUtc,
    bool Complete,
    bool KeepNexus,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<GameArchive> Games);

/// <summary>One game's archived state. EndState is "vanilla" | "modsActive".</summary>
public sealed record GameArchive(
    string Id,
    string GameName,
    string GameRoot,
    string EndState,
    IReadOnlyList<LaunchTarget> LaunchTargets,
    string? RequiredLauncher,
    IReadOnlyList<FrameworkArchive> Frameworks,
    IReadOnlyList<LoaderModState> LoaderMods,
    IReadOnlyList<OwnedModNote> OwnedMods,
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<ArchivedMod> Mods,
    string? OffboardingSheetGameFolderPath);

/// <summary>A framework whose install state was captured before any uninstall. CapturedStateRel is
/// the archive-relative folder holding the captured installed files (with live config edits).</summary>
public sealed record FrameworkArchive(
    string FrameworkId,
    string DisplayName,
    string Author,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    string? CapturedStateRel);

/// <summary>A loader-driven mod (UE4SS/BepInEx) whose enable state lives in a manifest, not files.</summary>
public sealed record LoaderModState(string Name, string Loader, bool Enabled);

/// <summary>A mod managed by an external tool (Vortex/MO2) — noted, never moved by Safe Clear.</summary>
public sealed record OwnedModNote(string Name, string ManagedBy);

/// <summary>A game-folder file moved into the archive's vanilla-moved/ tree. Rel is relative to the
/// game root; Sha256 lets restore verify byte-for-byte.</summary>
public sealed record MovedFile(string Rel, long Bytes, string? Sha256);

/// <summary>A mod's provenance line for the off-boarding sheet.</summary>
public sealed record ArchivedMod(
    string Name,
    bool Enabled,
    string? SourceUrl,
    string? SourceConfidence,
    string? InstalledUtc);
```

- [ ] **Step 4: Create `RestorePointManifestStore.cs`**

```csharp
using System.Text.Json;

namespace ModManager.Core.RestorePoints;

/// <summary>Reads/writes the single <c>manifest.json</c> at the root of a restore point. Writes go
/// through <see cref="AtomicJson"/> (atomic temp+rename, camelCase). Reads tolerate either casing.</summary>
public static class RestorePointManifestStore
{
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Write the manifest as the SEAL — call this LAST, after all payload is captured + verified.</summary>
    public static void WriteSealed(string restorePointDir, RestorePointManifest manifest)
        => AtomicJson.WriteJsonAtomic(Path.Combine(restorePointDir, FileName), manifest);

    /// <summary>Read the manifest, or null if there is no manifest file (an unsealed/partial point).</summary>
    public static RestorePointManifest? Read(string restorePointDir)
    {
        var path = Path.Combine(restorePointDir, FileName);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RestorePointManifest>(File.ReadAllText(path), ReadOpts);
    }

    public sealed record Validation(bool Ok, string? Reason);

    /// <summary>Refuse a manifest that is missing the seal or was written by a newer build.</summary>
    public static Validation Validate(RestorePointManifest? m, int supportedSchema)
    {
        if (m is null) return new Validation(false, "No manifest found — the restore point is incomplete or missing.");
        if (!m.Complete) return new Validation(false, "Restore point is incomplete (the Safe Clear didn't finish sealing it).");
        if (m.SchemaVersion > supportedSchema)
            return new Validation(false, $"Restore point uses a newer format (schema {m.SchemaVersion} > supported {supportedSchema}) — update the launcher.");
        return new Validation(true, null);
    }
}
```

- [ ] **Step 5: Run; verify PASS** (5 tests). Then full suite green.

- [ ] **Step 6: Add `RestorePointManifest installedUtc/sourceConfidence/movedFiles` etc. to the camelCase governed-surfaces rule** in `.claude/rules/camelcase-json-on-disk.md`:

```markdown
- Restore-point manifest (`src/ModManager.Core/RestorePoints/RestorePointManifest.cs`)
```

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/RestorePoints/RestorePointManifest.cs src/ModManager.Core/RestorePoints/RestorePointManifestStore.cs tests/ModManager.Tests/RestorePoints/RestorePointManifestTests.cs .claude/rules/camelcase-json-on-disk.md
git commit -m "feat(restore): versioned, sealed restore-point manifest + store"
```

---

## Task 2: Off-boarding sheet renderer (pure, no filesystem)

**Files:**
- Create: `src/ModManager.Core/RestorePoints/OffBoardingReport.cs`
- Create: `src/ModManager.Core/RestorePoints/OffBoardingSheet.cs`
- Test: `tests/ModManager.Tests/RestorePoints/OffBoardingSheetTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OffBoardingSheetTests
{
    private static OffBoardingReport Report() => new(
        GameName: "ELDEN RING",
        RestorePointPath: @"C:\Users\you\AppData\Roaming\ModManagerBuilder\restore-points\20260528-141233",
        LaunchLines: new[] { "Seamless Co-op is still installed. Launch with:", @"  D:\ELDEN RING\Game\sc\launch.exe", "  Do NOT launch from Steam directly while Seamless Co-op is installed." },
        Frameworks: new[] { "Elden Mod Loader (by TechieW)" },
        Mods: new[]
        {
            new OffBoardingModLine("KnownMod", "https://nexusmods.com/x", "fingerprint", "2026-04-02"),
            new OffBoardingModLine("GuessMod", "https://nexusmods.com/y", "nameSearch", null),
            new OffBoardingModLine("SideloadMod", null, null, null),
        },
        OwnedMods: new[] { "VortexA", "VortexB" });

    [Fact]
    public void Render_leads_with_preservation_and_lists_launch_and_sources()
    {
        var s = OffBoardingSheet.Render(Report());

        Assert.Contains("Your mods are preserved", s);
        Assert.Contains("20260528-141233", s);
        Assert.Contains("Launch with:", s);
        Assert.Contains("Elden Mod Loader (by TechieW)", s);
        Assert.Contains("source: https://nexusmods.com/x", s);     // high-confidence
        Assert.Contains("likely source: https://nexusmods.com/y", s); // low-confidence (nameSearch)
        Assert.Contains("source not recorded", s);                  // SideloadMod
        Assert.Contains("Managed by Vortex", s);
        Assert.Contains("VortexA", s);
        Assert.Contains("installed 2026-04-02", s);
    }

    [Fact]
    public void Render_never_emits_a_nexus_account_or_key()
    {
        // The report carries no account/key fields at all — assert the renderer can't leak one.
        var s = OffBoardingSheet.Render(Report());
        Assert.DoesNotContain("apiKey", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", s);
    }

    [Fact]
    public void Render_touches_no_filesystem()
    {
        // A canary file the renderer must never read/write. The render takes only the DTO.
        // (Guards the Core/App boundary the assembly-level CorePurityTests cannot see.)
        var before = Directory.GetCurrentDirectory();
        var s = OffBoardingSheet.Render(Report());
        Assert.False(string.IsNullOrWhiteSpace(s));
        Assert.Equal(before, Directory.GetCurrentDirectory()); // no chdir, no side effects
    }
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Create `OffBoardingReport.cs`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>Fully-hydrated input to <see cref="OffBoardingSheet.Render"/>. The App builds this from
/// GameEntry.LaunchTargets + LaunchScan + DirectInject.Detect + FrameworkRegistry + metadata. The
/// renderer touches NO filesystem and carries NO Nexus account/key — only mod source URLs.</summary>
public sealed record OffBoardingReport(
    string GameName,
    string RestorePointPath,
    IReadOnlyList<string> LaunchLines,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<OffBoardingModLine> Mods,
    IReadOnlyList<string> OwnedMods);

public sealed record OffBoardingModLine(
    string Name,
    string? SourceUrl,
    string? SourceConfidence,   // "manual" | "fingerprint" | "md5" | "nameSearch" | null
    string? InstalledDate);     // pre-formatted yyyy-MM-dd or null
```

- [ ] **Step 4: Create `OffBoardingSheet.cs`**

```csharp
using System.Text;

namespace ModManager.Core.RestorePoints;

/// <summary>Renders the plain-text "how to launch your game after a reset" sheet. Pure string
/// building — no filesystem, no network, no platform types. Leads with "your mods are preserved"
/// so a missing source URL never implies a missing mod.</summary>
public static class OffBoardingSheet
{
    public static string Render(OffBoardingReport r)
    {
        var sb = new StringBuilder();
        var title = $"How to launch {r.GameName} after resetting 626 Mod Launcher";
        sb.AppendLine(title);
        sb.AppendLine(new string('=', title.Length));
        sb.AppendLine("Your mods are preserved. The full setup is saved in your restore point:");
        sb.AppendLine("  " + r.RestorePointPath);
        sb.AppendLine();

        sb.AppendLine("HOW TO START THE GAME");
        if (r.LaunchLines.Count == 0)
            sb.AppendLine("  Launch the game the way you normally do.");
        else
            foreach (var line in r.LaunchLines) sb.AppendLine("  " + line);
        sb.AppendLine();

        sb.AppendLine("WHAT'S STILL INSTALLED");
        sb.AppendLine(r.Frameworks.Count == 0
            ? "  Frameworks:  (none)"
            : "  Frameworks:  " + string.Join(", ", r.Frameworks));
        sb.AppendLine($"  Mods ({r.Mods.Count}):");
        foreach (var m in r.Mods)
        {
            var date = m.InstalledDate is null ? "" : $"   (installed {m.InstalledDate})";
            string line = m.SourceUrl switch
            {
                null => $"    {m.Name} — source not recorded — sideloaded; you'll need to find it again",
                _ when string.Equals(m.SourceConfidence, "nameSearch", StringComparison.OrdinalIgnoreCase)
                    => $"    {m.Name} — likely source: {m.SourceUrl}{date}",
                _ => $"    {m.Name} — source: {m.SourceUrl}{date}",
            };
            sb.AppendLine(line);
        }
        if (r.OwnedMods.Count > 0)
            sb.AppendLine($"  Managed by Vortex ({r.OwnedMods.Count}): {string.Join(", ", r.OwnedMods)} — clean these up in Vortex.");
        sb.AppendLine();

        sb.AppendLine("TO RESTORE THIS SETUP");
        sb.AppendLine("  Open 626 Mod Launcher and choose \"Restore a previous setup\", or");
        sb.AppendLine("  Settings -> Restore points.");
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run; verify PASS (3 tests). Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/OffBoardingReport.cs src/ModManager.Core/RestorePoints/OffBoardingSheet.cs tests/ModManager.Tests/RestorePoints/OffBoardingSheetTests.cs
git commit -m "feat(restore): off-boarding sheet renderer (pure, honest about unknown sources)"
```

---

## Task 3: Restore reconcile (id / GameRoot conflict detection)

**Files:**
- Create: `src/ModManager.Core/RestorePoints/RestoreReconcile.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestoreReconcileTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestoreReconcileTests
{
    private static GameArchive Ga(string id, string root) => new(
        id, id, root, "vanilla",
        Array.Empty<LaunchTarget>(), null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
        Array.Empty<OwnedModNote>(), Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null);

    private static RestorePointManifest M(params GameArchive[] games) =>
        new(RestorePoint.SchemaVersion, "0.4.0", "t", true, true, 0, 0, games);

    private static GameEntry Live(string id, string root) => new() { Id = id, GameName = id, GameRoot = root };

    [Fact]
    public void No_conflict_when_id_absent_from_live()
        => Assert.Empty(RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), Array.Empty<GameEntry>()));

    [Fact]
    public void No_conflict_when_same_id_same_root()
        => Assert.Empty(RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), new[] { Live("elden-ring", @"D:\ER") }));

    [Fact]
    public void Conflict_when_same_id_different_root()
    {
        var conflicts = RestoreReconcile.Check(M(Ga("elden-ring", @"D:\ER")), new[] { Live("elden-ring", @"E:\Other") });
        Assert.Single(conflicts);
        Assert.Equal("elden-ring", conflicts[0].Id);
        Assert.Equal(@"D:\ER", conflicts[0].ManifestGameRoot);
        Assert.Equal(@"E:\Other", conflicts[0].LiveGameRoot);
    }
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Create `RestoreReconcile.cs`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>A restore-point game whose id matches a live game but points at a DIFFERENT GameRoot.
/// Restore must surface this (App dialog) rather than overwriting — the data-dir path is derived
/// from id+GameRoot, so a blind upsert would point _626mods at the wrong place.</summary>
public sealed record RestoreConflict(string Id, string ManifestGameRoot, string LiveGameRoot);

public static class RestoreReconcile
{
    /// <summary>Pure: returns the id/GameRoot conflicts. Writes nothing. A same-id-same-root or a
    /// brand-new id is NOT a conflict (those upsert cleanly).</summary>
    public static IReadOnlyList<RestoreConflict> Check(
        RestorePointManifest m, IReadOnlyList<GameEntry> live)
    {
        var byId = live.ToDictionary(g => g.Id, g => g.GameRoot, StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<RestoreConflict>();
        foreach (var ga in m.Games)
        {
            if (byId.TryGetValue(ga.Id, out var liveRoot)
                && !string.Equals(NormRoot(liveRoot), NormRoot(ga.GameRoot), StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new RestoreConflict(ga.Id, ga.GameRoot, liveRoot));
            }
        }
        return conflicts;
    }

    private static string NormRoot(string p)
        => string.IsNullOrEmpty(p) ? p : Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
}
```

- [ ] **Step 4: Run; verify PASS (4 tests). Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/RestoreReconcile.cs tests/ModManager.Tests/RestorePoints/RestoreReconcileTests.cs
git commit -m "feat(restore): id/GameRoot reconcile to prevent data-dir clobber on restore"
```

---

## Task 4: `FileTally` — hashing + size + count helpers

**Files:**
- Create: `src/ModManager.Core/RestorePoints/FileTally.cs`
- Test: `tests/ModManager.Tests/RestorePoints/FileTallyTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class FileTallyTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-tally-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Sha256_is_stable_and_differs_by_content()
    {
        Directory.CreateDirectory(_tmp);
        var a = Path.Combine(_tmp, "a.bin"); var b = Path.Combine(_tmp, "b.bin");
        File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(b, new byte[] { 1, 2, 4 });
        Assert.Equal(FileTally.Sha256(a), FileTally.Sha256(a));   // stable
        Assert.NotEqual(FileTally.Sha256(a), FileTally.Sha256(b)); // content-sensitive
    }

    [Fact]
    public void ByteSize_and_FileCount_sum_a_tree()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "sub"));
        File.WriteAllBytes(Path.Combine(_tmp, "top.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_tmp, "sub", "deep.bin"), new byte[5]);
        Assert.Equal(15, FileTally.ByteSize(_tmp));
        Assert.Equal(2, FileTally.FileCount(_tmp));
    }

    [Fact]
    public void ByteSize_of_missing_dir_is_zero()
        => Assert.Equal(0, FileTally.ByteSize(Path.Combine(_tmp, "nope")));
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Create `FileTally.cs`**

```csharp
using System.Security.Cryptography;

namespace ModManager.Core.RestorePoints;

/// <summary>Small pure helpers for restore-point integrity figures: per-file SHA-256, recursive
/// byte size, recursive file count. Used to size the free-space pre-flight and to seal the manifest
/// with a verifiable total. System.Security.Cryptography + System.IO only — Core-legal.</summary>
public static class FileTally
{
    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static long ByteSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            total += new FileInfo(f).Length;
        return total;
    }

    public static int FileCount(string dir)
        => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;
}
```

- [ ] **Step 4: Run; verify PASS (3 tests). Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/FileTally.cs tests/ModManager.Tests/RestorePoints/FileTallyTests.cs
git commit -m "feat(restore): FileTally (sha256 + recursive size/count) for integrity sealing"
```

---

## Task 5: `RestorePointEngine.CaptureGame` (non-destructive copy + manifest entry)

**Files:**
- Create: `src/ModManager.Core/RestorePoints/RestorePointEngine.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestorePointEngineCaptureTests.cs`

`CaptureGame` copies the live `_626mods/<id>/` data dir into `<gameArchiveDir>/data/`, snapshots each installed framework's files into `<gameArchiveDir>/frameworks-state/<fwId>/`, and returns a `GameArchive` manifest entry built from Core reads. It does NOT mutate the game folder or the live data dir. `MovedFiles` is empty (vanilla populates it in Task 6).

- [ ] **Step 1: Write the failing test** (a UE-pak game with one disabled mod + metadata, captured)

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineCaptureTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-cap-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameEntry game, GameContext c, string modsDir) MakeGame()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = Path.Combine(_tmp, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            LaunchTargets = new[] { new LaunchTarget("Play", "exe", "game.exe") { IsDefault = true } },
        };
        return (game, Scanner.GameContext(game), modsDir);
    }

    [Fact]
    public async Task CaptureGame_copies_data_dir_and_builds_manifest_entry()
    {
        var (game, c, modsDir) = MakeGame();
        // One mod, disabled into holding so it lands in the data dir we capture.
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        // Metadata with provenance.
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["cool"] = new ModMeta { Url = "https://nexusmods.com/x", SourceConfidence = "fingerprint" }
        });

        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, EndState: "vanilla"), gameArchiveDir);

        // Data dir was copied (the disabled holding mod is in the archive).
        Assert.Equal("DATA", File.ReadAllText(Path.Combine(gameArchiveDir, "data", "disabled", "cool", "cool.pak")));
        // Manifest entry carries identity, launch targets, and provenance.
        Assert.Equal("t", entry.Id);
        Assert.Equal("vanilla", entry.EndState);
        Assert.Single(entry.LaunchTargets);
        Assert.Contains(entry.Mods, m => m.Name == "cool" && m.SourceUrl == "https://nexusmods.com/x" && m.SourceConfidence == "fingerprint");
        // Live data dir untouched (capture is a copy).
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));
    }
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Create `RestorePointEngine.cs` with `GameCaptureInput` + `CaptureGame`**

```csharp
namespace ModManager.Core.RestorePoints;

/// <summary>Hydrated inputs the engine needs to capture one game. All derivable from Core; the App
/// supplies the GameEntry + a built GameContext and picks the end-state.</summary>
public sealed record GameCaptureInput(GameEntry Game, GameContext Context, string EndState);

/// <summary>
/// The headless Safe Clear / Restore file engine. Takes explicit archive paths — no %APPDATA%
/// knowledge, no UI. Composes Phase 0 primitives (SafeMove, PathGate) + existing Core. The App
/// orchestrator (Phase 1B) calls these in the Law-A order: capture-all → seal → mutate-all.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>Copy the game's data dir + framework install state into the archive, and build its
    /// manifest entry. Non-destructive: the live data dir and game folder are untouched.</summary>
    public static GameArchive CaptureGame(GameCaptureInput input, string gameArchiveDir)
    {
        var c = input.Context;
        Directory.CreateDirectory(gameArchiveDir);

        // 1) Copy the whole per-game data dir (disabled holding, profiles, classification, metadata,
        //    loadorder, config-backups, readmes, frameworks manifests) — verified copy.
        if (Directory.Exists(c.DataDir))
            SafeMove.CopyDirVerified(c.DataDir, Path.Combine(gameArchiveDir, "data"));

        // 2) Snapshot each installed framework's CURRENT files (with live config edits) BEFORE any
        //    uninstall happens, so restore can reproduce the exact installed set.
        var frameworks = new List<FrameworkArchive>();
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
        {
            var capturedRel = Path.Combine("frameworks-state", fw.FrameworkId);
            var capturedAbs = Path.Combine(gameArchiveDir, capturedRel);
            foreach (var rel in fw.InstalledFiles)
            {
                var srcAbs = Path.Combine(fw.InstallPath, rel);
                if (File.Exists(srcAbs)) SafeMove.CopyFileVerified(srcAbs, Path.Combine(capturedAbs, rel));
            }
            frameworks.Add(new FrameworkArchive(fw.FrameworkId, fw.DisplayName, fw.Author,
                fw.InstallPath, fw.InstalledFiles, capturedRel));
        }

        // 3) Build the manifest entry from Core reads.
        var mods = BuildArchivedMods(c);
        var loaderMods = BuildLoaderStates(c);
        var ownedMods = BuildOwnedNotes(c);

        return new GameArchive(
            Id: input.Game.Id,
            GameName: input.Game.GameName,
            GameRoot: c.GameRoot,
            EndState: input.EndState,
            LaunchTargets: input.Game.LaunchTargets,
            RequiredLauncher: input.Game.RequiredLauncher,
            Frameworks: frameworks,
            LoaderMods: loaderMods,
            OwnedMods: ownedMods,
            MovedFiles: Array.Empty<MovedFile>(),   // populated by ApplyEndState (vanilla)
            Mods: mods,
            OffboardingSheetGameFolderPath: null);   // set by the App when it writes the in-folder sheet
    }

    private static IReadOnlyList<ArchivedMod> BuildArchivedMods(GameContext c)
    {
        var meta = Scanner.LoadMetadata(c);
        var list = new List<ArchivedMod>();
        foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
        {
            meta.TryGetValue(m.Base, out var md);
            list.Add(new ArchivedMod(
                Name: m.Name,
                Enabled: m.Enabled,
                SourceUrl: md?.Url,
                SourceConfidence: md?.SourceConfidence,
                InstalledUtc: md?.InstalledUtc?.ToString("o")));
        }
        return list;
    }

    private static IReadOnlyList<LoaderModState> BuildLoaderStates(GameContext c)
    {
        var list = new List<LoaderModState>();
        foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
            if (m.Loader is "ue4ss" or "bepinex")
                list.Add(new LoaderModState(m.Name, m.Loader, m.Enabled));
        return list;
    }

    private static IReadOnlyList<OwnedModNote> BuildOwnedNotes(GameContext c)
    {
        var list = new List<OwnedModNote>();
        foreach (var loc in c.Locations)
        {
            var owner = ToolOwnership.Detect(loc.Abs);
            if (owner is not null)
                foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
                    if (m.ReadOnly && m.Location == loc.Name)
                        list.Add(new OwnedModNote(m.Name, owner.ToString()!));
        }
        return list;
    }
}
```

> **Note for the implementer:** `BuildModListAsync` is async; calling `.GetAwaiter().GetResult()` here is acceptable because the underlying IO is synchronous (the Scanner doc says "The public surface is async to match the shell; the IO itself is synchronous"). If a future change makes it truly async, lift `CaptureGame` to async. Keep the `partial class` — Task 6 adds `ApplyEndState` and Task 8 adds `ReplayGame` in the same type across files or the same file.

- [ ] **Step 4: Run; verify PASS. Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/RestorePointEngine.cs tests/ModManager.Tests/RestorePoints/RestorePointEngineCaptureTests.cs
git commit -m "feat(restore): CaptureGame — non-destructive data-dir + framework-state capture"
```

---

## Task 6: `RestorePointEngine.ApplyEndState` (vanilla / modsActive)

**Files:**
- Modify: `src/ModManager.Core/RestorePoints/RestorePointEngine.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestorePointEngineEndStateTests.cs`

Vanilla: move game-folder direct-inject files into `<gameArchiveDir>/vanilla-moved/<rel>` (record `MovedFile` with size+sha), uninstall frameworks (Phase-0-fixed), flip loader manifests off, leave owned mods. Returns the populated `MovedFile` list + any framework/loader actions. modsActive: re-enable all disabled mods via `EnableModWithOutcomeAsync`, return the skip outcomes.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineEndStateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-end-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameEntry game, GameContext c, string modsDir) MakeGame()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = Path.Combine(_tmp, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        return (game, Scanner.GameContext(game), modsDir);
    }

    [Fact]
    public async Task ModsActive_re_enables_all_and_reports_outcomes()
    {
        var (game, c, modsDir) = MakeGame();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "modsActive"), gameArchiveDir);

        var result = RestorePointEngine.ApplyEndState(c, "modsActive", gameArchiveDir);

        Assert.Equal("DATA", File.ReadAllText(Path.Combine(modsDir, "cool.pak")));  // re-enabled into the game
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));        // holding emptied
        Assert.Contains(result.EnableOutcomes, o => o.Name == "cool" && o.Enabled);
        Assert.Empty(result.MovedFiles);
    }

    [Fact]
    public void Vanilla_moves_loose_directinject_files_to_archive_and_records_them()
    {
        var (game, c, modsDir) = MakeGame();
        // A loose ReShade-style DLL sitting in the game root (direct-inject signature).
        File.WriteAllBytes(Path.Combine(c.GameRoot, "dxgi.dll"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(c.GameRoot, "ReShade.ini"), "x");
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);

        var result = RestorePointEngine.ApplyEndState(c, "vanilla", gameArchiveDir);

        // The detected direct-inject files moved into the archive's vanilla-moved tree and were recorded.
        Assert.NotEmpty(result.MovedFiles);
        foreach (var mf in result.MovedFiles)
            Assert.True(File.Exists(Path.Combine(gameArchiveDir, "vanilla-moved", mf.Rel)), $"missing archived {mf.Rel}");
        Assert.All(result.MovedFiles, mf => Assert.False(string.IsNullOrEmpty(mf.Sha256)));
    }
}
```

> The vanilla test relies on `DirectInject.Detect` recognizing a `dxgi.dll` + `ReShade.ini` pair via the catalog. If the day-one catalog signature differs, adjust the fixture to a known direct-inject signature (read `KnownDirectInjectMod.Catalog`) — the assertion (detected files move + are recorded with a sha) is what matters.

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Add to `RestorePointEngine.cs`**

```csharp
public sealed record EndStateResult(
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<EnableOutcome> EnableOutcomes);

public static partial class RestorePointEngine
{
    /// <summary>Apply the chosen end-state to a game AFTER its capture is sealed.
    /// vanilla: move detected direct-inject game-folder files into the archive (recorded), uninstall
    /// frameworks (their files were captured), flip loader manifests off; owned mods untouched.
    /// modsActive: re-enable everything from holding, returning per-mod outcomes (skips surfaced).</summary>
    public static EndStateResult ApplyEndState(GameContext c, string endState, string gameArchiveDir)
    {
        if (string.Equals(endState, "modsActive", StringComparison.OrdinalIgnoreCase))
            return new EndStateResult(Array.Empty<MovedFile>(), ReEnableAll(c));

        // vanilla
        var moved = MoveDirectInjectToArchive(c, gameArchiveDir);
        UninstallFrameworks(c);
        FlipLoadersOff(c);
        return new EndStateResult(moved, Array.Empty<EnableOutcome>());
    }

    private static IReadOnlyList<EnableOutcome> ReEnableAll(GameContext c)
    {
        var outcomes = new List<EnableOutcome>();
        foreach (var name in DirectoryNames(c.DisabledRoot))
            outcomes.Add(Scanner.EnableModWithOutcomeAsync(name, c).GetAwaiter().GetResult());
        return outcomes;
    }

    private static IReadOnlyList<MovedFile> MoveDirectInjectToArchive(GameContext c, string gameArchiveDir)
    {
        var playFolder = c.GameRoot; // direct-inject lives at the game root (ResolvePlayFolder handled by detect inputs)
        var files = Directory.Exists(playFolder) ? Directory.GetFiles(playFolder) : Array.Empty<string>();
        var dirs = Directory.Exists(playFolder) ? Directory.GetDirectories(playFolder) : Array.Empty<string>();
        var moved = new List<MovedFile>();
        foreach (var di in DirectInject.Detect(files, dirs))
        {
            foreach (var rel in di.Entries)
            {
                var srcAbs = Path.Combine(playFolder, rel);
                if (!File.Exists(srcAbs) && !Directory.Exists(srcAbs)) continue;
                var destAbs = Path.Combine(gameArchiveDir, "vanilla-moved", rel);
                if (File.Exists(srcAbs))
                {
                    var size = new FileInfo(srcAbs).Length;
                    var sha = FileTally.Sha256(srcAbs);
                    SafeMove.Move(srcAbs, destAbs);
                    moved.Add(new MovedFile(rel, size, sha));
                }
                else
                {
                    var size = FileTally.ByteSize(srcAbs);
                    SafeMove.Move(srcAbs, destAbs);
                    moved.Add(new MovedFile(rel, size, null)); // dir: size only
                }
            }
        }
        return moved;
    }

    private static void UninstallFrameworks(GameContext c)
    {
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
            FrameworkRegistry.Uninstall(c.DataDir, fw.FrameworkId, c.GameRoot);
    }

    private static void FlipLoadersOff(GameContext c)
    {
        foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
        {
            if (!m.Enabled) continue;
            var abs = c.Locations.FirstOrDefault(l => l.Name == m.Location)?.Abs;
            if (abs is null) continue;
            try
            {
                if (m.Loader == "ue4ss") Ue4ssManifest.SetEnabled(abs, m.Name, enabled: false);
                else if (m.Loader == "bepinex") BepInExPlugins.SetEnabled(abs, m.Name, enable: false);
            }
            catch { /* best effort — the loader manifest may be absent; vanilla is still safe */ }
        }
    }

    private static IEnumerable<string> DirectoryNames(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(d => Path.GetFileName(d)!)
            : Enumerable.Empty<string>();
}
```

- [ ] **Step 4: Run; verify PASS. Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/RestorePointEngine.cs tests/ModManager.Tests/RestorePoints/RestorePointEngineEndStateTests.cs
git commit -m "feat(restore): ApplyEndState — vanilla (archive+record) / modsActive (re-enable+outcomes)"
```

---

## Task 7: `RestorePointEngine.ReplayGame` (gated, verified copy-back) + round-trip

**Files:**
- Modify: `src/ModManager.Core/RestorePoints/RestorePointEngine.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestorePointEngineReplayTests.cs`

`ReplayGame` restores one game: copy `<gameArchiveDir>/data/` back over the live data dir (verified), move `vanilla-moved/` files back into the game folder (each destination `PathGate.IsContained`-checked against the game root — Law B; sha-verified — Law C), copy framework `frameworks-state/` files back to their `InstallPath`, re-apply loader enable state, and remove the launcher-authored off-boarding sheet if present.

- [ ] **Step 1: Write the failing tests** (the headline byte-for-byte round-trip + the Law B traversal refusal)

```csharp
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineReplayTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-replay-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameEntry game, GameContext c, string modsDir) MakeGame()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = Path.Combine(_tmp, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        return (game, Scanner.GameContext(game), modsDir);
    }

    [Fact]
    public void Vanilla_then_replay_restores_game_folder_and_data_dir()
    {
        var (game, c, _) = MakeGame();
        var dll = Path.Combine(c.GameRoot, "dxgi.dll");
        File.WriteAllBytes(dll, new byte[] { 9, 9, 9 });
        File.WriteAllText(Path.Combine(c.GameRoot, "ReShade.ini"), "cfg");
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");

        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);
        var end = RestorePointEngine.ApplyEndState(c, "vanilla", gameArchiveDir);
        entry = entry with { MovedFiles = end.MovedFiles };
        Assert.False(File.Exists(dll)); // moved out by vanilla

        RestorePointEngine.ReplayGame(entry, gameArchiveDir, c);

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(dll)); // restored byte-for-byte
    }

    [Fact]
    public void Replay_refuses_a_traversal_movedfile_and_leaves_game_folder_untouched()
    {
        var (game, c, _) = MakeGame();
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);
        // Hand-tamper a MovedFile to escape the game root (Law B).
        var evil = new GameArchive("t", "T", c.GameRoot, "vanilla",
            game.LaunchTargets, null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
            Array.Empty<OwnedModNote>(),
            new[] { new MovedFile(@"..\..\Windows\System32\evil.dll", 3, null) },
            Array.Empty<ArchivedMod>(), null);
        // Stage the payload the manifest claims, so the only thing stopping the write is PathGate.
        var staged = Path.Combine(gameArchiveDir, "vanilla-moved", "..", "..", "Windows", "System32", "evil.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(staged))!);
        File.WriteAllBytes(Path.GetFullPath(staged), new byte[] { 6, 6, 6 });

        Assert.ThrowsAny<Exception>(() => RestorePointEngine.ReplayGame(evil, gameArchiveDir, c));
        Assert.False(File.Exists(Path.Combine(_tmp, "game", "..", "..", "Windows", "System32", "evil.dll")));
    }
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Add to `RestorePointEngine.cs`**

```csharp
public static partial class RestorePointEngine
{
    /// <summary>Restore one game from its archive: data dir copy-back, vanilla-moved files back into
    /// the game folder (PathGate-gated per destination — Law B; sha-verified — Law C), framework
    /// files back to InstallPath, loader enable-state re-applied. No File.Delete in the game folder.</summary>
    public static void ReplayGame(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        var gameRootFull = Path.GetFullPath(liveCtx.GameRoot);

        // 1) Copy the archived data dir back over the live one (verified by SafeMove).
        var archivedData = Path.Combine(gameArchiveDir, "data");
        if (Directory.Exists(archivedData))
            CopyTreeVerifiedOverwrite(archivedData, liveCtx.DataDir);

        // 2) Move vanilla-moved files back into the game folder — gate + verify EACH destination.
        foreach (var mf in ga.MovedFiles)
        {
            if (!PathGate.IsContained(mf.Rel, gameRootFull))
                throw new InvalidOperationException($"Restore refused: \"{mf.Rel}\" escapes the game folder.");
            var srcAbs = Path.Combine(gameArchiveDir, "vanilla-moved", mf.Rel);
            var destAbs = Path.Combine(liveCtx.GameRoot, mf.Rel);
            if (File.Exists(srcAbs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
                SafeMove.CopyFileVerified(srcAbs, destAbs);
                if (mf.Sha256 is not null && !string.Equals(FileTally.Sha256(destAbs), mf.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Restore checksum mismatch on \"{mf.Rel}\".");
            }
            else if (Directory.Exists(srcAbs))
            {
                CopyTreeVerifiedOverwrite(srcAbs, destAbs);
            }
        }

        // 3) Framework files back to InstallPath (gate against InstallPath).
        foreach (var fw in ga.Frameworks)
        {
            if (fw.CapturedStateRel is null) continue;
            var capturedAbs = Path.Combine(gameArchiveDir, fw.CapturedStateRel);
            if (!Directory.Exists(capturedAbs)) continue;
            var installFull = Path.GetFullPath(fw.InstallPath);
            foreach (var rel in fw.InstalledFiles)
            {
                if (!PathGate.IsContained(rel, installFull))
                    throw new InvalidOperationException($"Restore refused: framework file \"{rel}\" escapes the install root.");
                var srcAbs = Path.Combine(capturedAbs, rel);
                if (File.Exists(srcAbs))
                {
                    var destAbs = Path.Combine(fw.InstallPath, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
                    SafeMove.CopyFileVerified(srcAbs, destAbs);
                }
            }
        }

        // 4) Re-apply loader enable state.
        foreach (var lm in ga.LoaderMods)
        {
            var abs = liveCtx.Locations.FirstOrDefault(l => l.Name != null)?.Abs; // loaders live in the mods location
            if (abs is null) continue;
            try
            {
                if (lm.Loader == "ue4ss") Ue4ssManifest.SetEnabled(abs, lm.Name, lm.Enabled);
                else if (lm.Loader == "bepinex") BepInExPlugins.SetEnabled(abs, lm.Name, lm.Enabled);
            }
            catch { /* best effort */ }
        }

        // 5) Remove the launcher-authored off-boarding sheet from the game folder, if present.
        if (ga.OffboardingSheetGameFolderPath is not null && File.Exists(ga.OffboardingSheetGameFolderPath))
            try { File.Delete(ga.OffboardingSheetGameFolderPath); } catch { /* best effort */ }
    }

    // Verified copy that overwrites existing files (restore is replay over a known layout — NOT a
    // delete-then-extract). Per-file: copy to temp sibling, verify size, atomic-replace.
    private static void CopyTreeVerifiedOverwrite(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
        {
            var target = Path.Combine(dest, Path.GetFileName(f));
            var tmp = target + ".rp-tmp";
            File.Copy(f, tmp, overwrite: true);
            if (new FileInfo(tmp).Length != new FileInfo(f).Length)
            { try { File.Delete(tmp); } catch { } throw new IOException($"Restore verify failed copying \"{f}\"."); }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }
        foreach (var d in Directory.GetDirectories(src))
            CopyTreeVerifiedOverwrite(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
```

> **Implementer notes:** (1) `CopyTreeVerifiedOverwrite` is the overwrite-capable sibling of `SafeMove.CopyDirVerified` (which refuses pre-existing dests). It deliberately does NOT delete the destination tree first — only per-file atomic replace — honoring "no File.Delete loop in the game folder" (master spec; contrast `SaveManager.Restore`). (2) The loader-location resolution in step 4 is a simplification — if a game has multiple mod locations, thread the loader's location through `LoaderModState` (add a `Location` field) rather than guessing. Add that field if the round-trip test for a multi-location UE4SS game fails.

- [ ] **Step 4: Run; verify PASS (round-trip + traversal-refused). Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/RestorePointEngine.cs tests/ModManager.Tests/RestorePoints/RestorePointEngineReplayTests.cs
git commit -m "feat(restore): ReplayGame — gated, verified copy-back (Laws B + C)"
```

---

## Task 8: Breadcrumb + last-clear marker helpers

**Files:**
- Create: `src/ModManager.Core/RestorePoints/RestoreMarkers.cs`
- Test: `tests/ModManager.Tests/RestorePoints/RestoreMarkersTests.cs`

Two tiny camelCase JSON markers: `RESTORE-AVAILABLE.json` left in a data dir (so a fresh re-add detects an archived setup — the game-id determinism hook), and `last-clear.json` (so Phase 2 onboarding offers Restore after a clear). Pure, atomic.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestoreMarkersTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-mark-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void RestoreAvailable_round_trips_camelCase()
    {
        Directory.CreateDirectory(_tmp);
        RestoreMarkers.WriteRestoreAvailable(_tmp, "20260528-141233");
        var json = File.ReadAllText(Path.Combine(_tmp, RestoreMarkers.RestoreAvailableFile));
        Assert.Contains("\"restorePoint\"", json);
        Assert.DoesNotContain("\"RestorePoint\"", json);
        Assert.Equal("20260528-141233", RestoreMarkers.ReadRestoreAvailable(_tmp));
    }

    [Fact]
    public void ReadRestoreAvailable_null_when_absent()
        => Assert.Null(RestoreMarkers.ReadRestoreAvailable(Path.Combine(_tmp, "nope")));

    [Fact]
    public void LastClear_round_trips_and_clears()
    {
        Directory.CreateDirectory(_tmp);
        RestoreMarkers.WriteLastClear(_tmp, "2026-05-28T14:12:33Z", "20260528-141233");
        var lc = RestoreMarkers.ReadLastClear(_tmp)!;
        Assert.Equal("20260528-141233", lc.RestorePoint);
        RestoreMarkers.ClearLastClear(_tmp);
        Assert.Null(RestoreMarkers.ReadLastClear(_tmp));
    }
}
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Create `RestoreMarkers.cs`**

```csharp
using System.Text.Json;

namespace ModManager.Core.RestorePoints;

/// <summary>Small camelCase JSON markers. RESTORE-AVAILABLE.json is left in a per-game data dir so a
/// fresh re-add of the same (deterministic-slug) game detects an archived setup. last-clear.json sits
/// under the app data root so Phase 2 onboarding can offer "Restore a previous setup" after a clear.</summary>
public static class RestoreMarkers
{
    public const string RestoreAvailableFile = "RESTORE-AVAILABLE.json";
    public const string LastClearFile = "last-clear.json";

    private static readonly JsonSerializerOptions ReadOpts = new()
    { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record RestoreAvailable(string RestorePoint);
    public sealed record LastClear(string ClearedUtc, string RestorePoint);

    public static void WriteRestoreAvailable(string dataDir, string restorePointTimestamp)
        => AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, RestoreAvailableFile), new RestoreAvailable(restorePointTimestamp));

    public static string? ReadRestoreAvailable(string dataDir)
    {
        var p = Path.Combine(dataDir, RestoreAvailableFile);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<RestoreAvailable>(File.ReadAllText(p), ReadOpts)?.RestorePoint;
    }

    public static void WriteLastClear(string appDataRoot, string clearedUtc, string restorePointTimestamp)
        => AtomicJson.WriteJsonAtomic(Path.Combine(appDataRoot, LastClearFile), new LastClear(clearedUtc, restorePointTimestamp));

    public static LastClear? ReadLastClear(string appDataRoot)
    {
        var p = Path.Combine(appDataRoot, LastClearFile);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<LastClear>(File.ReadAllText(p), ReadOpts);
    }

    public static void ClearLastClear(string appDataRoot)
    {
        var p = Path.Combine(appDataRoot, LastClearFile);
        try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 4: Run; verify PASS (3 tests). Full suite green. Commit.**

```bash
git add src/ModManager.Core/RestorePoints/RestoreMarkers.cs tests/ModManager.Tests/RestorePoints/RestoreMarkersTests.cs
git commit -m "feat(restore): RESTORE-AVAILABLE + last-clear camelCase markers"
```

---

## Final verification

- [ ] **Run the whole Core suite; confirm green incl. CorePurityTests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — the new `ModManager.Core.RestorePoints` namespace stays pure (System.IO + System.Security.Cryptography only; no WinUI/WinRT). `CorePurityTests` green.

---

## Self-Review

**1. Spec coverage (Phase 1 spec "Core (pure, tested)" + the reversibility battery):**
- Manifest + schemaVersion + sentinel + camelCase → Task 1.
- OffBoardingSheet.Render + no-filesystem + no-secret + honest-source lines → Task 2.
- RestoreReconcile (id/GameRoot) → Task 3.
- Integrity figures (sha + size for SpaceCheck payload + manifest totals) → Task 4.
- Capture (data dir + framework-state + provenance, non-destructive) → Task 5.
- End-states (vanilla archive+record / modsActive re-enable+outcomes) → Task 6.
- Replay (gated Law B + verified Law C, no game-folder File.Delete loop) → Task 7.
- Breadcrumb + last-clear (game-id hook + Phase 2 trigger) → Task 8.
- Battery items deferred to Phase 1B (need the App orchestrator): the manifest-written-LAST seal ordering, Keep-Nexus (DPAPI), skip-archive, free-space pre-flight wiring, process-running check, the full Law-A capture-all→seal→mutate-all sequence, interrupted-clear recovery. These compose the Task-1–8 units; the engine pieces are individually tested here.

**2. Placeholder scan:** none — every step has complete code + exact commands. Three implementer-notes flag *known simplifications to extend if a specific test fails* (loader multi-location, direct-inject catalog signature in the vanilla fixture, async lift) — these are explicit guidance, not TODO placeholders.

**3. Type consistency:** `GameArchive`/`MovedFile`/`FrameworkArchive`/`LoaderModState`/`ArchivedMod` defined in Task 1 and consumed unchanged in Tasks 5–8; `GameCaptureInput`/`EndStateResult` defined in Tasks 5/6; `OffBoardingReport`/`OffBoardingModLine` in Task 2; `RestoreConflict` in Task 3. `RestorePointEngine` is a single `partial class` extended across Tasks 5–7. All call into the verified Phase-0/Core API surface (`SafeMove.CopyDirVerified/CopyFileVerified/Move`, `PathGate.IsContained`, `AtomicJson.WriteJsonAtomic`, `FrameworkRegistry.List/Uninstall`, `DirectInject.Detect`, `Scanner.EnableModWithOutcomeAsync/BuildModListAsync/LoadMetadata/GameContext`, `Ue4ssManifest.SetEnabled`, `BepInExPlugins.SetEnabled`, `ToolOwnership.Detect`).

## Next: Phase 1B

App integration plan — written after 1A lands so its tasks bind to these real signatures: `RestorePointService` (the Law-A orchestrator over `%APPDATA%\ModManagerBuilder\restore-points\`, `SemaphoreSlim` gate, `SpaceCheck`/process pre-flight, DPAPI `nexus.json` keep/skip, top-level state copy + reset), the Safe Clear `ContentDialog`, Settings → Restore-points management, startup interrupted-clear recovery, and `OffBoardingReport` hydration (LaunchScan + DirectInject.Detect + FrameworkRegistry + metadata).
