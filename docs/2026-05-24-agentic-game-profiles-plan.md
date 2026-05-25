# Agentic Game Profiles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user register any moddable game by feeding its name to an agent of their choice — the agent returns a structured JSON profile, the app validates + resolves it (Steam-detect install, Ludusavi-first saves, on-disk verify) and pre-fills the existing Add Game wizard for confirm/edit.

**Architecture:** Pure Core builds the prompt and validates the answer (`GameProfilePrompt`, `GameProfileImport`) — the proven theme-generator pattern. The App resolves the validated draft to machine paths (`GameProfileResolver`) and pre-fills the existing `AddGameDialog`, which is extended with the save + launcher fields the full profile needs (manual add gets them too). No absolute path is ever in the profile; the app resolves all structure locally.

**Tech Stack:** .NET 10, C#, xUnit, System.Text.Json, System.IO. WinUI 3 (App layer).

Spec: [docs/2026-05-24-agentic-game-profiles-design.md](2026-05-24-agentic-game-profiles-design.md)

> **Run tests with the explicit project** (a bare `dotnet test` hangs building WinUI):
> `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App builds: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Use ABSOLUTE paths (cwd may reset). Conventional commits ending `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`. `ModManager.Core` stays pure (no UI refs).

---

## File Structure

- Create: `src/ModManager.Core/GameProfilePrompt.cs` — the prompt builder (twin of `ThemePrompt`).
- Create: `src/ModManager.Core/GameProfileImport.cs` — `GameProfileDraft`, `ProfileImportResult`, `Load` (parse + validate).
- Modify: `src/ModManager.Core/GameEntry.cs` — `GameInput` gains `SaveRoot`/`SaveSubPath`/`RequiredLauncher`; `GameEntry` gains `RequiredLauncher`.
- Modify: `src/ModManager.Core/EnginePresets.cs` — `BuildGameEntry` maps `RequiredLauncher`.
- Create: `src/ModManager.App/Services/GameProfileResolver.cs` — resolve to machine paths + on-disk verify.
- Modify: `src/ModManager.App/AddGameDialog.xaml` + `.xaml.cs` — save/launcher fields + the "Add with AI" pre-fill flow.
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (or the dialog host) — wire the resolved `SaveDir` onto the entry at register.
- Tests: `tests/ModManager.Tests/GameProfilePromptTests.cs`, `tests/ModManager.Tests/GameProfileImportTests.cs`.

---

## Task 1: GameProfilePrompt (the prompt builder)

**Files:** Create `src/ModManager.Core/GameProfilePrompt.cs`; test `tests/ModManager.Tests/GameProfilePromptTests.cs`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class GameProfilePromptTests
{
    [Fact]
    public void Build_pins_the_contract_for_the_named_game()
    {
        var p = GameProfilePrompt.Build("Skyrim Special Edition");
        Assert.Contains("Skyrim Special Edition", p);
        Assert.Contains("engine", p);
        Assert.Contains("bethesda", p);          // an EnginePresets key is listed
        Assert.Contains("saveRoot", p);
        Assert.Contains("DocumentsMyGames", p);   // a save-root enum value is listed
        Assert.Contains("requiredLauncher", p);
        Assert.DoesNotContain("```", p);          // no markdown fences requested
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfilePromptTests"`
Expected: FAIL — `GameProfilePrompt` undefined.

- [ ] **Step 3: Write minimal implementation**

Create `src/ModManager.Core/GameProfilePrompt.cs`:

```csharp
namespace ModManager.Core;

/// <summary>
/// Builds the prompt a user hands to any LLM to author a game registration profile. The model
/// returns JSON on the structured contract (relative/enum values only), which the launcher
/// validates (<see cref="GameProfileImport"/>) and resolves. Twin of <see cref="ThemePrompt"/> —
/// not agentic: the app crafts the ask and validates the answer.
/// </summary>
public static class GameProfilePrompt
{
    public static string Build(string? gameName)
    {
        var g = string.IsNullOrWhiteSpace(gameName) ? "the game" : gameName.Trim();
        var engines = string.Join(", ", EnginePresets.Presets.Keys);
        return
            "You are filling a registration profile for a PC game mod launcher.\n" +
            $"Game: {g}\n\n" +
            "Return ONLY a single JSON object - no prose, no markdown fences. Use STRUCTURED, RELATIVE\n" +
            "values only - NEVER an absolute machine path like C:\\Users\\... The app resolves real paths.\n\n" +
            "Fields:\n" +
            "  name (string),\n" +
            $"  engine (one of: {engines}),\n" +
            "  windowTitle (string, optional),\n" +
            "  steamAppId (string of digits, optional),\n" +
            "  modPath (string, relative to the install folder; optional - omit to use the engine default),\n" +
            "  fileExtensions (array of strings, optional), groupingRule (string, optional),\n" +
            "  saveRoot (one of: DocumentsMyGames, AppData, LocalAppData, SteamUserData, GameInstall),\n" +
            "  saveSubPath (string, relative path under saveRoot),\n" +
            "  requiredLauncher (string, relative path to the launcher exe that must be used when modded; optional),\n" +
            "  launchTargets (array of objects { label, kind: \"steam\" or \"exe\", target, isDefault }; optional),\n" +
            "  curseforgeGameId (number, optional).\n\n" +
            "Rules: valid JSON only; engine and saveRoot must be from the lists above; every path relative.";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfilePromptTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/GameProfilePrompt.cs tests/ModManager.Tests/GameProfilePromptTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): GameProfilePrompt - build the agent prompt for a game profile"
```

---

## Task 2: GameProfileImport (parse + validate)

**Files:** Create `src/ModManager.Core/GameProfileImport.cs`; test `tests/ModManager.Tests/GameProfileImportTests.cs`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class GameProfileImportTests
{
    private const string Valid = """
    { "name":"Elden Ring","engine":"fromsoft","steamAppId":"1245620",
      "modPath":"Game/mod","saveRoot":"AppData","saveSubPath":"EldenRing",
      "requiredLauncher":"Game/ersc_launcher.exe" }
    """;

    [Fact]
    public void Valid_profile_loads_with_no_errors()
    {
        var r = GameProfileImport.Load(Valid);
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Draft);
        Assert.Equal("Elden Ring", r.Draft!.Name);
        Assert.Equal("fromsoft", r.Draft.Engine);
        Assert.Equal("AppData", r.Draft.SaveRoot);
        Assert.Equal("Game/ersc_launcher.exe", r.Draft.RequiredLauncher);
    }

    [Fact]
    public void Bad_json_is_rejected_with_a_reason()
    {
        var r = GameProfileImport.Load("{ not json ");
        Assert.Null(r.Draft);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Unknown_engine_is_rejected_listing_allowed_keys()
    {
        var r = GameProfileImport.Load("""{ "name":"X","engine":"frostbite","saveRoot":"AppData","saveSubPath":"X" }""");
        Assert.Contains(r.Errors, e => e.Contains("engine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveRoot_outside_the_enum_is_rejected()
    {
        var r = GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"Desktop","saveSubPath":"X" }""");
        Assert.Contains(r.Errors, e => e.Contains("saveRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("C:/abs/mod")]   // absolute
    [InlineData("../escape")]    // traversal
    [InlineData("/rooted")]      // drive-rooted
    public void Absolute_or_traversal_paths_are_rejected(string modPath)
    {
        var json = $$"""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X","modPath":"{{modPath}}" }""";
        var r = GameProfileImport.Load(json);
        Assert.Contains(r.Errors, e => e.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_required_fields_are_rejected()
    {
        var r = GameProfileImport.Load("""{ "engine":"bethesda" }"""); // no name/saveRoot/saveSubPath
        Assert.Contains(r.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Errors, e => e.Contains("saveRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_numeric_steamAppId_is_rejected_but_absent_is_ok()
    {
        Assert.Contains(GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X","steamAppId":"abc" }""").Errors,
            e => e.Contains("steamAppId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(GameProfileImport.Load("""{ "name":"X","engine":"bethesda","saveRoot":"AppData","saveSubPath":"X" }""").Errors);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileImportTests"`
Expected: FAIL — `GameProfileImport` / `GameProfileDraft` undefined.

- [ ] **Step 3: Write minimal implementation**

Create `src/ModManager.Core/GameProfileImport.cs`:

```csharp
using System.Text.Json;

namespace ModManager.Core;

/// <summary>An agent-authored game profile, parsed. All paths are relative/enum; the App resolves them.</summary>
public sealed record GameProfileDraft(
    string? Name, string? Engine, string? WindowTitle, string? SteamAppId,
    string? ModPath, IReadOnlyList<string>? FileExtensions, string? GroupingRule,
    string? SaveRoot, string? SaveSubPath, string? RequiredLauncher, int? CurseforgeGameId);

/// <summary>Result of loading a profile: a Draft (non-null only when Errors is empty) plus any errors.</summary>
public sealed record ProfileImportResult(GameProfileDraft? Draft, IReadOnlyList<string> Errors);

/// <summary>
/// Parses + validates an agent-authored game profile. Mirrors the Themes.NormalizeTheme contract:
/// bad input is rejected with reasons, never half-applied.
/// </summary>
public static class GameProfileImport
{
    public static readonly IReadOnlyList<string> SaveRoots =
        new[] { "DocumentsMyGames", "AppData", "LocalAppData", "SteamUserData", "GameInstall" };

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static ProfileImportResult Load(string json)
    {
        GameProfileDraft? d;
        try { d = JsonSerializer.Deserialize<GameProfileDraft>(json, Opts); }
        catch (JsonException e) { return new ProfileImportResult(null, new[] { "Not valid JSON: " + e.Message }); }
        if (d is null) return new ProfileImportResult(null, new[] { "Empty profile." });

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Name)) errors.Add("Missing required field: name.");
        if (string.IsNullOrWhiteSpace(d.Engine)) errors.Add("Missing required field: engine.");
        else if (!EnginePresets.Presets.ContainsKey(d.Engine))
            errors.Add($"Unknown engine '{d.Engine}'. Allowed: {string.Join(", ", EnginePresets.Presets.Keys)}.");
        if (string.IsNullOrWhiteSpace(d.SaveRoot)) errors.Add("Missing required field: saveRoot.");
        else if (!SaveRoots.Contains(d.SaveRoot))
            errors.Add($"Unknown saveRoot '{d.SaveRoot}'. Allowed: {string.Join(", ", SaveRoots)}.");
        if (string.IsNullOrWhiteSpace(d.SaveSubPath)) errors.Add("Missing required field: saveSubPath.");

        foreach (var (label, path) in new[] { ("modPath", d.ModPath), ("saveSubPath", d.SaveSubPath), ("requiredLauncher", d.RequiredLauncher) })
            if (!string.IsNullOrEmpty(path) && !IsSafeRelative(path!))
                errors.Add($"The {label} path must be relative (no absolute path, drive root, or '..'): {path}");

        if (!string.IsNullOrEmpty(d.SteamAppId) && !d.SteamAppId.All(char.IsDigit))
            errors.Add($"steamAppId must be digits only: {d.SteamAppId}");

        return new ProfileImportResult(errors.Count == 0 ? d : null, errors);
    }

    // Relative + safe: no drive root (C:\), no rooted slash, no '..' or empty segment.
    private static bool IsSafeRelative(string p)
    {
        var n = p.Replace('\\', '/').Trim();
        if (n.Length == 0) return false;
        if (n.StartsWith('/')) return false;                 // rooted
        if (n.Length > 1 && n[1] == ':') return false;       // drive-rooted (C:...)
        return !n.Split('/').Any(s => s is "" or "." or "..");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileImportTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/GameProfileImport.cs tests/ModManager.Tests/GameProfileImportTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): GameProfileImport - parse + validate an agent game profile"
```

---

## Task 3: Extend GameInput / GameEntry / BuildGameEntry

The wizard + builder need to carry the save root, save subpath, and required launcher the full profile adds.

**Files:** Modify `src/ModManager.Core/GameEntry.cs`, `src/ModManager.Core/EnginePresets.cs`; test in `tests/ModManager.Tests/GameProfileImportTests.cs`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void BuildGameEntry_carries_the_required_launcher()
{
    var input = new GameInput { Name = "Elden Ring", Engine = "fromsoft", GameRoot = @"C:\game",
        RequiredLauncher = "Game/ersc_launcher.exe" };
    var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
    Assert.Equal("Game/ersc_launcher.exe", entry.RequiredLauncher);
    Assert.Equal("fromsoft", entry.Engine);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BuildGameEntry_carries"`
Expected: FAIL — `GameInput.RequiredLauncher` / `GameEntry.RequiredLauncher` undefined.

- [ ] **Step 3: Write minimal implementation**

In `src/ModManager.Core/GameEntry.cs`, add to `GameEntry` (near `SaveDir`, ~line 49):

```csharp
    // relative path (under GameRoot) to the launcher that must be used when modded (e.g. Seamless Co-op)
    public string? RequiredLauncher { get; set; }
```

And add to `GameInput` (the wizard input record, ~line 64-76):

```csharp
    public string? SaveRoot { get; init; }
    public string? SaveSubPath { get; init; }
    public string? RequiredLauncher { get; init; }
```

In `src/ModManager.Core/EnginePresets.cs`, inside `BuildGameEntry`, before `return entry;` (~line 77):

```csharp
        if (!string.IsNullOrEmpty(input.RequiredLauncher)) entry.RequiredLauncher = input.RequiredLauncher;
```

(SaveDir stays resolved + set by the App layer from `SaveRoot`/`SaveSubPath`; `BuildGameEntry` only carries the launcher, which is already relative.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileImportTests"` (the new test + all prior still green).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.Core/GameEntry.cs src/ModManager.Core/EnginePresets.cs tests/ModManager.Tests/GameProfileImportTests.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): GameInput/GameEntry carry save root/subpath + required launcher"
```

---

## Task 4: GameProfileResolver (App: resolve to machine paths + verify on disk)

Turns a validated `GameProfileDraft` into resolved paths with pass/warn checks. App layer (uses `SteamService`, `LudusaviService`, `Environment`). Build-verified; the pure enum→root mapping is small and lives here.

**Files:** Create `src/ModManager.App/Services/GameProfileResolver.cs`.

- [ ] **Step 1: Read the existing services first**

Read `src/ModManager.App/Services/SteamService.cs` (how it finds an install path from a Steam App id, and the `InstalledGames()` shape), `src/ModManager.App/Services/LudusaviService.cs` + `SaveLocator.cs` (how saves are resolved by App id), and `src/ModManager.Core/EnginePresets.cs` (the preset `ModPath`). Match those APIs in the resolver below — adjust method names to what actually exists.

- [ ] **Step 2: Implement the resolver**

Create `src/ModManager.App/Services/GameProfileResolver.cs`:

```csharp
using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>A resolved profile field with an on-disk check for the wizard preview.</summary>
public sealed record ResolvedField(string Label, string? Path, ResolveStatus Status, string? Note = null);
public enum ResolveStatus { Pass, Warn, Missing }

/// <summary>The draft resolved to this machine's real paths + per-field verification.</summary>
public sealed record ResolvedProfile(
    string? GameRoot, string? ModFolder, string? SaveDir, string? LauncherPath,
    IReadOnlyList<ResolvedField> Checks);

/// <summary>
/// Resolves a validated <see cref="GameProfileDraft"/> to machine paths and verifies them on disk.
/// Structure -> real paths: GameRoot from Steam (or a caller-supplied browse), save dir Ludusavi-first
/// then the saveRoot enum, launcher under GameRoot. Warnings never block (mods may not be installed yet).
/// </summary>
public sealed class GameProfileResolver
{
    private readonly SteamService _steam;
    private readonly LudusaviService _ludu;

    public GameProfileResolver(SteamService steam, LudusaviService ludu) { _steam = steam; _ludu = ludu; }

    /// <summary>Resolve. <paramref name="browsedGameRoot"/> overrides Steam detection when the user picked a folder.</summary>
    public async Task<ResolvedProfile> ResolveAsync(GameProfileDraft d, string? browsedGameRoot)
    {
        var checks = new List<ResolvedField>();

        // GameRoot: browse wins; else Steam-detect by app id. (Match the real SteamService API in Step 1.)
        var gameRoot = browsedGameRoot
            ?? (string.IsNullOrEmpty(d.SteamAppId) ? null : _steam.InstallPath(d.SteamAppId));
        checks.Add(Check("Install folder", gameRoot, Directory.Exists(gameRoot)));

        // mod folder: GameRoot + (modPath or engine-preset ModPath)
        var modRel = !string.IsNullOrEmpty(d.ModPath) ? d.ModPath
            : (d.Engine is not null && EnginePresets.Presets.TryGetValue(d.Engine, out var p) ? p.ModPath : null);
        var modFolder = (gameRoot is not null && modRel is not null) ? Path.Combine(gameRoot, modRel.Replace('/', Path.DirectorySeparatorChar)) : null;
        checks.Add(Check("Mod folder", modFolder, Directory.Exists(modFolder)));

        // save dir: Ludusavi by app id first; else expand the enum root + subpath
        string? saveDir = null;
        if (!string.IsNullOrEmpty(d.SteamAppId))
            saveDir = await _ludu.FindSaveDirAsync(d.SteamAppId); // match the real LudusaviService API
        saveDir ??= ExpandSaveRoot(d.SaveRoot, d.SaveSubPath, gameRoot);
        checks.Add(Check("Save folder", saveDir, Directory.Exists(saveDir)));

        // launcher under GameRoot
        var launcher = (gameRoot is not null && !string.IsNullOrEmpty(d.RequiredLauncher))
            ? Path.Combine(gameRoot, d.RequiredLauncher.Replace('/', Path.DirectorySeparatorChar)) : null;
        if (launcher is not null) checks.Add(Check("Required launcher", launcher, File.Exists(launcher)));

        return new ResolvedProfile(gameRoot, modFolder, saveDir, launcher, checks);
    }

    private static ResolvedField Check(string label, string? path, bool exists) =>
        new(label, path, path is null ? ResolveStatus.Missing : exists ? ResolveStatus.Pass : ResolveStatus.Warn,
            path is null ? "not resolved" : exists ? null : "not found on disk yet");

    /// <summary>Expand a saveRoot enum + relative subpath to a machine path. SteamUserData needs the
    /// Steam path + user; if unavailable it returns null (Ludusavi is the better source there anyway).</summary>
    private string? ExpandSaveRoot(string? root, string? sub, string? gameRoot)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sub)) return null;
        var rel = sub.Replace('/', Path.DirectorySeparatorChar);
        string? baseDir = root switch
        {
            "DocumentsMyGames" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"),
            "AppData" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LocalAppData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameInstall" => gameRoot,
            "SteamUserData" => _steam.UserDataDir(), // match the real API; may be null
            _ => null,
        };
        return baseDir is null ? null : Path.Combine(baseDir, rel);
    }
}
```

> If `SteamService` / `LudusaviService` expose different method names (e.g. no `InstallPath`/`FindSaveDirAsync`/`UserDataDir`), adapt to the real ones found in Step 1 — the `SaveLocator.DetectAsync(...)` call used by `OnSaves` in `MainWindow.xaml.cs` is the existing save-resolution entry point and may be the better thing to reuse for the save dir. Register `GameProfileResolver` in the DI host (`App.xaml.cs`, alongside the other services).

- [ ] **Step 3: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\agp4`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/Services/GameProfileResolver.cs src/ModManager.App/App.xaml.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): GameProfileResolver - resolve a profile to machine paths + verify on disk"
```

---

## Task 5: Add the save/launcher fields + "Add with AI" flow to AddGameDialog

**Files:** Modify `src/ModManager.App/AddGameDialog.xaml` + `.xaml.cs`.

- [ ] **Step 1: Read the existing dialog + the theme import dialog**

Read `src/ModManager.App/AddGameDialog.xaml` + `.xaml.cs` (it already has the popular-games quick-pick + `BuildInput()` producing a `GameInput`), and `src/ModManager.App/NewThemeDialog.xaml` + `.xaml.cs` (the proven Copy-prompt / paste-JSON / Import pattern to mirror). Note the literal-bool-in-XAML gotcha from the mod-update work: set `IsChecked`/`IsThreeState`-style defaults in code-behind, not markup.

- [ ] **Step 2: Add the new wizard fields (XAML)**

In `AddGameDialog.xaml`, add (near the existing engine/mod-path fields) a save-root `ComboBox` (`x:Name="SaveRootBox"`, items = the five enum values), a `TextBox SaveSubPathBox` ("Save subfolder"), and a `TextBox RequiredLauncherBox` ("Required launcher (relative exe, optional)"). Populate `SaveRootBox.ItemsSource = GameProfileImport.SaveRoots` in the ctor (code, not XAML).

- [ ] **Step 3: Add the "Add with AI" sub-flow (XAML + code-behind)**

Add to the dialog (e.g. an expander or a top "From an AI agent" section): a `TextBox AiGameNameBox`, a **Copy prompt** `Button` (`Click="OnCopyProfilePrompt"`), a `TextBox AiJsonBox` (multiline, "Paste the agent's JSON"), and an **Apply profile** `Button` (`Click="OnApplyProfile"`). Code-behind:

```csharp
private void OnCopyProfilePrompt(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
{
    var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
    pkg.SetText(ModManager.Core.GameProfilePrompt.Build(AiGameNameBox.Text));
    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    ProfileStatus.Text = "Prompt copied — run it in your agent, paste the JSON back.";
}

private async void OnApplyProfile(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
{
    var result = ModManager.Core.GameProfileImport.Load(AiJsonBox.Text ?? "");
    if (result.Draft is null) { ProfileStatus.Text = string.Join("  ", result.Errors); return; }
    var d = result.Draft;

    // resolve + verify (browse not yet attempted here; pass null so Steam detection runs)
    var resolver = App.AppHost.Services.GetRequiredService<Services.GameProfileResolver>();
    var resolved = await resolver.ResolveAsync(d, browsedGameRoot: null);

    // pre-fill the familiar wizard fields
    NameBox.Text = d.Name ?? "";
    SelectEngine(d.Engine);                 // helper: select the matching EngineBox item (fires OnEngineChanged)
    if (!string.IsNullOrEmpty(d.ModPath)) ModPathBox.Text = d.ModPath;
    if (!string.IsNullOrEmpty(d.SteamAppId)) SteamBox.Text = d.SteamAppId;
    SaveRootBox.SelectedItem = d.SaveRoot;
    SaveSubPathBox.Text = d.SaveSubPath ?? "";
    RequiredLauncherBox.Text = d.RequiredLauncher ?? "";
    if (!string.IsNullOrEmpty(resolved.GameRoot)) GameRootBox.Text = resolved.GameRoot; // the resolved install path

    // show the pass/warn checks
    ProfileStatus.Text = string.Join("   ", resolved.Checks.Select(c =>
        $"{(c.Status == Services.ResolveStatus.Pass ? "OK" : "!")} {c.Label}"));
    _resolvedSaveDir = resolved.SaveDir; // stash for BuildInput/register
}
```

(Use the real control names from Step 1 — `NameBox`/`EngineBox`/`ModPathBox`/`SteamBox`/`GameRootBox` may differ. Add a `ProfileStatus` `TextBlock` for messages and a private `string? _resolvedSaveDir`. `SelectEngine` selects the `EngineBox` item whose key matches, the same way the popular-games quick-pick already sets the engine.)

- [ ] **Step 4: Extend BuildInput()** to include the new fields:

```csharp
// in BuildInput(), add to the GameInput initializer:
SaveRoot = SaveRootBox.SelectedItem as string,
SaveSubPath = string.IsNullOrWhiteSpace(SaveSubPathBox.Text) ? null : SaveSubPathBox.Text.Trim(),
RequiredLauncher = string.IsNullOrWhiteSpace(RequiredLauncherBox.Text) ? null : RequiredLauncherBox.Text.Trim(),
```

Also expose the resolved save dir for the caller: add `public string? ResolvedSaveDir => _resolvedSaveDir;`.

- [ ] **Step 5: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\agp5`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/AddGameDialog.xaml src/ModManager.App/AddGameDialog.xaml.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): Add-with-AI flow + save/launcher fields in the Add Game wizard"
```

---

## Task 6: Persist the resolved save dir at register

`BuildGameEntry` carries the launcher; the App sets the resolved absolute `SaveDir` after building, so the save manager points at the right folder immediately.

**Files:** Modify `src/ModManager.App/MainWindow.xaml.cs` (the `OnAddGame` handler) and/or `src/ModManager.App/ViewModels/MainViewModel.cs` (`AddGameAsync`).

- [ ] **Step 1: Read the current register path**

Read `OnAddGame` in `MainWindow.xaml.cs` (it calls `dialog.BuildInput()` then `ViewModel.AddGameAsync(...)`) and `MainViewModel.AddGameAsync` (which calls `EnginePresets.BuildGameEntry` + registers via `LauncherService`). Find where the `GameEntry` is created so the resolved `SaveDir` can be applied.

- [ ] **Step 2: Apply the resolved save dir**

After `BuildGameEntry` produces the entry (in `AddGameAsync`), set the save dir from the dialog's resolved value when present. Simplest: pass the dialog's `ResolvedSaveDir` through to `AddGameAsync`, and after building the entry:

```csharp
if (!string.IsNullOrEmpty(resolvedSaveDir)) entry.SaveDir = resolvedSaveDir;
```

(If `AddGameAsync` currently takes only `GameInput`, add an optional `string? resolvedSaveDir = null` parameter and pass `dialog.ResolvedSaveDir` from `OnAddGame`.)

- [ ] **Step 3: Build-verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\agp6`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "feat(games): apply the resolved save dir when registering an agent-added game"
```

---

## Task 7: Full verification + smoke

- [ ] **Step 1: Full suite + app build**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (all green incl. the new prompt + import tests), then `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -o C:\Users\estev\AppData\Local\Temp\agpfinal` (0 errors).

- [ ] **Step 2: Data-safety / purity review** — confirm `ModManager.Core` stays UI-free (`CorePurityTests` green); the profile never carries an absolute path (validation rejects them); resolution + disk-verify never writes (read-only until the user confirms register).

- [ ] **Step 3: GUI smoke (manual)** — **+ Game → Add with AI**: type a game name, Copy prompt, run it in an agent, paste the JSON, Apply → the wizard pre-fills with pass/warn badges; confirm → the game registers and its mods/saves resolve. Try a deliberately bad JSON → inline error, nothing registered.

- [ ] **Step 4: Commit any doc updates**

```bash
git -C /c/Users/estev/Projects/626-mod-launcher add -A
git -C /c/Users/estev/Projects/626-mod-launcher commit -m "docs: note the agentic game-profile add flow"
```

---

## Self-review (done at write time)

- **Spec coverage:** prompt builder (T1) ✓; structured contract + parse/validate incl. enum/relative-path/required-field rules (T2) ✓; full-profile fields on GameInput/GameEntry/BuildGameEntry (T3) ✓; resolve GameRoot/mod/save(Ludusavi-first)/launcher + on-disk pass/warn (T4) ✓; pre-fill the familiar wizard + Copy-prompt/paste + new fields (T5) ✓; resolved save dir persisted (T6) ✓; verification + purity + smoke (T7) ✓.
- **Out of scope, correctly absent:** launch-time enforcement of requiredLauncher (schema/field only), profile sharing, auto-LLM.
- **Type consistency:** `GameProfileDraft` fields, `ProfileImportResult(Draft, Errors)`, `GameProfileImport.Load` + `.SaveRoots`, `GameProfilePrompt.Build`, `GameProfileResolver.ResolveAsync` -> `ResolvedProfile(GameRoot, ModFolder, SaveDir, LauncherPath, Checks)` + `ResolvedField`/`ResolveStatus`, `GameInput.{SaveRoot,SaveSubPath,RequiredLauncher}`, `GameEntry.RequiredLauncher` used consistently. App service/control APIs (SteamService, LudusaviService, AddGameDialog control names) are flagged in each task to verify against the real code before wiring — the only deliberate unknowns.
