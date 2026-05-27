# Framework Intake (F2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the framework intake flow from [`docs/superpowers/specs/2026-05-27-framework-intake-design.md`](../specs/2026-05-27-framework-intake-design.md): rename the generic "DLL PROXY" chip to "ELDEN MOD LOADER", add a `KnownFramework` catalog + classifier as Pre-check 4 in the drop pipeline, and an install-at-game-root flow with reversibility tracked under `_626mods/<game>/frameworks/<id>/`.

**Architecture:** Pure-core `KnownFramework` catalog (parallel to `ToolCatalog`) + pure-core `FrameworkInstaller` doing the file-ops + backup + manifest write. App-layer adds Pre-check 4 to `AddModsAsync` (sequencing: save-mod → UE4SS Lua → tool → **framework** → mod-intake), a confirmation dialog, and a Settings → Installed frameworks section. The existing `FrameworkDeps.Catalog` entry for ER's DLL proxy is renamed in-place (chip text + get-link change only; detection paths stay).

**Tech Stack:** C# / .NET 10, WinUI 3, xUnit, SharpCompress (existing dep for archive reads), FsAtomic (existing helper for JSON writes).

---

## File Structure

**Create (Core):**

- `src/ModManager.Core/Frameworks/KnownFramework.cs` — record + static `Catalog` + `Classify` method
- `src/ModManager.Core/Frameworks/FrameworkInstaller.cs` — `Install` method, `FrameworkInstallResult` record, `FrameworkInstallManifest` record (camelCase JSON shape)
- `src/ModManager.Core/Frameworks/FrameworkRegistry.cs` — enumerate `frameworks/<id>/install.json` per game; uninstall logic

**Create (App):**

- `src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml` + `.xaml.cs` — confirmation dialog with files list
- `src/ModManager.App/Frameworks/FrameworkUnrecognizedNudgeDialog.xaml` + `.xaml.cs` — feedback nudge for unrecognized framework-looking zips

**Create (Tests):**

- `tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs`
- `tests/ModManager.Tests/Frameworks/FrameworkInstallerTests.cs`
- `tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs`

**Modify:**

- `src/ModManager.Core/FrameworkDeps.cs:80` — rename `Name` + update `Note` for ER's DLL proxy entry
- `src/ModManager.App/ViewModels/MainViewModel.cs:1119-1147` — add Pre-check 4 between existing Pre-check 3 (tool) and mod intake
- `src/ModManager.App/SettingsDialog.xaml` + `.xaml.cs` — add "Installed frameworks" section parallel to "Installed tools"
- `NOTICE` — append framework attribution block
- `docs/smoke-tests/pending.md` — add F2 smoke entry

---

## Task 1: Rename Elden Mod Loader's chip text in `FrameworkDeps.Catalog`

**Files:**

- Modify: `src/ModManager.Core/FrameworkDeps.cs:78-89`
- Test: `tests/ModManager.Tests/FrameworkDepsTests.cs` (existing — add a new test)

- [ ] **Step 1: Write a failing test that the ER framework chip says "Elden Mod Loader"**

Add to existing `FrameworkDepsTests.cs`:

```csharp
[Fact]
public void Fromsoft_dll_proxy_entry_displays_as_Elden_Mod_Loader()
{
    var entry = FrameworkDeps.Catalog.Single(d => d.Engine == "fromsoft"
        && d.DetectRelativePaths.Contains("dinput8.dll"));
    Assert.Equal("Elden Mod Loader", entry.Name);
    Assert.Equal("https://www.nexusmods.com/eldenring/mods/117", entry.GetUrl);
    Assert.Contains("Elden Mod Loader", entry.Note);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Fromsoft_dll_proxy_entry_displays_as_Elden_Mod_Loader"`
Expected: FAIL — `Name` is currently `"DLL proxy (dinput8/version/winhttp)"`

- [ ] **Step 3: Rename the catalog entry**

In `FrameworkDeps.cs`, replace the existing ER DLL proxy entry:

```csharp
new FrameworkDep(
    Engine: "fromsoft",
    Name: "Elden Mod Loader",
    // Direct-inject mods chain off whichever DLL proxy is already installed. The catalog name
    // calls out Elden Mod Loader specifically — it's the loader most ER mods chain through and
    // the one the user is searching for. The DLL probes stay broad (dinput8/version/winhttp)
    // since the user might have a different proxy installed.
    DetectRelativePaths: new[]
    {
        "dinput8.dll",
        "version.dll",
        "winhttp.dll",
    },
    GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
    Note: "Elden Mod Loader — DLL proxy that direct-inject ER mods chain through (dinput8.dll). Most ER mods need this."),
```

- [ ] **Step 4: Run to verify it passes + nothing else regresses**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — full suite green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/FrameworkDeps.cs tests/ModManager.Tests/FrameworkDepsTests.cs
git commit -m "feat(frameworks): rename ER chip 'DLL PROXY' -> 'Elden Mod Loader'

The chip name is what the user searches for. 'DLL proxy (dinput8/version/winhttp)' is
technically accurate but unparseable; 'Elden Mod Loader' is what they're looking up.
Detection paths stay broad — different mods chain through dinput8 / version / winhttp.
Per docs/superpowers/specs/2026-05-27-framework-intake-design.md."
```

---

## Task 2: Add `KnownFramework` record + day-one catalog

**Files:**

- Create: `src/ModManager.Core/Frameworks/KnownFramework.cs`
- Test: `tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs`

- [ ] **Step 1: Write the failing test that the catalog ships ELM with the right shape**

Create `tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs`:

```csharp
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class KnownFrameworkTests
{
    [Fact]
    public void Catalog_ships_Elden_Mod_Loader_for_Elden_Ring()
    {
        var elm = KnownFramework.Catalog.Single(f => f.FrameworkId == "elden-mod-loader");

        Assert.Equal("Elden Mod Loader", elm.DisplayName);
        Assert.Equal("fromsoft", elm.Engine);
        Assert.Equal("1245620", elm.SteamAppId);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/117", elm.GetUrl);
        Assert.Equal("TechieW", elm.Author);
        Assert.Equal("GameRoot", elm.InstallRoot);
        Assert.Contains("dinput8.dll", elm.ZipSignatureFiles);
        Assert.Contains("mod_loader_config.ini", elm.ZipSignatureFiles);
    }

    [Fact]
    public void Catalog_entries_have_nonempty_required_fields()
    {
        foreach (var f in KnownFramework.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.FrameworkId), $"FrameworkId empty");
            Assert.False(string.IsNullOrWhiteSpace(f.DisplayName), $"DisplayName empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.Engine), $"Engine empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.GetUrl), $"GetUrl empty for {f.FrameworkId}");
            Assert.False(string.IsNullOrWhiteSpace(f.Author), $"Author empty for {f.FrameworkId}");
            Assert.NotEmpty(f.ZipSignatureFiles);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails (type doesn't exist)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~KnownFrameworkTests"`
Expected: FAIL — `KnownFramework` not defined.

- [ ] **Step 3: Create `KnownFramework.cs` with the record + day-one catalog**

```csharp
namespace ModManager.Core.Frameworks;

/// <summary>
/// A framework the launcher knows how to install. Parallel to <see cref="Tools.KnownTool"/>
/// — same shape (id + display + engine + author + get-url + zip-signature hints) but for
/// frameworks (UE4SS, BepInEx, Elden Mod Loader, ME2, etc.) instead of tools. Day-one entry
/// is Elden Mod Loader; adding a new framework is a one-record addition to the catalog.
///
/// Pure data — detection/install logic lives in <see cref="FrameworkInstaller"/>.
/// </summary>
public sealed record KnownFramework(
    string FrameworkId,
    string DisplayName,
    string Engine,
    string? SteamAppId,
    string GetUrl,
    string Author,
    IReadOnlyList<string> ZipFilenameHints,
    IReadOnlyList<string> ZipSignatureFiles,
    string InstallRoot,
    IReadOnlyList<string> ForbiddenPaths)
{
    /// <summary>
    /// Day-one catalog. Each entry is intake-installable: we know exactly what files to
    /// drop where, with reversibility tracked separately. Frameworks we DETECT but don't
    /// INSTALL (UE4SS, BepInEx, etc.) stay in <see cref="FrameworkDeps.Catalog"/>.
    /// </summary>
    public static IReadOnlyList<KnownFramework> Catalog { get; } = new[]
    {
        new KnownFramework(
            FrameworkId: "elden-mod-loader",
            DisplayName: "Elden Mod Loader",
            Engine: "fromsoft",
            SteamAppId: "1245620",
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Author: "TechieW",
            ZipFilenameHints: new[] { "elden", "mod", "loader" },
            // Signature files that prove a zip is ELM. mod_loader_config.ini is unique to ELM
            // (dinput8.dll alone is shared by many DLL proxies); both must be present.
            ZipSignatureFiles: new[] { "dinput8.dll", "mod_loader_config.ini" },
            InstallRoot: "GameRoot",
            // ER's game executable must never be overwritten by a framework install.
            ForbiddenPaths: new[] { "eldenring.exe", "start_protected_game.exe" }),
    };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~KnownFrameworkTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Frameworks/KnownFramework.cs tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs
git commit -m "feat(frameworks): KnownFramework record + day-one Elden Mod Loader catalog entry

Parallel to ToolCatalog. Each entry is intake-installable; we know exactly where the
files land. Detection-only frameworks (UE4SS, BepInEx, etc.) stay in FrameworkDeps.
Per docs/superpowers/specs/2026-05-27-framework-intake-design.md."
```

---

## Task 3: Add `KnownFramework.Classify` — match dropped zip against catalog

**Files:**

- Modify: `src/ModManager.Core/Frameworks/KnownFramework.cs`
- Modify: `tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs`

- [ ] **Step 1: Write failing tests for the classifier**

Append to `KnownFrameworkTests.cs`:

```csharp
public class ClassifyTests
{
    [Fact]
    public void Classify_matches_ELM_when_signature_files_present_in_zip()
    {
        var zipEntries = new[]
        {
            "dinput8.dll",
            "mod_loader_config.ini",
            "ModLoader/some.dll",
        };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.NotNull(result.Match);
        Assert.Equal("elden-mod-loader", result.Match.FrameworkId);
        Assert.False(result.LooksLikeFramework, "Recognized hit should NOT also flag looks-like.");
    }

    [Fact]
    public void Classify_no_match_when_wrong_engine()
    {
        var zipEntries = new[] { "dinput8.dll", "mod_loader_config.ini" };

        var result = KnownFramework.Classify(zipEntries, engine: "ue-pak", steamAppId: null);

        Assert.Null(result.Match);
    }

    [Fact]
    public void Classify_no_match_when_signature_files_missing()
    {
        // Only dinput8.dll — not enough to be ELM (which requires mod_loader_config.ini too).
        var zipEntries = new[] { "dinput8.dll", "somethingelse.dll" };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
    }

    [Fact]
    public void Classify_flags_looks_like_framework_for_unrecognized_proxy_dll_at_zip_root()
    {
        // Has dinput8.dll/version.dll/winhttp.dll at the zip root but ISN'T a catalog match.
        // (e.g. user has a homebrew DLL proxy or a framework we don't know about.)
        var zipEntries = new[] { "winhttp.dll", "some_other_thing.txt" };

        var result = KnownFramework.Classify(zipEntries, engine: "fromsoft", steamAppId: "1245620");

        Assert.Null(result.Match);
        Assert.True(result.LooksLikeFramework, "Bare DLL-proxy at zip root should look-like.");
    }

    [Fact]
    public void Classify_does_not_flag_looks_like_for_non_fromsoft_engines()
    {
        var zipEntries = new[] { "winhttp.dll" };

        var result = KnownFramework.Classify(zipEntries, engine: "ue-pak", steamAppId: null);

        Assert.Null(result.Match);
        Assert.False(result.LooksLikeFramework, "Looks-like is FromSoft-specific for v1.");
    }
}
```

- [ ] **Step 2: Run to verify all four fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ClassifyTests"`
Expected: FAIL — `Classify` method doesn't exist.

- [ ] **Step 3: Implement `Classify` in KnownFramework.cs**

Add inside the `KnownFramework` record:

```csharp
    /// <summary>
    /// Classifier result: which catalog entry matched (or null), and whether the dropped
    /// zip "looks like" an unrecognized framework that warrants a feedback nudge.
    /// </summary>
    public sealed record ClassifyResult(KnownFramework? Match, bool LooksLikeFramework);

    /// <summary>
    /// Run the dropped zip's entry names through the catalog. Returns the first match (by
    /// signature-files-all-present + filename-hints-any-match) scoped to the active engine
    /// + Steam App ID. If no match but the zip has a DLL proxy at its root for a FromSoft
    /// game, flag LooksLikeFramework so the App can show a feedback nudge.
    ///
    /// Pure: no IO. Caller pre-reads the archive entry names.
    /// </summary>
    public static ClassifyResult Classify(
        IEnumerable<string> zipEntryNames, string engine, string? steamAppId)
    {
        var entries = (zipEntryNames ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n.Replace('\\', '/'))
            .ToList();
        if (entries.Count == 0)
            return new ClassifyResult(null, false);

        var basenamesLower = entries
            .Select(n => System.IO.Path.GetFileName(n).ToLowerInvariant())
            .Where(n => n.Length > 0)
            .ToHashSet();

        foreach (var f in Catalog)
        {
            if (!string.Equals(f.Engine, engine, StringComparison.Ordinal)) continue;
            if (f.SteamAppId is not null
                && !string.Equals(f.SteamAppId, steamAppId, StringComparison.Ordinal)) continue;

            // Filename-hints are a soft filter; signature-files-ALL-present is the must.
            bool allSigsPresent = f.ZipSignatureFiles.All(s =>
                basenamesLower.Contains(s.ToLowerInvariant()));
            if (allSigsPresent)
                return new ClassifyResult(f, false);
        }

        // No catalog hit. Looks-like heuristic: a FromSoft drop with a DLL proxy at the zip
        // root (basename match only, no path segments) is probably a framework we don't know.
        bool looksLike = string.Equals(engine, "fromsoft", StringComparison.Ordinal)
            && entries.Any(e =>
                !e.Contains('/')
                && (string.Equals(System.IO.Path.GetFileName(e), "dinput8.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(System.IO.Path.GetFileName(e), "version.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(System.IO.Path.GetFileName(e), "winhttp.dll", StringComparison.OrdinalIgnoreCase)));

        return new ClassifyResult(null, looksLike);
    }
```

- [ ] **Step 4: Run to verify all four pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ClassifyTests"`
Expected: PASS.

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Frameworks/KnownFramework.cs tests/ModManager.Tests/Frameworks/KnownFrameworkTests.cs
git commit -m "feat(frameworks): KnownFramework.Classify matches dropped zips against catalog

Engine + SteamAppId-scoped match by signature-files-all-present. Sets LooksLikeFramework
when a FromSoft zip has a bare DLL-proxy at the root but doesn't match the catalog — the
App layer shows a feedback nudge so we can grow the catalog over time."
```

---

## Task 4: `FrameworkInstaller.Install` — extract to game root with backup + manifest

**Files:**

- Create: `src/ModManager.Core/Frameworks/FrameworkInstaller.cs`
- Create: `tests/ModManager.Tests/Frameworks/FrameworkInstallerTests.cs`

- [ ] **Step 1: Write failing tests covering the install contract**

Create `tests/ModManager.Tests/Frameworks/FrameworkInstallerTests.cs`:

```csharp
using System.IO.Compression;
using System.Text.Json;
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-install-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string MakeGameRoot()
    {
        var root = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(root);
        return root;
    }

    private string MakeGameData()
    {
        var dir = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string BuildZip(params (string Path, byte[] Bytes)[] entries)
    {
        var zipPath = Path.Combine(_tmp, $"src-{Guid.NewGuid():n}.zip");
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (path, bytes) in entries)
        {
            var entry = zip.CreateEntry(path);
            using var es = entry.Open();
            es.Write(bytes, 0, bytes.Length);
        }
        return zipPath;
    }

    private static KnownFramework Elm() => new(
        FrameworkId: "elden-mod-loader",
        DisplayName: "Elden Mod Loader",
        Engine: "fromsoft",
        SteamAppId: "1245620",
        GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
        Author: "TechieW",
        ZipFilenameHints: new[] { "elden", "mod", "loader" },
        ZipSignatureFiles: new[] { "dinput8.dll", "mod_loader_config.ini" },
        InstallRoot: "GameRoot",
        ForbiddenPaths: new[] { "eldenring.exe" });

    [Fact]
    public void Install_extracts_files_to_game_root()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(
            ("dinput8.dll", new byte[] { 1, 2, 3 }),
            ("mod_loader_config.ini", new byte[] { 4, 5 }),
            ("ModLoader/some.dll", new byte[] { 6 }));

        var result = FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        Assert.True(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "ModLoader", "some.dll")));
        Assert.Equal("elden-mod-loader", result.FrameworkId);
        Assert.Equal(3, result.InstalledFiles.Count);
    }

    [Fact]
    public void Install_writes_camelCase_manifest()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(("dinput8.dll", new byte[] { 1 }), ("mod_loader_config.ini", new byte[] { 2 }));

        FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        var manifestPath = Path.Combine(gameData, "frameworks", "elden-mod-loader", "install.json");
        Assert.True(File.Exists(manifestPath));

        var json = File.ReadAllText(manifestPath);
        Assert.Contains("\"frameworkId\":", json);
        Assert.Contains("\"displayName\":", json);
        Assert.Contains("\"installedFiles\":", json);
        Assert.Contains("\"installedUtc\":", json);
    }

    [Fact]
    public void Install_backs_up_existing_file_before_overwriting()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 9, 9, 9 });
        var zip = BuildZip(("dinput8.dll", new byte[] { 1 }), ("mod_loader_config.ini", new byte[] { 2 }));

        var result = FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        // Existing file replaced with new bytes.
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(gameRoot, "dinput8.dll")));
        // Backup preserved under the framework's backup subfolder.
        var backupRoot = Path.Combine(gameData, "frameworks", "elden-mod-loader", "backup");
        Assert.True(Directory.Exists(backupRoot));
        var backedUp = Directory.EnumerateFiles(backupRoot, "dinput8.dll", SearchOption.AllDirectories).Single();
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(backedUp));
        Assert.NotNull(result.BackupSnapshotPath);
    }

    [Fact]
    public void Install_refuses_zip_that_contains_a_forbidden_path()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(
            ("dinput8.dll", new byte[] { 1 }),
            ("mod_loader_config.ini", new byte[] { 2 }),
            ("eldenring.exe", new byte[] { 99 }));  // forbidden

        var ex = Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData));
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eldenring.exe", ex.Message);

        // None of the files should have been extracted.
        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
    }

    [Fact]
    public void Install_refuses_zip_with_path_escaping_install_root()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(("../escaped.dll", new byte[] { 1 }), ("dinput8.dll", new byte[] { 2 }), ("mod_loader_config.ini", new byte[] { 3 }));

        Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData));
    }
}
```

- [ ] **Step 2: Run to verify they fail (type doesn't exist)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkInstallerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `FrameworkInstaller.cs`**

```csharp
using System.IO.Compression;
using System.Text.Json;

namespace ModManager.Core.Frameworks;

/// <summary>
/// The persisted record of a framework install. Lives at
/// <c>&lt;gameData&gt;/frameworks/&lt;frameworkId&gt;/install.json</c>. camelCase JSON shape
/// matches the Electron-shared state-file convention from the legacy launcher.
/// </summary>
public sealed record FrameworkInstallManifest(
    string FrameworkId,
    string DisplayName,
    string Author,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    DateTime InstalledUtc,
    string? BackupSnapshotPath);

/// <summary>Result returned to the App layer after a successful install.</summary>
public sealed record FrameworkInstallResult(
    string FrameworkId,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    DateTime InstalledUtc,
    string? BackupSnapshotPath);

/// <summary>
/// Pure-core installer for catalog-known frameworks. Backs up any file it's about to
/// overwrite (so uninstall is reversible), then extracts the archive to the framework's
/// install root, then writes the manifest. Forbidden paths (declared by the catalog entry)
/// abort the entire install before any file is touched.
///
/// NO Electron, NO WinUI. System.IO + System.IO.Compression only — same dep surface as the
/// rest of Core.
/// </summary>
public static class FrameworkInstaller
{
    public static FrameworkInstallResult Install(
        string archivePath, KnownFramework framework, string gameRoot, string gameDataDir)
    {
        if (string.IsNullOrEmpty(archivePath)) throw new ArgumentException(nameof(archivePath));
        if (framework is null) throw new ArgumentNullException(nameof(framework));
        if (string.IsNullOrEmpty(gameRoot)) throw new ArgumentException(nameof(gameRoot));
        if (string.IsNullOrEmpty(gameDataDir)) throw new ArgumentException(nameof(gameDataDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException("Archive missing.", archivePath);

        string installRoot = framework.InstallRoot switch
        {
            "GameRoot" => gameRoot,
            _ => throw new InvalidOperationException(
                $"Unknown framework install root '{framework.InstallRoot}'."),
        };

        var frameworkDir = Path.Combine(gameDataDir, "frameworks", framework.FrameworkId);
        var backupRoot = Path.Combine(frameworkDir, "backup", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

        using var zip = ZipFile.OpenRead(archivePath);

        // 1) Validate every entry path BEFORE touching disk. Reject directory traversal +
        //    forbidden paths up front so partial-extract states are impossible.
        var plannedEntries = new List<(ZipArchiveEntry Entry, string RelativeNorm, string AbsTarget)>();
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;  // directory marker

            var relNorm = entry.FullName.Replace('\\', '/');
            var absTarget = Path.GetFullPath(Path.Combine(installRoot, relNorm));
            if (!absTarget.StartsWith(Path.GetFullPath(installRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                && !string.Equals(absTarget, Path.GetFullPath(installRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Archive entry '{entry.FullName}' resolves outside the install root — refusing install.");
            }

            if (framework.ForbiddenPaths.Any(forbidden =>
                string.Equals(Path.GetFileName(relNorm), forbidden, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relNorm, forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Archive contains a forbidden path '{entry.FullName}' — refusing install. " +
                    $"Frameworks must never overwrite the game's protected files.");
            }

            plannedEntries.Add((entry, relNorm, absTarget));
        }

        // 2) Back up existing files that will be overwritten. Each backup goes under a
        //    timestamped folder so multiple installs don't clobber each other.
        string? createdBackupRoot = null;
        foreach (var (_, relNorm, absTarget) in plannedEntries)
        {
            if (!File.Exists(absTarget)) continue;
            createdBackupRoot ??= backupRoot;
            var backupPath = Path.Combine(backupRoot, relNorm);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(absTarget, backupPath, overwrite: false);
        }

        // 3) Extract. Order doesn't matter — bounds checks all happened above.
        var installed = new List<string>(plannedEntries.Count);
        foreach (var (entry, relNorm, absTarget) in plannedEntries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absTarget)!);
            using (var src = entry.Open())
            using (var dst = File.Create(absTarget))
                src.CopyTo(dst);
            installed.Add(relNorm);
        }

        // 4) Write the manifest. camelCase via JsonSerializerOptions (the existing FsAtomic
        //    helper uses the same convention; we replicate inline here to keep Core dep-free).
        var manifest = new FrameworkInstallManifest(
            FrameworkId: framework.FrameworkId,
            DisplayName: framework.DisplayName,
            Author: framework.Author,
            InstallPath: installRoot,
            InstalledFiles: installed,
            InstalledUtc: DateTime.UtcNow,
            BackupSnapshotPath: createdBackupRoot);

        Directory.CreateDirectory(frameworkDir);
        var manifestPath = Path.Combine(frameworkDir, "install.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        // Atomic write: temp + rename. Mirrors FsAtomic.WriteJsonAtomic's contract.
        var tempPath = manifestPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
        File.Move(tempPath, manifestPath);

        return new FrameworkInstallResult(
            framework.FrameworkId, installRoot, installed, manifest.InstalledUtc, createdBackupRoot);
    }
}
```

- [ ] **Step 4: Run to verify all tests pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkInstallerTests"`
Expected: PASS — all 5 tests green.

- [ ] **Step 5: Run full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Frameworks/FrameworkInstaller.cs tests/ModManager.Tests/Frameworks/FrameworkInstallerTests.cs
git commit -m "feat(frameworks): FrameworkInstaller — extract to install root with backup + manifest

Two-phase: validate ALL archive entries up front (directory traversal + forbidden paths
abort before any file is touched), then back up existing targets to a timestamped
subfolder, then extract. Manifest is camelCase JSON via atomic temp+rename. The
forbidden-paths gate is what keeps a malformed framework zip from stomping on
eldenring.exe."
```

---

## Task 5: `FrameworkRegistry` — enumerate installed manifests + uninstall

**Files:**

- Create: `src/ModManager.Core/Frameworks/FrameworkRegistry.cs`
- Create: `tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Text.Json;
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkRegistryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-reg-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void List_returns_empty_when_no_installs()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var result = FrameworkRegistry.List(gameData);

        Assert.Empty(result);
    }

    [Fact]
    public void List_returns_one_manifest_per_framework()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(gameRoot);
        WriteFakeManifest(gameData, "elden-mod-loader", "Elden Mod Loader", "TechieW", gameRoot);
        WriteFakeManifest(gameData, "some-other", "Some Other", "alice", gameRoot);

        var result = FrameworkRegistry.List(gameData);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.FrameworkId == "elden-mod-loader");
        Assert.Contains(result, m => m.FrameworkId == "some-other");
    }

    [Fact]
    public void Uninstall_removes_installed_files_and_restores_backup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameRoot);

        // Pre-state: framework files were installed (bytes [1]), a backup was taken of an
        // existing dinput8.dll the install replaced (original bytes [9, 9]).
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(gameRoot, "mod_loader_config.ini"), new byte[] { 2 });

        var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
        var backupDir = Path.Combine(fwDir, "backup", "20260527000000");
        Directory.CreateDirectory(backupDir);
        File.WriteAllBytes(Path.Combine(backupDir, "dinput8.dll"), new byte[] { 9, 9 });

        WriteManifest(fwDir, new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW", gameRoot,
            new[] { "dinput8.dll", "mod_loader_config.ini" },
            DateTime.UtcNow, backupDir));

        FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

        // Backed-up file restored; non-backed-up file removed; framework dir + manifest gone.
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
        Assert.False(Directory.Exists(fwDir));
    }

    private static void WriteFakeManifest(string gameData, string id, string name, string author, string installRoot)
    {
        var fwDir = Path.Combine(gameData, "frameworks", id);
        WriteManifest(fwDir, new FrameworkInstallManifest(
            id, name, author, installRoot,
            Array.Empty<string>(), DateTime.UtcNow, null));
    }

    private static void WriteManifest(string fwDir, FrameworkInstallManifest m)
    {
        Directory.CreateDirectory(fwDir);
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        File.WriteAllText(Path.Combine(fwDir, "install.json"), JsonSerializer.Serialize(m, opts));
    }
}
```

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkRegistryTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `FrameworkRegistry.cs`**

```csharp
using System.Text.Json;

namespace ModManager.Core.Frameworks;

/// <summary>
/// Read + maintain the on-disk record of installed frameworks under
/// <c>&lt;gameData&gt;/frameworks/&lt;frameworkId&gt;/install.json</c>. Settings → Installed
/// frameworks reads via <see cref="List"/>; the uninstall button calls <see cref="Uninstall"/>.
/// </summary>
public static class FrameworkRegistry
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<FrameworkInstallManifest> List(string gameDataDir)
    {
        var root = Path.Combine(gameDataDir, "frameworks");
        if (!Directory.Exists(root)) return Array.Empty<FrameworkInstallManifest>();

        var manifests = new List<FrameworkInstallManifest>();
        foreach (var fwDir in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(fwDir, "install.json");
            if (!File.Exists(path)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(path), Json);
                if (m is not null) manifests.Add(m);
            }
            catch { /* ignore unreadable manifests — surface in a later log pass */ }
        }
        return manifests;
    }

    public static void Uninstall(string gameDataDir, string frameworkId, string gameRoot)
    {
        var fwDir = Path.Combine(gameDataDir, "frameworks", frameworkId);
        var manifestPath = Path.Combine(fwDir, "install.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                $"No install manifest for framework '{frameworkId}'.", manifestPath);

        var m = JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(manifestPath), Json)
                ?? throw new InvalidDataException($"Couldn't parse manifest for '{frameworkId}'.");

        // Delete every installed file. Don't follow symlinks; don't fail the whole uninstall
        // if one file is already gone (idempotent — partial cleanup is fine).
        foreach (var rel in m.InstalledFiles)
        {
            var abs = Path.Combine(gameRoot, rel);
            try { if (File.Exists(abs)) File.Delete(abs); } catch { /* leave for manual */ }
        }

        // Restore the backup (if any) — copy each file from the backup tree onto the install
        // root. This puts back the original files the install replaced.
        if (!string.IsNullOrEmpty(m.BackupSnapshotPath) && Directory.Exists(m.BackupSnapshotPath))
        {
            foreach (var src in Directory.EnumerateFiles(m.BackupSnapshotPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(m.BackupSnapshotPath, src);
                var dst = Path.Combine(gameRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        }

        // Tear down the framework dir. The whole subfolder goes — manifest + backup + any
        // future per-framework state we add.
        try { Directory.Delete(fwDir, recursive: true); } catch { }
    }
}
```

- [ ] **Step 4: Run all framework tests**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Frameworks"`
Expected: PASS.

- [ ] **Step 5: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Frameworks/FrameworkRegistry.cs tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs
git commit -m "feat(frameworks): FrameworkRegistry — enumerate installs + reverse uninstall

List() reads every install.json under <gameData>/frameworks/. Uninstall() removes the
installed files, copies the backed-up originals back over, and tears down the framework's
subfolder. Idempotent against partial state (a missing file mid-uninstall doesn't abort)."
```

---

## Task 6: `FrameworkInstallDialog` — confirmation UX

**Files:**

- Create: `src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml` + `.xaml.cs`

- [ ] **Step 1: Create the XAML**

`src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml`:

```xaml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.Frameworks.FrameworkInstallDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Title="Install framework"
    PrimaryButtonText="Install"
    CloseButtonText="Cancel"
    DefaultButton="Primary">
    <StackPanel Spacing="12" Width="440">
        <TextBlock x:Name="HeadlineText" FontWeight="SemiBold" TextWrapping="Wrap" />
        <TextBlock x:Name="AuthorText" Opacity="0.7" FontSize="12" TextWrapping="Wrap" />
        <TextBlock Text="Files that will be installed:" FontWeight="SemiBold" FontSize="12" />
        <ListView x:Name="FilesList" MaxHeight="200" SelectionMode="None">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="x:String">
                    <TextBlock Text="{x:Bind}" FontFamily="Consolas" FontSize="11" />
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
        <TextBlock x:Name="OverwriteWarning" Opacity="0.8" FontSize="12" TextWrapping="Wrap"
                   Visibility="Collapsed" />
        <TextBlock x:Name="LocationText" Opacity="0.6" FontSize="11" TextWrapping="Wrap" />
        <TextBlock Opacity="0.5" FontSize="11" TextWrapping="Wrap"
                   Text="Reversible from Settings → Installed frameworks. The launcher snapshots replaced files before extraction." />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Create the code-behind**

`src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Frameworks;

namespace ModManager.App.Frameworks;

public sealed partial class FrameworkInstallDialog : ContentDialog
{
    public FrameworkInstallDialog(
        KnownFramework framework,
        IReadOnlyList<string> filesToInstall,
        IReadOnlyList<string> filesThatWillBeReplaced,
        string installLocation)
    {
        InitializeComponent();
        HeadlineText.Text = $"{framework.DisplayName} — install at game root?";
        AuthorText.Text = $"by {framework.Author}  ·  {framework.GetUrl}";
        FilesList.ItemsSource = filesToInstall;
        LocationText.Text = $"Install location: {installLocation}";
        if (filesThatWillBeReplaced.Count > 0)
        {
            OverwriteWarning.Text =
                $"⚠ {filesThatWillBeReplaced.Count} existing file(s) will be replaced and " +
                $"backed up to _626mods/<game>/frameworks/{framework.FrameworkId}/backup/. " +
                $"Replaced: {string.Join(", ", filesThatWillBeReplaced)}";
            OverwriteWarning.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }
}
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml src/ModManager.App/Frameworks/FrameworkInstallDialog.xaml.cs
git commit -m "feat(frameworks): FrameworkInstallDialog — confirmation UX before install

Shows the framework name + author, the file list extracted from the zip, and a warning
when existing files will be replaced (with the backup path explained). Primary action
'Install', secondary 'Cancel'. Doesn't run any IO itself — the caller (MainViewModel)
invokes FrameworkInstaller.Install on Primary."
```

---

## Task 7: `FrameworkUnrecognizedNudgeDialog` — feedback nudge for looks-like

**Files:**

- Create: `src/ModManager.App/Frameworks/FrameworkUnrecognizedNudgeDialog.xaml` + `.xaml.cs`

- [ ] **Step 1: Create the XAML**

```xaml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.Frameworks.FrameworkUnrecognizedNudgeDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Title="Looks like a framework"
    PrimaryButtonText="Continue as mod"
    SecondaryButtonText="Open feedback link"
    CloseButtonText="Cancel"
    DefaultButton="Primary">
    <StackPanel Spacing="12" Width="440">
        <TextBlock TextWrapping="Wrap">
            This zip looks like a game framework or loader (it has a proxy DLL at its root),
            but it isn't in our catalog yet. We can't safely auto-install frameworks we don't
            recognize — but if you've used this one before, we'd love to add it.
        </TextBlock>
        <TextBlock TextWrapping="Wrap" Opacity="0.75" FontSize="12">
            If you want to install it anyway, the launcher will route it through regular mod
            intake (extracts to your mods folder). This usually doesn't work for frameworks
            but is sometimes correct.
        </TextBlock>
        <TextBlock x:Name="FilenameText" Opacity="0.6" FontFamily="Consolas" FontSize="11" />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Create the code-behind**

```csharp
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Frameworks;

public sealed partial class FrameworkUnrecognizedNudgeDialog : ContentDialog
{
    public const string FeedbackUrl = "https://github.com/estevanhernandez-stack-ed/626-mod-launcher/issues/new?labels=framework-request&title=Framework+request";

    public FrameworkUnrecognizedNudgeDialog(string archiveFileName)
    {
        InitializeComponent();
        FilenameText.Text = archiveFileName;
        this.SecondaryButtonClick += (_, _) =>
            Windows.System.Launcher.LaunchUriAsync(new Uri(FeedbackUrl)).AsTask();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Frameworks/FrameworkUnrecognizedNudgeDialog.xaml src/ModManager.App/Frameworks/FrameworkUnrecognizedNudgeDialog.xaml.cs
git commit -m "feat(frameworks): FrameworkUnrecognizedNudgeDialog — feedback nudge

When a dropped zip looks like a framework (DLL proxy at zip root) but isn't in our
catalog, this dialog lets the user open a feedback link or continue as a regular mod
install. Three actions: 'Continue as mod', 'Open feedback link', 'Cancel'."
```

---

## Task 8: Wire Pre-check 4 into `AddModsAsync`

**Files:**

- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs:1119-1147` (between existing Pre-check 3 and the mod-intake fallback)

- [ ] **Step 1: Add the framework Pre-check 4 block**

Read the current shape of Pre-check 3 in [`MainViewModel.cs:1119-1147`](../../../src/ModManager.App/ViewModels/MainViewModel.cs#L1119-L1147) and follow its pattern. Insert AFTER Pre-check 3 (tool intake) and BEFORE the existing mod-intake call:

```csharp
// Pre-check 4: known-framework intake. Catalog match -> confirmation -> install at the
// framework's designated root with reversibility tracked under
// _626mods/<game>/frameworks/<id>/. Looks-like-framework (DLL proxy at zip root, FromSoft
// only, no catalog match) -> nudge dialog with a feedback link, then fall through to mod
// intake. No match + no looks-like -> fall through to existing mod intake unchanged.
{
    var stillToProcess = new List<string>();
    foreach (var src in pathsAfterTool)  // <-- name matches the existing post-Pre-check-3 collection
    {
        if (!IsArchive(src)) { stillToProcess.Add(src); continue; }
        IReadOnlyList<string>? zipEntries = null;
        try { zipEntries = PeekZipEntries(src); }
        catch { /* fall through if we can't read */ }
        if (zipEntries is null) { stillToProcess.Add(src); continue; }

        var classify = ModManager.Core.Frameworks.KnownFramework.Classify(
            zipEntries, _ctx.Game.Engine, _ctx.Game.SteamAppId);
        if (classify.Match is not null)
        {
            // Filename for the warning preview — basenames only.
            var fileNames = zipEntries
                .Select(e => e.Replace('\\', '/'))
                .Where(e => !e.EndsWith("/", StringComparison.Ordinal))
                .ToList();
            var willOverwrite = fileNames
                .Where(e => System.IO.File.Exists(System.IO.Path.Combine(_ctx.GameRoot, e)))
                .ToList();

            var dlg = new ModManager.App.Frameworks.FrameworkInstallDialog(
                classify.Match, fileNames, willOverwrite, _ctx.GameRoot)
            { XamlRoot = App.MainWindow!.Content.XamlRoot };
            var result = await dlg.ShowAsync();
            if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            {
                StatusText = $"Skipped {classify.Match.DisplayName} install.";
                continue;
            }

            try
            {
                var r = ModManager.Core.Frameworks.FrameworkInstaller.Install(
                    src, classify.Match, _ctx.GameRoot, _ctx.DataDir);
                StatusText = $"Installed {classify.Match.DisplayName} ({r.InstalledFiles.Count} file(s) at game root).";
            }
            catch (Exception ex)
            {
                StatusText = $"Couldn't install {classify.Match.DisplayName}: {ex.Message}";
            }
            // After install: refresh framework presence so the chip disappears.
            await ReloadModsAsync();
            continue;
        }

        if (classify.LooksLikeFramework)
        {
            var nudge = new ModManager.App.Frameworks.FrameworkUnrecognizedNudgeDialog(
                System.IO.Path.GetFileName(src))
            { XamlRoot = App.MainWindow!.Content.XamlRoot };
            var result = await nudge.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None) continue; // Cancel
            // Primary ("Continue as mod") or Secondary ("Open feedback link") -> fall through
            // to mod intake. Secondary already launched the URL via the dialog's own handler.
        }

        stillToProcess.Add(src);
    }
    pathsAfterTool = stillToProcess;
}
```

You'll need to thread the right variable name (`pathsAfterTool` is illustrative — match the actual variable the existing Pre-check 3 uses). Also add `PeekZipEntries` helper if not present:

```csharp
private static IReadOnlyList<string> PeekZipEntries(string archivePath)
{
    using var zip = System.IO.Compression.ZipFile.OpenRead(archivePath);
    return zip.Entries.Select(e => e.FullName).ToList();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 3: Run the App-layer tests (and existing pre-check tests)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — no regressions in existing intake tests.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(frameworks): wire framework intake as Pre-check 4 in AddModsAsync

Sequencing: save-mod -> UE4SS Lua -> tool -> framework -> mod-intake. Catalog match
shows the confirmation dialog and routes to FrameworkInstaller. Looks-like-framework
shows the feedback nudge then falls through to mod intake. No match means the existing
behavior is unchanged."
```

---

## Task 9: Settings → Installed frameworks section

**Files:**

- Modify: `src/ModManager.App/SettingsDialog.xaml` (add a new section parallel to "Installed tools")
- Modify: `src/ModManager.App/SettingsDialog.xaml.cs` (load + render manifests, wire Uninstall button)

- [ ] **Step 1: Add the new section to the XAML**

Locate the existing "Installed tools" section in `SettingsDialog.xaml` and copy its shape below it. Replace tool-specific bindings with framework-specific ones. The section template:

```xaml
<TextBlock Text="Installed frameworks" FontWeight="SemiBold" Margin="0,12,0,0" />
<ItemsRepeater x:Name="FrameworksRepeater">
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="local:InstalledFrameworkRow">
            <Grid ColumnSpacing="8" Padding="0,6"
                  ToolTipService.ToolTip="Catalog metadata only — never bundled. Reversible.">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" VerticalAlignment="Center">
                    <TextBlock Text="{x:Bind DisplayName}" FontWeight="SemiBold" />
                    <TextBlock Text="{x:Bind Detail}" Opacity="0.6" FontSize="12" />
                </StackPanel>
                <HyperlinkButton Grid.Column="1" Content="Get it here" NavigateUri="{x:Bind GetUriObj}"
                                 Opacity="0.75" FontSize="12" />
                <Button Grid.Column="2" Content="Uninstall" Tag="{x:Bind FrameworkId}"
                        Click="OnUninstallFramework" />
            </Grid>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
<TextBlock x:Name="FrameworksEmpty" Text="No frameworks installed by the launcher yet."
           Opacity="0.6" FontSize="12" Visibility="Collapsed" />
<TextBlock Opacity="0.5" FontSize="11" TextWrapping="Wrap"
           Text="The launcher never bundles frameworks. Drop a recognized framework zip to install; this section tracks what's been installed and lets you reverse it." />
```

- [ ] **Step 2: Add the row type + loader in the code-behind**

Add to `SettingsDialog.xaml.cs`:

```csharp
public sealed record InstalledFrameworkRow(
    string FrameworkId,
    string DisplayName,
    string Detail,
    string GetUrl)
{
    public Uri GetUriObj => new(GetUrl);
}

// In the existing settings loader where Installed tools are populated, add:
private void LoadInstalledFrameworks()
{
    // Enumerate every per-game data dir. Game data dir derivation already exists at the
    // existing Installed-tools loader — reuse its pattern.
    var rows = new List<InstalledFrameworkRow>();
    foreach (var gameDataDir in EnumerateAllGameDataDirs())
    {
        foreach (var m in ModManager.Core.Frameworks.FrameworkRegistry.List(gameDataDir))
        {
            rows.Add(new InstalledFrameworkRow(
                FrameworkId: m.FrameworkId,
                DisplayName: m.DisplayName,
                Detail: $"by {m.Author}  ·  installed {m.InstalledUtc.ToLocalTime():g}  ·  {m.InstallPath}",
                GetUrl: GetUrlForFramework(m.FrameworkId)));
        }
    }
    FrameworksRepeater.ItemsSource = rows;
    FrameworksEmpty.Visibility = rows.Count == 0
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;
}

private static string GetUrlForFramework(string frameworkId)
    => ModManager.Core.Frameworks.KnownFramework.Catalog
        .FirstOrDefault(f => f.FrameworkId == frameworkId)?.GetUrl ?? "";

private async void OnUninstallFramework(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
{
    if (sender is not Microsoft.UI.Xaml.Controls.Button btn) return;
    if (btn.Tag is not string frameworkId) return;
    // For v1 — uninstall from every game where this framework is installed. v2 could prompt
    // for a specific game when multiple are installed.
    foreach (var gameDataDir in EnumerateAllGameDataDirs())
    {
        var matching = ModManager.Core.Frameworks.FrameworkRegistry.List(gameDataDir)
            .Any(m => m.FrameworkId == frameworkId);
        if (!matching) continue;
        try
        {
            // The install path is stored on the manifest; pull it to know which game root to clean.
            var m = ModManager.Core.Frameworks.FrameworkRegistry.List(gameDataDir)
                .First(x => x.FrameworkId == frameworkId);
            ModManager.Core.Frameworks.FrameworkRegistry.Uninstall(gameDataDir, frameworkId, m.InstallPath);
        }
        catch { /* surface in a later log pass */ }
    }
    LoadInstalledFrameworks();  // re-render
}
```

Use the EXISTING `EnumerateAllGameDataDirs` helper from `SettingsDialog.xaml.cs` (the Installed-tools section uses it). Call `LoadInstalledFrameworks()` from the same load entry point that fires `LoadInstalledTools()`.

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/SettingsDialog.xaml src/ModManager.App/SettingsDialog.xaml.cs
git commit -m "feat(frameworks): Settings -> Installed frameworks section + uninstall flow

Parallel shape to the existing Installed tools section: row per framework with display
name, author, install date, install path, 'Get it here' link, and Uninstall button.
Uninstall removes installed files and restores any pre-install backup via
FrameworkRegistry.Uninstall."
```

---

## Task 10: NOTICE update + smoke entry

**Files:**

- Modify: `NOTICE`
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Append a Frameworks attribution block to NOTICE**

Read the existing NOTICE file structure (it has a "Tools" attribution block from PR #56). Add a parallel "Frameworks" block. Day-one entry:

```
== Frameworks (catalog metadata only, never bundled) ==

This launcher recognizes the following frameworks but does NOT bundle their binaries.
The actual framework files come from the upstream author's distribution; the launcher
only knows how to install / detect / surface them.

- Elden Mod Loader — by TechieW
  Source: https://www.nexusmods.com/eldenring/mods/117
  License: see the project's distribution page
  Used by: the launcher's framework intake catalog (KnownFramework)
```

- [ ] **Step 2: Append a smoke entry to docs/smoke-tests/pending.md**

```markdown
## PR #?? — Framework intake (Elden Mod Loader) (merged YYYY-MM-DD)

**Shipped:** Per docs/superpowers/specs/2026-05-27-framework-intake-design.md:
- ER chip text renamed from "NEEDS DLL PROXY..." to "NEEDS ELDEN MOD LOADER" + Nexus link.
- Drop-zip Pre-check 4 routes recognized framework zips through a confirmation dialog +
  install-at-game-root flow with backup. Day-one catalog entry: Elden Mod Loader.
- Looks-like-framework (DLL proxy at zip root, no catalog match, FromSoft only) gets a
  feedback nudge.
- Settings → Installed frameworks lists + uninstalls every framework the launcher installed.

**Smoke steps:**
- [ ] Open ER without Elden Mod Loader installed → chip on every direct-inject mod row reads "NEEDS ELDEN MOD LOADER"; clicking opens https://www.nexusmods.com/eldenring/mods/117.
- [ ] Drop the ELM zip → confirmation dialog opens showing the file list + author → confirm → toast "Installed Elden Mod Loader (N files at game root)" → chip disappears on next reload.
- [ ] Settings → Installed frameworks → ELM row visible with install date + path + Get-link → Uninstall → ELM files gone from game root; backup (if any) restored; chip returns.
- [ ] Drop a zip with `winhttp.dll` at the root that ISN'T ELM → feedback nudge dialog → "Open feedback link" launches the GitHub issue template.
- [ ] Drop a zip that has `eldenring.exe` inside → install refused with a toast naming the forbidden path; no files extracted.
```

- [ ] **Step 3: Commit**

```bash
git add NOTICE docs/smoke-tests/pending.md
git commit -m "docs(frameworks): NOTICE attribution + smoke entry for framework intake

Honor-the-builders: explicit 'metadata only, never bundled' language for Elden Mod
Loader. Smoke entry covers the five cases that unit tests can't reach (real dialog
flow, real install, real uninstall, looks-like nudge, forbidden-path refusal)."
```

---

## Task 11: Final cross-task review

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — original 753 + ~12 new framework tests.

- [ ] **Step 2: Publish a smoke build**

Run: `dotnet publish src/ModManager.App/ModManager.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishReadyToRun=false`
Verify the publish folder has `resources.pri` + `vcruntime140*.dll`. Re-zip the publish folder into `dist/626-Mod-Launcher-portable-win-x64.zip` for Este to smoke.

- [ ] **Step 3: Open the PR**

Branch off master via `git checkout -b feat/framework-intake origin/master` before starting Task 1. After all tasks, open the PR with `gh pr create`, body referencing the spec + the smoke list.

---

## Self-review against the spec

**Spec coverage:**

- Chip rename + GetUrl + Note update — Task 1 ✓
- KnownFramework record with all fields from spec — Task 2 ✓
- Classify with ELM signature match + looks-like-framework heuristic — Task 3 ✓
- FrameworkInstaller with backup + manifest + forbidden-paths — Task 4 ✓
- FrameworkRegistry.List + Uninstall — Task 5 ✓
- Confirmation dialog — Task 6 ✓
- Feedback nudge — Task 7 ✓
- Pre-check 4 wired into AddModsAsync — Task 8 ✓
- Settings UX — Task 9 ✓
- NOTICE + smoke list — Task 10 ✓

**Placeholder scan:** All steps have concrete code, exact paths, exact commands. The only "match the actual variable" placeholder is in Task 8 Step 1 (`pathsAfterTool`) — the implementer reads the existing pre-check-3 code to pick up the real name. That's a one-line lookup, not an open question.

**Type consistency:** `FrameworkInstallManifest` is used by both `FrameworkInstaller` (Task 4) and `FrameworkRegistry` (Task 5) — field shapes match. `InstalledFrameworkRow` is App-only (Task 9). `KnownFramework.ClassifyResult` is Core-only and is used in Task 8.
