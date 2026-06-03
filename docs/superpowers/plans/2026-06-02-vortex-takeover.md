# Vortex Takeover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user who has migrated off Vortex take over a Vortex-managed mod folder so the launcher manages it normally (toggle / uninstall / conduct), reversibly and with explicit consent.

**Architecture:** A pure-Core `VortexTakeover` primitive archives the Vortex ownership marker out of the folder (move-to-holding) and records the folder in a persisted taken-over set; the ownership/posture read consults that set so a taken-over folder reads as not-owned even with a marker still present, and a *reappeared* marker reads as "re-deployed". A shared `OwnershipMarkers` helper is the single source of truth for what counts as a marker. The App adds a banner + an on-block prompt that call the Core primitive and reload.

**Tech Stack:** .NET 10 / C#, xUnit (headless Core tests), `System.IO` + `System.Text.Json` only in Core, WinUI 3 in the App shell. camelCase JSON on disk via `AtomicJson`.

**Reference reading before starting:**
- Spec: `docs/superpowers/specs/2026-06-02-vortex-takeover-design.md`
- `src/ModManager.Core/ToolOwnership.cs` — current marker detection (the thing being refactored)
- `src/ModManager.Core/Coordination.cs` — `PostureFor`
- `src/ModManager.Core/VortexManifest.cs` — existing Vortex manifest reader (pattern reference)
- `src/ModManager.Core/AtomicJson.cs` — `WriteJsonAtomic<T>` (camelCase, atomic; the canonical write wrapper)
- `src/ModManager.Core/Ue4ssLuaInstaller.cs` — the stage-then-commit / rollback pattern to mirror
- `src/ModManager.Core/Scanner.cs:39-76` (`GameContext`) and `:168-225` (`BuildModList`) — where posture is read
- `.claude/rules/camelcase-json-on-disk.md` — the round-trip test requirement

**Build/test commands (Windows):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- One test: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~<TestClass>"`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- NEVER run bare `dotnet test`/`dotnet build` at repo root (the WinUI project hangs).

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ModManager.Core/OwnershipMarkers.cs` | NEW. Single source of truth for the marker file set + which owner each implies. |
| `src/ModManager.Core/VortexTakeover.cs` | NEW. `TakeOver` / `Undo` / `TakeOverGame` + result/manifest records. The reversible primitive + persisted state. |
| `src/ModManager.Core/ToolOwnership.cs` | MODIFY. Refactor `Detect` onto `OwnershipMarkers`; add taken-over-aware `Resolve`. |
| `src/ModManager.Core/Coordination.cs` | MODIFY. `PostureFor` gains a `reDeployed` input. |
| `src/ModManager.Core/Scanner.cs` | MODIFY. Load taken-over set in `GameContext`; thread it through the posture read in `BuildModList`. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | MODIFY. Takeover VM actions; banner state; reload-after. |
| `src/ModManager.App/Vortex/VortexTakeoverDialog.xaml(.cs)` | NEW. On-block consent prompt (modeled on `FrameworkUnrecognizedNudgeDialog`). |
| `src/ModManager.App/MainWindow.xaml(.cs)` | MODIFY. Banner bar + buttons. |
| `tests/ModManager.Tests/OwnershipMarkersTests.cs` | NEW. |
| `tests/ModManager.Tests/VortexTakeoverTests.cs` | NEW. |
| `tests/ModManager.Tests/CoordinationTests.cs` | MODIFY/NEW. Re-deployed posture. |
| `tests/ModManager.Tests/ToolOwnershipResolveTests.cs` | NEW. |

The App tasks (banner + dialog) are the last two tasks; everything before them is headless Core and fully testable.

---

## Task 1: OwnershipMarkers — the shared marker set

**Files:**
- Create: `src/ModManager.Core/OwnershipMarkers.cs`
- Test: `tests/ModManager.Tests/OwnershipMarkersTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class OwnershipMarkersTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "own-markers-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string Folder()
    {
        Directory.CreateDirectory(_tmp);
        return _tmp;
    }

    [Fact]
    public void Finds_vortex_deployment_manifest()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "vortex.deployment.windrose-scripts.json"), "{}");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Single(markers);
        Assert.Equal(OwnerTool.Vortex, markers[0].Owner);
        Assert.EndsWith("vortex.deployment.windrose-scripts.json", markers[0].Path);
    }

    [Fact]
    public void Finds_vortex_managed_flag_file()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "__folder_managed_by_vortex"), "");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Contains(markers, m => m.Owner == OwnerTool.Vortex && m.Path.EndsWith("__folder_managed_by_vortex"));
    }

    [Fact]
    public void Finds_mo2_meta_ini()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "meta.ini"), "[General]");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Contains(markers, m => m.Owner == OwnerTool.Mo2 && m.Path.EndsWith("meta.ini"));
    }

    [Fact]
    public void Empty_when_no_markers_or_missing_folder()
    {
        Assert.Empty(OwnershipMarkers.MarkerFilesIn(Folder()));
        Assert.Empty(OwnershipMarkers.MarkerFilesIn(Path.Combine(_tmp, "does-not-exist")));
    }

    [Fact]
    public void OwnerOf_returns_the_first_marker_owner_or_null()
    {
        var f = Folder();
        Assert.Null(OwnershipMarkers.OwnerOf(f));
        File.WriteAllText(Path.Combine(f, "vortex.deployment.x.json"), "{}");
        Assert.Equal(OwnerTool.Vortex, OwnershipMarkers.OwnerOf(f));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OwnershipMarkersTests"`
Expected: FAIL — `OwnershipMarkers` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ModManager.Core;

/// <summary>One on-disk ownership marker found in a folder: its absolute path + the tool it implies.</summary>
public sealed record OwnershipMarker(string Path, OwnerTool Owner);

/// <summary>
/// Single source of truth for the marker files that mean "another mod manager owns this folder".
/// Both <see cref="ToolOwnership.Detect"/> (read) and <see cref="VortexTakeover"/> (archive) consult
/// this so detection and takeover can never drift on what counts as a marker. Pure System.IO; no writes.
/// </summary>
public static class OwnershipMarkers
{
    // Vortex: a per-folder flag file and/or a deployment manifest. MO2: a per-mod meta.ini.
    private const string VortexFlag = "__folder_managed_by_vortex";
    private const string VortexManifestGlob = "vortex.deployment.*.json";
    private const string Mo2Meta = "meta.ini";

    /// <summary>Every ownership marker physically present in <paramref name="folderAbs"/>.</summary>
    public static IReadOnlyList<OwnershipMarker> MarkerFilesIn(string folderAbs)
    {
        var found = new List<OwnershipMarker>();
        if (string.IsNullOrWhiteSpace(folderAbs)) return found;
        try
        {
            if (!Directory.Exists(folderAbs)) return found;

            var flag = Path.Combine(folderAbs, VortexFlag);
            if (File.Exists(flag)) found.Add(new OwnershipMarker(flag, OwnerTool.Vortex));

            foreach (var m in Directory.EnumerateFiles(folderAbs, VortexManifestGlob))
                found.Add(new OwnershipMarker(m, OwnerTool.Vortex));

            var meta = Path.Combine(folderAbs, Mo2Meta);
            if (File.Exists(meta)) found.Add(new OwnershipMarker(meta, OwnerTool.Mo2));
        }
        catch { /* unreadable folder -> treat as no markers */ }
        return found;
    }

    /// <summary>The owner implied by the first marker present, or null when none.</summary>
    public static OwnerTool? OwnerOf(string folderAbs)
        => MarkerFilesIn(folderAbs) is { Count: > 0 } m ? m[0].Owner : null;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OwnershipMarkersTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/OwnershipMarkers.cs tests/ModManager.Tests/OwnershipMarkersTests.cs
git commit -m "feat(vortex): OwnershipMarkers — single source of truth for owner markers"
```

---

## Task 2: Refactor ToolOwnership.Detect onto OwnershipMarkers (no behavior change)

**Files:**
- Modify: `src/ModManager.Core/ToolOwnership.cs`
- Test: existing `ToolOwnership` behavior must stay green; add a regression test alongside Task 1's file if one doesn't exist.

- [ ] **Step 1: Write the failing/guard test**

Add to `tests/ModManager.Tests/OwnershipMarkersTests.cs`:

```csharp
    [Fact]
    public void Detect_still_matches_the_same_set_after_refactor()
    {
        var f = Folder();
        Assert.Null(ToolOwnership.Detect(f));
        File.WriteAllText(Path.Combine(f, "vortex.deployment.x.json"), "{}");
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(f));
    }
```

- [ ] **Step 2: Run test to verify current behavior**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OwnershipMarkersTests"`
Expected: the new test PASSES already (Detect still uses inline logic). This guards the refactor.

- [ ] **Step 3: Refactor Detect to delegate to OwnershipMarkers**

Replace the body of `Detect` in `src/ModManager.Core/ToolOwnership.cs` (keep the method + enum + XML docs):

```csharp
    public static OwnerTool? Detect(string folderAbs) => OwnershipMarkers.OwnerOf(folderAbs);
```

Leave `enum OwnerTool { Vortex, Mo2 }` where it is (other code references it).

- [ ] **Step 4: Run the full suite to verify nothing regressed**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (same count as before + the new guard test). If any owner-related test fails, the marker set drifted — reconcile `OwnershipMarkers` with the old inline logic.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ToolOwnership.cs tests/ModManager.Tests/OwnershipMarkersTests.cs
git commit -m "refactor(vortex): ToolOwnership.Detect delegates to OwnershipMarkers"
```

---

## Task 3: TakenOverStore — the persisted taken-over set

**Files:**
- Create: `src/ModManager.Core/VortexTakeover.cs` (start the file with the persisted-state type only; the operation comes in Task 4)
- Test: `tests/ModManager.Tests/VortexTakeoverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class VortexTakeoverTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vtx-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string DataDir()
    {
        var d = Path.Combine(_tmp, "data");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void TakenOverSet_round_trips_as_camelCase()
    {
        var data = DataDir();
        Assert.Empty(TakenOverStore.Load(data));

        TakenOverStore.Add(data, @"C:\game\R5\Binaries\Win64\ue4ss\Mods");
        var json = File.ReadAllText(Path.Combine(data, "taken-over.json"));
        Assert.Contains("\"folders\"", json);   // camelCase key on disk
        Assert.DoesNotContain("\"Folders\"", json);

        var set = TakenOverStore.Load(data);
        Assert.Contains(@"C:\game\R5\Binaries\Win64\ue4ss\Mods", set);
    }

    [Fact]
    public void Add_is_idempotent_and_Remove_drops_the_entry()
    {
        var data = DataDir();
        TakenOverStore.Add(data, @"C:\g\mods");
        TakenOverStore.Add(data, @"C:\g\mods"); // dup
        Assert.Single(TakenOverStore.Load(data));

        TakenOverStore.Remove(data, @"C:\g\mods");
        Assert.Empty(TakenOverStore.Load(data));
    }

    [Fact]
    public void Load_treats_missing_or_corrupt_file_as_empty()
    {
        var data = DataDir();
        Assert.Empty(TakenOverStore.Load(data));               // missing
        File.WriteAllText(Path.Combine(data, "taken-over.json"), "{ not json");
        Assert.Empty(TakenOverStore.Load(data));               // corrupt
    }

    [Fact]
    public void Contains_is_case_insensitive_on_path()
    {
        var data = DataDir();
        TakenOverStore.Add(data, @"C:\Game\Mods");
        Assert.True(TakenOverStore.Load(data).Contains(@"c:\game\mods"));
    }
}
```

Note: `TakenOverStore.Load` must return a case-insensitive set (so the last test passes).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverTests"`
Expected: FAIL — `TakenOverStore` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/ModManager.Core/VortexTakeover.cs` with the persisted-state type:

```csharp
using System.Text.Json;

namespace ModManager.Core;

/// <summary>The persisted set of folders the user has taken over from another manager. Lives at
/// <c>&lt;dataDir&gt;/taken-over.json</c> (camelCase). Posture consults it so a taken-over folder reads
/// as not-owned even if a marker is still physically present.</summary>
public sealed class TakenOverState
{
    public int Version { get; set; } = 1;
    public List<string> Folders { get; set; } = new();
}

/// <summary>Read/write the taken-over set. camelCase via AtomicJson; case-insensitive on path.</summary>
public static class TakenOverStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "taken-over.json");

    /// <summary>The taken-over folders as a case-insensitive set. Missing/corrupt file -> empty.</summary>
    public static HashSet<string> Load(string dataDir)
    {
        try
        {
            var p = PathFor(dataDir);
            if (!File.Exists(p)) return new(StringComparer.OrdinalIgnoreCase);
            var state = JsonSerializer.Deserialize<TakenOverState>(File.ReadAllText(p), Json);
            return new HashSet<string>(state?.Folders ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Add(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Add(folderAbs)) return;            // already present -> no rewrite
        Save(dataDir, set);
    }

    public static void Remove(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Remove(folderAbs)) return;
        Save(dataDir, set);
    }

    private static void Save(string dataDir, HashSet<string> set)
        => AtomicJson.WriteJsonAtomic(PathFor(dataDir), new TakenOverState { Version = 1, Folders = set.ToList() });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VortexTakeover.cs tests/ModManager.Tests/VortexTakeoverTests.cs
git commit -m "feat(vortex): TakenOverStore — persisted camelCase taken-over set"
```

---

## Task 4: VortexTakeover.TakeOver + Undo (reversible marker archive)

**Files:**
- Modify: `src/ModManager.Core/VortexTakeover.cs` (add the operation + manifest types)
- Test: `tests/ModManager.Tests/VortexTakeoverTests.cs` (add cases)

- [ ] **Step 1: Write the failing test**

Add to `VortexTakeoverTests`:

```csharp
    // A folder under a fake game root, holding a Vortex manifest. Returns (gameRoot, folderAbs).
    private (string gameRoot, string folderAbs) FolderWithVortexMarker()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var folder = Path.Combine(gameRoot, "R5", "Binaries", "Win64", "ue4ss", "Mods");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "vortex.deployment.windrose-scripts.json"), "{\"files\":[]}");
        File.WriteAllText(Path.Combine(folder, "SomeMod.lua"), "real mod content"); // must survive
        return (gameRoot, folder);
    }

    [Fact]
    public void TakeOver_archives_the_marker_out_and_records_the_folder()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();

        var result = VortexTakeover.TakeOver(data, gameRoot, folder);

        Assert.True(result.Success);
        // Marker gone from the live folder...
        Assert.False(File.Exists(Path.Combine(folder, "vortex.deployment.windrose-scripts.json")));
        // ...real mod untouched...
        Assert.Equal("real mod content", File.ReadAllText(Path.Combine(folder, "SomeMod.lua")));
        // ...folder now reads as NOT owned (marker physically gone)...
        Assert.Null(ToolOwnership.Detect(folder));
        // ...and it's recorded in the taken-over set + an archive manifest exists.
        Assert.Contains(folder, TakenOverStore.Load(data));
        Assert.True(Directory.Exists(Path.Combine(data, "vortex-takeover")));
    }

    [Fact]
    public void Undo_restores_the_marker_byte_for_byte_and_clears_the_record()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();
        var markerPath = Path.Combine(folder, "vortex.deployment.windrose-scripts.json");
        var before = File.ReadAllBytes(markerPath);

        VortexTakeover.TakeOver(data, gameRoot, folder);
        Assert.False(File.Exists(markerPath));

        VortexTakeover.Undo(data, folder);

        Assert.True(File.Exists(markerPath));
        Assert.Equal(before, File.ReadAllBytes(markerPath));         // byte-for-byte
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(folder)); // owned again
        Assert.DoesNotContain(folder, TakenOverStore.Load(data));    // record cleared
    }

    [Fact]
    public void TakeOver_on_a_folder_with_no_marker_is_a_noop_success()
    {
        var data = DataDir();
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var folder = Path.Combine(gameRoot, "clean");
        Directory.CreateDirectory(folder);

        var result = VortexTakeover.TakeOver(data, gameRoot, folder);
        Assert.True(result.Success);
        Assert.Empty(result.ArchivedMarkers);
    }

    [Fact]
    public void TakeOver_is_idempotent_no_duplicate_set_entry()
    {
        var data = DataDir();
        var (gameRoot, folder) = FolderWithVortexMarker();

        VortexTakeover.TakeOver(data, gameRoot, folder);
        // simulate a Vortex re-deploy dropping a fresh marker, then take over again
        File.WriteAllText(Path.Combine(folder, "vortex.deployment.windrose-scripts.json"), "{\"files\":[]}");
        VortexTakeover.TakeOver(data, gameRoot, folder);

        Assert.Single(TakenOverStore.Load(data).Where(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)));
        Assert.False(File.Exists(Path.Combine(folder, "vortex.deployment.windrose-scripts.json")));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverTests"`
Expected: FAIL — `VortexTakeover.TakeOver` / `Undo` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `src/ModManager.Core/VortexTakeover.cs`:

```csharp
/// <summary>One archived marker in a takeover manifest: where it came from + the file we stored.</summary>
public sealed record ArchivedMarker(string OriginalPath, string ArchivedName, string Owner);

/// <summary>The manifest written into a takeover archive dir, recording how to reverse the takeover.</summary>
public sealed class TakeoverManifest
{
    public int Version { get; set; } = 1;
    public DateTime TakenOverUtc { get; set; }
    public List<ArchivedMarker> Markers { get; set; } = new();
}

/// <summary>The result of a TakeOver call.</summary>
public sealed record TakeoverResult(bool Success, string FolderAbs, IReadOnlyList<ArchivedMarker> ArchivedMarkers, string? Error = null);

/// <summary>
/// Reversible takeover of a folder owned by another manager (Vortex / MO2). Archives every ownership
/// marker out of the folder into <c>&lt;dataDir&gt;/vortex-takeover/&lt;locationKey&gt;/</c> (move, never
/// delete), records the folder in the taken-over set, and writes a manifest so Undo can restore the
/// markers byte-for-byte. Stage-then-commit: a mid-move failure rolls back, leaving the folder owned.
/// Pure System.IO + System.Text.Json. Game-scoped by caller (operates on one folder).
/// </summary>
public static partial class VortexTakeover
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Stable, human-readable archive key for a folder: its path relative to the game root,
    /// slugified. Collision-free because it encodes the full relative path.</summary>
    public static string LocationKey(string gameRoot, string folderAbs)
    {
        var rel = Path.GetRelativePath(gameRoot, folderAbs);
        var slug = rel.Replace(Path.DirectorySeparatorChar, '_').Replace('/', '_').Replace(':', '_');
        return string.IsNullOrWhiteSpace(slug) || slug == "." ? "_root" : slug;
    }

    public static TakeoverResult TakeOver(string dataDir, string gameRoot, string folderAbs)
    {
        var markers = OwnershipMarkers.MarkerFilesIn(folderAbs);
        if (markers.Count == 0)
            return new TakeoverResult(true, folderAbs, Array.Empty<ArchivedMarker>()); // already ours

        var archiveDir = Path.Combine(dataDir, "vortex-takeover", LocationKey(gameRoot, folderAbs));
        Directory.CreateDirectory(archiveDir);

        var moved = new List<(string from, string to)>();
        var archived = new List<ArchivedMarker>();
        try
        {
            foreach (var m in markers)
            {
                var name = Path.GetFileName(m.Path);
                var dest = Path.Combine(archiveDir, name);
                File.Move(m.Path, dest, overwrite: true);  // move-to-holding (reversible)
                moved.Add((m.Path, dest));
                archived.Add(new ArchivedMarker(m.Path, name, m.Owner.ToString().ToLowerInvariant()));
            }
        }
        catch (Exception ex)
        {
            // Roll back any markers already moved, leave the folder owned.
            foreach (var (from, to) in moved)
                try { File.Move(to, from, overwrite: true); } catch { /* best-effort */ }
            return new TakeoverResult(false, folderAbs, Array.Empty<ArchivedMarker>(), ex.Message);
        }

        AtomicJson.WriteJsonAtomic(Path.Combine(archiveDir, "takeover.json"),
            new TakeoverManifest { Version = 1, TakenOverUtc = DateTime.UtcNow, Markers = archived });
        TakenOverStore.Add(dataDir, folderAbs);
        return new TakeoverResult(true, folderAbs, archived);
    }

    public static void Undo(string dataDir, string folderAbs)
    {
        // Recover the archive dir from any game root by scanning vortex-takeover/* for a manifest whose
        // markers point back into folderAbs. Simpler + robust to a gameRoot we don't have here.
        var root = Path.Combine(dataDir, "vortex-takeover");
        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "takeover.json");
                if (!File.Exists(manifestPath)) continue;
                TakeoverManifest? man;
                try { man = System.Text.Json.JsonSerializer.Deserialize<TakeoverManifest>(File.ReadAllText(manifestPath), Json); }
                catch { continue; }
                if (man is null) continue;
                if (!man.Markers.Any(mk => string.Equals(Path.GetDirectoryName(mk.OriginalPath), folderAbs, StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var mk in man.Markers)
                {
                    var archived = Path.Combine(dir, mk.ArchivedName);
                    try
                    {
                        if (File.Exists(archived))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(mk.OriginalPath)!);
                            File.Move(archived, mk.OriginalPath, overwrite: true);
                        }
                    }
                    catch { /* degrade: restore what we can */ }
                }
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
                break;
            }
        }
        TakenOverStore.Remove(dataDir, folderAbs);
    }
}
```

Note: the `partial` keyword anticipates Task 5 adding `TakeOverGame` in the same class.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverTests"`
Expected: PASS (8 tests total in the class now).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VortexTakeover.cs tests/ModManager.Tests/VortexTakeoverTests.cs
git commit -m "feat(vortex): reversible TakeOver + Undo (archive marker move-to-holding)"
```

---

## Task 5: VortexTakeover.TakeOverGame (game-scoped convenience)

**Files:**
- Modify: `src/ModManager.Core/VortexTakeover.cs`
- Test: `tests/ModManager.Tests/VortexTakeoverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void TakeOverGame_takes_over_only_the_passed_locations_not_a_sibling_game()
    {
        var data = DataDir();
        var gameRoot = Path.Combine(_tmp, "GameRoot");

        // Two owned folders for THIS game.
        var locA = Path.Combine(gameRoot, "R5", "A");
        var locB = Path.Combine(gameRoot, "R5", "B");
        foreach (var l in new[] { locA, locB })
        {
            Directory.CreateDirectory(l);
            File.WriteAllText(Path.Combine(l, "vortex.deployment.x.json"), "{}");
        }
        // A sibling game's owned folder that must NOT be touched.
        var sibling = Path.Combine(_tmp, "OtherGame", "mods");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "vortex.deployment.y.json"), "{}");

        var results = VortexTakeover.TakeOverGame(data, gameRoot, new[] { locA, locB });

        Assert.Equal(2, results.Count(r => r.Success));
        Assert.Null(ToolOwnership.Detect(locA));
        Assert.Null(ToolOwnership.Detect(locB));
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(sibling)); // untouched
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~TakeOverGame"`
Expected: FAIL — `TakeOverGame` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to the `VortexTakeover` partial class:

```csharp
    /// <summary>Take over every passed location. Caller passes ONLY the active game's Vortex-owned
    /// locations (from ctx.Locations) — this never discovers folders on its own, so it is intrinsically
    /// game-scoped and can never touch another game's folders.</summary>
    public static IReadOnlyList<TakeoverResult> TakeOverGame(
        string dataDir, string gameRoot, IReadOnlyList<string> ownedLocationAbsPaths)
        => (ownedLocationAbsPaths ?? Array.Empty<string>())
            .Select(folder => TakeOver(dataDir, gameRoot, folder))
            .ToList();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/VortexTakeover.cs tests/ModManager.Tests/VortexTakeoverTests.cs
git commit -m "feat(vortex): TakeOverGame — game-scoped loop over passed locations"
```

---

## Task 6: Ownership resolve with taken-over awareness + re-deploy signal

**Files:**
- Modify: `src/ModManager.Core/ToolOwnership.cs`
- Test: `tests/ModManager.Tests/ToolOwnershipResolveTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class ToolOwnershipResolveTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "own-resolve-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string FolderWithMarker()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "vortex.deployment.x.json"), "{}");
        return _tmp;
    }

    private static HashSet<string> Set(params string[] s) => new(s, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Owned_when_marker_present_and_not_taken_over()
    {
        var f = FolderWithMarker();
        var r = ToolOwnership.Resolve(f, takenOver: Set());
        Assert.Equal(OwnershipState.Owned, r.State);
        Assert.Equal(OwnerTool.Vortex, r.Owner);
    }

    [Fact]
    public void NotOwned_when_taken_over_even_with_marker_present()
    {
        var f = FolderWithMarker();
        var r = ToolOwnership.Resolve(f, takenOver: Set(f));
        Assert.Equal(OwnershipState.NotOwned, r.State);
    }

    [Fact]
    public void ReDeployed_when_taken_over_and_a_fresh_marker_reappeared()
    {
        var f = FolderWithMarker();                       // marker present
        var r = ToolOwnership.Resolve(f, takenOver: Set(f)); // and recorded as taken over
        Assert.Equal(OwnershipState.ReDeployed, r.State);
        // ReDeployed is a SUBTYPE of "we still manage it" — see Coordination in Task 7.
    }

    [Fact]
    public void NotOwned_when_no_marker_and_not_taken_over()
    {
        Directory.CreateDirectory(_tmp);
        var r = ToolOwnership.Resolve(_tmp, takenOver: Set());
        Assert.Equal(OwnershipState.NotOwned, r.State);
    }
}
```

Wait — the `Owned` and `ReDeployed` tests both have a marker; the difference is whether the folder is in the taken-over set. But `NotOwned_when_taken_over` ALSO has a marker + is taken over. Resolve that ambiguity: when taken-over AND marker present, the state is **ReDeployed** (a marker came back). When taken-over AND no marker, **NotOwned**. Fix the second test to remove the marker:

Replace `NotOwned_when_taken_over_even_with_marker_present` body with:

```csharp
    [Fact]
    public void NotOwned_when_taken_over_and_marker_already_archived()
    {
        Directory.CreateDirectory(_tmp);                  // taken over, marker gone
        var r = ToolOwnership.Resolve(_tmp, takenOver: Set(_tmp));
        Assert.Equal(OwnershipState.NotOwned, r.State);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolOwnershipResolveTests"`
Expected: FAIL — `ToolOwnership.Resolve` / `OwnershipState` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `src/ModManager.Core/ToolOwnership.cs`:

```csharp
/// <summary>How the launcher reads a folder's ownership, given the taken-over set.</summary>
public enum OwnershipState
{
    NotOwned,    // ours to manage (no marker, or taken over and marker archived)
    Owned,       // another manager owns it (marker present, not taken over)
    ReDeployed,  // taken over BUT a marker reappeared — the other manager re-deployed
}

/// <summary>The resolved ownership of a folder.</summary>
public sealed record OwnershipResolution(OwnershipState State, OwnerTool? Owner);
```

And add the method inside `ToolOwnership`:

```csharp
    /// <summary>Resolve a folder's ownership against the taken-over set. A taken-over folder reads as
    /// NotOwned even with a marker still present — UNLESS a fresh marker reappeared, which reads as
    /// ReDeployed so the App can surface a "Vortex re-deployed" notice.</summary>
    public static OwnershipResolution Resolve(string folderAbs, IReadOnlySet<string> takenOver)
    {
        var owner = OwnershipMarkers.OwnerOf(folderAbs);
        var isTakenOver = takenOver is not null && takenOver.Contains(folderAbs);

        if (owner is null) return new OwnershipResolution(OwnershipState.NotOwned, null);   // no marker
        if (isTakenOver) return new OwnershipResolution(OwnershipState.ReDeployed, owner);  // marker came back
        return new OwnershipResolution(OwnershipState.Owned, owner);                        // owned as today
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolOwnershipResolveTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ToolOwnership.cs tests/ModManager.Tests/ToolOwnershipResolveTests.cs
git commit -m "feat(vortex): ToolOwnership.Resolve — taken-over-aware ownership + re-deploy signal"
```

---

## Task 7: Coordination.PostureFor gains the re-deployed input

**Files:**
- Modify: `src/ModManager.Core/Coordination.cs`
- Test: `tests/ModManager.Tests/CoordinationTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class CoordinationTests
{
    [Fact]
    public void Owned_marker_not_taken_over_is_Coexist()
        => Assert.Equal(Posture.Coexist,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: false, reDeployed: false));

    [Fact]
    public void Taken_over_loader_folder_is_Conductor_not_Coexist()
        // owner is null because the marker was archived; loader can drive its manifest
        => Assert.Equal(Posture.Conductor,
            Coordination.PostureFor(owner: null, declaredManaged: null, loaderCanConduct: true, reDeployed: false));

    [Fact]
    public void ReDeployed_still_lets_us_manage_Own_or_Conductor()
    {
        // We took it over; a marker came back. We KEEP managing (Own/Conductor), App shows a notice.
        Assert.Equal(Posture.Conductor,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: true, reDeployed: true));
        Assert.Equal(Posture.Own,
            Coordination.PostureFor(OwnerTool.Vortex, declaredManaged: null, loaderCanConduct: false, reDeployed: true));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CoordinationTests"`
Expected: FAIL — `PostureFor` has no `reDeployed` parameter.

- [ ] **Step 3: Write minimal implementation**

Replace `PostureFor` in `src/ModManager.Core/Coordination.cs` with:

```csharp
    public static Posture PostureFor(OwnerTool? owner, string? declaredManaged, bool loaderCanConduct, bool reDeployed = false)
    {
        // Re-deployed = we took the folder over but a marker reappeared. We KEEP managing it (the App
        // raises a "re-deployed" notice); a conducting loader still conducts, otherwise we Own it.
        if (reDeployed) return loaderCanConduct ? Posture.Conductor : Posture.Own;

        if (owner is not null) return Posture.Coexist;
        if (loaderCanConduct) return Posture.Conductor;
        if (!string.IsNullOrEmpty(declaredManaged)) return Posture.Coexist;
        return Posture.Own;
    }
```

The `reDeployed = false` default keeps every existing caller compiling unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CoordinationTests"`
Expected: PASS (3 tests). Then run the full suite to confirm existing `PostureFor` callers still pass:
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Coordination.cs tests/ModManager.Tests/CoordinationTests.cs
git commit -m "feat(vortex): PostureFor handles the re-deployed signal"
```

---

## Task 8: Scanner threads the taken-over set into the posture read

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (`GameContext` ~line 39-76; `BuildModList` ~line 168-225)
- Test: `tests/ModManager.Tests/VortexTakeoverScanTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class VortexTakeoverScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "vtx-scan-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A ue-pak game whose single folders-location is a UE4SS Mods dir holding a Vortex marker + one mod folder.
    private (GameEntry game, string folder) Setup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var mods = Path.Combine(gameRoot, "R5", "Binaries", "Win64", "ue4ss", "Mods");
        var modFolder = Path.Combine(mods, "ShantiesMod");
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, "Scripts", "main.lua"), "x"); // makes it a mod folder
        Directory.CreateDirectory(Path.Combine(modFolder, "Scripts"));
        File.WriteAllText(Path.Combine(modFolder, "Scripts", "main.lua"), "x");
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "ShantiesMod : 1"); // UE4SS manifest -> folders form
        File.WriteAllText(Path.Combine(mods, "vortex.deployment.x.json"), "{}"); // Vortex marker

        var game = new GameEntry
        {
            Id = "windrose", GameName = "Windrose", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "by_folder",
            FileExtensions = new[] { "pak" },
            DataDir = Path.Combine(_tmp, "data"),
            ModLocations = new[] { new ModLocation("mods", "Mods", "R5/Binaries/Win64/ue4ss/Mods") },
        };
        Directory.CreateDirectory(game.DataDir!);
        return (game, mods);
    }

    [Fact]
    public async Task Mod_in_a_vortex_folder_is_readonly_until_taken_over_then_managed()
    {
        var (game, folder) = Setup();
        var ctx = Scanner.GameContext(game);

        var before = (await Scanner.BuildModListAsync(ctx)).First(m => m.Name == "ShantiesMod");
        Assert.True(before.ReadOnly);   // Vortex-owned -> read-only

        // Take it over, rebuild context (re-reads taken-over.json), rescan.
        VortexTakeover.TakeOver(game.DataDir!, ctx.GameRoot, folder);
        var ctx2 = Scanner.GameContext(game);
        var after = (await Scanner.BuildModListAsync(ctx2)).First(m => m.Name == "ShantiesMod");
        Assert.False(after.ReadOnly);   // taken over -> ours to manage
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverScanTests"`
Expected: FAIL — after takeover the mod is still ReadOnly because the scan doesn't consult the taken-over set yet.

- [ ] **Step 3: Implementation — load the set in GameContext, store it on the context, use it in BuildModList**

3a. In `src/ModManager.Core/GameContext.cs`, add a property to `GameContext`:

```csharp
    /// <summary>Folders the user has taken over from another manager (loaded from taken-over.json).
    /// Posture reads this so a taken-over folder is managed despite a lingering marker.</summary>
    public IReadOnlySet<string> TakenOver { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

3b. In `src/ModManager.Core/Scanner.cs` `GameContext(...)` (the `return new GameContext { ... }` block ~line 57-75), add:

```csharp
            TakenOver = TakenOverStore.Load(dataDir),
```

3c. In `BuildModList` (~line 170-173), where the posture is computed per location, replace the owner+posture lines. Current code:

```csharp
            var owner = ToolOwnership.Detect(loc.Abs);
            var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var isBepInEx = loc.Form != "folders" && string.Equals(c.Game.Engine, "bepinex", StringComparison.OrdinalIgnoreCase);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: isUe4ss || isBepInEx);
```

Replace with:

```csharp
            var ownership = ToolOwnership.Resolve(loc.Abs, c.TakenOver);
            var owner = ownership.State == OwnershipState.Owned ? ownership.Owner : null;
            var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var isBepInEx = loc.Form != "folders" && string.Equals(c.Game.Engine, "bepinex", StringComparison.OrdinalIgnoreCase);
            var posture = Coordination.PostureFor(
                owner, loc.Managed,
                loaderCanConduct: isUe4ss || isBepInEx,
                reDeployed: ownership.State == OwnershipState.ReDeployed);
```

The `managedLabel` line just below uses `owner?.ToString()` — leave it; for a taken-over folder `owner` is now null so it reads unmanaged, which is correct.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~VortexTakeoverScanTests"`
Expected: PASS. Then full suite:
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all green — confirms no existing scan test regressed).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs src/ModManager.Core/GameContext.cs tests/ModManager.Tests/VortexTakeoverScanTests.cs
git commit -m "feat(vortex): scan consults taken-over set for posture + re-deploy"
```

---

## Task 9: MainViewModel — takeover actions + owned/re-deploy state

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`
- (No new Core; this wires the App to the Core primitive. App view-model logic is not unit-tested in this codebase — verified via the App build + the smoke test in Task 11.)

- [ ] **Step 1: Add takeover state + actions to MainViewModel**

Near the other observable collections / computed properties, add:

```csharp
    /// <summary>Active-game locations that are Vortex/MO2-owned and NOT yet taken over — drives the
    /// "Some folders are managed by Vortex" banner. Recomputed each ReloadModsAsync.</summary>
    public ObservableCollection<string> OwnedLocations { get; } = new();

    /// <summary>Active-game locations we took over but where a marker has REAPPEARED (Vortex re-deployed).</summary>
    public ObservableCollection<string> ReDeployedLocations { get; } = new();

    public bool HasOwnedLocations => OwnedLocations.Count > 0;
    public bool HasReDeployedLocations => ReDeployedLocations.Count > 0;
```

- [ ] **Step 2: Populate them in ReloadModsAsync**

In `ReloadModsAsync`, in the populate path (after `_ctx` is set and before the final notifies), add:

```csharp
            OwnedLocations.Clear();
            ReDeployedLocations.Clear();
            foreach (var loc in _ctx.Locations)
            {
                var res = ToolOwnership.Resolve(loc.Abs, _ctx.TakenOver);
                if (res.State == OwnershipState.Owned) OwnedLocations.Add(loc.Abs);
                else if (res.State == OwnershipState.ReDeployed) ReDeployedLocations.Add(loc.Abs);
            }
            OnPropertyChanged(nameof(HasOwnedLocations));
            OnPropertyChanged(nameof(HasReDeployedLocations));
```

And clear both in the empty-game path (where the other collections are cleared), with the same two `OnPropertyChanged`.

- [ ] **Step 3: Add the takeover methods**

```csharp
    /// <summary>Take over one Vortex-owned folder, then rescan so its rows flip to managed.</summary>
    public async Task TakeOverFolderAsync(string folderAbs)
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = VortexTakeover.TakeOver(_ctx.DataDir, _ctx.GameRoot, folderAbs);
            StatusText = r.Success
                ? $"Took over {System.IO.Path.GetFileName(folderAbs.TrimEnd('\\', '/'))} — you manage it here now."
                : $"Couldn't take over the folder: {r.Error}";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Take over every Vortex-owned (or re-deployed) location for the ACTIVE game.</summary>
    public async Task TakeOverGameAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var targets = OwnedLocations.Concat(ReDeployedLocations).Distinct().ToList();
            var results = VortexTakeover.TakeOverGame(_ctx.DataDir, _ctx.GameRoot, targets);
            var ok = results.Count(x => x.Success);
            StatusText = $"Took over {ok} folder{(ok == 1 ? "" : "s")} for {_ctx.Game.GameName} — you manage them here now.";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }
```

- [ ] **Step 4: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(vortex): MainViewModel takeover actions + owned/re-deployed state"
```

---

## Task 10: App — on-block dialog + banner

**Files:**
- Create: `src/ModManager.App/Vortex/VortexTakeoverDialog.xaml` + `.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` (banner) + `MainWindow.xaml.cs` (wire buttons + on-block prompt)

- [ ] **Step 1: Create the on-block dialog (model on `Frameworks/FrameworkUnrecognizedNudgeDialog`)**

`src/ModManager.App/Vortex/VortexTakeoverDialog.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.Vortex.VortexTakeoverDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Vortex manages this folder"
    PrimaryButtonText="Take over folder"
    CloseButtonText="Not now"
    DefaultButton="Primary">
    <StackPanel Spacing="8">
        <TextBlock x:Name="BodyText" TextWrapping="Wrap" />
        <TextBlock TextWrapping="Wrap" Opacity="0.7"
                   Text="After this, manage these mods in the launcher — re-deploying in Vortex may undo your changes." />
    </StackPanel>
</ContentDialog>
```

`src/ModManager.App/Vortex/VortexTakeoverDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Vortex;

public sealed partial class VortexTakeoverDialog : ContentDialog
{
    public VortexTakeoverDialog(string modName)
    {
        InitializeComponent();
        BodyText.Text = $"\"{modName}\" lives in a folder Vortex used to deploy. Take it over so you can manage it here?";
    }
}
```

- [ ] **Step 2: Add the banner to MainWindow.xaml**

Above the mod list (find the mod-list container; place the banner directly before it). Insert:

```xml
<Border x:Name="VortexBanner"
        Visibility="{x:Bind ViewModel.HasOwnedLocations, Mode=OneWay}"
        Background="{StaticResource ThemeBarBg}" BorderBrush="{StaticResource ThemeBorder}"
        BorderThickness="0,0,0,1" Padding="12,6">
    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <TextBlock Text="Some folders here are managed by Vortex." VerticalAlignment="Center" />
        <Button Content="Take them over" Click="OnTakeOverGame" />
        <Button Content="Dismiss" Click="OnDismissVortexBanner" />
    </StackPanel>
</Border>

<Border x:Name="VortexReDeployBanner"
        Visibility="{x:Bind ViewModel.HasReDeployedLocations, Mode=OneWay}"
        Background="{StaticResource ThemeBarBg}" BorderBrush="{StaticResource ThemeBorder}"
        BorderThickness="0,0,0,1" Padding="12,6">
    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <TextBlock Text="Vortex re-deployed into a folder you took over." VerticalAlignment="Center" />
        <Button Content="Take over again" Click="OnTakeOverGame" />
    </StackPanel>
</Border>
```

(If `HasOwnedLocations` is `bool`, the existing `BoolToVisibility` converter the project uses must wrap it. Check how other `Visibility="{x:Bind ViewModel.HasX}"` bindings in `MainWindow.xaml` do it and match exactly — the project already has computed `Visibility` properties elsewhere; if so, add `OwnedBannerVisibility`/`ReDeployBannerVisibility` `Visibility` getters on the VM instead and bind those.)

- [ ] **Step 3: Wire the banner buttons + a session dismiss flag in MainWindow.xaml.cs**

```csharp
    private bool _suppressVortexBanner;

    private async void OnTakeOverGame(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.TakeOverGameAsync();
    }

    private void OnDismissVortexBanner(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _suppressVortexBanner = true;
        VortexBanner.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }
```

- [ ] **Step 4: Add the on-block prompt where the owned-folder toggle/uninstall is refused**

In `MainWindow.xaml.cs`, the owned-folder warning lives near line 144 (`row.Mod.ReadOnly && row.Mod.Loader is "ue4ss" or "bepinex"`) and the uninstall path. At the point where an action is blocked because the folder is owned, offer takeover first:

```csharp
    // Returns true if the folder is now ours (taken over or already ours), false if the user declined.
    private async Task<bool> EnsureNotVortexOwnedAsync(ViewModels.ModRowViewModel row)
    {
        if (ViewModel?.ActiveContextPublic is not { } ctx) return true;
        var folder = row.ModFolderAbs;
        if (string.IsNullOrEmpty(folder)) return true;
        var res = ModManager.Core.ToolOwnership.Resolve(
            System.IO.Path.GetDirectoryName(folder) ?? folder, ctx.TakenOver);
        if (res.State != ModManager.Core.OwnershipState.Owned) return true;

        var dlg = new Vortex.VortexTakeoverDialog(row.DisplayName) { XamlRoot = this.Content.XamlRoot };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return false;
        await ViewModel.TakeOverFolderAsync(System.IO.Path.GetDirectoryName(folder) ?? folder);
        return true;
    }
```

Call `if (!await EnsureNotVortexOwnedAsync(row)) return;` at the top of the uninstall handler (`OnUninstall`) and the owned-folder toggle path, BEFORE the existing owned-folder warning. (Add `ActiveContextPublic` as a public passthrough on the VM if not present: `public GameContext? ActiveContextPublic => _ctx;`.)

- [ ] **Step 5: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. Fix any binding/converter mismatch per the note in Step 2.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/Vortex/ src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(vortex): on-block takeover dialog + Vortex banner"
```

---

## Task 11: Full verification + reviewers + smoke

**Files:** none (verification only)

- [ ] **Step 1: Full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS (all, including the new Vortex tests).

- [ ] **Step 2: CorePurity guard**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"`
Expected: PASS — no WinUI/WinRT leaked into Core.

- [ ] **Step 3: App build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: reversibility-auditor**

Dispatch the `reversibility-auditor` agent on `VortexTakeover.cs` (the new file-op site): confirm marker move-to-holding is reversible, mid-move rollback leaves the folder owned, Undo restores byte-for-byte, no `File.Delete` on a marker. Implement any Important/Critical findings before merge.

- [ ] **Step 5: Live smoke on Windrose**

Launch the dev build. With Windrose active (its `ue4ss\Mods` still holds `vortex.deployment.windrose-scripts.json`):
1. The Vortex banner appears.
2. Try to uninstall the shanties row → on-block dialog appears → "Take over folder".
3. The marker is archived to `<dataDir>/vortex-takeover/...`, the row becomes managed (uninstall now available), and `taken-over.json` lists the folder.
4. Verify `Undo` path (via a temporary test hook or by manually restoring) is not needed for the smoke, but confirm the archived marker exists on disk.

- [ ] **Step 6: Log the decision + open the PR**

Log to the 626 dashboard (`manage_decisions`, project `DP1YCsh7iAN1yAiR8sAd`): the takeover ownership-override model, the reversible archive choice, game-scoped non-goal. Open the PR `feat/vortex-takeover → master`.

---

## Self-review notes (filled by author)

- **Spec coverage:** marker archive (T4), undo (T4), taken-over set (T3), posture override (T6-8), re-deploy detection (T6-8), banner + on-block (T10), game-scoped TakeOverGame (T5), camelCase round-trip (T3), reversibility (T4 + T11 auditor). All spec sections map to a task.
- **Type consistency:** `OwnershipState` / `OwnershipResolution` / `ToolOwnership.Resolve` consistent across T6-T10. `PostureFor(..., reDeployed)` consistent T7-T8. `TakeOver(dataDir, gameRoot, folderAbs)` signature consistent T4/T5/T9. `GameContext.TakenOver` consistent T8/T9/T10.
- **Open risk flagged for executor:** the banner `Visibility` binding (T10 Step 2) depends on how the project converts `bool→Visibility`; the note instructs matching the existing pattern or adding `Visibility` getters on the VM. Resolve at implementation time against the real `MainWindow.xaml`.
