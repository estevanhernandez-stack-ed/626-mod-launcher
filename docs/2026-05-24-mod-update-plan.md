# Mod Update via Collision-Aware Intake — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user update an installed mod by dropping a new version — every colliding file is surfaced in one prompt with per-file replace + replace-all, and replaced files move to a reversible backup instead of being silently skipped.

**Architecture:** Split intake into a pure **plan** phase (classify a drop into add / collision / unsafe, no writes), an App **dialog** (choose which collisions to replace), and a pure **execute** phase (install new, back up + replace chosen collisions, skip the rest). Shared `IntakePlan` / `IntakeCollision` types serve both the standard mod-folder path (`Scanner`) and the direct-inject path (`DirectInject`).

**Tech Stack:** .NET 10, C#, xUnit, System.IO / System.IO.Compression. WinUI 3 (App layer).

Spec: [docs/2026-05-24-mod-update-design.md](2026-05-24-mod-update-design.md)

---

## File Structure

- Create: `src/ModManager.Core/IntakePlan.cs` — `IntakePlan`, `IntakeCollision` records.
- Create: `src/ModManager.Core/ReplacedStore.cs` — reversible backup of a replaced file (cross-volume safe) + metadata.
- Modify: `src/ModManager.Core/GameContext.cs` — add `IntakeResult.Updated`.
- Modify: `src/ModManager.Core/Scanner.cs` — `PlanIntake`, `ExecuteIntake` (standard mod-folder path).
- Modify: `src/ModManager.Core/DirectInject.cs` — `Plan`, `Execute` (direct-inject path).
- Create: `src/ModManager.App/UpdateModsDialog.xaml` + `.xaml.cs` — the collision dialog.
- Modify: `src/ModManager.App/Services/DirectInjectService.cs` — expose `Plan` + `Execute`.
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — `AddModsAsync` runs plan → dialog → execute.
- Create: `tests/ModManager.Tests/IntakeUpdateTests.cs` — Core tests for both paths.

> **Run tests with the explicit project** (a bare `dotnet test` hangs building WinUI):
> `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App builds need `-p:Platform=x64`.
> On a copy-lock, kill `testhost.exe` / `ModManager.App.exe` and retry.

---

## Task 1: Shared plan types + IntakeResult.Updated

**Files:**
- Create: `src/ModManager.Core/IntakePlan.cs`
- Modify: `src/ModManager.Core/GameContext.cs:37-41`
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class IntakeUpdateTests
{
    [Fact]
    public void Plan_types_and_updated_result_exist()
    {
        var col = new IntakeCollision("ersc.dll", "ersc.dll", @"C:\game\ersc.dll", @"C:\drop\ersc.dll");
        var plan = new IntakePlan(new[] { new IntakeItem("new.dll", "new.dll", @"C:\drop\new.dll") }, new[] { col }, Array.Empty<SkippedItem>());
        Assert.Equal("new.dll", plan.ToAdd[0].Name);
        Assert.Equal("ersc.dll", plan.Collisions[0].Name);

        var result = new IntakeResult();
        result.Updated.Add("ersc.dll");
        Assert.Single(result.Updated);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.Plan_types"`
Expected: FAIL — `IntakeCollision` / `IntakePlan` undefined; `IntakeResult.Updated` undefined.

- [ ] **Step 3: Write minimal implementation**

Create `src/ModManager.Core/IntakePlan.cs`:

```csharp
namespace ModManager.Core;

/// <summary>A new (non-colliding) file in a drop. IncomingSource is a loose path or "zipPath!entryName".
/// RelPath is the destination key (flat name for mod-folder games; nested for direct-inject).</summary>
public sealed record IntakeItem(string Name, string RelPath, string IncomingSource);

/// <summary>A file in a drop whose destination already exists — needs a replace/skip decision.
/// RelPath is the identity key; IncomingSource is a loose path or "zipPath!entryName".</summary>
public sealed record IntakeCollision(string Name, string RelPath, string ExistingPath, string IncomingSource);

/// <summary>The result of planning a drop without touching disk: what is new, what collides, what is refused.</summary>
public sealed record IntakePlan(
    IReadOnlyList<IntakeItem> ToAdd,
    IReadOnlyList<IntakeCollision> Collisions,
    IReadOnlyList<SkippedItem> Unsafe);
```

In `src/ModManager.Core/GameContext.cs`, add `Updated` to `IntakeResult`:

```csharp
public sealed class IntakeResult
{
    public List<string> Added { get; } = new();
    public List<string> Updated { get; } = new();
    public List<SkippedItem> Skipped { get; } = new();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.Plan_types"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/IntakePlan.cs src/ModManager.Core/GameContext.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): IntakePlan/IntakeCollision types + IntakeResult.Updated"
```

---

## Task 2: ReplacedStore — reversible backup of a replaced file

**Files:**
- Create: `src/ModManager.Core/ReplacedStore.cs`
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void ReplacedStore_moves_old_file_into_timestamped_backup_recoverably()
{
    var root = Path.Combine(Path.GetTempPath(), "mmb-rs-" + Guid.NewGuid().ToString("N"));
    var live = Path.Combine(root, "game");
    var backup = Path.Combine(root, "data", "replaced");
    Directory.CreateDirectory(live);
    var existing = Path.Combine(live, "ersc.dll");
    File.WriteAllText(existing, "OLD");

    var dir = ReplacedStore.NewBatch(backup);              // one folder per drop
    var saved = ReplacedStore.Backup(existing, "ersc.dll", dir);

    Assert.False(File.Exists(existing));                   // moved out of the live folder
    Assert.True(File.Exists(saved));                       // recoverable copy exists
    Assert.Equal("OLD", File.ReadAllText(saved));          // bytes preserved
    try { Directory.Delete(root, true); } catch { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.ReplacedStore"`
Expected: FAIL — `ReplacedStore` undefined.

- [ ] **Step 3: Write minimal implementation**

Create `src/ModManager.Core/ReplacedStore.cs`:

```csharp
using System.Globalization;

namespace ModManager.Core;

/// <summary>
/// Holds the prior version of a file replaced during a mod update, so a replace is always
/// reversible (the law: never overwrite-destroy). One timestamped batch folder per drop.
/// </summary>
public static class ReplacedStore
{
    /// <summary>A fresh timestamped backup folder under <paramref name="replacedRoot"/> for one update batch.</summary>
    public static string NewBatch(string replacedRoot)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var dir = Path.Combine(replacedRoot, stamp);
        var n = 1;
        while (Directory.Exists(dir)) dir = Path.Combine(replacedRoot, $"{stamp}-{n++}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Move the live file into the batch folder under its relative path; returns the backup path.
    /// Cross-volume safe (game and data dir may be on different drives).</summary>
    public static string Backup(string existingAbs, string relPath, string batchDir)
    {
        var dest = Path.Combine(batchDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try { File.Move(existingAbs, dest); }
        catch (IOException) { File.Copy(existingAbs, dest, overwrite: false); File.Delete(existingAbs); }
        return dest;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.ReplacedStore"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/ReplacedStore.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): ReplacedStore — reversible timestamped backup of replaced files"
```

---

## Task 3: Scanner.PlanIntake (standard mod-folder path)

`PlanIntake` reuses the existing `ExpandPaths` + `Intake.ClassifyDrop` logic but classifies each resolved destination instead of copying. Standard intake places files flat into the primary mod location, so `RelPath` == file name.

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (add after `AddMods`, near line 619)
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
private static GameContext FlatCtx(string root, params string[] exts)
{
    var mods = Path.Combine(root, "mods");
    Directory.CreateDirectory(mods);
    return new GameContext
    {
        GameRoot = root, DataDir = Path.Combine(root, "_data"), DisabledRoot = Path.Combine(root, "_data", "disabled"),
        ProfilesDir = Path.Combine(root, "_data", "profiles"), SavesDir = Path.Combine(root, "_data", "saves"),
        ClassificationPath = Path.Combine(root, "_data", "classification.json"),
        Exts = exts, FileRe = new System.Text.RegularExpressions.Regex(".*"),
        Locations = new[] { new ModLocationCtx { Abs = mods, Mirrors = Array.Empty<string>() } },
        GroupingRule = "",
    };
}

[Fact]
public void Scanner_PlanIntake_splits_new_from_colliding()
{
    var root = Path.Combine(Path.GetTempPath(), "mmb-sp-" + Guid.NewGuid().ToString("N"));
    var ctx = FlatCtx(root, "dll");
    File.WriteAllText(Path.Combine(ctx.Locations[0].Abs, "old.dll"), "INSTALLED"); // already installed
    var drop = Path.Combine(root, "drop");
    Directory.CreateDirectory(drop);
    File.WriteAllText(Path.Combine(drop, "old.dll"), "NEWVER");   // collides
    File.WriteAllText(Path.Combine(drop, "fresh.dll"), "NEW");    // new

    var plan = Scanner.PlanIntake(new[] { Path.Combine(drop, "old.dll"), Path.Combine(drop, "fresh.dll") }, ctx);

    Assert.Contains(plan.ToAdd, a => a.RelPath == "fresh.dll");
    Assert.Contains(plan.Collisions, c => c.Name == "old.dll");
    try { Directory.Delete(root, true); } catch { }
}
```

> **Note:** if `GameContext` / `ModLocationCtx` property names differ from those used here, adjust the helper to the real shape (check `GameContext.cs` and `ModLocations.cs`). The helper is the only place that constructs a context directly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.Scanner_PlanIntake"`
Expected: FAIL — `Scanner.PlanIntake` undefined.

- [ ] **Step 3: Write minimal implementation**

In `Scanner.cs`, add a destination resolver and `PlanIntake`. Mirror how `PlaceFile` picks the primary location:

```csharp
/// <summary>The absolute destination a placed file would take (primary mod location), and its rel key.</summary>
private static (string Abs, string Rel) DestFor(string fileName, GameContext c)
{
    var primary = c.Locations.FirstOrDefault() ?? throw new InvalidOperationException("No mod location configured for this game.");
    return (Path.Combine(primary.Abs, fileName), fileName);
}

/// <summary>Classify a drop into add / collision / unsafe without writing anything.</summary>
public static IntakePlan PlanIntake(IEnumerable<string> paths, GameContext c)
{
    var add = new List<IntakeItem>();
    var collisions = new List<IntakeCollision>();
    var unsafeItems = new List<SkippedItem>();

    foreach (var p in ExpandPaths(paths, c))
    {
        var kind = Intake.ClassifyDrop(p, c.Exts);
        if (kind == "skip") { unsafeItems.Add(new SkippedItem(Path.GetFileName(p), "not a mod file")); continue; }
        if (kind == "mod")
        {
            var name = Path.GetFileName(p);
            var (abs, rel) = DestFor(name, c);
            if (File.Exists(abs)) collisions.Add(new IntakeCollision(name, rel, abs, p));
            else add.Add(new IntakeItem(name, rel, p));
        }
        else if (kind == "zip")
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(p);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (Intake.ClassifyDrop(entry.FullName, c.Exts) != "mod") continue;
                var name = Path.GetFileName(entry.FullName); // basename neutralizes zip-slip
                var (abs, rel) = DestFor(name, c);
                var incoming = $"{p}!{entry.FullName}";
                if (File.Exists(abs)) collisions.Add(new IntakeCollision(name, rel, abs, incoming));
                else if (!add.Any(a => a.RelPath == rel)) add.Add(new IntakeItem(name, rel, incoming));
            }
        }
    }
    return new IntakePlan(add, collisions, unsafeItems);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.Scanner_PlanIntake"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): Scanner.PlanIntake — classify a drop (add/collision/unsafe), no writes"
```

---

## Task 4: Scanner.ExecuteIntake (install / replace-with-backup / skip)

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs`
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Scanner_ExecuteIntake_replaces_chosen_backs_up_old_skips_unchosen()
{
    var root = Path.Combine(Path.GetTempPath(), "mmb-se-" + Guid.NewGuid().ToString("N"));
    var ctx = FlatCtx(root, "dll");
    var mods = ctx.Locations[0].Abs;
    File.WriteAllText(Path.Combine(mods, "old.dll"), "OLD");
    File.WriteAllText(Path.Combine(mods, "keep.dll"), "KEEP");
    var drop = Path.Combine(root, "drop"); Directory.CreateDirectory(drop);
    File.WriteAllText(Path.Combine(drop, "old.dll"), "NEW");    // collision, will replace
    File.WriteAllText(Path.Combine(drop, "keep.dll"), "NEW2");  // collision, will NOT replace
    File.WriteAllText(Path.Combine(drop, "fresh.dll"), "ADD");  // new

    var paths = new[] { "old.dll", "keep.dll", "fresh.dll" }.Select(f => Path.Combine(drop, f));
    var plan = Scanner.PlanIntake(paths, ctx);
    var result = Scanner.ExecuteIntake(plan, new HashSet<string> { "old.dll" }, ctx);

    Assert.Equal("NEW", File.ReadAllText(Path.Combine(mods, "old.dll"))); // replaced
    Assert.Equal("KEEP", File.ReadAllText(Path.Combine(mods, "keep.dll"))); // untouched (not chosen)
    Assert.Equal("ADD", File.ReadAllText(Path.Combine(mods, "fresh.dll"))); // added
    Assert.Contains("old.dll", result.Updated);
    Assert.Contains("fresh.dll", result.Added);
    Assert.Contains(result.Skipped, s => s.Name == "keep.dll");
    // old version recoverable somewhere under the replaced store
    var backups = Directory.GetFiles(Path.Combine(ctx.DataDir, "replaced"), "old.dll", SearchOption.AllDirectories);
    Assert.True(backups.Length == 1 && File.ReadAllText(backups[0]) == "OLD");
    try { Directory.Delete(root, true); } catch { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.Scanner_ExecuteIntake"`
Expected: FAIL — `Scanner.ExecuteIntake` undefined.

- [ ] **Step 3: Write minimal implementation**

Add to `Scanner.cs` a helper that copies a planned source (a loose file path, or the `"zipPath!entryName"` marker), and the executor. `IntakeItem` (Task 1) already carries the source for adds.

```csharp
/// <summary>Copy a planned source — a loose file path, or "zipPath!entryName" — to dest (overwrite allowed).</summary>
private static void CopyPlanned(string incoming, string destAbs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
    var bang = incoming.IndexOf('!');
    if (bang < 0) { File.Copy(incoming, destAbs, overwrite: true); return; }
    using var zip = System.IO.Compression.ZipFile.OpenRead(incoming[..bang]);
    var entry = zip.GetEntry(incoming[(bang + 1)..]) ?? throw new FileNotFoundException($"Zip entry gone: {incoming}");
    entry.ExtractToFile(destAbs, overwrite: true);
}

/// <summary>Execute a plan: install new files, back-up-then-replace chosen collisions, skip the rest.</summary>
public static IntakeResult ExecuteIntake(IntakePlan plan, ISet<string> replaceRelPaths, GameContext c)
{
    var result = new IntakeResult();
    foreach (var u in plan.Unsafe) result.Skipped.Add(u);

    var primary = c.Locations.FirstOrDefault() ?? throw new InvalidOperationException("No mod location configured for this game.");
    Directory.CreateDirectory(primary.Abs);
    string? batch = null;
    string Batch() => batch ??= ReplacedStore.NewBatch(Path.Combine(c.DataDir, "replaced"));

    foreach (var item in plan.ToAdd)
    {
        try { CopyPlanned(item.IncomingSource, Path.Combine(primary.Abs, item.RelPath)); result.Added.Add(item.RelPath); }
        catch (Exception e) { result.Skipped.Add(new SkippedItem(item.Name, e.Message)); }
    }
    foreach (var col in plan.Collisions)
    {
        if (!replaceRelPaths.Contains(col.RelPath)) { result.Skipped.Add(new SkippedItem(col.Name, "kept existing")); continue; }
        try
        {
            ReplacedStore.Backup(col.ExistingPath, col.RelPath, Batch()); // moves old out, reversibly
            CopyPlanned(col.IncomingSource, col.ExistingPath);
            result.Updated.Add(col.RelPath);
        }
        catch (Exception e) { result.Skipped.Add(new SkippedItem(col.Name, e.Message)); }
    }
    return result;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests"`
Expected: PASS (all intake-update tests, including the updated Task 1/3 assertions).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): Scanner.ExecuteIntake — install/replace-with-backup/skip"
```

---

## Task 5: DirectInject.Plan (direct-inject path)

Mirrors `DirectInject.Install` (wrapper-prefix flatten + `SafeRelative` guard + `Exists` check) but classifies instead of copying. `RelPath` can be nested (e.g. `SeamlessCoop/ersc_settings.ini`).

**Files:**
- Modify: `src/ModManager.Core/DirectInject.cs`
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void DirectInject_Plan_splits_new_from_colliding_in_play_folder()
{
    var root = Path.Combine(Path.GetTempPath(), "mmb-dip-" + Guid.NewGuid().ToString("N"));
    var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
    File.WriteAllText(Path.Combine(play, "ersc.dll"), "OLD"); // installed

    var zipPath = Path.Combine(root, "seamless.zip");
    using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
    {
        z.CreateEntry("ersc.dll").Open().Dispose();                 // collides
        z.CreateEntry("launch_elden_ring_seamlesscoop.exe").Open().Dispose(); // new
    }

    var plan = DirectInject.Plan(play, new[] { zipPath });
    Assert.Contains(plan.Collisions, c => c.RelPath.Equals("ersc.dll", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(plan.ToAdd, a => a.RelPath.EndsWith("seamlesscoop.exe", StringComparison.OrdinalIgnoreCase));
    try { Directory.Delete(root, true); } catch { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.DirectInject_Plan"`
Expected: FAIL — `DirectInject.Plan` undefined.

- [ ] **Step 3: Write minimal implementation**

Add to `DirectInject.cs`, reusing `WrapperPrefix` / `SafeRelative` / `IsUnder` / `Exists`:

```csharp
/// <summary>Classify a drop against the play folder into add / collision / unsafe — no writes.</summary>
public static IntakePlan Plan(string playFolder, IEnumerable<string> sourcePaths)
{
    var add = new List<IntakeItem>();
    var collisions = new List<IntakeCollision>();
    var unsafeItems = new List<SkippedItem>();

    void Consider(string rel, string existingAbsDir, string incoming)
    {
        var dest = Path.Combine(existingAbsDir, rel);
        if (!IsUnder(playFolder, dest)) { unsafeItems.Add(new SkippedItem(rel, "unsafe path")); return; }
        var name = Path.GetFileName(rel);
        if (Exists(dest)) collisions.Add(new IntakeCollision(name, rel, dest, incoming));
        else add.Add(new IntakeItem(name, rel, incoming));
    }

    foreach (var src in sourcePaths ?? Enumerable.Empty<string>())
    {
        try
        {
            if (Directory.Exists(src))
            {
                var baseName = new DirectoryInfo(src).Name;
                foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    Consider(Path.Combine(baseName, Path.GetRelativePath(src, file)), playFolder, file);
            }
            else if (src.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(src);
                var prefix = WrapperPrefix(zip.Entries.Select(e => e.FullName));
                foreach (var entry in zip.Entries)
                {
                    var rel = SafeRelative(entry.FullName, prefix);
                    if (rel is null) { if (!entry.FullName.EndsWith("/")) unsafeItems.Add(new SkippedItem(entry.FullName, "unsafe path")); continue; }
                    Consider(rel, playFolder, $"{src}!{entry.FullName}");
                }
            }
            else Consider(Path.GetFileName(src), playFolder, src);
        }
        catch (Exception e) { unsafeItems.Add(new SkippedItem(Path.GetFileName(src), e.Message)); }
    }
    return new IntakePlan(add, collisions, unsafeItems);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.DirectInject_Plan"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/DirectInject.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): DirectInject.Plan — classify a play-folder drop, no writes"
```

---

## Task 6: DirectInject.Execute (install / replace-with-backup / skip)

**Files:**
- Modify: `src/ModManager.Core/DirectInject.cs`
- Test: `tests/ModManager.Tests/IntakeUpdateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void DirectInject_Execute_updates_whole_set_when_all_chosen_and_backs_up()
{
    var root = Path.Combine(Path.GetTempPath(), "mmb-die-" + Guid.NewGuid().ToString("N"));
    var play = Path.Combine(root, "game"); Directory.CreateDirectory(play);
    var backup = Path.Combine(root, "data", "replaced");
    File.WriteAllText(Path.Combine(play, "ersc.dll"), "OLD-DLL");
    File.WriteAllText(Path.Combine(play, "ersc_settings.ini"), "OLD-INI");

    var zipPath = Path.Combine(root, "seamless.zip");
    using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
    {
        using (var w = new StreamWriter(z.CreateEntry("ersc.dll").Open())) w.Write("NEW-DLL");
        using (var w = new StreamWriter(z.CreateEntry("ersc_settings.ini").Open())) w.Write("NEW-INI");
    }

    var plan = DirectInject.Plan(play, new[] { zipPath });
    var replaceAll = plan.Collisions.Select(c => c.RelPath).ToHashSet();
    var result = DirectInject.Execute(play, backup, plan, replaceAll);

    Assert.Equal("NEW-DLL", File.ReadAllText(Path.Combine(play, "ersc.dll")));
    Assert.Equal("NEW-INI", File.ReadAllText(Path.Combine(play, "ersc_settings.ini")));
    Assert.Equal(2, result.Updated.Count); // both updated together — no desync
    Assert.True(Directory.GetFiles(backup, "*", SearchOption.AllDirectories).Length >= 2); // old versions kept
    try { Directory.Delete(root, true); } catch { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.DirectInject_Execute"`
Expected: FAIL — `DirectInject.Execute` undefined.

- [ ] **Step 3: Write minimal implementation**

Add to `DirectInject.cs`. Reuse the `incoming` marker convention (`path` or `zip!entry`):

```csharp
private static void CopyIncoming(string incoming, string destAbs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
    var bang = incoming.IndexOf('!');
    if (bang < 0) { File.Copy(incoming, destAbs, overwrite: true); return; }
    using var zip = ZipFile.OpenRead(incoming[..bang]);
    var entry = zip.GetEntry(incoming[(bang + 1)..]) ?? throw new FileNotFoundException($"Zip entry gone: {incoming}");
    entry.ExtractToFile(destAbs, overwrite: true);
}

/// <summary>Execute a play-folder plan: install new files, back-up-then-replace chosen collisions, skip the rest.</summary>
public static IntakeResult Execute(string playFolder, string replacedRoot, IntakePlan plan, ISet<string> replaceRelPaths)
{
    var result = new IntakeResult();
    foreach (var u in plan.Unsafe) result.Skipped.Add(u);
    Directory.CreateDirectory(playFolder);
    string? batch = null;
    string Batch() => batch ??= ReplacedStore.NewBatch(replacedRoot);

    foreach (var item in plan.ToAdd)
    {
        try { CopyIncoming(item.IncomingSource, Path.Combine(playFolder, item.RelPath)); result.Added.Add(item.RelPath); }
        catch (Exception e) { result.Skipped.Add(new SkippedItem(item.Name, e.Message)); }
    }
    foreach (var col in plan.Collisions)
    {
        if (!replaceRelPaths.Contains(col.RelPath)) { result.Skipped.Add(new SkippedItem(col.Name, "kept existing")); continue; }
        try
        {
            ReplacedStore.Backup(col.ExistingPath, col.RelPath, Batch());
            CopyIncoming(col.IncomingSource, col.ExistingPath);
            result.Updated.Add(col.RelPath);
        }
        catch (Exception e) { result.Skipped.Add(new SkippedItem(col.Name, e.Message)); }
    }
    return result;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IntakeUpdateTests.DirectInject_Execute"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/DirectInject.cs tests/ModManager.Tests/IntakeUpdateTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): DirectInject.Execute — install/replace-with-backup/skip"
```

---

## Task 7: UpdateModsDialog (App)

A modal dialog listing collisions with per-file Replace checkboxes + a Replace-all toggle, and the new files as informational rows. Returns the chosen rel-paths.

**Files:**
- Create: `src/ModManager.App/UpdateModsDialog.xaml`
- Create: `src/ModManager.App/UpdateModsDialog.xaml.cs`

- [ ] **Step 1: Create the XAML** (`UpdateModsDialog.xaml`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.UpdateModsDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Update installed mods?"
    PrimaryButtonText="Apply" CloseButtonText="Cancel" DefaultButton="Primary">
    <StackPanel Spacing="8" MinWidth="420">
        <TextBlock TextWrapping="Wrap" Opacity="0.8"
                   Text="These files are already installed. Check the ones to replace — the old version is kept and can be reverted." />
        <CheckBox x:Name="ReplaceAllBox" Content="Replace all" IsChecked="True" Checked="OnReplaceAll" Unchecked="OnReplaceAll" />
        <ItemsControl x:Name="CollisionList">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <CheckBox Content="{Binding Name}" IsChecked="{Binding Replace, Mode=TwoWay}" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock x:Name="AddsHeader" Opacity="0.6" FontSize="12" />
        <ItemsControl x:Name="AddsList">
            <ItemsControl.ItemTemplate>
                <DataTemplate><TextBlock Text="{Binding}" Opacity="0.6" FontSize="12" /></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Create the code-behind** (`UpdateModsDialog.xaml.cs`)

```csharp
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

public sealed partial class UpdateModsDialog : ContentDialog
{
    public sealed class Row { public string Name { get; set; } = ""; public string RelPath { get; set; } = ""; public bool Replace { get; set; } = true; }

    private readonly ObservableCollection<Row> _rows = new();

    public UpdateModsDialog(IntakePlan plan)
    {
        InitializeComponent();
        foreach (var c in plan.Collisions) _rows.Add(new Row { Name = c.Name, RelPath = c.RelPath, Replace = true });
        CollisionList.ItemsSource = _rows;
        var adds = plan.ToAdd.Select(a => "+ " + a.RelPath).ToList();
        AddsHeader.Text = adds.Count > 0 ? $"Will also install {adds.Count} new file(s):" : "";
        AddsList.ItemsSource = adds;
    }

    /// <summary>The rel-paths the user chose to replace.</summary>
    public ISet<string> ChosenReplacements() => _rows.Where(r => r.Replace).Select(r => r.RelPath).ToHashSet();

    private void OnReplaceAll(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var on = ReplaceAllBox.IsChecked == true;
        foreach (var r in _rows) r.Replace = on;
        CollisionList.ItemsSource = null; CollisionList.ItemsSource = _rows; // refresh checkbox bindings
    }
}
```

- [ ] **Step 3: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\mu7`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/UpdateModsDialog.xaml src/ModManager.App/UpdateModsDialog.xaml.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): UpdateModsDialog — per-file replace + replace-all collision prompt"
```

---

## Task 8: Wire plan -> dialog -> execute in the App

Replace the single-shot intake in `MainViewModel.AddModsAsync` with plan → (dialog if collisions) → execute, for both the direct-inject and standard branches. `DirectInjectService` gains `Plan` + `Execute` passthroughs resolving the play folder + a `replaced` root under the game's data dir.

**Files:**
- Modify: `src/ModManager.App/Services/DirectInjectService.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs:383-436`

- [ ] **Step 1: Add Plan/Execute to DirectInjectService**

In `DirectInjectService.cs`, alongside `Install` (line 58), add (resolve the same play folder `Install` uses; put the replaced store under that folder so it travels with the game):

```csharp
public IntakePlan Plan(GameEntry game, IEnumerable<string> paths)
{
    var folder = ResolvePlayFolder(game); // the same resolver Install uses
    return folder is null ? new IntakePlan(Array.Empty<IntakeItem>(), Array.Empty<IntakeCollision>(), Array.Empty<SkippedItem>())
                          : DirectInject.Plan(folder, paths);
}

public IntakeResult Execute(GameEntry game, IntakePlan plan, ISet<string> replace)
{
    var folder = ResolvePlayFolder(game);
    if (folder is null) return new IntakeResult();
    var replacedRoot = System.IO.Path.Combine(folder, "_626", "replaced");
    return DirectInject.Execute(folder, replacedRoot, plan, replace);
}
```

> If the play-folder resolver in `DirectInjectService` is a private inline expression rather than a `ResolvePlayFolder` method, extract it into one first so both `Install` and the new methods share it.

- [ ] **Step 2: Rewrite the two AddModsAsync branches**

In `MainViewModel.cs`, replace the body of the `DirectInjectBacked` branch (lines ~392-414) and the standard branch (lines ~416-434) to run plan → dialog → execute. Add a helper:

```csharp
// Returns the replacements the user approved, or null if they cancelled.
private async Task<ISet<string>?> ConfirmReplacementsAsync(IntakePlan plan)
{
    if (plan.Collisions.Count == 0) return new HashSet<string>(); // nothing to confirm
    var dlg = new UpdateModsDialog(plan) { XamlRoot = App.MainWindow.Content.XamlRoot };
    var res = await dlg.ShowAsync();
    return res == ContentDialogResult.Primary ? dlg.ChosenReplacements() : null;
}
```

Direct-inject branch:

```csharp
if (DirectInjectBacked)
{
    IsBusy = true;
    try
    {
        var plan = _direct.Plan(_ctx.Game, paths);
        var chosen = await ConfirmReplacementsAsync(plan);
        if (chosen is null) { StatusText = "Update cancelled."; return; }
        var r = _direct.Execute(_ctx.Game, plan, chosen);
        if (r.Added.Count > 0 || r.Updated.Count > 0) _svc.Redetect(_ctx.Game.Id);
        await ReloadModsAsync();
        StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
            + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : ".");
    }
    catch (Exception e) { StatusText = e.Message; }
    finally { IsBusy = false; }
    return;
}
```

Standard branch:

```csharp
IsBusy = true;
try
{
    var plan = Scanner.PlanIntake(paths, _ctx);
    var chosen = await ConfirmReplacementsAsync(plan);
    if (chosen is null) { StatusText = "Update cancelled."; return; }
    var r = Scanner.ExecuteIntake(plan, chosen, _ctx);
    var identified = 0;
    if (r.Added.Count > 0)
    {
        try { identified = (await Scanner.FingerprintIdentifyAsync(_ctx, _svc.CurseForge, r.Added)).Matched; } catch { }
        try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); } catch { }
    }
    StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
        + (identified > 0 ? $", identified {identified} on CurseForge" : "");
    await ReloadModsAsync();
}
catch (Exception e) { StatusText = e.Message; }
finally { IsBusy = false; }
```

> `App.MainWindow` must expose the main window for `XamlRoot`. If it isn't already public, add a `public static Window MainWindow { get; }` set in `App.OnLaunched` (check `App.xaml.cs` — most templates already keep a window reference).

- [ ] **Step 3: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\mu8`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/Services/DirectInjectService.cs src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/App.xaml.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(intake): wire plan -> collision dialog -> execute in AddModsAsync (both paths)"
```

---

## Task 9: Full verification + data-safety review

**Files:**
- Modify: `CLAUDE.md` / `README.md` if the intake surface is worth a note.

- [ ] **Step 1: Full suite + app build**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (expect all green, 278 + the new intake-update tests), then `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\mufinal` (expect 0 errors).

- [ ] **Step 2: Data-safety review (the laws)** — confirm by re-reading the diff:
  - A replace **moves** the old file to `replaced/<ts>/` before writing the new one (never destroys). ✓ (Tasks 2/4/6 tests)
  - A collision the user did not check is **left untouched** and reported skipped. ✓ (Task 4 test)
  - Path-traversal / unsafe entries are refused in planning and never executed. ✓ (carried in `Unsafe`)
  - No `require('electron')`-equivalent (UI ref) leaked into Core. ✓ (`CorePurityTests` still green)

- [ ] **Step 3: GUI smoke (manual, optional)** — drop a new Seamless zip over an installed one; confirm the dialog lists `ersc.dll` etc., Replace-all + Apply updates them, and the status reads "Updated N…". Old files present under `<playFolder>\_626\replaced\<ts>\`.

- [ ] **Step 4: Commit any doc updates**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add -A
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "docs: note mod-update (collision-aware intake) surface"
```

---

## Self-review (done at write time)

- **Spec coverage:** plan/dialog/execute (T1/T3/T4/T5/T6/T7/T8) ✓; prompt replace-or-skip (T7/T8) ✓; file-by-file + replace-all (T7) ✓; reversible backup (T2, asserted T4/T6) ✓; both paths (Scanner T3/T4 + DirectInject T5/T6) ✓; updated status surfacing (T8) ✓; data-safety review (T9) ✓.
- **Out-of-scope correctly absent:** no revert-update UI, no whole-set auto-replace, no ME2 drop-install.
- **Type consistency:** `IntakeItem(Name,RelPath,IncomingSource)`, `IntakeCollision(Name,RelPath,ExistingPath,IncomingSource)`, `IntakePlan(ToAdd:IReadOnlyList<IntakeItem>, Collisions, Unsafe)`, `IntakeResult.Updated`, `ReplacedStore.NewBatch/Backup`, `PlanIntake/ExecuteIntake`, `DirectInject.Plan/Execute` used consistently from Task 1 onward. The `incoming` marker convention `"zipPath!entryName"` is shared by both cores' copy helpers (`CopyPlanned` / `CopyIncoming`).
