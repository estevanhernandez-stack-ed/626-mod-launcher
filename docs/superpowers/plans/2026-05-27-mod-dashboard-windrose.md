# Mod Dashboard (Windrose-first) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build (App): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL: `Get-Process -Name ModManager.App,testhost -ErrorAction SilentlyContinue | Stop-Process -Force`.

**Goal:** Ship a per-game **mod dashboard** with drop-zip-installable tools (auto-snapshot for save editors) and an inline INI editor — Windrose as the day-one target game, WSE Save Editor + WSE Save Fix as the day-one catalog entries.

**Architecture:** Pure-core `ModManager.Core.Tools.*` (catalog, detector, registry, intake) + pure-core `ModManager.Core.IniEdit.IniEditService` (snapshot-before-write). Thin WinUI shell in `ModManager.App` for the panel, dialogs, and drop-pipeline routing. State on disk: `_626mods/<game>/tools/` (extracted tool folders), `_626mods/<game>/tools.json` (per-game registry, camelCase), `_626mods/<game>/.ini-history/` (INI undo `.bak` files).

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. Reuses `SharpCompress` (already in Core) for zip extraction and `System.Text.Json` for the registry. No new NuGets.

**Spec:** [`docs/superpowers/specs/2026-05-27-mod-dashboard-windrose-design.md`](../specs/2026-05-27-mod-dashboard-windrose-design.md)

---

## File map

| Path | Created or modified | Responsibility |
|---|---|---|
| `src/ModManager.Core/Tools/ToolEntry.cs` | Create | Pure record — one installed tool |
| `src/ModManager.Core/Tools/ToolCatalog.cs` | Create | Static catalog of known tools + `KnownTool` record |
| `src/ModManager.Core/Tools/ToolDetector.cs` | Create | Pure classifier — Tool / Mod / Ambiguous |
| `src/ModManager.Core/Tools/ToolRegistry.cs` | Create | Per-game JSON persistence (camelCase) |
| `src/ModManager.Core/Tools/ToolIntake.cs` | Create | Extract zip → pick runnable → register |
| `src/ModManager.Core/IniEdit/IniEditService.cs` | Create | Snapshot-before-write for INI files |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | Modify | Add `Tools` collection + drop pipeline routing |
| `src/ModManager.App/ViewModels/ModRowViewModel.cs` | Modify | Expose INI file list + pencil icon visibility |
| `src/ModManager.App/Tools/ToolsPanel.xaml(.cs)` | Create | Slim row UI control + button rendering |
| `src/ModManager.App/Tools/ToolConfigureDialog.xaml(.cs)` | Create | Right-click config dialog (change runnable, toggle, rename, uninstall) |
| `src/ModManager.App/Tools/ToolLauncher.cs` | Create | Click handler — snapshot + Process.Start + exit detection |
| `src/ModManager.App/IniEdit/IniEditorDialog.xaml(.cs)` | Create | Inline INI text editor dialog |
| `src/ModManager.App/MainWindow.xaml` | Modify | Embed `ToolsPanel` above mod list |
| `src/ModManager.App/SettingsDialog.xaml(.cs)` | Modify | Add "Installed tools" section in About |
| `NOTICE` | Modify | Append WSE Project attribution block |
| `tests/ModManager.Tests/Tools/*Tests.cs` | Create | Unit tests for each core component |
| `tests/ModManager.Tests/IniEdit/IniEditServiceTests.cs` | Create | Unit tests for INI snapshot-before-write |

---

## Task 1: Core — `ToolEntry` record

**Files:**
- Create: `src/ModManager.Core/Tools/ToolEntry.cs`
- Create: `tests/ModManager.Tests/Tools/ToolEntryTests.cs`

Pure data shape. No I/O.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Tools/ToolEntryTests.cs
using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolEntryTests
{
    [Fact]
    public void ToolEntry_carries_id_name_paths_flags_and_source()
    {
        var entry = new ToolEntry(
            ToolId: "wse-save-editor",
            DisplayName: "WSE Save Editor",
            InstallDir: @"C:\_626mods\windrose\tools\wse-save-editor",
            Runnable: "WSE_Save_Editor.exe",
            EditsSaves: true,
            GetUrl: "https://www.nexusmods.com/windrose/mods/153",
            Source: "catalog");

        Assert.Equal("wse-save-editor", entry.ToolId);
        Assert.True(entry.EditsSaves);
        Assert.Equal("catalog", entry.Source);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolEntryTests"`
Expected: FAIL (CS0246 — `ToolEntry` not found).

- [ ] **Step 3: Implement the record**

```csharp
// src/ModManager.Core/Tools/ToolEntry.cs
namespace ModManager.Core.Tools;

/// <summary>
/// One installed third-party tool registered with the launcher. Persisted to
/// <c>_626mods/&lt;game&gt;/tools.json</c> as camelCase JSON. Catalog-recognized tools have
/// <c>Source = "catalog"</c>; heuristically-installed tools have <c>Source = "user"</c>.
/// </summary>
/// <param name="ToolId">Stable id derived from the extracted folder name (kebab-case).</param>
/// <param name="DisplayName">Button label.</param>
/// <param name="InstallDir">Absolute path under <c>_626mods/&lt;game&gt;/tools/&lt;id&gt;/</c>.</param>
/// <param name="Runnable">Relative path inside InstallDir to the launch target (.exe / .bat / .ps1 / .cmd).</param>
/// <param name="EditsSaves">If true, the launcher snapshots the save folder before launching the tool.</param>
/// <param name="GetUrl">Optional "Get it here" link for the catalog chip when the tool is uninstalled.</param>
/// <param name="Source">"catalog" (known tool, pre-filled metadata) or "user" (heuristic install).</param>
public sealed record ToolEntry(
    string ToolId,
    string DisplayName,
    string InstallDir,
    string Runnable,
    bool EditsSaves,
    string? GetUrl,
    string Source);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolEntryTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Tools/ToolEntry.cs tests/ModManager.Tests/Tools/ToolEntryTests.cs
git commit -m "feat(tools): ToolEntry record — installed tool data shape

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: Core — `ToolCatalog` (WSE Save Editor + WSE Save Fix)

**Files:**
- Create: `src/ModManager.Core/Tools/ToolCatalog.cs`
- Create: `tests/ModManager.Tests/Tools/ToolCatalogTests.cs`

Static catalog of known tools, mirroring `FrameworkDeps.Catalog`. The implementer must look up the WSE Save Fix Nexus URL during this task (web search `"WSE Save Fix" site:nexusmods.com` or check RimmyCode's project README). If unfindable, leave it `null` and add a TODO comment with a date for follow-up.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Tools/ToolCatalogTests.cs
using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolCatalogTests
{
    [Fact]
    public void Catalog_has_wse_save_editor_entry_for_windrose()
    {
        var entry = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");
        Assert.Equal("WSE Save Editor", entry.DisplayName);
        Assert.Equal("ue-pak", entry.Engine);
        Assert.Equal("3041230", entry.SteamAppId);
        Assert.True(entry.EditsSaves);
        Assert.Equal("https://www.nexusmods.com/windrose/mods/153", entry.GetUrl);
        Assert.Contains("RimmyCode", entry.Author);
    }

    [Fact]
    public void Catalog_has_wse_save_fix_entry_for_windrose()
    {
        var entry = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-fix");
        Assert.Equal("ue-pak", entry.Engine);
        Assert.Equal("3041230", entry.SteamAppId);
        Assert.True(entry.EditsSaves);
        Assert.Contains("RimmyCode", entry.Author);
    }

    [Fact]
    public void Every_entry_has_at_least_one_zip_filename_hint()
    {
        foreach (var entry in ToolCatalog.Catalog)
        {
            Assert.NotEmpty(entry.ZipFilenameHints);
        }
    }

    [Fact]
    public void Every_entry_has_at_least_one_expected_runnable_hint()
    {
        foreach (var entry in ToolCatalog.Catalog)
        {
            Assert.NotEmpty(entry.ExpectedRunnableHints);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolCatalogTests"`
Expected: FAIL (CS0103 — `ToolCatalog` not found).

- [ ] **Step 3: Implement `ToolCatalog.cs`**

```csharp
// src/ModManager.Core/Tools/ToolCatalog.cs
namespace ModManager.Core.Tools;

/// <summary>
/// One catalog-known tool. Distinct from <see cref="ToolEntry"/> — this is the TEMPLATE the
/// detector matches against during drop. When matched, ToolIntake produces a ToolEntry from
/// the KnownTool metadata + the extracted folder.
/// </summary>
/// <param name="ToolId">Stable kebab-case id (matches ToolEntry.ToolId once installed).</param>
/// <param name="DisplayName">Button label.</param>
/// <param name="Engine">Engine the tool applies to (e.g. "ue-pak", "fromsoft").</param>
/// <param name="SteamAppId">Steam App ID the tool applies to (e.g. "3041230" for Windrose).</param>
/// <param name="EditsSaves">If true, launcher snapshots saves before launching the tool.</param>
/// <param name="GetUrl">Nexus / vendor page URL for the "Get it here" chip when uninstalled.</param>
/// <param name="ZipFilenameHints">Case-insensitive substrings that match this tool's zip filename.</param>
/// <param name="ExpectedRunnableHints">Filenames the runnable surfacing should prefer.</param>
/// <param name="Author">Attribution string for honor-the-builders surfaces.</param>
public sealed record KnownTool(
    string ToolId,
    string DisplayName,
    string Engine,
    string SteamAppId,
    bool EditsSaves,
    string? GetUrl,
    IReadOnlyList<string> ZipFilenameHints,
    IReadOnlyList<string> ExpectedRunnableHints,
    string Author);

/// <summary>
/// Static catalog of third-party tools the launcher knows about. Mirrors
/// <c>ModManager.Core.FrameworkDeps.Catalog</c> in shape. Add an entry here when a new tool
/// becomes day-one-supported; the detector and UI pick it up automatically.
///
/// Day-one entries: WSE Save Editor + WSE Save Fix (Windrose, by RimmyCode / WSE Project).
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<KnownTool> Catalog { get; } = new[]
    {
        new KnownTool(
            ToolId: "wse-save-editor",
            DisplayName: "WSE Save Editor",
            Engine: "ue-pak",
            SteamAppId: "3041230",
            EditsSaves: true,
            GetUrl: "https://www.nexusmods.com/windrose/mods/153",
            ZipFilenameHints: new[]
            {
                "windrose-save-editor",
                "wse-save-editor",
                "wse_save_editor",
                "save editor",
            },
            ExpectedRunnableHints: new[]
            {
                "WSE_Save_Editor.exe",
                "Windrose_Save_Editor.exe",
                "Save_Editor.exe",
            },
            Author: "RimmyCode (WSE Project)"),

        new KnownTool(
            ToolId: "wse-save-fix",
            DisplayName: "WSE Save Fix",
            Engine: "ue-pak",
            SteamAppId: "3041230",
            EditsSaves: true,
            // TODO 2026-06-15: pin the actual Nexus URL — search `"WSE Save Fix" site:nexusmods.com`
            // or check RimmyCode's repo README. Same author as WSE Save Editor.
            GetUrl: null,
            ZipFilenameHints: new[]
            {
                "wse-save-fix",
                "wse_save_fix",
                "save fix",
                "save-fix",
            },
            ExpectedRunnableHints: new[]
            {
                "WSE_Save_Fix.exe",
                "Save_Fix.exe",
            },
            Author: "RimmyCode (WSE Project)"),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolCatalogTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Tools/ToolCatalog.cs tests/ModManager.Tests/Tools/ToolCatalogTests.cs
git commit -m "feat(tools): ToolCatalog — WSE Save Editor + WSE Save Fix day-one entries

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: Core — `ToolDetector.Classify` (catalog match + heuristic + ambiguous)

**Files:**
- Create: `src/ModManager.Core/Tools/ToolDetector.cs`
- Create: `tests/ModManager.Tests/Tools/ToolDetectorTests.cs`

Pure classifier. Given an archive path and a GameContext, returns one of: `Tool` (with optional KnownTool match) or `Mod`. The detector enumerates archive entries via `SharpCompress.Archives.ArchiveFactory.Open(...)`.

**v1 simplification from spec:** the spec lists three outcomes (Tool / Mod / Ambiguous → dialog). v1 is deterministic — when both heuristic-tool AND mod signatures are present, mod-intake wins (safer default since the mod path is mature; tool intake is new). The ambiguous-dialog branch is deferred to v2. A user who experiences a misclassification can uninstall + re-drop with a different filename.

Classification rules (in priority order):

1. **Catalog match (highest confidence):** zip filename contains any of the active-engine catalog entries' `ZipFilenameHints` (case-insensitive) → return `Tool(knownTool)`.
2. **Mod signature wins** if archive contains recognized mod signatures: `.pak` files, `.lua` files under `Scripts/`, `manifest.json` at root with a `name`/`author`/`version` shape, or other engine-specific mod patterns. Return `Mod`.
3. **Heuristic-tool:** archive contains at least one executable (`.exe`, `.bat`, `.ps1`, `.cmd`) at any depth → return `Tool(null)`.
4. **Default Mod** for everything else (docs-only, unknown shapes). Lower-risk default — user re-drops if misclassified.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Tools/ToolDetectorTests.cs
using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolDetectorTests
{
    private static string MakeZip(string filename, params (string path, string content)[] entries)
    {
        var root = TestSupport.TempDir("tooldet-");
        var zipPath = Path.Combine(root, filename);
        using var zip = SharpCompress.Archives.Zip.ZipArchive.Create();
        foreach (var (path, content) in entries)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            zip.AddEntry(path, ms);
        }
        using var fs = File.Create(zipPath);
        zip.SaveTo(fs, new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.Deflate));
        return zipPath;
    }

    [Fact]
    public void Catalog_match_by_zip_filename_returns_known_tool()
    {
        var zip = MakeZip("WSE-Save-Editor-v1.2.zip",
            ("WSE_Save_Editor.exe", "binary"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.NotNull(known);
        Assert.Equal("wse-save-editor", known!.ToolId);
    }

    [Fact]
    public void Heuristic_tool_returns_tool_with_null_known()
    {
        var zip = MakeZip("some-random-utility.zip",
            ("utility.exe", "binary"),
            ("README.md", "docs"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Pak_file_in_archive_returns_mod_even_when_exe_present()
    {
        var zip = MakeZip("mixed-mod.zip",
            ("R5/Content/Paks/~mods/MyMod_P.pak", "binary"),
            ("installer.exe", "binary"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Lua_under_scripts_returns_mod()
    {
        var zip = MakeZip("ue4ss-lua-mod.zip",
            ("R5/Binaries/Win64/ue4ss/Mods/MyMod/Scripts/main.lua", "lua"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Archive_with_only_docs_and_no_exe_returns_mod_default()
    {
        var zip = MakeZip("docs-only.zip",
            ("README.md", "docs"),
            ("LICENSE.txt", "license"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Bat_file_only_returns_tool()
    {
        var zip = MakeZip("script-tool.zip", ("run.bat", "echo hi"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Catalog_match_is_case_insensitive_on_zip_filename()
    {
        var zip = MakeZip("WSE_Save_Editor_1.5.zip",
            ("WSE_Save_Editor.exe", "binary"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Equal("wse-save-editor", known!.ToolId);
    }

    [Fact]
    public void Catalog_entry_for_wrong_engine_does_not_match()
    {
        var zip = MakeZip("WSE-Save-Editor.zip",
            ("WSE_Save_Editor.exe", "binary"));
        // FromSoft game — WSE entries shouldn't match
        var (cls, known) = ToolDetector.Classify(zip, engine: "fromsoft", steamAppId: "1245620");
        // Falls through to heuristic-tool (has .exe)
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Manifest_json_with_mod_shape_returns_mod()
    {
        var zip = MakeZip("smapi-mod.zip",
            ("manifest.json", """{"Name":"MyMod","Author":"someone","Version":"1.0.0"}"""));
        var (cls, _) = ToolDetector.Classify(zip, engine: "smapi", steamAppId: "413150");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Malformed_archive_does_not_throw_returns_mod_default()
    {
        var root = TestSupport.TempDir("tooldet-");
        var bogus = Path.Combine(root, "bogus.zip");
        File.WriteAllText(bogus, "not a zip");
        var (cls, _) = ToolDetector.Classify(bogus, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolDetectorTests"`
Expected: FAIL (CS0103 — `ToolDetector` not found).

- [ ] **Step 3: Implement `ToolDetector.cs`**

```csharp
// src/ModManager.Core/Tools/ToolDetector.cs
using SharpCompress.Archives;

namespace ModManager.Core.Tools;

public enum ToolClassification { Tool, Mod }

/// <summary>
/// Pure classifier — given a dropped archive path + the active game's engine + steamAppId,
/// returns whether the archive should be installed as a tool or routed through the existing
/// mod intake.
/// </summary>
public static class ToolDetector
{
    private static readonly string[] ExecutableExtensions = { ".exe", ".bat", ".ps1", ".cmd" };

    public static (ToolClassification, KnownTool?) Classify(string archivePath, string engine, string steamAppId)
    {
        // 1. Catalog match by zip filename + applicable engine + steamAppId.
        var filename = Path.GetFileName(archivePath).ToLowerInvariant();
        foreach (var known in ToolCatalog.Catalog)
        {
            if (known.Engine != engine || known.SteamAppId != steamAppId) continue;
            foreach (var hint in known.ZipFilenameHints)
            {
                if (filename.Contains(hint.ToLowerInvariant()))
                    return (ToolClassification.Tool, known);
            }
        }

        // 2 + 3. Inspect archive contents — mod signature wins; else heuristic-tool; else mod.
        List<string> paths;
        try
        {
            using var arc = ArchiveFactory.Open(archivePath);
            paths = arc.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => e.Key?.Replace('\\', '/') ?? "")
                .ToList();
        }
        catch
        {
            // Malformed archive — caller's existing intake will surface the real error.
            return (ToolClassification.Mod, null);
        }

        if (HasModSignature(paths)) return (ToolClassification.Mod, null);
        if (HasExecutable(paths)) return (ToolClassification.Tool, null);
        return (ToolClassification.Mod, null);
    }

    private static bool HasModSignature(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            var lower = p.ToLowerInvariant();
            if (lower.EndsWith(".pak")) return true;
            if (lower.Contains("/scripts/") && lower.EndsWith(".lua")) return true;
            if (lower.EndsWith("/manifest.json") || lower == "manifest.json") return true;
        }
        return false;
    }

    private static bool HasExecutable(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (Array.IndexOf(ExecutableExtensions, ext) >= 0) return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolDetectorTests"`
Expected: PASS (10/10).

- [ ] **Step 5: Run full suite to confirm no regressions**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green (~700+ passed, 2 known skips).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Tools/ToolDetector.cs tests/ModManager.Tests/Tools/ToolDetectorTests.cs
git commit -m "feat(tools): ToolDetector.Classify — catalog + heuristic drop classification

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: Core — `ToolRegistry` (per-game JSON persistence)

**Files:**
- Create: `src/ModManager.Core/Tools/ToolRegistry.cs`
- Create: `tests/ModManager.Tests/Tools/ToolRegistryTests.cs`

Per-game JSON file at `_626mods/<game>/tools.json`. camelCase per the shared-json convention. Atomic writes via `File.WriteAllText` to a `.tmp` + `File.Move` (overwrite: true).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Tools/ToolRegistryTests.cs
using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var reg = ToolRegistry.Load(dir);
        Assert.Empty(reg.Tools);
    }

    [Fact]
    public void Save_then_load_round_trips_entries()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var entries = new[]
        {
            new ToolEntry("wse-save-editor", "WSE Save Editor",
                @"C:\tools\wse-save-editor", "WSE_Save_Editor.exe",
                EditsSaves: true,
                GetUrl: "https://www.nexusmods.com/windrose/mods/153",
                Source: "catalog"),
        };
        ToolRegistry.Save(dir, entries);

        var reg = ToolRegistry.Load(dir);
        Assert.Single(reg.Tools);
        Assert.Equal("wse-save-editor", reg.Tools[0].ToolId);
        Assert.True(reg.Tools[0].EditsSaves);
    }

    [Fact]
    public void Save_writes_camelCase_json()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var entries = new[]
        {
            new ToolEntry("wse-save-editor", "WSE Save Editor", @"C:\tools\wse",
                "WSE_Save_Editor.exe", EditsSaves: true, GetUrl: null, Source: "catalog"),
        };
        ToolRegistry.Save(dir, entries);

        var json = File.ReadAllText(Path.Combine(dir, "tools.json"));
        Assert.Contains("\"toolId\":", json);
        Assert.Contains("\"editsSaves\":", json);
        Assert.DoesNotContain("\"ToolId\":", json);
    }

    [Fact]
    public void Save_is_atomic_no_partial_file_left_on_disk()
    {
        // Save twice; second call must overwrite the first cleanly.
        var dir = TestSupport.TempDir("toolreg-");
        ToolRegistry.Save(dir, new[]
        {
            new ToolEntry("first", "First", "x", "x.exe", false, null, "user"),
        });
        ToolRegistry.Save(dir, new[]
        {
            new ToolEntry("second", "Second", "y", "y.exe", false, null, "user"),
        });

        var reg = ToolRegistry.Load(dir);
        Assert.Single(reg.Tools);
        Assert.Equal("second", reg.Tools[0].ToolId);

        // No leftover .tmp file.
        Assert.False(File.Exists(Path.Combine(dir, "tools.json.tmp")));
    }

    [Fact]
    public void Load_throws_InvalidDataException_on_malformed_json()
    {
        var dir = TestSupport.TempDir("toolreg-");
        File.WriteAllText(Path.Combine(dir, "tools.json"), "not json");
        Assert.Throws<InvalidDataException>(() => ToolRegistry.Load(dir));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolRegistryTests"`
Expected: FAIL (CS0103 — `ToolRegistry` not found).

- [ ] **Step 3: Implement `ToolRegistry.cs`**

```csharp
// src/ModManager.Core/Tools/ToolRegistry.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModManager.Core.Tools;

/// <summary>The shape of <c>tools.json</c>. camelCase per the shared-json convention.</summary>
public sealed record ToolRegistryFile(IReadOnlyList<ToolEntry> Tools);

/// <summary>
/// Per-game tools.json persistence at <c>&lt;gameDataDir&gt;/tools.json</c>. The "gameDataDir"
/// is the launcher's per-game folder (e.g. <c>_626mods/windrose/</c>). The caller resolves it
/// from the GameContext.
/// </summary>
public static class ToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ToolRegistryFile Load(string gameDataDir)
    {
        var path = Path.Combine(gameDataDir, "tools.json");
        if (!File.Exists(path)) return new ToolRegistryFile(Array.Empty<ToolEntry>());

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) { throw new InvalidDataException($"Couldn't read tools.json: {e.Message}", e); }

        try
        {
            return JsonSerializer.Deserialize<ToolRegistryFile>(text, JsonOptions)
                ?? new ToolRegistryFile(Array.Empty<ToolEntry>());
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"tools.json is malformed: {e.Message}", e);
        }
    }

    public static void Save(string gameDataDir, IReadOnlyList<ToolEntry> tools)
    {
        Directory.CreateDirectory(gameDataDir);
        var path = Path.Combine(gameDataDir, "tools.json");
        var tmp = path + ".tmp";

        var json = JsonSerializer.Serialize(new ToolRegistryFile(tools), JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolRegistryTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Tools/ToolRegistry.cs tests/ModManager.Tests/Tools/ToolRegistryTests.cs
git commit -m "feat(tools): ToolRegistry — per-game camelCase JSON load+save

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: Core — `ToolIntake.Install` (extract + pick runnable + register)

**Files:**
- Create: `src/ModManager.Core/Tools/ToolIntake.cs`
- Create: `tests/ModManager.Tests/Tools/ToolIntakeTests.cs`

Extracts a zip to `<gameDataDir>/tools/<tool-id>/`, picks the runnable (catalog hint > heuristic), and writes the registry. Returns the resulting `ToolEntry`. When the runnable can't be picked deterministically (multiple legitimate runnables, no catalog match), returns the entry with `Runnable = ""` and a list of candidate paths the caller can pass to the install dialog for user choice.

Runnable picker priority:
1. Catalog entry's `ExpectedRunnableHints` — first hint that exists in the extracted folder wins.
2. Single `.exe` in the extracted folder → use it.
3. Multiple `.exe`s, one filename matches the zip name OR the `DisplayName` (case-insensitive) → use it.
4. Filter out filenames matching `install`, `setup`, `update`, `dep` (case-insensitive substring).
5. Multiple legit runnables remain → return empty `Runnable` + candidates list.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Tools/ToolIntakeTests.cs
using ModManager.Core.Tools;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace ModManager.Tests.Tools;

public class ToolIntakeTests
{
    private static string MakeZip(string filename, params (string path, string content)[] entries)
    {
        var root = TestSupport.TempDir("toolintake-");
        var zipPath = Path.Combine(root, filename);
        using var zip = ZipArchive.Create();
        foreach (var (path, content) in entries)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            zip.AddEntry(path, ms);
        }
        using var fs = File.Create(zipPath);
        zip.SaveTo(fs, new WriterOptions(CompressionType.Deflate));
        return zipPath;
    }

    [Fact]
    public void Install_extracts_zip_and_registers_entry()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor-v1.zip",
            ("WSE_Save_Editor.exe", "binary"),
            ("README.md", "docs"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.NotNull(result.Entry);
        Assert.Equal("wse-save-editor", result.Entry!.ToolId);
        Assert.Equal("WSE_Save_Editor.exe", result.Entry.Runnable);
        Assert.True(File.Exists(Path.Combine(result.Entry.InstallDir, "WSE_Save_Editor.exe")));

        var reg = ToolRegistry.Load(dataDir);
        Assert.Single(reg.Tools);
    }

    [Fact]
    public void Install_picks_catalog_hint_when_multiple_exe_present()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip",
            ("WSE_Save_Editor.exe", "binary"),
            ("setup.exe", "binary"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.Equal("WSE_Save_Editor.exe", result.Entry!.Runnable);
    }

    [Fact]
    public void Install_picks_single_exe_when_no_catalog_match()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("random-tool.zip",
            ("Cool_Utility.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("Cool_Utility.exe", result.Entry!.Runnable);
        Assert.Equal("user", result.Entry.Source);
    }

    [Fact]
    public void Install_returns_candidates_when_ambiguous()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("multi-tool.zip",
            ("MainEditor.exe", "binary"),
            ("ItemBrowser.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("", result.Entry!.Runnable);
        Assert.Contains("MainEditor.exe", result.Candidates);
        Assert.Contains("ItemBrowser.exe", result.Candidates);
    }

    [Fact]
    public void Install_filters_install_setup_update_patterns_from_candidates()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("toolwithsetup.zip",
            ("MainTool.exe", "binary"),
            ("install_dependencies.bat", "binary"),
            ("update_assets.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("MainTool.exe", result.Entry!.Runnable);
    }

    [Fact]
    public void Install_default_source_is_catalog_when_known_tool_provided()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip", ("WSE_Save_Editor.exe", "x"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.Equal("catalog", result.Entry!.Source);
        Assert.True(result.Entry.EditsSaves);
        Assert.Equal("https://www.nexusmods.com/windrose/mods/153", result.Entry.GetUrl);
    }

    [Fact]
    public void Install_creates_install_dir_under_gameDataDir_tools_toolid()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip", ("WSE_Save_Editor.exe", "x"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        var expected = Path.Combine(dataDir, "tools", "wse-save-editor");
        Assert.Equal(expected, result.Entry!.InstallDir);
        Assert.True(Directory.Exists(expected));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolIntakeTests"`
Expected: FAIL (CS0103 — `ToolIntake` not found).

- [ ] **Step 3: Implement `ToolIntake.cs`**

```csharp
// src/ModManager.Core/Tools/ToolIntake.cs
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModManager.Core.Tools;

/// <summary>Result of <see cref="ToolIntake.Install"/>. When <c>Entry.Runnable</c> is empty,
/// the caller should show a "pick the main launcher" dialog using <c>Candidates</c>.</summary>
public sealed record ToolInstallResult(ToolEntry Entry, IReadOnlyList<string> Candidates);

/// <summary>
/// Extracts a tool archive to <c>&lt;gameDataDir&gt;/tools/&lt;tool-id&gt;/</c>, picks the
/// launch runnable, and writes the registry. Pure (no UI dependency) — the App layer wraps
/// this in the drop flow.
/// </summary>
public static class ToolIntake
{
    private static readonly string[] ExecutableExtensions = { ".exe", ".bat", ".ps1", ".cmd" };
    private static readonly string[] InstallerFilterSubstrings =
        { "install", "setup", "update", "dep" };

    public static ToolInstallResult Install(string archivePath, string gameDataDir, KnownTool? knownTool)
    {
        var toolId = knownTool?.ToolId ?? SlugFromArchiveName(archivePath);
        var displayName = knownTool?.DisplayName ?? PrettyFromSlug(toolId);
        var installDir = Path.Combine(gameDataDir, "tools", toolId);

        Directory.CreateDirectory(installDir);
        ExtractAll(archivePath, installDir);

        var runnables = FindRunnables(installDir);
        var (pickedRunnable, candidates) = PickRunnable(runnables, installDir, knownTool, archivePath);

        var entry = new ToolEntry(
            ToolId: toolId,
            DisplayName: displayName,
            InstallDir: installDir,
            Runnable: pickedRunnable,
            EditsSaves: knownTool?.EditsSaves ?? false,
            GetUrl: knownTool?.GetUrl,
            Source: knownTool is null ? "user" : "catalog");

        // Update registry — replace any prior entry with the same ToolId.
        var existing = ToolRegistry.Load(gameDataDir).Tools;
        var next = existing.Where(t => t.ToolId != toolId).Append(entry).ToList();
        ToolRegistry.Save(gameDataDir, next);

        return new ToolInstallResult(entry, candidates);
    }

    private static void ExtractAll(string archivePath, string targetDir)
    {
        using var arc = ArchiveFactory.Open(archivePath);
        foreach (var entry in arc.Entries.Where(e => !e.IsDirectory))
        {
            entry.WriteToDirectory(targetDir, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
        }
    }

    private static List<string> FindRunnables(string installDir)
    {
        return Directory.EnumerateFiles(installDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Array.IndexOf(ExecutableExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0)
            .Select(f => Path.GetRelativePath(installDir, f).Replace('\\', '/'))
            .ToList();
    }

    private static (string runnable, IReadOnlyList<string> candidates) PickRunnable(
        IReadOnlyList<string> runnables,
        string installDir,
        KnownTool? known,
        string archivePath)
    {
        // 1. Catalog hint match (case-insensitive on filename only).
        if (known is not null)
        {
            foreach (var hint in known.ExpectedRunnableHints)
            {
                var hit = runnables.FirstOrDefault(r =>
                    string.Equals(Path.GetFileName(r), hint, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return (hit, Array.Empty<string>());
            }
        }

        // 2. Filter installer/setup patterns out of the candidate set.
        var filtered = runnables
            .Where(r => !InstallerFilterSubstrings.Any(s =>
                Path.GetFileName(r).Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (filtered.Count == 1) return (filtered[0], Array.Empty<string>());

        // 3. Multiple .exes — one matches zip name or display name?
        var zipNameStem = Path.GetFileNameWithoutExtension(archivePath).ToLowerInvariant();
        var displayNameStem = known?.DisplayName?.Replace(" ", "_").ToLowerInvariant() ?? "";

        var nameMatch = filtered.FirstOrDefault(r =>
        {
            var stem = Path.GetFileNameWithoutExtension(r).ToLowerInvariant();
            return stem == zipNameStem
                || (!string.IsNullOrEmpty(displayNameStem) && stem == displayNameStem);
        });
        if (nameMatch is not null) return (nameMatch, Array.Empty<string>());

        // 4. Ambiguous — caller asks user.
        return ("", filtered);
    }

    private static string SlugFromArchiveName(string archivePath)
    {
        var stem = Path.GetFileNameWithoutExtension(archivePath).ToLowerInvariant();
        // kebab-case: replace whitespace + underscores + dots with hyphens; strip non-alphanumeric.
        var chars = stem.Select(c => char.IsLetterOrDigit(c) ? c : '-');
        var slug = new string(chars.ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static string PrettyFromSlug(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ToolIntakeTests"`
Expected: PASS (7/7).

- [ ] **Step 5: Run full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Tools/ToolIntake.cs tests/ModManager.Tests/Tools/ToolIntakeTests.cs
git commit -m "feat(tools): ToolIntake.Install — extract + pick runnable + register

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 6: Core — `IniEditService.SaveWithBackup` (snapshot-before-write)

**Files:**
- Create: `src/ModManager.Core/IniEdit/IniEditService.cs`
- Create: `tests/ModManager.Tests/IniEdit/IniEditServiceTests.cs`

Snapshot-before-write for INI files. The contract is identical to the FromSoft save editor's snapshot-first law: if the snapshot copy fails, the new write never happens.

Backup path layout: `<gameDataDir>/.ini-history/<modId>/<iniRelativePath>.<unixTimestamp>.bak`

Backup retention (v1): keep the last 10 `.bak` files per INI. Older ones are pruned synchronously after a successful save.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/IniEdit/IniEditServiceTests.cs
using ModManager.Core.IniEdit;

namespace ModManager.Tests.IniEdit;

public class IniEditServiceTests
{
    [Fact]
    public void SaveWithBackup_writes_new_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod");

        Assert.Equal("new=2", File.ReadAllText(iniPath));
    }

    [Fact]
    public void SaveWithBackup_creates_bak_with_previous_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod");

        var histDir = Path.Combine(gameDir, ".ini-history", "MyMod");
        var baks = Directory.GetFiles(histDir, "*.bak");
        Assert.Single(baks);
        Assert.Equal("old=1", File.ReadAllText(baks[0]));
    }

    [Fact]
    public void SaveWithBackup_aborts_when_backup_dir_unwritable()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "mods", "MyMod", "config.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "old=1");

        // Block the history dir by creating a FILE where the directory should land.
        var histDir = Path.Combine(gameDir, ".ini-history");
        File.WriteAllText(histDir, "blocker");

        Assert.Throws<IOException>(() =>
            IniEditService.SaveWithBackup(iniPath, "new=2", gameDir, "MyMod"));

        // Original file UNCHANGED — snapshot failure aborts before any write.
        Assert.Equal("old=1", File.ReadAllText(iniPath));
    }

    [Fact]
    public void SaveWithBackup_keeps_last_10_baks_prunes_older()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "initial");

        for (int i = 0; i < 15; i++)
        {
            IniEditService.SaveWithBackup(iniPath, $"v{i}", gameDir, "MyMod");
            Thread.Sleep(5); // ensure unique timestamps
        }

        var histDir = Path.Combine(gameDir, ".ini-history", "MyMod");
        var baks = Directory.GetFiles(histDir, "*.bak");
        Assert.Equal(10, baks.Length);
    }

    [Fact]
    public void RestorePrevious_returns_most_recent_bak_contents()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "v1");

        IniEditService.SaveWithBackup(iniPath, "v2", gameDir, "MyMod");
        Thread.Sleep(5);
        IniEditService.SaveWithBackup(iniPath, "v3", gameDir, "MyMod");

        var previous = IniEditService.RestorePrevious(iniPath, gameDir, "MyMod");
        Assert.Equal("v2", previous);
    }

    [Fact]
    public void RestorePrevious_returns_null_when_no_bak_history()
    {
        var gameDir = TestSupport.TempDir("ini-");
        var iniPath = Path.Combine(gameDir, "config.ini");
        File.WriteAllText(iniPath, "current");

        var previous = IniEditService.RestorePrevious(iniPath, gameDir, "MyMod");
        Assert.Null(previous);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IniEditServiceTests"`
Expected: FAIL (CS0103 — `IniEditService` not found).

- [ ] **Step 3: Implement `IniEditService.cs`**

```csharp
// src/ModManager.Core/IniEdit/IniEditService.cs
namespace ModManager.Core.IniEdit;

/// <summary>
/// Snapshot-before-write for INI files. Mirrors the FromSoft save editor's safety law: if the
/// backup copy fails, the new write never happens.
///
/// Backup path: <c>&lt;gameDataDir&gt;/.ini-history/&lt;modId&gt;/&lt;iniName&gt;.&lt;timestamp&gt;.bak</c>.
/// Retention: keep the last 10 per INI; older ones pruned synchronously after a successful save.
/// </summary>
public static class IniEditService
{
    public const int MaxBackupsPerFile = 10;

    public static void SaveWithBackup(string iniPath, string newContents, string gameDataDir, string modId)
    {
        var bakDir = Path.Combine(gameDataDir, ".ini-history", modId);
        Directory.CreateDirectory(bakDir);

        var iniName = Path.GetFileName(iniPath);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bakPath = Path.Combine(bakDir, $"{iniName}.{timestamp}.bak");

        // Snapshot the current contents first. Atomic — temp + rename.
        var currentContents = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";
        var bakTmp = bakPath + ".tmp";
        File.WriteAllText(bakTmp, currentContents);
        File.Move(bakTmp, bakPath, overwrite: true);

        // Write new contents.
        var iniTmp = iniPath + ".tmp";
        File.WriteAllText(iniTmp, newContents);
        File.Move(iniTmp, iniPath, overwrite: true);

        // Prune older backups.
        var allBaks = Directory.GetFiles(bakDir, $"{iniName}.*.bak")
            .OrderByDescending(f => f)
            .Skip(MaxBackupsPerFile)
            .ToList();
        foreach (var stale in allBaks)
        {
            try { File.Delete(stale); } catch { /* swallow — retention is best-effort */ }
        }
    }

    public static string? RestorePrevious(string iniPath, string gameDataDir, string modId)
    {
        var bakDir = Path.Combine(gameDataDir, ".ini-history", modId);
        if (!Directory.Exists(bakDir)) return null;

        var iniName = Path.GetFileName(iniPath);
        var mostRecent = Directory.GetFiles(bakDir, $"{iniName}.*.bak")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        return mostRecent is null ? null : File.ReadAllText(mostRecent);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~IniEditServiceTests"`
Expected: PASS (6/6).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/IniEdit/IniEditService.cs tests/ModManager.Tests/IniEdit/IniEditServiceTests.cs
git commit -m "feat(ini-edit): IniEditService.SaveWithBackup + RestorePrevious

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 7: App — drop pipeline routing through `ToolDetector`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

Add tool classification + routing to `AddModsAsync`. Before any mod-intake call, run `ToolDetector.Classify` per dropped archive. If Tool: call `ToolIntake.Install`, then refresh the tools collection (added in Task 8) and append a toast line. If Mod: existing flow unchanged.

The Core change Task 8 will add to `MainViewModel`: an `ObservableCollection<ToolEntry> Tools` property. For Task 7, the routing logic writes through `ToolRegistry.Save` and the collection refresh comes in Task 8.

- [ ] **Step 1: Read existing `AddModsAsync` to find the right insertion point**

Open `src/ModManager.App/ViewModels/MainViewModel.cs`, find `public async Task AddModsAsync(IReadOnlyList<string> paths)`. The flow today: per-archive loop → `Scanner.PlanIntake` → user-confirms-overwrite → `Scanner.ExecuteIntake`. Tool routing inserts BEFORE `Scanner.PlanIntake` per archive.

- [ ] **Step 2: Add the `using` for the Tools namespace**

In the `using` block at the top of `MainViewModel.cs`, add:

```csharp
using ModManager.Core.Tools;
```

- [ ] **Step 3: Add the tool-routing branch inside the per-archive loop**

Inside `AddModsAsync`, before the existing `Scanner.PlanIntake` call (or whatever code currently classifies each archive), insert:

```csharp
// Tool classification — runs before mod intake. Only routes when a game is registered.
if (_ctx is not null)
{
    var (cls, known) = ToolDetector.Classify(archivePath, _ctx.Game.Engine ?? "", _ctx.Game.SteamAppId ?? "");
    if (cls == ToolClassification.Tool)
    {
        var dataDir = ResolveGameDataDir(_ctx); // helper added below
        try
        {
            var result = ToolIntake.Install(archivePath, dataDir, known);
            installedTools.Add(result.Entry);
            ambiguousRunnables[result.Entry.ToolId] = result.Candidates;
            continue; // skip the mod-intake branch for this archive
        }
        catch (Exception ex)
        {
            statusParts.Add($"Tool install failed: {ex.Message}");
            continue;
        }
    }
}
```

Add these local variables at the top of `AddModsAsync` (above the loop):

```csharp
var installedTools = new List<ToolEntry>();
var ambiguousRunnables = new Dictionary<string, IReadOnlyList<string>>();
```

After the loop, before `StatusText = ...`, append tool feedback to the status parts:

```csharp
if (installedTools.Count > 0)
{
    foreach (var t in installedTools)
    {
        statusParts.Add($"Installed {t.DisplayName} as a tool for {_ctx?.Game.Name ?? "this game"}");
    }
}
```

- [ ] **Step 4: Add the `ResolveGameDataDir` helper**

Add as a private static method on `MainViewModel`:

```csharp
private static string ResolveGameDataDir(GameContext ctx)
{
    // Per-game data dir under the launcher's _626mods root.
    // Matches the existing pattern: _626mods/<game-id>/
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(appData, "626-mod-launcher", "_626mods", ctx.Game.Id);
}
```

(The exact `_626mods` root location may differ. If the project has a `ModLauncherPaths` helper or similar, use that instead. Search `src/ModManager.App` for an existing path resolver before adding this method.)

- [ ] **Step 5: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. (Pre-existing MVVMTK0045 warnings on existing `[ObservableProperty]` fields are unchanged.)

- [ ] **Step 6: Run the full test suite (verify no regressions)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): drop pipeline routes tools through ToolIntake before mod intake

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 8: App — `ToolsPanel` UI (slim row above mod list)

**Files:**
- Create: `src/ModManager.App/Tools/ToolsPanel.xaml`
- Create: `src/ModManager.App/Tools/ToolsPanel.xaml.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (add `Tools` collection + refresh)
- Modify: `src/ModManager.App/MainWindow.xaml` (embed `ToolsPanel`)

The control renders three states: installed (buttons), empty (placeholder text), known-but-uninstalled (`[Get …↗]` chips).

For known-but-uninstalled detection: the panel iterates `ToolCatalog.Catalog`, filters by `(engine, steamAppId)` matching the active game, and excludes those already in `MainViewModel.Tools`. Each surviving catalog entry becomes a `[Get …]` chip with `GetUrl` opening in the system browser.

- [ ] **Step 1: Add the `Tools` collection + `MissingTools` derived property on `MainViewModel`**

In `MainViewModel.cs`, near the `MissingFrameworks` collection added in PR #51, add:

```csharp
/// <summary>Tools installed for the active game. Refreshed at every <see cref="ReloadModsAsync"/>.</summary>
public ObservableCollection<ToolEntry> Tools { get; } = new();

/// <summary>Catalog entries that apply to the active game but aren't installed. Surfaced as
/// "Get it here" chips on the tools row.</summary>
public ObservableCollection<KnownTool> MissingTools { get; } = new();

public bool HasTools => Tools.Count > 0;
public bool HasMissingTools => MissingTools.Count > 0;
public bool ToolsRowVisible => _ctx is not null;
```

In `ReloadModsAsync`, after the existing `MissingFrameworks` refresh block (right before the bottom-of-method `OnPropertyChanged` notifies), add:

```csharp
// Refresh tools collection from the per-game registry.
Tools.Clear();
if (_ctx is not null)
{
    var dataDir = ResolveGameDataDir(_ctx);
    try
    {
        foreach (var t in ToolRegistry.Load(dataDir).Tools) Tools.Add(t);
    }
    catch (InvalidDataException) { /* malformed tools.json — surface to status, leave empty */ }
}

// Derive missing-tools: catalog entries that apply but aren't installed.
MissingTools.Clear();
if (_ctx is not null)
{
    var installedIds = new HashSet<string>(Tools.Select(t => t.ToolId));
    foreach (var known in ToolCatalog.Catalog)
    {
        if (known.Engine != _ctx.Game.Engine) continue;
        if (known.SteamAppId != _ctx.Game.SteamAppId) continue;
        if (installedIds.Contains(known.ToolId)) continue;
        MissingTools.Add(known);
    }
}

OnPropertyChanged(nameof(HasTools));
OnPropertyChanged(nameof(HasMissingTools));
OnPropertyChanged(nameof(ToolsRowVisible));
```

In the `_ctx is null` early-return block in `ReloadModsAsync` (the same place we clear `MissingFrameworks`), add the parallel `Tools.Clear()` + `MissingTools.Clear()` + `OnPropertyChanged` calls.

- [ ] **Step 2: Create `ToolsPanel.xaml`**

Create `src/ModManager.App/Tools/ToolsPanel.xaml`:

```xml
<UserControl
    x:Class="ModManager.App.Tools.ToolsPanel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:core="using:ModManager.Core.Tools">

    <Border Background="{ThemeResource SubtleFillColorTransparentBrush}"
            BorderThickness="0,0,0,1"
            BorderBrush="{ThemeResource ControlElevationBorderBrush}"
            Padding="12,6">
        <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">

            <!-- Installed tools -->
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Tools, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Orientation="Horizontal" Spacing="6" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="core:ToolEntry">
                        <Button Padding="10,4"
                                ToolTipService.ToolTip="{x:Bind DisplayName}"
                                Click="OnToolClick"
                                CommandParameter="{x:Bind}">
                            <TextBlock Text="{x:Bind DisplayName}" />
                        </Button>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>

            <!-- Missing tools (catalog-known, not installed) -->
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.MissingTools, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Orientation="Horizontal" Spacing="6" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="core:KnownTool">
                        <HyperlinkButton NavigateUri="{x:Bind GetUrl, Converter={StaticResource StringToUriConverter}}"
                                         Padding="10,4">
                            <TextBlock Text="{x:Bind DisplayName, Converter={StaticResource PrefixGetConverter}}" />
                        </HyperlinkButton>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>

            <!-- Add Tool affordance -->
            <Button Padding="8,4" Click="OnAddToolClick">
                <SymbolIcon Symbol="Add" />
            </Button>

            <!-- Empty-state hint -->
            <TextBlock Text="No tools yet. Drop a zip to install, or click +."
                       VerticalAlignment="Center"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       Visibility="{x:Bind EmptyHintVisibility, Mode=OneWay}" />
        </StackPanel>
    </Border>
</UserControl>
```

(If `StringToUriConverter` or `PrefixGetConverter` doesn't exist in the project's resources, define them in the code-behind or add inline `x:Bind` functions returning the right types.)

- [ ] **Step 3: Create `ToolsPanel.xaml.cs`**

Create `src/ModManager.App/Tools/ToolsPanel.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

public sealed partial class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; set; }

    public Visibility EmptyHintVisibility =>
        (ViewModel?.HasTools ?? false) || (ViewModel?.HasMissingTools ?? false)
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is ToolEntry entry)
        {
            // Task 9 wires this up.
            ViewModel?.LaunchToolAsync(entry);
        }
    }

    private void OnAddToolClick(object sender, RoutedEventArgs e)
    {
        // Task 9 wires this up.
        ViewModel?.PromptAddToolAsync();
    }
}
```

- [ ] **Step 4: Embed `ToolsPanel` in `MainWindow.xaml`**

In `src/ModManager.App/MainWindow.xaml`, find the existing main-view layout (the area above the mod list). Insert the panel just above the mod list:

```xml
<tools:ToolsPanel x:Name="ToolsRow"
                  Grid.Row="2"
                  Visibility="{x:Bind ViewModel.ToolsRowVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}" />
```

Add the namespace import at the top of `MainWindow.xaml`:

```xml
xmlns:tools="using:ModManager.App.Tools"
```

In the code-behind (`MainWindow.xaml.cs`), wire the ViewModel reference in `OnLoaded` (or wherever `MainViewModel` is assigned to other panels):

```csharp
ToolsRow.ViewModel = ViewModel;
```

Adjust the existing `Grid.RowDefinitions` if needed to accommodate the new row.

- [ ] **Step 5: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/Tools/ToolsPanel.xaml src/ModManager.App/Tools/ToolsPanel.xaml.cs \
        src/ModManager.App/MainWindow.xaml \
        src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): ToolsPanel slim-row UI above mod list

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 9: App — tool click behavior (snapshot, launch, exit detection)

**Files:**
- Create: `src/ModManager.App/Tools/ToolLauncher.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (add `LaunchToolAsync` + `PromptAddToolAsync`)

`ToolLauncher.LaunchAsync(entry, snapshotCallback, exitCallback)` does the work:
1. If `entry.EditsSaves` → call `snapshotCallback` (the App layer's hook to `SaveManager.Backup`). On failure, throw — the VM surfaces the error toast.
2. Start the process via `Process.Start` with `UseShellExecute = true`. Set `EnableRaisingEvents = true`. Subscribe to `Process.Exited`.
3. Return immediately (fire-and-continue). When `Exited` fires, post `exitCallback` to the UI thread.

- [ ] **Step 1: Create `ToolLauncher.cs`**

```csharp
// src/ModManager.App/Tools/ToolLauncher.cs
using System.Diagnostics;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

public static class ToolLauncher
{
    /// <summary>
    /// Launch a tool. Performs the pre-launch snapshot if <c>entry.EditsSaves</c>, then starts
    /// the process async. Returns when the process has STARTED — not when it exits. Exit
    /// notification fires via <paramref name="onExit"/> on a thread-pool thread; the caller
    /// must marshal to the UI thread.
    /// </summary>
    public static void Launch(
        ToolEntry entry,
        Func<string>? snapshot,           // returns the snapshot label on success; throws on failure
        Action<string?> onExit)           // called with the snapshot label (or null) when the process exits
    {
        string? snapshotLabel = null;
        if (entry.EditsSaves && snapshot is not null)
        {
            snapshotLabel = snapshot(); // may throw — caller catches + surfaces toast
        }

        var exePath = Path.Combine(entry.InstallDir, entry.Runnable);
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"Runnable not found: {exePath}. Use 'Open install folder' to reach the tool manually.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true, // honors .bat / .ps1 / .cmd via shell
            WorkingDirectory = entry.InstallDir,
        };

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exePath}");

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => onExit(snapshotLabel);
    }
}
```

- [ ] **Step 2: Add `LaunchToolAsync` + `PromptAddToolAsync` to `MainViewModel`**

In `MainViewModel.cs`, near the other public async methods:

```csharp
public async Task LaunchToolAsync(ToolEntry entry)
{
    try
    {
        ToolLauncher.Launch(
            entry,
            snapshot: entry.EditsSaves ? () => SnapshotSavesForTool(entry) : null,
            onExit: snapLabel =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    var s = snapLabel is null
                        ? $"{entry.DisplayName} closed."
                        : $"{entry.DisplayName} closed. Snapshot saved as '{snapLabel}'.";
                    StatusText = s;
                });
            });

        StatusText = entry.EditsSaves
            ? $"Snapshotting save before launching {entry.DisplayName}…"
            : $"Launching {entry.DisplayName}…";
    }
    catch (Exception ex)
    {
        StatusText = $"Couldn't launch {entry.DisplayName}: {ex.Message}";
    }
    await Task.CompletedTask;
}

public async Task PromptAddToolAsync()
{
    // Open the file picker; reuse the existing drop pipeline.
    var picker = new Windows.Storage.Pickers.FileOpenPicker();
    picker.FileTypeFilter.Add(".zip");
    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
    var file = await picker.PickSingleFileAsync();
    if (file is not null)
    {
        await AddModsAsync(new[] { file.Path });
    }
}

private string SnapshotSavesForTool(ToolEntry tool)
{
    // Reuse SaveManager.Backup (same primitive as the FromSoft save editor).
    // Resolve the save folder from the active game's profile.
    if (_ctx is null) throw new InvalidOperationException("No active game.");
    var savesDir = ResolveSaveDir(_ctx); // existing helper used by SavesDialog
    var snapshotsDir = ResolveSnapshotsDir(_ctx);
    var label = $"before-{tool.DisplayName.Replace(' ', '-')}-{DateTime.Now:yyyy-MM-dd HH:mm}";
    var snap = SaveManager.Backup(savesDir, snapshotsDir, label, auto: false);
    return snap.Label;
}
```

(The helpers `ResolveSaveDir`, `ResolveSnapshotsDir` already exist in the codebase from the FromSoft save editor work. Verify their signatures and use them as-is.)

- [ ] **Step 3: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/Tools/ToolLauncher.cs \
        src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): ToolLauncher + LaunchToolAsync — snapshot-then-process-start

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 10: App — `ToolConfigureDialog` (change runnable, toggle, rename, uninstall)

**Files:**
- Create: `src/ModManager.App/Tools/ToolConfigureDialog.xaml`
- Create: `src/ModManager.App/Tools/ToolConfigureDialog.xaml.cs`
- Modify: `src/ModManager.App/Tools/ToolsPanel.xaml(.cs)` (add right-click → open dialog)

The dialog shows the current tool's settings + a list of detected runnables (re-scanned from `InstallDir`). User can:
- Change the runnable (pick from the scanned list)
- Toggle `EditsSaves`
- Rename `DisplayName`
- Uninstall (deletes `InstallDir` + removes from registry)
- Open the install folder

- [ ] **Step 1: Create `ToolConfigureDialog.xaml`**

```xml
<ContentDialog
    x:Class="ModManager.App.Tools.ToolConfigureDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Configure tool"
    PrimaryButtonText="Save"
    SecondaryButtonText="Uninstall"
    CloseButtonText="Cancel"
    DefaultButton="Primary">

    <StackPanel Spacing="12" Width="400">
        <TextBox Header="Display name" x:Name="DisplayNameBox" />
        <ComboBox Header="Runnable" x:Name="RunnableBox" />
        <CheckBox Content="Edits saves (snapshot before launch)" x:Name="EditsSavesBox" />
        <HyperlinkButton Content="Open install folder" Click="OnOpenFolderClick" />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Create `ToolConfigureDialog.xaml.cs`**

```csharp
// src/ModManager.App/Tools/ToolConfigureDialog.xaml.cs
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

public sealed partial class ToolConfigureDialog : ContentDialog
{
    private readonly ToolEntry _original;
    private readonly string _gameDataDir;

    public ToolConfigureDialog(ToolEntry tool, string gameDataDir)
    {
        InitializeComponent();
        _original = tool;
        _gameDataDir = gameDataDir;

        DisplayNameBox.Text = tool.DisplayName;
        EditsSavesBox.IsChecked = tool.EditsSaves;

        // Scan the install dir for executables so the user can re-pick.
        if (Directory.Exists(tool.InstallDir))
        {
            var runnables = Directory.EnumerateFiles(tool.InstallDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".exe" or ".bat" or ".ps1" or ".cmd";
                })
                .Select(f => Path.GetRelativePath(tool.InstallDir, f).Replace('\\', '/'))
                .ToList();
            RunnableBox.ItemsSource = runnables;
            RunnableBox.SelectedItem = tool.Runnable;
        }

        // Wire the Uninstall action.
        SecondaryButtonClick += OnUninstallClick;
    }

    public ToolEntry? UpdatedEntry { get; private set; }
    public bool Uninstalled { get; private set; }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _original.InstallDir,
            UseShellExecute = true,
        });
    }

    private void OnUninstallClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            if (Directory.Exists(_original.InstallDir))
                Directory.Delete(_original.InstallDir, recursive: true);

            var registry = ToolRegistry.Load(_gameDataDir);
            var remaining = registry.Tools.Where(t => t.ToolId != _original.ToolId).ToList();
            ToolRegistry.Save(_gameDataDir, remaining);

            Uninstalled = true;
        }
        catch
        {
            args.Cancel = true; // keep the dialog open; caller surfaces the error
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        PrimaryButtonClick += (_, _) =>
        {
            UpdatedEntry = _original with
            {
                DisplayName = DisplayNameBox.Text,
                Runnable = RunnableBox.SelectedItem as string ?? _original.Runnable,
                EditsSaves = EditsSavesBox.IsChecked ?? false,
            };

            // Persist.
            var registry = ToolRegistry.Load(_gameDataDir);
            var updated = registry.Tools
                .Select(t => t.ToolId == _original.ToolId ? UpdatedEntry : t)
                .ToList();
            ToolRegistry.Save(_gameDataDir, updated);
        };
    }
}
```

- [ ] **Step 3: Wire right-click → open dialog in `ToolsPanel`**

In `ToolsPanel.xaml`, change the tool button to handle `RightTapped`:

```xml
<Button Padding="10,4"
        ToolTipService.ToolTip="{x:Bind DisplayName}"
        Click="OnToolClick"
        RightTapped="OnToolRightTapped"
        CommandParameter="{x:Bind}">
    <TextBlock Text="{x:Bind DisplayName}" />
</Button>
```

In `ToolsPanel.xaml.cs`, add:

```csharp
private async void OnToolRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
{
    if (sender is Button btn && btn.CommandParameter is ToolEntry entry && ViewModel is not null)
    {
        var dataDir = ViewModel.ResolveGameDataDirPublic(); // expose via public method if private
        var dialog = new ToolConfigureDialog(entry, dataDir) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.ReloadModsAsync(); // refresh tools from registry
    }
}
```

If `ResolveGameDataDir` is private on `MainViewModel`, add a public wrapper:

```csharp
public string ResolveGameDataDirPublic() =>
    _ctx is null ? "" : ResolveGameDataDir(_ctx);
```

- [ ] **Step 4: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/Tools/ToolConfigureDialog.xaml \
        src/ModManager.App/Tools/ToolConfigureDialog.xaml.cs \
        src/ModManager.App/Tools/ToolsPanel.xaml \
        src/ModManager.App/Tools/ToolsPanel.xaml.cs \
        src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): ToolConfigureDialog — change runnable, toggle, rename, uninstall

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 11: App — mod row INI access + `IniEditorDialog`

**Files:**
- Modify: `src/ModManager.App/ViewModels/ModRowViewModel.cs` (add INI list + pencil icon visibility)
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (populate `IniFiles` at row build time)
- Create: `src/ModManager.App/IniEdit/IniEditorDialog.xaml`
- Create: `src/ModManager.App/IniEdit/IniEditorDialog.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` (pencil icon on row template)

Detection: at row build time in `MainViewModel.ReloadModsAsync`, glob `*.ini` recursively under the mod's resolved folder (cap at 20 hits). Store the list on `ModRowViewModel.IniFiles`. The pencil icon visibility binds to `HasIniFiles`.

Click flow: pencil icon → if `IniFiles.Count == 1`, open editor directly; else open a small picker; either way → `IniEditorDialog` opens with the contents. Save → `IniEditService.SaveWithBackup`. Restore previous → `IniEditService.RestorePrevious`.

- [ ] **Step 1: Add `IniFiles` + derived properties to `ModRowViewModel`**

In `src/ModManager.App/ViewModels/ModRowViewModel.cs`, near the other `init` properties:

```csharp
/// <summary>Absolute paths to .ini files inside this mod's folder. Capped at 20 hits.</summary>
public IReadOnlyList<string> IniFiles { get; init; } = Array.Empty<string>();

public bool HasIniFiles => IniFiles.Count > 0;
public Visibility IniIconVisibility => HasIniFiles ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 2: Populate `IniFiles` in `MainViewModel.ReloadModsAsync`**

Inside the row-build loop, before the `rows.Add(new ModRowViewModel(...))` call:

```csharp
var iniFiles = Array.Empty<string>();
if (Directory.Exists(rep.ModFolderAbs))
{
    try
    {
        iniFiles = Directory.EnumerateFiles(rep.ModFolderAbs, "*.ini", SearchOption.AllDirectories)
            .Take(20)
            .ToArray();
    }
    catch { /* leave empty on enumerate failure */ }
}
```

Add `IniFiles = iniFiles,` to the `new ModRowViewModel(...) { ... }` initializer.

- [ ] **Step 3: Create `IniEditorDialog.xaml`**

```xml
<ContentDialog
    x:Class="ModManager.App.IniEdit.IniEditorDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Edit INI"
    PrimaryButtonText="Save"
    SecondaryButtonText="Restore previous"
    CloseButtonText="Cancel"
    DefaultButton="Primary">

    <StackPanel Spacing="8" Width="600" Height="400">
        <TextBlock x:Name="PathLabel" FontWeight="SemiBold" />
        <TextBox x:Name="ContentsBox"
                 AcceptsReturn="True"
                 TextWrapping="NoWrap"
                 FontFamily="Consolas"
                 ScrollViewer.VerticalScrollBarVisibility="Auto"
                 ScrollViewer.HorizontalScrollBarVisibility="Auto"
                 Height="320" />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 4: Create `IniEditorDialog.xaml.cs`**

```csharp
// src/ModManager.App/IniEdit/IniEditorDialog.xaml.cs
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.IniEdit;

namespace ModManager.App.IniEdit;

public sealed partial class IniEditorDialog : ContentDialog
{
    private readonly string _iniPath;
    private readonly string _gameDataDir;
    private readonly string _modId;

    public IniEditorDialog(string iniPath, string gameDataDir, string modId)
    {
        InitializeComponent();
        _iniPath = iniPath;
        _gameDataDir = gameDataDir;
        _modId = modId;

        PathLabel.Text = iniPath;
        ContentsBox.Text = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";

        PrimaryButtonClick += OnSaveClick;
        SecondaryButtonClick += OnRestoreClick;
    }

    public string? StatusMessage { get; private set; }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            IniEditService.SaveWithBackup(_iniPath, ContentsBox.Text, _gameDataDir, _modId);
            StatusMessage = $"Saved {Path.GetFileName(_iniPath)}. Previous version kept in INI history.";
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            StatusMessage = $"Couldn't save: {ex.Message}";
        }
    }

    private void OnRestoreClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // don't dismiss the dialog
        var previous = IniEditService.RestorePrevious(_iniPath, _gameDataDir, _modId);
        if (previous is null)
        {
            StatusMessage = "No previous version to restore.";
        }
        else
        {
            ContentsBox.Text = previous;
        }
    }
}
```

- [ ] **Step 5: Wire the pencil icon in the mod row XAML**

In `src/ModManager.App/MainWindow.xaml`, find the mod-row `DataTemplate`. Add a pencil-icon button to the chip strip:

```xml
<Button Padding="6,2"
        Visibility="{x:Bind IniIconVisibility, Mode=OneWay}"
        Click="OnEditIniClick"
        ToolTipService.ToolTip="Edit .ini files in this mod"
        CommandParameter="{x:Bind}">
    <SymbolIcon Symbol="Edit" />
</Button>
```

In `MainWindow.xaml.cs`:

```csharp
private async void OnEditIniClick(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.CommandParameter is ModRowViewModel row)
    {
        string? iniPath;
        if (row.IniFiles.Count == 1)
        {
            iniPath = row.IniFiles[0];
        }
        else
        {
            // Tiny picker dialog inline.
            var picker = new ContentDialog
            {
                Title = $"Edit which INI in {row.DisplayName}?",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot,
            };
            var list = new ListView { ItemsSource = row.IniFiles };
            picker.Content = list;
            picker.PrimaryButtonText = "Open";
            picker.IsPrimaryButtonEnabled = false;
            list.SelectionChanged += (_, _) => picker.IsPrimaryButtonEnabled = list.SelectedItem is not null;
            var pickResult = await picker.ShowAsync();
            iniPath = pickResult == ContentDialogResult.Primary ? list.SelectedItem as string : null;
        }
        if (iniPath is null) return;

        var dataDir = ViewModel.ResolveGameDataDirPublic();
        var dialog = new IniEditorDialog(iniPath, dataDir, row.ModId) { XamlRoot = this.Content.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.StatusMessage is not null)
            ViewModel.StatusText = dialog.StatusMessage;
    }
}
```

(`row.ModId` may be exposed as `row.ToolId`-equivalent. If `ModRowViewModel` doesn't already have a `ModId` property, derive it from `row.DisplayName` as a slug, or expose it as an `init` property populated in `ReloadModsAsync`.)

- [ ] **Step 6: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add src/ModManager.App/ViewModels/ModRowViewModel.cs \
        src/ModManager.App/ViewModels/MainViewModel.cs \
        src/ModManager.App/IniEdit/IniEditorDialog.xaml \
        src/ModManager.App/IniEdit/IniEditorDialog.xaml.cs \
        src/ModManager.App/MainWindow.xaml \
        src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat(app): mod row INI access + IniEditorDialog with snapshot-before-save

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 12: Honor-the-builders — NOTICE + Settings → About

**Files:**
- Modify: `NOTICE`
- Modify: `src/ModManager.App/SettingsDialog.xaml(.cs)`

Three surfaces:

1. **Tool button tooltip** — already wired via `ToolTipService.ToolTip="{x:Bind DisplayName}"` in Task 8. Upgrade the tooltip to include the author + "never bundled" disclaimer.
2. **Settings → About** — new "Installed tools" section that lists each tool with author + Nexus link.
3. **NOTICE** — attribution block for catalog tools.

- [ ] **Step 1: Upgrade the tool button tooltip**

In `src/ModManager.App/Tools/ToolsPanel.xaml`, replace the simple `ToolTipService.ToolTip="{x:Bind DisplayName}"` on the tool button with a TextBlock-shaped tooltip:

```xml
<Button.Tooltip>
    <ToolTip>
        <TextBlock>
            <Run Text="{x:Bind DisplayName}" FontWeight="SemiBold" />
            <LineBreak/>
            <Run Text="Catalog metadata only — never bundled." />
            <LineBreak/>
            <Run Text="Click to launch." />
        </TextBlock>
    </ToolTip>
</Button.Tooltip>
```

For catalog-recognized tools, we'd ideally include the author. For v1, the static "Catalog metadata only — never bundled" line is enough — author shows in Settings → About.

- [ ] **Step 2: Append to `NOTICE`**

Append to the existing `NOTICE` file at repo root:

```
================================================================================
WSE Save Editor and WSE Save Fix — by RimmyCode (WSE Project)
================================================================================

The launcher recognizes WSE Save Editor and WSE Save Fix as third-party tools for
the game Windrose. These tools are surfaced as launch buttons in the launcher's
mod dashboard when the user installs them (by dropping their zip into the
launcher).

NEVER BUNDLED. The launcher does not include the WSE Save Editor or WSE Save Fix
source code, binaries, item-ID database, or any of their assets. We carry only:

  - A fingerprint catalog of zip-filename hints (for drop classification)
  - An expected runnable filename hint (for the launch button)
  - The Nexus URL where the user can download the tool themselves

Get WSE Save Editor:  https://www.nexusmods.com/windrose/mods/153
Source:               https://github.com/RimmyCode/Windrose-Save-Editor
License:              Personal use, per the tool's Nexus Mods page terms.

If you are the author of these tools and would like the launcher to stop surfacing
them, open an issue at the launcher repo and we'll remove the catalog entries.
```

- [ ] **Step 3: Add "Installed tools" section to Settings → About**

In `src/ModManager.App/SettingsDialog.xaml`, find the About section. Add:

```xml
<TextBlock Text="Installed tools" FontWeight="SemiBold" Margin="0,16,0,4" />
<ItemsRepeater x:Name="InstalledToolsList">
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="core:ToolEntry">
            <StackPanel Margin="0,2">
                <TextBlock Text="{x:Bind DisplayName}" FontWeight="SemiBold" />
                <TextBlock Text="Catalog metadata only — never bundled. See NOTICE for full attribution."
                           Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                           FontSize="12" />
                <HyperlinkButton Content="Get it on Nexus"
                                 NavigateUri="{x:Bind GetUrl, Converter={StaticResource StringToUriConverter}}"
                                 Visibility="{x:Bind GetUrl, Converter={StaticResource NotNullToVisibilityConverter}}"
                                 Padding="0" />
            </StackPanel>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

In `SettingsDialog.xaml.cs`, populate the list from all per-game registries discovered under the launcher's `_626mods` root:

```csharp
private void RefreshInstalledTools()
{
    var all = new List<ToolEntry>();
    var dataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626-mod-launcher", "_626mods");
    if (Directory.Exists(dataRoot))
    {
        foreach (var gameDir in Directory.EnumerateDirectories(dataRoot))
        {
            try { all.AddRange(ToolRegistry.Load(gameDir).Tools); }
            catch { /* skip malformed */ }
        }
    }
    InstalledToolsList.ItemsSource = all;
}
```

Call `RefreshInstalledTools()` on dialog open.

(If `StringToUriConverter` or `NotNullToVisibilityConverter` doesn't exist in `App.xaml` resources, add them.)

- [ ] **Step 4: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add NOTICE \
        src/ModManager.App/SettingsDialog.xaml \
        src/ModManager.App/SettingsDialog.xaml.cs \
        src/ModManager.App/Tools/ToolsPanel.xaml
git commit -m "docs: attribute WSE Project (RimmyCode) on three surfaces

- NOTICE: attribution block + never-bundled disclaimer
- Settings → About: 'Installed tools' section with Nexus links
- Tool button tooltip: 'Catalog metadata only — never bundled'

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 13: Smoke + PR

**Files:** none — smoke + PR only.

- [ ] **Step 1: Full test suite green**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green, ~25 new tests on top of the prior ~715 baseline.

- [ ] **Step 2: App build clean**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. (Pre-existing MVVMTK0045 warnings unchanged.)

- [ ] **Step 3: Manual smoke — tool install (heuristic)**

1. Launch the app.
2. Switch to Windrose (or any registered game).
3. Drop a tool zip with a single `.exe` and no mod signatures.
4. Verify: toast says "Installed [name] as a tool for [game]." The tools row shows a new button.

- [ ] **Step 4: Manual smoke — tool install (catalog match)**

1. Drop a zip whose filename contains "WSE Save Editor" (rename a test zip to this if needed).
2. Verify: toast confirms it was installed AS the catalog entry (display name = "WSE Save Editor", `EditsSaves: true`).

- [ ] **Step 5: Manual smoke — click a save-editing tool**

1. Click the WSE Save Editor button (or a heuristic tool you've toggled `EditsSaves: true` on).
2. Verify: status reads "Snapshotting save before launching…" → the snapshot lands in the Saves dialog's Snapshots list → tool launches → close the tool → status updates with the snapshot label.

- [ ] **Step 6: Manual smoke — INI editor**

1. Find or fake a mod folder that contains a `.ini` file under the active game.
2. Click the pencil icon on the row.
3. Edit the contents, click Save.
4. Verify: toast confirms save. Open `_626mods/<game>/.ini-history/<modId>/` and confirm the `.bak` file landed.
5. Re-open the INI editor → click "Restore previous" → text reverts in the box → click Save → the original contents land back.

- [ ] **Step 7: Manual smoke — Get-it-here chip**

1. With a clean install (no tools registered), switch to Windrose.
2. Verify: the tools row shows `[Get WSE Save Editor ↗]` and (if Nexus URL is pinned) `[Get WSE Save Fix ↗]` chips.
3. Click one. Verify the Nexus page opens in the browser.

- [ ] **Step 8: Manual smoke — configure dialog**

1. Right-click an installed tool button.
2. Verify the configure dialog opens with the current values.
3. Change the runnable, save, verify the next click launches the new runnable.
4. Re-open and click Uninstall → verify the tool is removed from the row + the install folder is gone.

- [ ] **Step 9: Update `docs/smoke-tests/pending.md`**

Append a new entry to `docs/smoke-tests/pending.md` listing the smoke steps above (so future-Este can re-run them on a clean machine). Commit separately under a `docs:` prefix.

- [ ] **Step 10: Push the branch + open the PR**

Push the feature branch:

```bash
git push -u origin <current-branch>
```

Open the PR via `gh pr create --base master`:

- **Title:** `feat: mod dashboard — tools panel (Windrose) + INI editor`
- **Body** (use a HEREDOC):

```bash
gh pr create --base master --title "feat: mod dashboard — tools panel (Windrose) + INI editor" --body "$(cat <<'EOF'
## Summary

Ships the mod dashboard MVP: a per-game tools panel (drop-zip installable, with auto-snapshot for save editors) plus an inline INI editor with snapshot-before-save.

**Tools panel (slim row above the mod list):**
- Drop a tool zip → smart classifier routes it through ToolIntake (catalog match > heuristic > defaults to mod)
- WSE Save Editor + WSE Save Fix are day-one catalog entries (Windrose)
- Save-editing tools (catalog or user-toggled) snapshot the save folder before launch
- Right-click a button → configure dialog (change runnable, toggle EditsSaves, rename, uninstall)
- Known-but-uninstalled tools show \`[Get … ↗]\` chips (same pattern as PR #51's NEEDS-framework chip)

**INI editor:**
- Mod rows with .ini files show a pencil icon
- Click → inline text editor → save creates a .bak first (snapshot-before-write)
- Restore previous reads the most recent .bak

**Honor-the-builders:**
- NOTICE: WSE Project (RimmyCode) attribution + "never bundled" disclaimer
- Settings → About: 'Installed tools' section with Nexus links
- Tool button tooltip: 'Catalog metadata only — never bundled'

Plan: \`docs/superpowers/plans/2026-05-27-mod-dashboard-windrose.md\`
Spec: \`docs/superpowers/specs/2026-05-27-mod-dashboard-windrose-design.md\`

## Test plan

- [x] \`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj\` — all green (~25 new tests)
- [x] \`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64\` — 0 errors
- [x] Manual smoke: tool install (heuristic), tool install (catalog), save-editing tool click, INI edit + restore, Get-it-here chip, configure dialog. (See \`docs/smoke-tests/pending.md\` entry.)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Do NOT push if any test failed or any manual smoke step missed.

---

## Done conditions

- [ ] All tests green
- [ ] App build clean
- [ ] Tools row visible above mod list when a game is active
- [ ] Drop a tool zip → installs as tool → button appears
- [ ] Click WSE Save Editor → snapshot lands → tool launches → toast on exit
- [ ] Mod row with .ini files → pencil icon → editor opens → save creates .bak → restore works
- [ ] Known-but-uninstalled tools show `[Get … ↗]` chips with working links
- [ ] NOTICE updated; Settings → About lists installed tools
- [ ] PR opened against master (not stacked on any other branch)
