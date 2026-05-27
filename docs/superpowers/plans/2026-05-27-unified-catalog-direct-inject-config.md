# Unified-Catalog Phase 1: Direct-Inject Mod Config Discovery (F3) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship F3 from [`docs/superpowers/specs/2026-05-27-unified-catalog-direct-inject-config-design.md`](../specs/2026-05-27-unified-catalog-direct-inject-config-design.md). Phase 1: migrate `DirectInject.Catalog` to a new kind-tagged `KnownDirectInjectMod` schema (foundation for unifying Tools/Frameworks later), add `ConfigPaths` per entry, and drive the pencil icon for direct-inject mods (Seamless Co-op's INI) from the catalog instead of the never-fired `*.ini` glob (direct-inject rows have no `folderAbs`).

**Architecture:** New `ModManager.Core.Catalog.KnownDirectInjectMod` record + Catalog. Existing private `DirectInject.Signature` is replaced (its detection logic is field-rename-only). New `DirectInjectModConfigResolver` resolves `ConfigPaths` (catalog + user override) to absolute paths that exist on disk. App row builder populates `IniFiles` from the resolver for direct-inject rows.

**Tech Stack:** C# / .NET 10, WinUI 3, xUnit, FsAtomic (existing JSON-write helper).

---

## File Structure

**Create (Core):**

- `src/ModManager.Core/Catalog/KnownDirectInjectMod.cs` — record + static `Catalog` (6 entries migrated from existing `Signature` + new `ConfigPaths` field)
- `src/ModManager.Core/Catalog/DirectInjectConfigOverrides.cs` — per-game user-override storage (Load/Save, atomic JSON)
- `src/ModManager.Core/Catalog/DirectInjectModConfigResolver.cs` — resolve config paths for a mod, applying override + catalog default + filesystem existence

**Create (Tests):**

- `tests/ModManager.Tests/Catalog/KnownDirectInjectModTests.cs`
- `tests/ModManager.Tests/Catalog/DirectInjectConfigOverridesTests.cs`
- `tests/ModManager.Tests/Catalog/DirectInjectModConfigResolverTests.cs`

**Modify:**

- `src/ModManager.Core/DirectInject.cs:32-50` — replace private `Signature` + `Catalog` with `KnownDirectInjectMod.Catalog` references; refactor `MatchSignaturesInZip`, `Detect`, `DetectLoaderMods` (field-rename mechanical)
- `src/ModManager.App/ViewModels/MainViewModel.cs:318-331` — populate `IniFiles` for direct-inject rows via the resolver
- `src/ModManager.App/SettingsDialog.xaml` + `.xaml.cs` — add "Direct-inject mod configs" section with per-mod override path picker (minimum viable)
- `docs/smoke-tests/pending.md` — add F3 smoke entry

---

## Task 1: Define `KnownDirectInjectMod` record + day-one Catalog

**Files:**

- Create: `src/ModManager.Core/Catalog/KnownDirectInjectMod.cs`
- Test: `tests/ModManager.Tests/Catalog/KnownDirectInjectModTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class KnownDirectInjectModTests
{
    [Fact]
    public void Catalog_ships_Seamless_with_known_config_paths()
    {
        var seamless = KnownDirectInjectMod.Catalog.Single(m => m.ModId == "seamless-coop");

        Assert.Equal("Seamless Co-op", seamless.DisplayName);
        Assert.Equal("co-op", seamless.ChipKind);
        Assert.Equal("fromsoft", seamless.Engine);
        Assert.Equal("Yui", seamless.Author);
        Assert.Equal("PlayFolder", seamless.InstallRoot);
        // The ER+Seamless INI path the user edits before every session.
        Assert.Contains("SeamlessCoop/seamlesscoopsettings.ini", seamless.ConfigPaths);
        // The on-disk signature that proves Seamless is installed.
        Assert.Contains("ersc.dll", seamless.InstallSignatureFiles);
        Assert.Contains("seamlesscoop", seamless.InstallSignatureDirs);
    }

    [Fact]
    public void Catalog_includes_six_directinject_mods()
    {
        var modIds = KnownDirectInjectMod.Catalog.Select(m => m.ModId).ToList();
        Assert.Contains("reshade", modIds);
        Assert.Contains("seamless-coop", modIds);
        Assert.Contains("erss2-frame-gen", modIds);
        Assert.Contains("ultrawide-fix", modIds);
        Assert.Contains("modded-regulation", modIds);
        Assert.Contains("dll-mod-loader", modIds);
    }

    [Fact]
    public void Every_entry_has_required_fields()
    {
        foreach (var m in KnownDirectInjectMod.Catalog)
        {
            Assert.Equal("directInjectMod", m.Kind);
            Assert.False(string.IsNullOrWhiteSpace(m.ModId));
            Assert.False(string.IsNullOrWhiteSpace(m.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(m.ChipKind));
            Assert.False(string.IsNullOrWhiteSpace(m.Engine));
            Assert.False(string.IsNullOrWhiteSpace(m.InstallRoot));
        }
    }
}
```

- [ ] **Step 2: Run to verify failures (type doesn't exist)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~KnownDirectInjectMod"`

- [ ] **Step 3: Create the record + catalog**

Create `src/ModManager.Core/Catalog/KnownDirectInjectMod.cs`:

```csharp
namespace ModManager.Core.Catalog;

/// <summary>
/// A direct-inject mod the launcher knows about. Phase 1 of the unified mod/tool/framework
/// catalog — the <c>Kind</c> field is "directInjectMod" today; later phases will fold Tools
/// ("tool") and Frameworks ("framework") into the same schema.
///
/// Replaces the old private <c>DirectInject.Signature</c> record. Detection field names rename
/// one-to-one (<c>Files</c> -> <c>InstallSignatureFiles</c>, etc.). New fields:
/// <see cref="ConfigPaths"/> drives the pencil icon (catalog-known INI/TOML/JSON paths);
/// <see cref="ForbiddenOverridePaths"/> + <see cref="UserOverrideKey"/> + <see cref="InstallRoot"/>
/// support user-configurable install/config locations with safety gating.
///
/// Pure data — detection logic stays in <see cref="DirectInject"/>; config resolution lives in
/// <see cref="DirectInjectModConfigResolver"/>.
/// </summary>
public sealed record KnownDirectInjectMod(
    string Kind,
    string ModId,
    string DisplayName,
    string ChipKind,
    string Author,
    string Engine,
    string? SteamAppId,
    string? GetUrl,
    IReadOnlyList<string> InstallSignatureFiles,
    IReadOnlyList<string> InstallSignatureDirs,
    IReadOnlyList<string> InstallSignatureContains,
    string InstallRoot,
    IReadOnlyList<string> ConfigPaths,
    IReadOnlyList<string> ForbiddenOverridePaths)
{
    /// <summary>
    /// Day-one catalog. Migrated from <c>DirectInject.Signature</c> array; same detection
    /// behavior. New: <see cref="ConfigPaths"/> for the pencil icon.
    /// </summary>
    public static IReadOnlyList<KnownDirectInjectMod> Catalog { get; } = new[]
    {
        Mk(modId: "reshade", display: "ReShade", chip: "graphics", author: "crosire",
           files: new[] { "reshadepreset.ini", "reshade.ini" },
           dirs: new[] { "reshade-shaders" },
           configs: new[] { "reshade.ini", "reshadepreset.ini" }),

        Mk(modId: "seamless-coop", display: "Seamless Co-op", chip: "co-op", author: "Yui",
           getUrl: "https://www.nexusmods.com/eldenring/mods/510",
           files: new[] { "ersc.dll", "ersc_settings.ini", "launch_elden_ring_seamlesscoop.exe" },
           dirs: new[] { "seamlesscoop" },
           configs: new[] { "SeamlessCoop/seamlesscoopsettings.ini", "ersc_settings.ini" }),

        Mk(modId: "erss2-frame-gen", display: "ERSS2 Frame Gen", chip: "upscaler", author: "(unknown)",
           files: new[] { "erss-fg.dll", "erss-fg.toml", "erss2loader.log" },
           dirs: new[] { "erss2" },
           configs: new[] { "erss-fg.toml" }),

        Mk(modId: "ultrawide-fix", display: "Ultrawide / Widescreen Fix", chip: "display",
           author: "(community)",
           contains: new[] { "ultrawide", "widescreen" }),

        Mk(modId: "modded-regulation", display: "Modded regulation.bin", chip: "gameplay",
           author: "(varies)",
           files: new[] { "regulation.bin" }),

        Mk(modId: "dll-mod-loader", display: "DLL mod loader", chip: "dll", author: "(community)",
           files: new[] { "dinput8.dll" }),
    };

    private static KnownDirectInjectMod Mk(
        string modId, string display, string chip, string author,
        string? getUrl = null,
        string[]? files = null, string[]? dirs = null, string[]? contains = null,
        string[]? configs = null)
        => new(
            Kind: "directInjectMod",
            ModId: modId,
            DisplayName: display,
            ChipKind: chip,
            Author: author,
            Engine: "fromsoft",
            SteamAppId: null,
            GetUrl: getUrl,
            InstallSignatureFiles: files ?? Array.Empty<string>(),
            InstallSignatureDirs: dirs ?? Array.Empty<string>(),
            InstallSignatureContains: contains ?? Array.Empty<string>(),
            InstallRoot: "PlayFolder",
            ConfigPaths: configs ?? Array.Empty<string>(),
            ForbiddenOverridePaths: Array.Empty<string>());
}
```

- [ ] **Step 4: Run to verify all three tests pass**

Expected: PASS — all 3 catalog tests green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Catalog/KnownDirectInjectMod.cs tests/ModManager.Tests/Catalog/KnownDirectInjectModTests.cs
git commit -m "feat(catalog): KnownDirectInjectMod + 6 day-one entries with ConfigPaths

Phase 1 of the unified mod/tool/framework catalog. Kind-tagged record ('directInjectMod'
today; 'tool' and 'framework' fold in later phases). Migrated detection field shape
from DirectInject.Signature; ADDED ConfigPaths for the pencil icon.

Seamless Co-op's ConfigPaths: SeamlessCoop/seamlesscoopsettings.ini + ersc_settings.ini.

Per docs/superpowers/plans/2026-05-27-unified-catalog-direct-inject-config.md."
```

---

## Task 2: Refactor DirectInject internals to use the new catalog

**Files:**

- Modify: `src/ModManager.Core/DirectInject.cs` (replace private `Signature` + `Catalog` references with `KnownDirectInjectMod.Catalog`)

- [ ] **Step 1: Run the existing DirectInject test suite to establish the contract**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DirectInject"`
Expected: all green. Note the count — that's the contract Task 2 preserves.

- [ ] **Step 2: Remove private `Signature` record + `Catalog` array + `Sig` helper**

Delete lines 30-50 of `DirectInject.cs` (the `Signature` record, the `Catalog` array, and the `Sig` helper).

- [ ] **Step 3: Add a using for the new catalog + refactor `MatchSignaturesInZip` + `Detect` + `DetectLoaderMods` to iterate `KnownDirectInjectMod.Catalog`**

At the top of `DirectInject.cs`:

```csharp
using ModManager.Core.Catalog;
```

Replace `foreach (var sig in Catalog)` with `foreach (var sig in KnownDirectInjectMod.Catalog)`. Replace `sig.Files`, `sig.Dirs`, `sig.FileContains` with `sig.InstallSignatureFiles`, `sig.InstallSignatureDirs`, `sig.InstallSignatureContains`. Replace `sig.Name` with `sig.DisplayName` and `sig.Kind` with `sig.ChipKind`.

In `DetectLoaderMods`, the only `Catalog` reference is for resolving the "DLL mod loader" entry — same rename treatment.

- [ ] **Step 4: Re-run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: every test still passes (Task 2 is mechanical refactor).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/DirectInject.cs
git commit -m "refactor(direct-inject): use KnownDirectInjectMod.Catalog (field-rename only)

Detection behavior unchanged — every existing test still passes. The private
Signature record + Catalog array are gone; replaced by the public
KnownDirectInjectMod.Catalog. Field renames (Files -> InstallSignatureFiles, etc.)
applied uniformly in MatchSignaturesInZip / Detect / DetectLoaderMods."
```

---

## Task 3: `DirectInjectConfigOverrides` — per-game override storage

**Files:**

- Create: `src/ModManager.Core/Catalog/DirectInjectConfigOverrides.cs`
- Test: `tests/ModManager.Tests/Catalog/DirectInjectConfigOverridesTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class DirectInjectConfigOverridesTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-overrides-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var result = DirectInjectConfigOverrides.Load(gameData);

        Assert.Empty(result.OverridesByModId);
    }

    [Fact]
    public void Save_then_Load_round_trips_camelCase()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var pre = new DirectInjectConfigOverrides(new Dictionary<string, Dictionary<string, string>>
        {
            ["seamless-coop"] = new()
            {
                ["SeamlessCoop/seamlesscoopsettings.ini"] = "D:/elsewhere/seamless.ini",
            },
        });

        DirectInjectConfigOverrides.Save(gameData, pre);

        var path = Path.Combine(gameData, "direct-inject", "config-overrides.json");
        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"overridesByModId\":", json);

        var post = DirectInjectConfigOverrides.Load(gameData);
        Assert.Equal("D:/elsewhere/seamless.ini",
            post.OverridesByModId["seamless-coop"]["SeamlessCoop/seamlesscoopsettings.ini"]);
    }

    [Fact]
    public void Load_tolerates_unreadable_JSON_by_returning_empty()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        var dir = Path.Combine(gameData, "direct-inject");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config-overrides.json"), "{ not valid json");

        var result = DirectInjectConfigOverrides.Load(gameData);

        Assert.Empty(result.OverridesByModId);
    }
}
```

- [ ] **Step 2: Implement `DirectInjectConfigOverrides.cs`**

```csharp
using System.Text.Json;

namespace ModManager.Core.Catalog;

/// <summary>
/// Per-game user overrides for direct-inject mod config-file paths. Persisted at
/// <c>&lt;gameData&gt;/direct-inject/config-overrides.json</c>. Empty/missing/unreadable file
/// is treated as "no overrides" — never throws on read. Atomic temp+rename on write.
/// </summary>
public sealed record DirectInjectConfigOverrides(
    IReadOnlyDictionary<string, Dictionary<string, string>> OverridesByModId)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static DirectInjectConfigOverrides Empty { get; } =
        new(new Dictionary<string, Dictionary<string, string>>());

    public static DirectInjectConfigOverrides Load(string gameDataDir)
    {
        var path = Path.Combine(gameDataDir, "direct-inject", "config-overrides.json");
        if (!File.Exists(path)) return Empty;
        try
        {
            var doc = JsonSerializer.Deserialize<DirectInjectConfigOverrides>(File.ReadAllText(path), Json);
            return doc ?? Empty;
        }
        catch { return Empty; }
    }

    public static void Save(string gameDataDir, DirectInjectConfigOverrides overrides)
    {
        var dir = Path.Combine(gameDataDir, "direct-inject");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config-overrides.json");
        var json = JsonSerializer.Serialize(overrides, Json);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DirectInjectConfigOverrides"`
Expected: 3 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/Catalog/DirectInjectConfigOverrides.cs tests/ModManager.Tests/Catalog/DirectInjectConfigOverridesTests.cs
git commit -m "feat(catalog): DirectInjectConfigOverrides — per-game user overrides for INI paths

camelCase JSON at <gameData>/direct-inject/config-overrides.json. Load is tolerant of
missing/unreadable files (returns Empty). Save is atomic temp+rename. Lets users point
the launcher at a Seamless INI that isn't where the catalog defaults expect."
```

---

## Task 4: `DirectInjectModConfigResolver`

**Files:**

- Create: `src/ModManager.Core/Catalog/DirectInjectModConfigResolver.cs`
- Test: `tests/ModManager.Tests/Catalog/DirectInjectModConfigResolverTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class DirectInjectModConfigResolverTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-resolve-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Resolve_returns_existing_default_path_for_Seamless()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameFolder = Path.Combine(gameRoot, "Game");
        var seamlessFolder = Path.Combine(gameFolder, "SeamlessCoop");
        Directory.CreateDirectory(seamlessFolder);
        var iniPath = Path.Combine(seamlessFolder, "seamlesscoopsettings.ini");
        File.WriteAllText(iniPath, "test");

        var result = DirectInjectModConfigResolver.Resolve(
            "Seamless Co-op", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Contains(iniPath, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_uses_override_when_present()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var customDir = Path.Combine(_tmp, "Elsewhere");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        Directory.CreateDirectory(customDir);
        var customIni = Path.Combine(customDir, "custom-seamless.ini");
        File.WriteAllText(customIni, "test");

        var overrides = new DirectInjectConfigOverrides(new Dictionary<string, Dictionary<string, string>>
        {
            ["seamless-coop"] = new()
            {
                ["SeamlessCoop/seamlesscoopsettings.ini"] = customIni,
            },
        });

        var result = DirectInjectModConfigResolver.Resolve("Seamless Co-op", gameRoot, overrides);

        Assert.Contains(customIni, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_skips_paths_that_dont_exist_on_disk()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        // No SeamlessCoop folder created — the default config path won't exist.

        var result = DirectInjectModConfigResolver.Resolve(
            "Seamless Co-op", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_returns_empty_for_unknown_mod_name()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(gameRoot);

        var result = DirectInjectModConfigResolver.Resolve(
            "Definitely Not A Real Mod", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Implement `DirectInjectModConfigResolver.cs`**

```csharp
namespace ModManager.Core.Catalog;

/// <summary>
/// Resolve the absolute on-disk paths of a direct-inject mod's known config files. For each
/// <see cref="KnownDirectInjectMod.ConfigPaths"/> entry, an override (if set) wins; else the
/// path is computed relative to the mod's resolved install root (<c>PlayFolder</c> for
/// FromSoft). Only paths that ACTUALLY exist on disk are returned.
/// </summary>
public static class DirectInjectModConfigResolver
{
    public static IReadOnlyList<string> Resolve(
        string modDisplayName, string gameRoot, DirectInjectConfigOverrides overrides)
    {
        var entry = KnownDirectInjectMod.Catalog.FirstOrDefault(m => m.DisplayName == modDisplayName);
        if (entry is null) return Array.Empty<string>();

        var installRoot = ResolveInstallRoot(entry.InstallRoot, gameRoot);
        overrides.OverridesByModId.TryGetValue(entry.ModId, out var modOverrides);

        var resolved = new List<string>();
        foreach (var rel in entry.ConfigPaths)
        {
            string abs;
            if (modOverrides is not null && modOverrides.TryGetValue(rel, out var custom))
                abs = custom;
            else
                abs = Path.Combine(installRoot, rel);

            if (File.Exists(abs)) resolved.Add(abs);
        }
        return resolved;
    }

    private static string ResolveInstallRoot(string installRootSymbol, string gameRoot)
    {
        return installRootSymbol switch
        {
            "GameRoot" => gameRoot,
            "PlayFolder" => ResolvePlayFolder(gameRoot),
            _ => gameRoot,
        };
    }

    private static string ResolvePlayFolder(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return gameRoot;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DirectInjectModConfigResolver"`
Expected: 4 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/Catalog/DirectInjectModConfigResolver.cs tests/ModManager.Tests/Catalog/DirectInjectModConfigResolverTests.cs
git commit -m "feat(catalog): DirectInjectModConfigResolver

Looks up KnownDirectInjectMod.Catalog by display name, resolves InstallRoot (PlayFolder
-> <gameRoot>/Game), applies user override per ConfigPath if set, returns only paths
that exist on disk. Pure-core, no Electron/WinUI."
```

---

## Task 5: Hook the resolver into the row builder

**Files:**

- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs:318-331`

- [ ] **Step 1: Load overrides + resolve config paths for direct-inject rows**

In `ReloadModsAsync`, BEFORE the foreach loop builds rows, load the overrides once:

```csharp
var directInjectOverrides = ModManager.Core.Catalog.DirectInjectConfigOverrides.Load(_ctx.DataDir);
```

Then, replace the existing INI globbing block:

```csharp
// (existing lines 320-331)
IReadOnlyList<string> iniFiles = Array.Empty<string>();
if (!string.IsNullOrEmpty(folderAbs) && Directory.Exists(folderAbs))
{
    try
    {
        iniFiles = Directory.EnumerateFiles(folderAbs, "*.ini", SearchOption.AllDirectories)
            .Take(20)
            .ToArray();
    }
    catch { /* leave empty on enumerate failure */ }
}
```

…with the catalog-aware branch:

```csharp
IReadOnlyList<string> iniFiles = Array.Empty<string>();
if (rep.Location == "direct-inject")
{
    // Direct-inject rows have no mod folder — the *.ini glob never fires. Catalog-known
    // INIs (Seamless's seamlesscoopsettings.ini, ReShade's reshade.ini, etc.) come from
    // KnownDirectInjectMod.ConfigPaths via the resolver, with per-user overrides applied.
    iniFiles = ModManager.Core.Catalog.DirectInjectModConfigResolver
        .Resolve(rep.Name, _ctx.GameRoot, directInjectOverrides);
}
else if (!string.IsNullOrEmpty(folderAbs) && Directory.Exists(folderAbs))
{
    try
    {
        iniFiles = Directory.EnumerateFiles(folderAbs, "*.ini", SearchOption.AllDirectories)
            .Take(20)
            .ToArray();
    }
    catch { /* leave empty on enumerate failure */ }
}
```

- [ ] **Step 2: Build + test**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: both clean.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(catalog): row builder uses catalog resolver for direct-inject INI discovery

Direct-inject rows had folderAbs='' so the *.ini glob never fired -> no pencil icon.
Replace that path with KnownDirectInjectMod.ConfigPaths resolution. Folder-mod rows
keep the existing glob behavior unchanged."
```

---

## Task 6: Settings → Direct-inject config overrides UX (minimum viable)

**Files:**

- Modify: `src/ModManager.App/SettingsDialog.xaml` (add a new section parallel to "Installed frameworks")
- Modify: `src/ModManager.App/SettingsDialog.xaml.cs` (loader + override picker handler)

- [ ] **Step 1: Add the XAML section**

Locate the "Installed frameworks" section (added in F2 / PR #62) and add this one below it:

```xaml
<TextBlock Text="Direct-inject mod configs" FontWeight="SemiBold" Margin="0,16,0,4" />
<TextBlock Opacity="0.55" FontSize="12" TextWrapping="Wrap"
           Text="Per-mod config-file path overrides for direct-inject mods (Seamless Co-op, ReShade, etc.). Leave blank to use the catalog default — Override when your install is in an unusual location." />
<ItemsRepeater x:Name="DirectInjectConfigsList">
    <ItemsRepeater.Layout>
        <StackLayout Orientation="Vertical" Spacing="8" />
    </ItemsRepeater.Layout>
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="local:DirectInjectConfigRow">
            <Grid ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" VerticalAlignment="Center">
                    <TextBlock Text="{x:Bind Title}" FontWeight="SemiBold" />
                    <TextBlock Text="{x:Bind Subtitle}" Opacity="0.6" FontSize="12" TextWrapping="Wrap" />
                </StackPanel>
                <Button Grid.Column="1" Content="Override…" Tag="{x:Bind}"
                        Click="OnDirectInjectOverrideClick" VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
<TextBlock x:Name="DirectInjectConfigsEmpty"
           Text="No direct-inject mods detected for the active game yet."
           Opacity="0.55" FontSize="12" Visibility="Collapsed" />
```

- [ ] **Step 2: Add the row record + loader + click handler**

In `SettingsDialog.xaml.cs`, after the existing `InstalledFrameworkRow` record:

```csharp
/// <summary>One row in the Settings → Direct-inject mod configs list. Subtitle shows the
/// catalog default OR the user's override (whichever is active).</summary>
public sealed record DirectInjectConfigRow(
    string ModId,
    string DisplayName,
    string RelativeConfigPath,
    string EffectivePath,
    string Title,
    string Subtitle);
```

Add `RefreshDirectInjectConfigs()` modeled after `RefreshInstalledFrameworks()`. Enumerate catalog entries for the active game's engine, for each one's `ConfigPaths` compute the effective path (override if set, else catalog default), build one row per (mod × config). Call it from the constructor alongside `RefreshInstalledFrameworks()`.

Handler:

```csharp
private async void OnDirectInjectOverrideClick(object sender, RoutedEventArgs e)
{
    if (sender is not Button btn || btn.Tag is not DirectInjectConfigRow row) return;

    var picker = new Windows.Storage.Pickers.FileOpenPicker();
    WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
    picker.FileTypeFilter.Add(".ini");
    picker.FileTypeFilter.Add(".toml");
    picker.FileTypeFilter.Add(".cfg");
    picker.FileTypeFilter.Add("*");
    var file = await picker.PickSingleFileAsync();
    if (file is null) return;

    var dataDir = _vm.GameDataDirPublic();
    if (string.IsNullOrEmpty(dataDir)) return;

    var current = ModManager.Core.Catalog.DirectInjectConfigOverrides.Load(dataDir);
    var newMap = current.OverridesByModId.ToDictionary(
        kv => kv.Key, kv => new Dictionary<string, string>(kv.Value));
    if (!newMap.TryGetValue(row.ModId, out var modOverrides))
    {
        modOverrides = new Dictionary<string, string>();
        newMap[row.ModId] = modOverrides;
    }
    modOverrides[row.RelativeConfigPath] = file.Path;

    ModManager.Core.Catalog.DirectInjectConfigOverrides.Save(
        dataDir, new ModManager.Core.Catalog.DirectInjectConfigOverrides(newMap));

    Changed = true;  // re-render mod rows on close
    RefreshDirectInjectConfigs();
    StatusText.Text = $"Override saved for {row.DisplayName}.";
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -c Debug -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/SettingsDialog.xaml src/ModManager.App/SettingsDialog.xaml.cs
git commit -m "feat(catalog): Settings -> Direct-inject mod configs section + override picker

Minimum viable UX: per (mod, config-path) row with display name + effective path +
'Override…' button (file picker). Saved overrides flow through DirectInjectConfigOverrides
and pick up on next ReloadModsAsync via Changed=true."
```

---

## Task 7: Smoke entry

**Files:**

- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Append a smoke block at the end of pending.md**

```markdown

---

## PR #?? — Unified-catalog Phase 1: direct-inject mod config discovery (F3) (merged YYYY-MM-DD)

**Shipped:** Per [`docs/superpowers/specs/2026-05-27-unified-catalog-direct-inject-config-design.md`](../superpowers/specs/2026-05-27-unified-catalog-direct-inject-config-design.md):

- New `KnownDirectInjectMod` schema (kind-tagged; future Phase 1b folds Tools + Frameworks into it).
- Migrated `DirectInject.Catalog` — same detection behavior, plus a new `ConfigPaths` field per entry. Seamless Co-op's path: `SeamlessCoop/seamlesscoopsettings.ini` + `ersc_settings.ini`.
- `DirectInjectModConfigResolver` — looks up a mod's known config files, applies per-user override, returns only paths that exist on disk.
- Row builder hook — direct-inject rows now get a pencil icon when the catalog's known INI exists on disk (Seamless Co-op specifically).
- Settings → Direct-inject mod configs — minimum viable override UX. Per-row "Override…" file picker; saved override re-renders rows on dialog close.

**Smoke steps:**

- [ ] On ER with Seamless Co-op installed → mod list shows the Seamless Co-op row → pencil icon visible → click → INI editor opens with the actual seamlesscoopsettings.ini contents → edit + save → `.bak` lands under `<gameData>/.ini-history/seamless-coop/`.
- [ ] Same with a manual install in an unusual location: drop the standard Seamless install, manually move the INI to a different drive (e.g. `D:\some-other-place\settings.ini`). Pencil icon disappears (catalog default no longer resolves). Settings → Direct-inject mod configs → click "Override…" → pick the moved INI. Re-open the mod list → pencil icon back; clicking edits the override location.
- [ ] On ER WITHOUT Seamless installed → no Seamless row, no pencil icon, no errors.
- [ ] Folder-tracked mods on any engine still get their existing recursive `*.ini` glob behavior unchanged.

**Why these matter:** the resolver path is unit-tested but the row-render hook + Settings picker integration only exercise on a real Windows machine with a real Seamless install.
```

- [ ] **Step 2: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(f3): smoke entry for unified-catalog Phase 1"
```

---

## Task 8: Final review + portable + PR

- [ ] **Step 1: Run full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: 780 original + ~12 new = ~792 passing.

- [ ] **Step 2: Publish portable**

Run: `dotnet publish src/ModManager.App/ModManager.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishReadyToRun=false`
Re-zip into `dist/626-Mod-Launcher-portable-win-x64.zip`.

- [ ] **Step 3: Open PR**

Branch was `feat/known-direct-inject-mod-catalog`. Open the PR referencing the spec + plan + smoke entry.

---

## Self-review against the spec

**Spec coverage:**

- `KnownDirectInjectMod` record with kind tag + all fields — Task 1 ✓
- Migration of DirectInject.Catalog (all 6 entries) — Task 1 + Task 2 ✓
- Per-mod user overrides at `<gameData>/direct-inject/config-overrides.json` (camelCase atomic JSON) — Task 3 ✓
- `DirectInjectModConfigResolver` (catalog + override + filesystem check) — Task 4 ✓
- Row builder hook — Task 5 ✓
- Settings UX — Task 6 ✓
- Smoke entry — Task 7 ✓

**Deferred (explicit, per spec):**

- ForbiddenOverridePaths validation enforcement at Save time. The schema carries the field (Task 1) but the per-game-context interpolation (`${gameRoot}/${exeName}`, `${gameData}/**`) isn't wired in Phase 1. Day-one direct-inject mods have empty `ForbiddenOverridePaths` so there's nothing to enforce — Phase 1b adds this when entries grow forbidden paths.
- Migrating Tools/Frameworks to the unified schema — explicit out-of-scope per spec.
- Glob fallback for INI discovery — explicit out-of-scope per spec.

**Placeholder scan:** All steps contain runnable code + exact commands. No TBDs.

**Type consistency:** `DirectInjectConfigOverrides` is used by Save+Load (Task 3), Resolver (Task 4), row builder (Task 5), Settings handler (Task 6). Same shape throughout (`OverridesByModId` keyed by `ModId` → inner dict keyed by relative `ConfigPath`).
