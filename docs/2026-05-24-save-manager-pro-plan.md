# Professional Save Manager — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the save manager into a professional, user-managed tool — all FromSoft save types with 3-way cloning, granular per-type restore, and opt-in automatic backups with a retention cap — built on a repeatable per-game **Game Profile** foundation.

**Architecture:** New pure-Core `GameProfile` + `GameProfiles.Resolve(engine, appId)` declares a game's save types (FromSoft → `.sl2/.co2/.err`); `SaveManager` consumes the resolved types instead of a hard-coded dict and gains `IsAuto` tagging, `Prune`, `TypesInSnapshot`, `RestoreType`. `GameEntry` gains `AutoBackupOnLaunch` + `SaveAutoKeep`; the launch path snapshots+prunes before launch when enabled. UI in `SavesDialog` becomes profile-driven (clone menu, per-type restore, auto-backup toggle). All file logic stays in pure cores tested with `node`-style temp-dir xUnit tests.

**Tech Stack:** .NET 10, C#, xUnit, System.IO / System.IO.Compression. WinUI 3 (App layer).

Spec: [docs/2026-05-24-save-manager-pro-design.md](2026-05-24-save-manager-pro-design.md)

---

## File Structure

- Create: `src/ModManager.Core/GameProfile.cs` — `SaveType`, `GameProfile`, `GameProfiles.Resolve` (save slice; other capabilities reserved).
- Modify: `src/ModManager.Core/SaveManager.cs` — consume `IReadOnlyList<SaveType>`; `SaveSnapshot.IsAuto`; auto-tagging in `Backup`; `Prune`; `TypesInSnapshot`; `RestoreType`; `RestoreType`/`Restore` auto-tag.
- Modify: `src/ModManager.Core/GameEntry.cs` — `AutoBackupOnLaunch`, `SaveAutoKeep`.
- Modify: `src/ModManager.App/Services/LauncherService.cs` — `SetAutoBackup(gameId, bool, int?)`.
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — before-launch backup+prune in `Launch`/`LaunchTargetExplicit`.
- Modify: `src/ModManager.App/SavesDialog.xaml` + `.xaml.cs` — profile-driven clone menu, per-type restore menu, auto-backup checkbox + keep count, `IsAuto` display.
- Tests: `tests/ModManager.Tests/GameProfileTests.cs` (new), `tests/ModManager.Tests/SaveManagerProTests.cs` (new), update `tests/ModManager.Tests/SaveCloneTests.cs`.

> **Note on existing API change:** `SaveManager.ListSaveFiles(saveDir)` and the `SaveManager.SaveTypes` dict (shipped earlier) are refactored to be profile-driven. `SaveCloneTests` and `SavesDialog` are updated accordingly (Tasks 2 + 9).

---

## Task 1: GameProfile + GameProfiles.Resolve (save slice)

**Files:**
- Create: `src/ModManager.Core/GameProfile.cs`
- Test: `tests/ModManager.Tests/GameProfileTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class GameProfileTests
{
    [Fact]
    public void FromSoft_declares_its_three_save_types_in_order()
    {
        var p = GameProfiles.Resolve("fromsoft", "1245620");
        Assert.Equal(new[] { ".sl2", ".co2", ".err" }, p.SaveTypes.Select(s => s.Extension).ToArray());
        Assert.Equal("Vanilla", p.SaveTypes[0].Label);
        Assert.Equal("Seamless Co-op", p.SaveTypes[1].Label);
        Assert.Equal("Reforged", p.SaveTypes[2].Label);
    }

    [Fact]
    public void Unknown_engine_declares_no_save_types_baseline_only()
    {
        Assert.Empty(GameProfiles.Resolve("custom", null).SaveTypes);
        Assert.Empty(GameProfiles.Resolve(null, null).SaveTypes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileTests"`
Expected: FAIL — `GameProfiles` / `GameProfile` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ModManager.Core;

/// <summary>One save kind a game uses: file extension + plain-English label.</summary>
public sealed record SaveType(string Extension, string Label);

/// <summary>
/// Declarable per-game knowledge the app consults to know which features apply. This round only
/// SaveTypes is populated/used; LaunchOptions / AntiCheat / ModLayout converge onto this later.
/// </summary>
public sealed record GameProfile(string Engine, IReadOnlyList<SaveType> SaveTypes);

/// <summary>
/// Resolves a <see cref="GameProfile"/> for a game — engine-level defaults, with a per-App-ID
/// override hook. Repeatable: adding a game/engine's save types is a one-line catalog entry.
/// Unknown games resolve to no declared save types (baseline backup/restore still works).
/// </summary>
public static class GameProfiles
{
    public static GameProfile Resolve(string? engine, string? steamAppId)
        => new(engine ?? "", SaveTypesFor(engine));

    private static IReadOnlyList<SaveType> SaveTypesFor(string? engine) => engine switch
    {
        "fromsoft" => new[]
        {
            new SaveType(".sl2", "Vanilla"),
            new SaveType(".co2", "Seamless Co-op"),
            new SaveType(".err", "Reforged"),
        },
        _ => Array.Empty<SaveType>(),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/GameProfile.cs tests/ModManager.Tests/GameProfileTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): GameProfile foundation — per-game save-type catalog (save slice)"
```

---

## Task 2: SaveManager consumes profile save types

Refactor `ListSaveFiles` to take resolved types; drop the hard-coded `SaveTypes` dict. Update existing `SaveCloneTests` to pass types.

**Files:**
- Modify: `src/ModManager.Core/SaveManager.cs`
- Test: `tests/ModManager.Tests/SaveCloneTests.cs`

- [ ] **Step 1: Update the failing test (SaveCloneTests) to the new signature**

Replace the two helper usages so the test passes resolved types:

```csharp
private static IReadOnlyList<SaveType> FromSoft => GameProfiles.Resolve("fromsoft", null).SaveTypes;

// in Lists_only_known_save_types_with_labels:
var files = SaveManager.ListSaveFiles(_dir, FromSoft);
// (rest unchanged)

// CloneToType calls are unchanged (no type list needed).
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveCloneTests"`
Expected: FAIL — `ListSaveFiles` no longer takes a single arg (compile error) until impl updated.

- [ ] **Step 3: Implement — replace the dict-based members**

In `SaveManager.cs`, remove the `SaveTypes` dictionary property and change `ListSaveFiles`:

```csharp
/// <summary>Recognized save files in the folder, labeled by the game's declared save types.</summary>
public static IReadOnlyList<SaveFile> ListSaveFiles(string saveDir, IReadOnlyList<SaveType> saveTypes)
{
    if (!Directory.Exists(saveDir)) return Array.Empty<SaveFile>();
    var byExt = saveTypes.ToDictionary(t => t.Extension, t => t.Label, StringComparer.OrdinalIgnoreCase);
    return Directory.GetFiles(saveDir)
        .Where(f => byExt.ContainsKey(System.IO.Path.GetExtension(f)))
        .Select(f => new SaveFile(System.IO.Path.GetFileName(f), System.IO.Path.GetExtension(f), byExt[System.IO.Path.GetExtension(f)]))
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
```

Update `CloneToType`'s error message to not depend on the dict (use the extension):

```csharp
if (File.Exists(dest) && !overwrite)
    throw new IOException($"A {targetExt} save already exists ({destName}). Snapshot it first if you want to replace it.");
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveCloneTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/SaveManager.cs tests/ModManager.Tests/SaveCloneTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "refactor(saves): ListSaveFiles is profile-driven (drop hard-coded SaveTypes dict)"
```

---

## Task 3: Auto-tagged snapshots (IsAuto + reserved prefix)

**Files:**
- Modify: `src/ModManager.Core/SaveManager.cs`
- Test: `tests/ModManager.Tests/SaveManagerProTests.cs` (new)

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class SaveManagerProTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-smp-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(_root, "save");
    private string Snaps => Path.Combine(_root, "snaps");

    public SaveManagerProTests()
    {
        Directory.CreateDirectory(Save);
        File.WriteAllText(Path.Combine(Save, "ER0000.sl2"), "S");
    }
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public void Auto_backup_is_flagged_user_backup_is_not()
    {
        var auto = SaveManager.Backup(Save, Snaps, "before-launch", auto: true);
        var user = SaveManager.Backup(Save, Snaps, "my checkpoint");
        Assert.True(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == auto.FileName).IsAuto);
        Assert.False(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == user.FileName).IsAuto);
    }

    [Fact]
    public void A_user_label_cannot_masquerade_as_auto()
    {
        var sneaky = SaveManager.Backup(Save, Snaps, "auto-before-launch"); // user, not auto
        Assert.False(SaveManager.ListSnapshots(Snaps).Single(s => s.FileName == sneaky.FileName).IsAuto);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.Auto_backup_is_flagged"`
Expected: FAIL — `Backup` has no `auto` param; `SaveSnapshot.IsAuto` missing.

- [ ] **Step 3: Implement**

Add `IsAuto` to the record:

```csharp
public sealed record SaveSnapshot(string Path, string FileName, string Label, DateTime TakenUtc, long SizeBytes, bool IsAuto);
```

Add the reserved prefix + auto param to `Backup`, stripping it from user labels:

```csharp
private const string AutoPrefix = "auto-";

public static SaveSnapshot Backup(string saveDir, string snapshotsDir, string? label = null, bool auto = false)
{
    if (!Directory.Exists(saveDir))
        throw new DirectoryNotFoundException($"Save folder not found: {saveDir}");

    Directory.CreateDirectory(snapshotsDir);
    var takenUtc = DateTime.UtcNow;
    var stamp = takenUtc.ToString(TimeFormat, CultureInfo.InvariantCulture);

    var safe = SanitizeLabel(label);
    // The auto- prefix is reserved for the app: strip it from user labels so a user backup
    // can never be misclassified as auto (and pruned).
    while (safe.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase)) safe = safe[AutoPrefix.Length..];
    if (auto) safe = AutoPrefix + (safe.Length > 0 ? safe : "snapshot");

    var fileName = safe.Length > 0 ? $"{stamp}__{safe}.zip" : $"{stamp}.zip";
    var path = System.IO.Path.Combine(snapshotsDir, fileName);
    var n = 1;
    while (File.Exists(path))
        path = System.IO.Path.Combine(snapshotsDir, (safe.Length > 0 ? $"{stamp}__{safe}-{n}" : $"{stamp}-{n}") + ".zip");

    ZipFile.CreateFromDirectory(saveDir, path);
    return new SaveSnapshot(path, System.IO.Path.GetFileName(path), safe, takenUtc, new FileInfo(path).Length, IsAutoLabel(safe));
}

private static bool IsAutoLabel(string label) => label.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase);
```

Update `ListSnapshots` to set `IsAuto` (it already parses `label`): in the `outList.Add(...)` call, append `IsAutoLabel(label)` as the final argument. Update the fallback branch (where `label = name`) too — it flows through the same `IsAutoLabel(label)`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests"`
Expected: PASS (the two IsAuto tests).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/SaveManager.cs tests/ModManager.Tests/SaveManagerProTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): auto-tagged snapshots (IsAuto) with reserved auto- prefix"
```

---

## Task 4: Prune (keep all user + newest N auto)

**Files:**
- Modify: `src/ModManager.Core/SaveManager.cs`
- Test: `tests/ModManager.Tests/SaveManagerProTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Prune_keeps_all_user_and_newest_N_auto()
{
    SaveManager.Backup(Save, Snaps, "keep me");                 // user
    for (var i = 0; i < 5; i++) { System.Threading.Thread.Sleep(1100); SaveManager.Backup(Save, Snaps, "before-launch", auto: true); }

    SaveManager.Prune(Snaps, keepLastAuto: 2);

    var left = SaveManager.ListSnapshots(Snaps);
    Assert.Equal(1, left.Count(s => !s.IsAuto));   // user backup kept
    Assert.Equal(2, left.Count(s => s.IsAuto));    // newest 2 autos kept
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.Prune_keeps"`
Expected: FAIL — `Prune` not defined.

- [ ] **Step 3: Implement**

```csharp
/// <summary>Keep every user snapshot plus the newest <paramref name="keepLastAuto"/> auto snapshots;
/// delete older autos only. A user (non-auto) snapshot is never deleted.</summary>
public static void Prune(string snapshotsDir, int keepLastAuto)
{
    if (keepLastAuto < 0) keepLastAuto = 0;
    var autos = ListSnapshots(snapshotsDir).Where(s => s.IsAuto).ToList(); // newest-first (ListSnapshots orders desc)
    foreach (var old in autos.Skip(keepLastAuto))
        Delete(old.Path);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.Prune_keeps"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/SaveManager.cs tests/ModManager.Tests/SaveManagerProTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): Prune — bound auto snapshots, never touch user backups"
```

---

## Task 5: TypesInSnapshot

**Files:**
- Modify: `src/ModManager.Core/SaveManager.cs`
- Test: `tests/ModManager.Tests/SaveManagerProTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void TypesInSnapshot_reports_declared_types_present()
{
    File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "C"); // Seamless save also present
    var snap = SaveManager.Backup(Save, Snaps, "two types");
    var types = GameProfiles.Resolve("fromsoft", null).SaveTypes;

    var present = SaveManager.TypesInSnapshot(snap.Path, types).Select(t => t.Extension).ToList();
    Assert.Contains(".sl2", present);
    Assert.Contains(".co2", present);
    Assert.DoesNotContain(".err", present); // not in the save folder
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.TypesInSnapshot"`
Expected: FAIL — `TypesInSnapshot` not defined.

- [ ] **Step 3: Implement**

```csharp
/// <summary>The declared save types that actually appear inside a snapshot zip.</summary>
public static IReadOnlyList<SaveType> TypesInSnapshot(string snapshotZip, IReadOnlyList<SaveType> saveTypes)
{
    if (!File.Exists(snapshotZip)) return Array.Empty<SaveType>();
    using var zip = ZipFile.OpenRead(snapshotZip);
    var exts = new HashSet<string>(zip.Entries.Select(e => System.IO.Path.GetExtension(e.FullName)), StringComparer.OrdinalIgnoreCase);
    return saveTypes.Where(t => exts.Contains(t.Extension)).ToList();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.TypesInSnapshot"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/SaveManager.cs tests/ModManager.Tests/SaveManagerProTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): TypesInSnapshot — which declared save types a snapshot holds"
```

---

## Task 6: RestoreType (granular per-type restore)

**Files:**
- Modify: `src/ModManager.Core/SaveManager.cs`
- Test: `tests/ModManager.Tests/SaveManagerProTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RestoreType_restores_only_the_chosen_type_and_snapshots_first()
{
    File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "COOP-OLD");
    var snap = SaveManager.Backup(Save, Snaps, "checkpoint");
    File.WriteAllText(Path.Combine(Save, "ER0000.co2"), "COOP-NEW");   // co2 changed
    File.WriteAllText(Path.Combine(Save, "ER0000.sl2"), "VANILLA-NEW"); // sl2 changed

    SaveManager.RestoreType(snap.Path, Save, Snaps, ".co2");

    Assert.Equal("COOP-OLD", File.ReadAllText(Path.Combine(Save, "ER0000.co2"))); // co2 rolled back
    Assert.Equal("VANILLA-NEW", File.ReadAllText(Path.Combine(Save, "ER0000.sl2"))); // sl2 untouched
    Assert.Contains(SaveManager.ListSnapshots(Snaps), s => s.IsAuto); // current state was snapshotted first
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests.RestoreType"`
Expected: FAIL — `RestoreType` not defined.

- [ ] **Step 3: Implement**

```csharp
/// <summary>Restore only files of <paramref name="extension"/> from a snapshot, leaving other save
/// types in place. Snapshots current state first (auto) so it stays reversible.</summary>
public static void RestoreType(string snapshotZip, string saveDir, string snapshotsDir, string extension)
{
    if (!File.Exists(snapshotZip)) throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

    if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
        Backup(saveDir, snapshotsDir, "before-restore", auto: true);

    Directory.CreateDirectory(saveDir);
    using var zip = ZipFile.OpenRead(snapshotZip);
    foreach (var entry in zip.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
        if (!string.Equals(System.IO.Path.GetExtension(entry.FullName), extension, StringComparison.OrdinalIgnoreCase)) continue;
        var dest = System.IO.Path.Combine(saveDir, entry.FullName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
        entry.ExtractToFile(dest, overwrite: true);
    }
}
```

Also re-tag the existing whole-folder `Restore`'s safety snapshot as auto: change its `Backup(saveDir, snapshotsDir, "before-restore")` call to `Backup(saveDir, snapshotsDir, "before-restore", auto: true)`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveManagerProTests"`
Expected: PASS (all SaveManagerProTests).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/SaveManager.cs tests/ModManager.Tests/SaveManagerProTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): RestoreType — granular per-type restore, reversible"
```

---

## Task 7: GameEntry fields + LauncherService setter

**Files:**
- Modify: `src/ModManager.Core/GameEntry.cs`
- Modify: `src/ModManager.App/Services/LauncherService.cs`

- [ ] **Step 1: Add the fields (GameEntry.cs)**

In the `// where this game's saves live` region, add:

```csharp
// auto-backup-before-launch opt-in + how many auto snapshots to retain (null = unlimited)
public bool AutoBackupOnLaunch { get; set; }
public int? SaveAutoKeep { get; set; } = 25;
```

- [ ] **Step 2: Add the persist setter (LauncherService.cs)** — next to `SetSaveDir`:

```csharp
/// <summary>Persist a game's auto-backup-before-launch preference + retention count.</summary>
public void SetAutoBackup(string gameId, bool onLaunch, int? keepAuto)
{
    var reg = LoadRegistry();
    var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
    if (g is null) return;
    g.AutoBackupOnLaunch = onLaunch;
    g.SaveAutoKeep = keepAuto;
    SaveRegistry(reg);
}
```

- [ ] **Step 3: Build-verify (no test — registry IO)**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o /tmp/svp7`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/GameEntry.cs src/ModManager.App/Services/LauncherService.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): GameEntry auto-backup-on-launch + retention; LauncherService setter"
```

---

## Task 8: Before-launch auto-backup + prune (VM)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add a private helper** (after `Launch`/`LaunchTargetExplicit`):

```csharp
// When the game opts in, snapshot the save (auto) and prune before launching. Best-effort —
// a backup failure surfaces but never blocks play.
private void AutoBackupBeforeLaunch()
{
    if (_ctx is null || !_ctx.Game.AutoBackupOnLaunch) return;
    var dir = _ctx.SaveDir;
    if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;
    try
    {
        SaveManager.Backup(dir, _ctx.SavesDir, "before-launch", auto: true);
        SaveManager.Prune(_ctx.SavesDir, _ctx.Game.SaveAutoKeep ?? int.MaxValue);
    }
    catch (Exception e) { StatusText = "Auto-backup before launch failed: " + e.Message; }
}
```

- [ ] **Step 2: Call it in both launch entry points**

In `Launch()`: add `AutoBackupBeforeLaunch();` as the first line inside the `try`, before `_svc.Launch(...)`.
In `LaunchTargetExplicit(...)`: add `AutoBackupBeforeLaunch();` as the first line inside the `try`, before `_svc.Launch(target, ...)`.

- [ ] **Step 3: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o /tmp/svp8`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/ViewModels/MainViewModel.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): auto-backup + prune before launch when the game opts in"
```

---

## Task 9: SavesDialog UI — profile-driven clone menu, per-type restore, auto-backup toggle

**Files:**
- Modify: `src/ModManager.App/SavesDialog.xaml`
- Modify: `src/ModManager.App/SavesDialog.xaml.cs`

- [ ] **Step 1: Resolve the profile in the dialog** — in `SavesDialog.xaml.cs` ctor, store `private readonly IReadOnlyList<SaveType> _saveTypes;` set from `GameProfiles.Resolve(ctx.Game.Engine, ctx.Game.SteamAppId).SaveTypes`. Also store `_gameId` (already present) and the current game's `AutoBackupOnLaunch`/`SaveAutoKeep`.

- [ ] **Step 2: Update `RefreshSaveFiles`** to use `_saveTypes` and build a multi-target clone menu. Replace the single `CloneLabel`/`TargetExt` row model with one carrying all other-type targets:

```csharp
public sealed record SaveCloneTarget(string Label, string Ext);
public sealed record SaveFileRow(string Name, string TypeLabel, IReadOnlyList<SaveCloneTarget> Targets);
```

```csharp
private void RefreshSaveFiles()
{
    var rows = (string.IsNullOrEmpty(_saveDir) ? Array.Empty<SaveFile>() : SaveManager.ListSaveFiles(_saveDir, _saveTypes))
        .Select(f => new SaveFileRow(f.Name, f.TypeLabel,
            _saveTypes.Where(t => !string.Equals(t.Extension, f.Extension, StringComparison.OrdinalIgnoreCase))
                      .Select(t => new SaveCloneTarget("Clone to " + t.Label, t.Extension)).ToList()))
        .ToList();
    SaveFileList.ItemsSource = rows;
    SaveFilesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
```

- [ ] **Step 3: Clone menu in XAML** — replace the single clone `Button` in the save-file `DataTemplate` with a `DropDownButton` whose `MenuFlyout` is populated on opening (code-behind), since x:Bind can't bind a flyout's items. Add to the `DataTemplate`:

```xml
<DropDownButton Grid.Column="1" Content="Clone to…" VerticalAlignment="Center" Tag="{x:Bind}">
    <DropDownButton.Flyout>
        <MenuFlyout Opening="OnCloneMenuOpening" />
    </DropDownButton.Flyout>
</DropDownButton>
```

Code-behind:

```csharp
private void OnCloneMenuOpening(object sender, object e)
{
    if (sender is not MenuFlyout menu || menu.Target is not FrameworkElement fe || fe is not DropDownButton ddb
        || ddb.Tag is not SaveFileRow row) return;
    menu.Items.Clear();
    foreach (var t in row.Targets)
    {
        var item = new MenuFlyoutItem { Text = t.Label, Tag = (row.Name, t.Ext) };
        item.Click += OnCloneTo;
        menu.Items.Add(item);
    }
}

private void OnCloneTo(object sender, RoutedEventArgs e)
{
    if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, string> pair }) return;
    if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
    try
    {
        var created = SaveManager.CloneToType(_saveDir, pair.Item1, pair.Item2);
        StatusText.Text = $"Cloned {pair.Item1} → {created}. Your original is untouched.";
        RefreshSaveFiles();
    }
    catch (Exception ex) { StatusText.Text = ex.Message; }
}
```

- [ ] **Step 4: Per-type restore menu** — in the snapshot `DataTemplate`, add a `DropDownButton "Restore only…"` next to `Restore`; populate on opening from `SaveManager.TypesInSnapshot`:

```xml
<DropDownButton Grid.Column="2" Content="Only…" VerticalAlignment="Center" Tag="{x:Bind}">
    <DropDownButton.Flyout><MenuFlyout Opening="OnRestoreTypeOpening" /></DropDownButton.Flyout>
</DropDownButton>
```

(Adjust the snapshot grid to add a column for it.) Code-behind:

```csharp
private void OnRestoreTypeOpening(object sender, object e)
{
    if (sender is not MenuFlyout menu || menu.Target is not DropDownButton ddb || ddb.Tag is not SaveRow row) return;
    menu.Items.Clear();
    foreach (var t in SaveManager.TypesInSnapshot(row.Snap.Path, _saveTypes))
    {
        var item = new MenuFlyoutItem { Text = "Restore only " + t.Label, Tag = (row.Snap.Path, t.Extension) };
        item.Click += OnRestoreType;
        menu.Items.Add(item);
    }
    if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No typed saves in this snapshot", IsEnabled = false });
}

private void OnRestoreType(object sender, RoutedEventArgs e)
{
    if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, string> pair }) return;
    if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
    try
    {
        SaveManager.RestoreType(pair.Item1, _saveDir, _savesDir, pair.Item2);
        StatusText.Text = "Restored that save type. Your previous state was snapshotted first.";
        Refresh();
    }
    catch (Exception ex) { StatusText.Text = ex.Message; }
}
```

- [ ] **Step 5: Auto-backup checkbox + IsAuto display** — add to `SavesDialog.xaml` (near the folder row):

```xml
<CheckBox x:Name="AutoBackupCheck" Content="Auto-backup before launch" Checked="OnAutoBackupChanged" Unchecked="OnAutoBackupChanged" />
```

Code-behind: set `AutoBackupCheck.IsChecked = ctx.Game.AutoBackupOnLaunch;` in the ctor; handler persists:

```csharp
private void OnAutoBackupChanged(object sender, RoutedEventArgs e)
    => _svc.SetAutoBackup(_gameId, AutoBackupCheck.IsChecked == true, 25);
```

In the snapshot row `Detail` text, prefix auto snapshots — in `Refresh()`, build the title with an "auto · " marker when `s.IsAuto` (e.g. `s.IsAuto ? "auto · " + label : label`).

- [ ] **Step 6: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o /tmp/svp9`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/SavesDialog.xaml src/ModManager.App/SavesDialog.xaml.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(saves): profile-driven clone menu, per-type restore, auto-backup toggle"
```

---

## Task 10: Documentation & Security Verification

**Files:**
- Modify: `CLAUDE.md` (What's where / Common tasks if needed), memory note.

- [ ] **Step 1: Full suite + build**

Run: `dotnet test` (expect all green, ~272+), then `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o /tmp/svpfinal` (expect 0 errors).

- [ ] **Step 2: Secrets / dependency check** — confirm no secrets or new runtime deps were added (Core stays zero-dep): `git -C /c/Users/estev/Projects/626-mod-launcher diff --stat 2b917ce..HEAD` and eyeball; confirm `ModManager.Core.csproj` has no new `PackageReference`.

- [ ] **Step 3: Data-safety review** — re-confirm the operating laws hold: every save-changing op snapshots first (RestoreType, clone-replace), clone never overwrites without the gated path, Prune never deletes a user snapshot. (Covered by tests in Tasks 3–6.)

- [ ] **Step 4: Update CLAUDE.md** if the save-manager surface or `GameProfile` is worth a row in "What's where"; add/refresh the `launch-options-feature` / a new `game-profile` memory.

- [ ] **Step 5: Commit any doc updates**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add -A
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "docs: note GameProfile foundation + pro save manager"
```

---

## Self-review (done at write time)

- **Spec coverage:** GameProfile foundation (T1) ✓; all save types + clone (T1/T2/T9) ✓; auto-tag (T3) ✓; retention/Prune (T4) ✓; per-type restore (T5/T6/T9) ✓; before-launch auto-backup (T7/T8) ✓; UI (T9) ✓; docs/security (T10) ✓.
- **Out-of-scope** (per-slot restore, checksums, cloud, catalog migration of launch/anti-cheat/mods) — correctly absent.
- **Type consistency:** `SaveType(Extension, Label)`, `GameProfile(Engine, SaveTypes)`, `GameProfiles.Resolve`, `SaveSnapshot(..., IsAuto)`, `Backup(..., auto)`, `Prune(dir, keepLastAuto)`, `TypesInSnapshot(zip, saveTypes)`, `RestoreType(zip, saveDir, snapshotsDir, extension)` used consistently across tasks.
