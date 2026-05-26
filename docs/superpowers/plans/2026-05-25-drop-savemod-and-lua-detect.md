# Drop Save-Mods + UE4SS Lua Detection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`.

**Goal:** Two real-world drop bugs caught in Este's testing — both rooted in the same gap:

1. **Save-mod silent skip:** Dropping a save/world-mod zip currently routes through `Scanner.PlanIntake`, which classifies every entry against the engine's pak/dll extensions. Save-mod entries (`RocksDB/.../Worlds/<guid>/...`) match none, so the plan is empty and the user sees "Updated 0, added 0, skipped 0" — no install, no diagnosis. The Core (`SaveModDetect` / `SaveModInstaller` / `SaveModStore`) is shipped; the App side never wired it. **Fix:** auto-detect save mods on drop, route to `SaveModInstaller.InstallWorld`, surface in `SavesDialog` with Reset/Remove actions.

2. **UE4SS Lua mod silent skip:** Dropping `R5ModSettings-*.7z` (a UE4SS Lua mod: `<Name>/Scripts/*.lua` + optional `<Name>/dlls/*.dll` + optional `<Name>/enabled.txt`) silently skips for the same reason — Lua/DLL aren't pak extensions. **Fix:** detect the UE4SS-Lua archive structure and surface a clear status message. For Windrose specifically, `ue4ss\Mods` is Vortex-managed (the owned-folder invariant forbids us writing there), so the message points the user at Vortex; actual auto-install to a non-owned `ue4ss\Mods` is deferred.

**Architecture:** Both fixes layer in front of `Scanner.PlanIntake` in `MainViewModel.AddModsAsync` without touching `PlanIntake` itself. Two new Core seams: `SaveModFlow.TryHandleDrops` (orchestrates Detect → Installer → Store, one verdict per zip) and `Ue4ssLuaDetect.Detect` (pure pattern recognition). The VM consumes them in order — save-mod first, Lua-mod second, then the existing pak/dll intake on whatever's left. `SavesDialog` grows an "Installed save mods" list bound to `SaveModStore.Load(ctx.DataDir)` with Reset / Remove buttons.

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit.

---

## Task 1: `Ue4ssLuaDetect.Detect`

**Files:**
- Create: `src/ModManager.Core/Ue4ssLuaDetect.cs`
- Create: `tests/ModManager.Tests/Ue4ssLuaDetectTests.cs`

**Detection signal:** A top-level folder name `<X>` is treated as a UE4SS Lua mod when:
- it contains a `Scripts/` subfolder with at least one `.lua` file, OR
- it contains an `enabled.txt` at its root AND a `dlls/` subfolder with at least one `.dll`.

A pak/ucas/utoc anywhere VETOES (that's a content mod, not a Lua mod).

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssLuaDetectTests
{
    [Fact]
    public void Detects_a_classic_ue4ss_lua_mod_by_Scripts_lua()
    {
        var entries = new[]
        {
            "R5ModSettings/",
            "R5ModSettings/Scripts/",
            "R5ModSettings/Scripts/main.lua",
            "R5ModSettings/Scripts/R5ModSettings.lua",
            "R5ModSettings/enabled.txt",
            "R5ModSettings/dlls/main.dll",
        };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.True(v.IsLuaMod);
        Assert.Equal("R5ModSettings", v.ModFolderName);
    }

    [Fact]
    public void Detects_a_lua_mod_with_only_enabled_txt_and_dlls()
    {
        var entries = new[] { "NativeMod/enabled.txt", "NativeMod/dlls/main.dll" };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.True(v.IsLuaMod);
        Assert.Equal("NativeMod", v.ModFolderName);
    }

    [Fact]
    public void A_pak_anywhere_vetoes_lua_detection()
    {
        var entries = new[] { "R5ModSettings/Scripts/main.lua", "extra/AwesomeMod_P.pak" };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.False(v.IsLuaMod);
    }

    [Fact]
    public void Bare_lua_files_without_a_folder_are_not_a_lua_mod()
    {
        var entries = new[] { "main.lua", "helper.lua" };
        Assert.False(Ue4ssLuaDetect.Detect(entries).IsLuaMod);
    }

    [Fact]
    public void Empty_entries_return_not_a_lua_mod()
    {
        Assert.False(Ue4ssLuaDetect.Detect(Array.Empty<string>()).IsLuaMod);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile-fail counts as red)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Ue4ssLuaDetectTests"`
Expected: FAIL — `Ue4ssLuaDetect` does not exist.

- [ ] **Step 3: Implement Ue4ssLuaDetect**

```csharp
namespace ModManager.Core;

/// <summary>The verdict for a candidate UE4SS Lua mod: whether it is one, and the top-level
/// folder name the archive uses for it (the directory that should land under ue4ss\Mods).</summary>
public sealed record Ue4ssLuaVerdict(bool IsLuaMod, string? ModFolderName);

/// <summary>
/// Recognizes a UE4SS Lua-mod ARCHIVE structure: a top-level folder containing either
/// <c>Scripts/*.lua</c> or <c>enabled.txt + dlls/*.dll</c>. A pak/ucas/utoc anywhere VETOES
/// (that's a content mod, not a Lua mod). Pure — no filesystem, no Electron.
/// </summary>
public static class Ue4ssLuaDetect
{
    private static readonly string[] PakExtensions = { ".pak", ".ucas", ".utoc" };

    public static Ue4ssLuaVerdict Detect(IEnumerable<string> zipEntryNames)
    {
        var names = (zipEntryNames ?? Enumerable.Empty<string>())
            .Select(n => (n ?? "").Replace('\\', '/').TrimStart('/'))
            .Where(n => n.Length > 0)
            .ToList();
        if (names.Count == 0) return new Ue4ssLuaVerdict(false, null);

        // Veto: any pak/content file means this is a content mod, not a Lua mod.
        foreach (var n in names)
            if (PakExtensions.Contains(System.IO.Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                return new Ue4ssLuaVerdict(false, null);

        // Group entries by their TOP-LEVEL folder (the first path segment).
        var byTop = names
            .Select(n => new { Segs = n.Split('/'), Full = n })
            .Where(x => x.Segs.Length >= 2)
            .GroupBy(x => x.Segs[0], StringComparer.OrdinalIgnoreCase);

        foreach (var g in byTop)
        {
            var inFolder = g.Select(x => x.Full).ToList();
            var hasScriptsLua = inFolder.Any(p =>
                p.StartsWith(g.Key + "/Scripts/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            var hasEnabledTxt = inFolder.Any(p =>
                string.Equals(p, g.Key + "/enabled.txt", StringComparison.OrdinalIgnoreCase));
            var hasDllInDlls = inFolder.Any(p =>
                p.StartsWith(g.Key + "/dlls/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (hasScriptsLua || (hasEnabledTxt && hasDllInDlls))
                return new Ue4ssLuaVerdict(true, g.Key);
        }
        return new Ue4ssLuaVerdict(false, null);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: 5/5 pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Ue4ssLuaDetect.cs tests/ModManager.Tests/Ue4ssLuaDetectTests.cs
git commit -m "feat: Ue4ssLuaDetect for UE4SS Lua-mod archive recognition"
```

---

## Task 2: `SaveModFlow.TryHandleDrops`

**Files:**
- Create: `src/ModManager.Core/SaveModFlow.cs`
- Create: `tests/ModManager.Tests/SaveModFlowTests.cs`

A thin orchestrator that wraps `SaveModDetect` + `SaveModInstaller.InstallWorld` + `SaveModStore.Upsert` into a single drop-time call. Returns one verdict per archive: `Installed | NotASaveMod | Failed(reason)`. Non-archive paths pass through as `NotASaveMod` untouched.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

public class SaveModFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smf-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public void Non_archive_paths_are_passed_through_as_NotASaveMod()
    {
        var loose = Path.Combine(_root, "AwesomeMod.pak"); Directory.CreateDirectory(_root); File.WriteAllText(loose, "");
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { loose }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

    [Fact]
    public void A_content_zip_is_NotASaveMod()
    {
        var zip = MakeZip("content.zip", new[] { ("AwesomeMod_P.pak", "x") });
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }

    [Fact]
    public void A_world_zip_installs_into_the_save_tree_and_records_an_entry()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        // World zip carries <guid>/data.json
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });

        // A save-profiles dir with one profile + a RocksDB version subfolder.
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1"); Directory.CreateDirectory(oneProfile);
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));

        var data = NewDir("data");
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: NewDir("snaps"),
            dataDir: data, saveModPath: null, forbidden: null);

        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.Installed, verdicts[0].Outcome);
        Assert.Equal(guid, verdicts[0].WorldGuid);
        // File landed under <profile>/RocksDB/1.0/Worlds/<guid>/data.json
        Assert.True(File.Exists(Path.Combine(oneProfile, "RocksDB", "1.0", "Worlds", guid, "data.json")));
        // Store has an entry.
        var entries = SaveModStore.Load(data);
        Assert.Single(entries);
        Assert.Equal(guid, entries[0].Guid);
    }

    [Fact]
    public void A_save_zip_with_no_savedir_fails_with_a_clear_reason()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });
        // No profile dir at all -> InstallWorld throws "No save profile found - open the game once."
        var profiles = Path.Combine(_root, "nosaves"); // doesn't exist
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null);
        Assert.Equal(SaveModDropOutcome.Failed, verdicts[0].Outcome);
        Assert.False(string.IsNullOrEmpty(verdicts[0].Reason));
    }

    // -------- helpers --------
    private string NewDir(string name) { var d = Path.Combine(_root, name); Directory.CreateDirectory(d); return d; }
    private string MakeZip(string name, IEnumerable<(string Entry, string Content)> entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (e, c) in entries)
        {
            var ent = zip.CreateEntry(e);
            using var w = new StreamWriter(ent.Open());
            w.Write(c);
        }
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile-fail counts as red)**

- [ ] **Step 3: Implement SaveModFlow**

```csharp
using System.IO.Compression;

namespace ModManager.Core;

/// <summary>Outcome of a single archive drop through the save-mod fast-path.</summary>
public enum SaveModDropOutcome { Installed, NotASaveMod, Failed }

/// <summary>One archive's verdict + the world GUID (when installed) + a reason (when failed).</summary>
public sealed record SaveModDropVerdict(
    string SourcePath, SaveModDropOutcome Outcome, string? WorldGuid, string? Reason);

/// <summary>
/// Drop-time orchestrator over <see cref="SaveModDetect"/> + <see cref="SaveModInstaller"/> +
/// <see cref="SaveModStore"/>. Per archive: detect, then install + record, OR pass through as
/// NotASaveMod. Non-archive paths short-circuit to NotASaveMod (the caller's regular intake
/// keeps owning loose files / non-save zips). Pure System.IO; no Electron / UI.
/// </summary>
public static class SaveModFlow
{
    public static IReadOnlyList<SaveModDropVerdict> TryHandleDrops(
        IEnumerable<string> paths,
        IReadOnlyList<string> saveTypeExtensions,
        string saveProfilesDir,
        string snapshotsDir,
        string dataDir,
        string? saveModPath,
        IReadOnlyList<string>? forbidden)
    {
        var verdicts = new List<SaveModDropVerdict>();
        foreach (var p in paths ?? Enumerable.Empty<string>())
        {
            verdicts.Add(Handle(p, saveTypeExtensions, saveProfilesDir, snapshotsDir, dataDir, saveModPath, forbidden));
        }
        return verdicts;
    }

    private static SaveModDropVerdict Handle(
        string path,
        IReadOnlyList<string> saveTypeExtensions,
        string saveProfilesDir,
        string snapshotsDir,
        string dataDir,
        string? saveModPath,
        IReadOnlyList<string>? forbidden)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !IsArchive(path))
            return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, null);

        IReadOnlyList<string> names;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            names = zip.Entries.Select(e => e.FullName).ToList();
        }
        catch (Exception e)
        {
            // Unreadable as a zip: leave it to the regular intake path to skip with its own reason.
            return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, e.Message);
        }

        var verdict = SaveModDetect.Detect(names, saveTypeExtensions);
        if (!verdict.IsSaveMod) return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, null);
        if (string.IsNullOrEmpty(verdict.WorldGuid))
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, null,
                "Save mod detected but no world GUID — only Worlds/<GUID> packages auto-install for now.");

        try
        {
            var worldDir = SaveModInstaller.InstallWorld(
                saveProfilesDir, snapshotsDir, dataDir,
                path, verdict.WorldGuid!, saveModPath, forbidden);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            SaveModStore.Upsert(dataDir, new SaveModEntry(verdict.WorldGuid!, name, path, DateTime.UtcNow));
            return new SaveModDropVerdict(path, SaveModDropOutcome.Installed, verdict.WorldGuid, null);
        }
        catch (Exception e)
        {
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, verdict.WorldGuid, e.Message);
        }
    }

    private static bool IsArchive(string p)
    {
        var lower = p.ToLowerInvariant();
        return Intake.ArchiveExtensions.Any(a => lower.EndsWith(a));
    }
}
```

Note: `ZipFile.OpenRead` is OK here because save-mod sources have been .zip throughout the threat model + tests. `.7z`/`.rar` save mods would need the `Archive` seam, but we keep the fast-path narrow to avoid touching the SharpCompress surface for save-tree writes — those need the controlled `SaveModInstaller.ExtractWorld` path, which already uses `ZipFile`.

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SaveModFlow.cs tests/ModManager.Tests/SaveModFlowTests.cs
git commit -m "feat: SaveModFlow drop-time orchestrator over Detect+Installer+Store"
```

---

## Task 3: Wire both pre-checks into `MainViewModel.AddModsAsync`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add the pre-checks in `AddModsAsync` BEFORE `Scanner.PlanIntake`**

In the existing else-branch (after the ConfigBacked and DirectInjectBacked early-returns), wrap the current `Scanner.PlanIntake` call with two pre-checks. The save-mod check moves install + status itself; the Lua-mod check only sets status. Both carve their handled paths out of the list that proceeds into the existing intake.

Insert this BEFORE the `var plan = Scanner.PlanIntake(paths, _ctx);` line:

```csharp
// Pre-check 1: save/world-mod drops. Routes detected zips to SaveModFlow and carves them
// out so the regular intake doesn't try to classify their non-pak contents.
var remaining = paths.ToList();
var savedCount = 0;
var saveSkipReasons = new List<string>();
if (!string.IsNullOrEmpty(_ctx.SaveDir))
{
    var saveTypeExts = GameProfiles.Resolve(_ctx.Game.Engine, _ctx.Game.SteamAppId)
        .SaveTypes.Select(t => t.Extension).ToList();
    var verdicts = SaveModFlow.TryHandleDrops(
        remaining, saveTypeExts,
        saveProfilesDir: _ctx.SaveDir!,
        snapshotsDir: _ctx.SavesDir,
        dataDir: _ctx.DataDir,
        saveModPath: _ctx.Game.SaveModPath,
        forbidden: _ctx.Game.SaveModForbidden);
    foreach (var v in verdicts)
    {
        if (v.Outcome == SaveModDropOutcome.Installed) { savedCount++; remaining.Remove(v.SourcePath); }
        else if (v.Outcome == SaveModDropOutcome.Failed)
        { saveSkipReasons.Add($"{Path.GetFileName(v.SourcePath)}: {v.Reason}"); remaining.Remove(v.SourcePath); }
    }
}

// Pre-check 2: UE4SS Lua-mod drops. Detection only - ue4ss\Mods is typically owned by Vortex,
// so we surface a clear message instead of writing. Carves the matched archives out so the
// regular intake doesn't silently skip every Lua/.dll entry.
var luaDetected = new List<string>();
remaining = remaining.Where(p =>
{
    if (string.IsNullOrEmpty(p) || !File.Exists(p)) return true;
    var lower = p.ToLowerInvariant();
    if (!Intake.ArchiveExtensions.Any(a => lower.EndsWith(a))) return true;
    try
    {
        using var arch = Archive.Open(p);
        var v = Ue4ssLuaDetect.Detect(arch.EntryNames);
        if (v.IsLuaMod) { luaDetected.Add(v.ModFolderName ?? Path.GetFileNameWithoutExtension(p)); return false; }
        return true;
    }
    catch { return true; }
}).ToList();
```

After the pre-checks, run the existing `Scanner.PlanIntake(remaining, _ctx)` (note: `remaining` not `paths`). At the end of the existing try-block, append the pre-check outcomes to the status:

Find:
```csharp
StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
    + (identified > 0 ? $", identified {identified} on CurseForge" : "")
    + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "");
```

Replace with:
```csharp
var statusParts = new List<string>();
if (savedCount > 0) statusParts.Add($"installed {savedCount} save-mod world{(savedCount == 1 ? "" : "s")}");
foreach (var reason in saveSkipReasons) statusParts.Add(reason);
if (luaDetected.Count > 0)
    statusParts.Add($"{string.Join(", ", luaDetected)} {(luaDetected.Count == 1 ? "looks like a" : "look like")} UE4SS Lua mod{(luaDetected.Count == 1 ? "" : "s")} — install via Vortex (ue4ss\\Mods is managed)");
statusParts.Add($"updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}");
StatusText = string.Join(". ", statusParts)
    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
    + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
    + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "");
```

Also change `var plan = Scanner.PlanIntake(paths, _ctx);` to `var plan = Scanner.PlanIntake(remaining, _ctx);`.

- [ ] **Step 2: Add the using imports needed**

At the top of MainViewModel.cs, ensure `using System.IO;` is present (for `File.Exists` / `Path`). Already imported in nearly every file in the project — verify.

- [ ] **Step 3: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: ALL PASS (no regressions).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat: save-mod auto-install + UE4SS Lua detection on drop"
```

---

## Task 4: SavesDialog — installed save mods section

**Files:**
- Modify: `src/ModManager.App/SavesDialog.xaml`
- Modify: `src/ModManager.App/SavesDialog.xaml.cs`

- [ ] **Step 1: Add the XAML section between "Save files" and "Snapshots"**

Insert immediately AFTER the `<TextBlock x:Name="SaveFilesEmpty" .../>` and BEFORE `<TextBlock Text="Snapshots" FontWeight="SemiBold" />`:

```xml
<TextBlock Text="Installed save mods" FontWeight="SemiBold" />
<ListView x:Name="SaveModList" MaxHeight="200" SelectionMode="None">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="local:SaveModRow">
            <Grid ColumnSpacing="8" Padding="0,6">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" VerticalAlignment="Center">
                    <TextBlock Text="{x:Bind Title}" FontWeight="SemiBold" />
                    <TextBlock Text="{x:Bind Detail}" Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
                </StackPanel>
                <Button Grid.Column="1" Content="Reset" Click="OnSaveModReset" VerticalAlignment="Center"
                        ToolTipService.ToolTip="Re-install from the kept source zip - snapshots first" />
                <Button Grid.Column="2" Click="OnSaveModRemove" VerticalAlignment="Center"
                        Background="Transparent" BorderThickness="0"
                        ToolTipService.ToolTip="Remove from the save tree - snapshots first">
                    <FontIcon Glyph="&#xE74D;" FontSize="14" Foreground="{StaticResource ThemeDanger}" />
                </Button>
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
<TextBlock x:Name="SaveModEmpty" Text="No save mods installed. Drop a world zip to install." Opacity="0.6" FontSize="12" Visibility="Collapsed" />
```

- [ ] **Step 2: Add the SaveModRow record + handlers in code-behind**

Add the record next to the existing `SaveRow` / `SaveFileRow` records:

```csharp
/// <summary>One installed-save-mod row: title + when/source.</summary>
public sealed record SaveModRow(SaveModEntry Entry, string Title, string Detail);
```

Add `_dataDir` field next to `_savesDir`:

```csharp
private readonly string _dataDir;
```

In the constructor, set it from ctx:

```csharp
_dataDir = ctx.DataDir;
```

And add `RefreshSaveMods();` right after `RefreshSaveFiles();`.

Add the methods inside the class:

```csharp
private void RefreshSaveMods()
{
    var rows = SaveModStore.Load(_dataDir)
        .Select(e => new SaveModRow(e, e.Name,
            $"{e.InstalledUtc.ToLocalTime():g}  ·  world {Short(e.Guid)}  ·  {System.IO.Path.GetFileName(e.SourceZip)}"))
        .OrderByDescending(r => r.Entry.InstalledUtc)
        .ToList();
    SaveModList.ItemsSource = rows;
    SaveModEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

private void OnSaveModReset(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.DataContext is not SaveModRow row) return;
    if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
    try
    {
        SaveModInstaller.ResetWorld(_saveDir, _savesDir, row.Entry.SourceZip,
            row.Entry.Guid, /* saveModPath */ null, /* forbidden */ null);
        StatusText.Text = $"Reset {row.Entry.Name} — previous state snapshotted first.";
        Refresh();
    }
    catch (Exception ex) { StatusText.Text = ex.Message; }
}

private void OnSaveModRemove(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.DataContext is not SaveModRow row) return;
    if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
    try
    {
        SaveModInstaller.RemoveWorld(_saveDir, _savesDir, row.Entry.Guid,
            /* saveModPath */ null, /* forbidden */ null);
        SaveModStore.Remove(_dataDir, row.Entry.Guid);
        StatusText.Text = $"Removed {row.Entry.Name} — previous state snapshotted first.";
        Refresh();
        RefreshSaveMods();
    }
    catch (Exception ex) { StatusText.Text = ex.Message; }
}

private static string Short(string g) => g.Length <= 8 ? g : g[..8] + "…";
```

Note: passing `_saveDir` (the save profiles dir set by the user) to `ResetWorld`/`RemoveWorld` — `SaveModInstaller` then walks `<profile>/RocksDB/<version>/Worlds/<guid>`. The `saveModPath` is left null here (default RocksDB layout); a per-game override could be added later by reading `ctx.Game.SaveModPath` into the dialog.

Actually — fix that: thread `_saveModPath` + `_saveModForbidden` through the dialog same way `_saveTypes` is threaded, so Reset/Remove honor the game's profile.

Replace the two reset/remove `null` args with `_saveModPath` / `_saveModForbidden`, and add the fields:

```csharp
private readonly string? _saveModPath;
private readonly IReadOnlyList<string>? _saveModForbidden;
```

Initialize from ctx:

```csharp
_saveModPath = ctx.Game.SaveModPath;
_saveModForbidden = ctx.Game.SaveModForbidden;
```

- [ ] **Step 3: Build + run tests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: ALL PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/SavesDialog.xaml src/ModManager.App/SavesDialog.xaml.cs
git commit -m "feat: SavesDialog 'Installed save mods' section with Reset and Remove"
```

---

## Self-Review Notes

- **Spec coverage:** Bug 1 covered by Tasks 2 (SaveModFlow), 3 (VM wiring), 4 (SavesDialog UI). Bug 2 covered by Tasks 1 (Ue4ssLuaDetect) + 3 (VM status). The two scope confirmations from the user (refuse-with-message for Lua, list+Reset+Remove for save mods) are both honored.
- **Owned-folder invariant respected:** No new write paths to `ue4ss\Mods`. Save-mod writes go through `SaveModInstaller`, which already enforces the `RocksDB_v2*` forbidden-segment guards and snapshots-first.
- **Existing intake untouched:** `Scanner.PlanIntake` / `ExecuteIntake` are unchanged; both pre-checks run BEFORE intake and carve their handled archives out of the list. Loose-file drops are unaffected.
- **Reversibility:** Save-mod install snapshots first (existing). Reset/Remove snapshot first (existing). Status surfaces every outcome — no silent skips on drop.
- **Test discipline:** 9 new Core tests (5 Ue4ssLuaDetect + 4 SaveModFlow). VM/UI changes are integration glue over tested cores — consistent with the codebase's pattern of not unit-testing WinUI dialogs.
