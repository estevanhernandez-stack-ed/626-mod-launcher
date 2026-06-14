# Richer Steam Detection — Phase 1 Implementation Plan (v0.6.2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use Steam data we already read but ignore — auto-fill the game folder on a popular-game pick, and show local Steam cover art in Add Game and the game tiles — behind a store-detection seam that GOG/Epic/Xbox can plug into later.

**Architecture:** A new App-side `IStoreLibrary` seam (IO-bearing) with a Steam implementation; pure parsing/records/match/art-pick logic stays in Core. The Steam appmanifest parser widens by one field (`buildId`, for the committed Phase 2). The popular-pick folder auto-fill reuses the already-resolved `InstallDir`; cover art is resolved from `appcache/librarycache/<appid>/` (prefer `header.jpg`, else any `.jpg`).

**Tech Stack:** .NET 10, C#, WinUI 3 (App), xUnit (Core tests). Pure-Core / thin-App split enforced by `CorePurityTests`.

**Spec:** [docs/superpowers/specs/2026-06-14-richer-steam-detection-design.md](../specs/2026-06-14-richer-steam-detection-design.md)

---

## File Structure

**Create (Core):**
- `src/ModManager.Core/InstalledGame.cs` — the store-agnostic installed-game record (replaces the App-side `SteamGame`). Fields: `StoreKind`, `AppId`, `Name`, `InstallDir`, optional `BuildId`.
- `src/ModManager.Core/InstalledGameMatch.cs` — pure `ByAppId(list, appId)` lookup.
- `src/ModManager.Core/SteamArt.cs` — pure `PickCover(files)` cover-art chooser.

**Create (App):**
- `src/ModManager.App/Services/IStoreLibrary.cs` — the seam interface.

**Modify (Core):**
- `src/ModManager.Core/SteamParse.cs` — add `BuildId` to `AppManifest` + its parse.

**Modify (App):**
- `src/ModManager.App/Services/SteamService.cs` — implement `IStoreLibrary`; return `InstalledGame` (with `BuildId`); add `StoreKind` + `ResolveCoverArtPath`.
- `src/ModManager.App/App.xaml.cs` — register `IStoreLibrary`.
- `src/ModManager.App/AddGameDialog.xaml(.cs)` — stash the installed-games list; auto-fill folder on popular pick; show cover art in the lists.
- `src/ModManager.App/MainWindow.xaml(.cs)` — pass `InstalledGame`; show cover art on the game switcher/tile (reuse the `ModRowViewModel.Thumbnail` pattern).
- Callers that referenced the App record `SteamGame` (rename to `InstalledGame`): `AddGameDialog.xaml.cs` (ctor param + the two `.Cast<SteamGame>()` at :131 and :156) and `MainWindow.xaml.cs:222` (a `var`, so it just flows the now-`InstalledGame` list through). NOTE: `SteamGameImport`/`SteamImportCandidate` (Core) and `GameProfileResolver` reference the *service* or a different type, NOT this record — do not touch them.

**Test:**
- `tests/ModManager.Tests/SteamParseTests.cs` — buildId parse.
- `tests/ModManager.Tests/InstalledGameMatchTests.cs` — match helper.
- `tests/ModManager.Tests/SteamArtTests.cs` — cover picker.

---

## Task 1: Widen the appmanifest parser with `buildId`

**Files:**
- Modify: `src/ModManager.Core/SteamParse.cs`
- Test: `tests/ModManager.Tests/SteamParseTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `SteamParseTests.cs` (the fixture there already contains `"buildid"` and `"StateFlags" "4"` lines):

```csharp
[Fact]
public void ParseAppManifest_extracts_buildId()
{
    const string acf = """
    "AppState"
    {
        "appid"     "1091500"
        "name"      "Cyberpunk 2077"
        "installdir"    "Cyberpunk 2077"
        "buildid"   "17556649"
        "StateFlags"    "4"
    }
    """;
    var m = SteamParse.ParseAppManifest(acf);
    Assert.Equal("1091500", m.AppId);
    Assert.Equal("Cyberpunk 2077", m.Name);
    Assert.Equal("Cyberpunk 2077", m.InstallDir);
    Assert.Equal("17556649", m.BuildId);
}

[Fact]
public void ParseAppManifest_buildId_is_null_when_absent()
{
    var m = SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" \"name\" \"X\" \"installdir\" \"X\" }");
    Assert.Null(m.BuildId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ParseAppManifest_extracts_buildId"`
Expected: FAIL — `AppManifest` has no `BuildId` member (compile error).

- [ ] **Step 3: Add the field + parse**

In `SteamParse.cs`, add the regex next to the others:

```csharp
[GeneratedRegex("\"buildid\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
private static partial Regex BuildIdKeyRe();
```

Extend `ParseAppManifest`'s return:

```csharp
public static AppManifest ParseAppManifest(string? acfText)
{
    var s = acfText ?? "";
    return new AppManifest(
        AppIdKeyRe().Match(s) is { Success: true } a ? a.Groups[1].Value : null,
        NameKeyRe().Match(s) is { Success: true } n ? n.Groups[1].Value : null,
        InstallDirKeyRe().Match(s) is { Success: true } d ? d.Groups[1].Value : null,
        BuildIdKeyRe().Match(s) is { Success: true } b ? b.Groups[1].Value : null);
}
```

Extend the record (append the optional field so existing positional callers in tests still compile if any — there are none in `src/`):

```csharp
public sealed record AppManifest(string? AppId, string? Name, string? InstallDir, string? BuildId = null);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ParseAppManifest"`
Expected: PASS (all ParseAppManifest tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SteamParse.cs tests/ModManager.Tests/SteamParseTests.cs
git commit -m "feat(steam): parse buildId from the appmanifest"
```

---

## Task 2: The `InstalledGame` Core record

**Files:**
- Create: `src/ModManager.Core/InstalledGame.cs`
- (No test — a plain record; covered by Task 3/4 tests.)

- [ ] **Step 1: Create the record**

`src/ModManager.Core/InstalledGame.cs`:

```csharp
namespace ModManager.Core;

/// <summary>One installed game discovered from a store library. Store-agnostic so GOG/Epic/Xbox
/// adapters can return the same shape later (see IStoreLibrary). <see cref="BuildId"/> is the store's
/// installed-version stamp (Steam's appmanifest buildid) — used by the Phase 2 "game updated under
/// your mods" check; null when the store doesn't expose one.</summary>
public sealed record InstalledGame(string StoreKind, string AppId, string Name, string InstallDir)
{
    public string? BuildId { get; init; }
}
```

- [ ] **Step 2: Build Core to verify it compiles**

Run: `dotnet build src/ModManager.Core/ModManager.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.Core/InstalledGame.cs
git commit -m "feat(steam): add store-agnostic InstalledGame record"
```

---

## Task 3: Pure cover-art picker (`SteamArt.PickCover`)

Grounded on the real on-disk layout: `appcache/librarycache/<appid>/` holds `header.jpg` for many games plus newer hashed `<sha1>.jpg` files. Prefer the named `header.jpg`; otherwise take any `.jpg`. Pure: the caller passes the file list, this just picks.

**Files:**
- Create: `src/ModManager.Core/SteamArt.cs`
- Test: `tests/ModManager.Tests/SteamArtTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ModManager.Tests/SteamArtTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class SteamArtTests
{
    [Fact]
    public void PickCover_prefers_header_jpg()
    {
        var files = new[]
        {
            @"C:\lc\1091500\7807d6dcd71d8161465619b4f041794b0353a6d0.jpg",
            @"C:\lc\1091500\header.jpg",
        };
        Assert.Equal(@"C:\lc\1091500\header.jpg", SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_falls_back_to_any_jpg_when_no_header()
    {
        var files = new[] { @"C:\lc\1042420\dadc80fcc935495943969e0d3cd90cae6c79d8ff.jpg" };
        Assert.Equal(@"C:\lc\1042420\dadc80fcc935495943969e0d3cd90cae6c79d8ff.jpg", SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_ignores_non_jpg_and_returns_null_when_none()
    {
        var files = new[] { @"C:\lc\1\icon.ico", @"C:\lc\1\notes.txt" };
        Assert.Null(SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_handles_empty()
    {
        Assert.Null(SteamArt.PickCover(System.Array.Empty<string>()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamArtTests"`
Expected: FAIL — `SteamArt` does not exist.

- [ ] **Step 3: Implement the pure picker**

`src/ModManager.Core/SteamArt.cs`:

```csharp
using System.IO;

namespace ModManager.Core;

/// <summary>Pure cover-art selection from a Steam librarycache app folder's file list. The IO
/// (enumerating appcache/librarycache/&lt;appid&gt;/) is the App adapter's job; this just chooses.
/// Grounded on the observed layout: a named header.jpg when present, else newer hashed &lt;sha1&gt;.jpg.
/// Returns null when there's no usable image.</summary>
public static class SteamArt
{
    public static string? PickCover(IReadOnlyList<string> filesInAppFolder)
    {
        string? header = null;
        string? anyJpg = null;
        foreach (var f in filesInAppFolder)
        {
            var name = Path.GetFileName(f);
            if (name.Equals("header.jpg", StringComparison.OrdinalIgnoreCase)) { header = f; break; }
            if (anyJpg is null && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) anyJpg = f;
        }
        return header ?? anyJpg;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamArtTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SteamArt.cs tests/ModManager.Tests/SteamArtTests.cs
git commit -m "feat(steam): pure cover-art picker (header.jpg, else any jpg)"
```

---

## Task 4: Pure appid → installed-game match

**Files:**
- Create: `src/ModManager.Core/InstalledGameMatch.cs`
- Test: `tests/ModManager.Tests/InstalledGameMatchTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/ModManager.Tests/InstalledGameMatchTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class InstalledGameMatchTests
{
    private static readonly IReadOnlyList<InstalledGame> Games = new[]
    {
        new InstalledGame("steam", "1091500", "Cyberpunk 2077", @"D:\Steam\steamapps\common\Cyberpunk 2077"),
        new InstalledGame("steam", "489830", "Skyrim SE", @"D:\Steam\steamapps\common\Skyrim Special Edition"),
    };

    [Fact]
    public void ByAppId_returns_the_match()
    {
        var g = InstalledGameMatch.ByAppId(Games, "1091500");
        Assert.Equal(@"D:\Steam\steamapps\common\Cyberpunk 2077", g!.InstallDir);
    }

    [Fact]
    public void ByAppId_returns_null_for_unknown()
        => Assert.Null(InstalledGameMatch.ByAppId(Games, "999999"));

    [Fact]
    public void ByAppId_returns_null_for_empty_appid()
        => Assert.Null(InstalledGameMatch.ByAppId(Games, ""));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InstalledGameMatchTests"`
Expected: FAIL — `InstalledGameMatch` does not exist.

- [ ] **Step 3: Implement**

`src/ModManager.Core/InstalledGameMatch.cs`:

```csharp
namespace ModManager.Core;

/// <summary>Pure lookup of an installed game by store app id. Used to auto-fill the game folder when
/// a curated quick-pick is chosen and the user already has the game installed.</summary>
public static class InstalledGameMatch
{
    public static InstalledGame? ByAppId(IReadOnlyList<InstalledGame> games, string? appId)
        => string.IsNullOrEmpty(appId) ? null : games.FirstOrDefault(g => g.AppId == appId);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InstalledGameMatchTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/InstalledGameMatch.cs tests/ModManager.Tests/InstalledGameMatchTests.cs
git commit -m "feat(steam): pure appid -> installed-game match helper"
```

---

## Task 5: The `IStoreLibrary` seam + Steam implementation

Refactor `SteamService` to satisfy a store-agnostic seam: return `InstalledGame` (with `BuildId`), expose `StoreKind`, and add `ResolveCoverArtPath`. Keep the existing Steam-specific members (`CurrentUserId64`, `IsRunning`, `EnsureRunning`) — they aren't part of the seam.

**Files:**
- Create: `src/ModManager.App/Services/IStoreLibrary.cs`
- Modify: `src/ModManager.App/Services/SteamService.cs`
- Modify: `src/ModManager.App/App.xaml.cs:38` (DI — `SteamService` is already registered there)

- [ ] **Step 1: Define the seam**

`src/ModManager.App/Services/IStoreLibrary.cs`:

```csharp
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>A game-store library adapter. The IO lives here (filesystem/registry); pure parsing +
/// selection logic lives in Core. Steam is the only implementation today; GOG/Epic/Xbox satisfy the
/// same contract on demand (StoreIds already carries their ids, so no schema migration is needed).</summary>
public interface IStoreLibrary
{
    /// <summary>Stable lowercase store key, e.g. "steam".</summary>
    string StoreKind { get; }

    /// <summary>Installed games discovered from the store's on-disk metadata.</summary>
    IReadOnlyList<InstalledGame> InstalledGames();

    /// <summary>Absolute path to a locally-cached cover image for the app id, or null if none.</summary>
    string? ResolveCoverArtPath(string appId);
}
```

- [ ] **Step 2: Make `SteamService` implement it**

In `SteamService.cs`: delete the App-side `SteamGame` record (line 10) — it's replaced by `ModManager.Core.InstalledGame`. Add `using ModManager.Core;` (already present). Change the class declaration to `public sealed class SteamService : IStoreLibrary`. Add `public string StoreKind => "steam";`.

Change `InstalledGames()` to return `InstalledGame` and carry `BuildId` + `StoreKind`:

```csharp
public IReadOnlyList<InstalledGame> InstalledGames()
{
    var steam = FindSteamPath();
    if (steam is null) return Array.Empty<InstalledGame>();

    var games = new List<InstalledGame>();
    var seen = new HashSet<string>();
    foreach (var lib in Libraries(steam))
    {
        var steamapps = Path.Combine(lib, "steamapps");
        if (!Directory.Exists(steamapps)) continue;
        string[] manifests;
        try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
        catch { continue; }
        foreach (var acf in manifests)
        {
            try
            {
                var m = SteamParse.ParseAppManifest(File.ReadAllText(acf));
                if (m.AppId is null || string.IsNullOrEmpty(m.Name) || string.IsNullOrEmpty(m.InstallDir)) continue;
                if (!seen.Add(m.AppId)) continue;
                var full = Path.Combine(steamapps, "common", m.InstallDir);
                if (Directory.Exists(full))
                    games.Add(new InstalledGame("steam", m.AppId, m.Name!, full) { BuildId = m.BuildId });
            }
            catch { /* skip a malformed manifest */ }
        }
    }
    return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
```

Add the art resolver (uses the existing `FindSteamPath`; enumerates the appid folder; defers the choice to Core):

```csharp
public string? ResolveCoverArtPath(string appId)
{
    if (string.IsNullOrEmpty(appId)) return null;
    var steam = FindSteamPath();
    if (steam is null) return null;
    var dir = Path.Combine(steam, "appcache", "librarycache", appId);
    if (!Directory.Exists(dir)) return null;
    try { return SteamArt.PickCover(Directory.GetFiles(dir)); }
    catch { return null; }
}
```

- [ ] **Step 3: Register the seam in DI**

`SteamService` is already registered at `App.xaml.cs:38` (`services.AddSingleton<SteamService>();`). Insert the seam mapping immediately after it (mirrors the existing `ICurseForgeClient` singleton-mapping pattern at line 32):

```csharp
services.AddSingleton<IStoreLibrary>(sp => sp.GetRequiredService<SteamService>());
```

(Verified: consumers resolve `SteamService` via DI — `GetRequiredService<SteamService>()` at `MainWindow.xaml.cs:222`, ctor-injected into `GameProfileResolver`/`MainViewModel` — never `new`-ed, so the `IStoreLibrary` singleton resolves cleanly.)

- [ ] **Step 4: Fix the now-broken callers (type rename only)**

Grep and update every `SteamGame` reference to `InstalledGame`:

Run: `rg -n "SteamGame" src/ModManager.App`
For each hit (expected: `AddGameDialog.xaml.cs` ctor param + the two `.Cast<SteamGame>()` at :131/:156, `MainWindow.xaml.cs:222` where it builds the list as a `var`), change the type `SteamGame` → `InstalledGame`. Field names (`AppId`, `Name`, `InstallDir`) are unchanged, so only the type name changes. Do NOT touch `SteamGameImport` / `SteamImportCandidate` (Core types — different thing) or `GameProfileResolver` (references the *service*, not this record). `BatchSteamList`'s template binds `{Binding Name}` (`AddGameDialog.xaml:41`) and takes the list directly — `InstalledGame.Name` still exists, so it survives the rename with no change.

- [ ] **Step 5: Build the App + run the Core suite**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all pass (incl. `CorePurityTests` — no UI types entered Core).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/Services/IStoreLibrary.cs src/ModManager.App/Services/SteamService.cs src/ModManager.App/App.xaml.cs src/ModManager.App/AddGameDialog.xaml.cs src/ModManager.App/MainWindow.xaml.cs
git commit -m "refactor(steam): IStoreLibrary seam + InstalledGame return; Steam impl carries buildId + cover art"
```

---

## Task 6: Auto-fill the game folder on a popular-game pick

The popular pick already sets `SteamBox` (the app id). Look up the installed game by that id and pre-fill `FolderBox` from its `InstallDir`. `steamGames` reaches the ctor but isn't stashed — stash it first.

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs` (ctor; `OnPopularSelected` starts at :239 — insert after the synchronous `SteamBox.Text = g.SteamAppId;` fill at :249, before the deferred engine-select block)

- [ ] **Step 1: Stash the installed-games list as a field**

Near the other private fields (top of the class), add:

```csharp
// Installed store games, kept so a popular-game pick can auto-fill the folder we already resolved.
private readonly IReadOnlyList<InstalledGame> _installedGames;
```

In the ctor (signature param is now `IReadOnlyList<InstalledGame> steamGames` after Task 5), assign first thing:

```csharp
_installedGames = steamGames;
```

- [ ] **Step 2: Auto-fill the folder in `OnPopularSelected`**

After the existing `SteamBox.Text = g.SteamAppId;` line (end of `OnPopularSelected`), add:

```csharp
// We already parsed this game's install folder from Steam — fill it so the pick is one step from
// Add instead of making the user Browse to a path we know. Editable; user can still change it.
if (InstalledGameMatch.ByAppId(_installedGames, g.SteamAppId) is { } installed)
    FolderBox.Text = installed.InstallDir;
```

Add `using ModManager.Core;` if not present (it is).

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke (record expectation; UI not unit-testable here)**

Expected behavior: open Add Game → pick a popular game you have installed on Steam (e.g. Cyberpunk 2077) → Name, Engine, Mod folder, App ID **and Game folder** all fill; Add works without Browse. Pick one you don't have installed → folder stays blank (Browse still works). The pure match itself is covered by `InstalledGameMatchTests` (Task 4).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/AddGameDialog.xaml.cs
git commit -m "feat(add-game): auto-fill the game folder on a popular pick from the installed Steam game"
```

---

## Task 7: Cover art in the Add Game picker + game tiles

Show the locally-cached cover where the UI is text-only today. The App converts the resolved path to a `BitmapImage` (App-side per purity), mirroring the `ModRowViewModel.Thumbnail` pattern ([ModRowViewModel.cs:285](../../../src/ModManager.App/ViewModels/ModRowViewModel.cs), [MainWindow.xaml:350](../../../src/ModManager.App/MainWindow.xaml)).

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs` (the `SteamAddRow` display VM), `src/ModManager.App/AddGameDialog.xaml` (the `SteamGamesList`, `DisplayMemberPath="Display"` at :73)
- Modify: `src/ModManager.App/MainWindow.xaml(.cs)` (game switcher tile — optional within Phase 1; see Step 4)

- [ ] **Step 1: Add a cover-image path to the Steam quick-add row VM**

In `AddGameDialog.xaml.cs`, the row VM is `private sealed record SteamAddRow(GameInput Input, string Display);`. Extend it with the resolved cover path:

```csharp
private sealed record SteamAddRow(GameInput Input, string Display, string? CoverPath);
```

Where the rows are built (the `foreach (var g in steamGames)` loop, ~line 57), resolve art via the seam. The dialog needs the store library — add a field and resolve it from DI in the ctor:

```csharp
// near fields:
private readonly IStoreLibrary _store = App.AppHost.Services.GetRequiredService<IStoreLibrary>();
```

In the addable-row construction, pass the cover:

```csharp
addable.Add(new SteamAddRow(plan.Input, $"{g.Name}  ·  {label}", _store.ResolveCoverArtPath(g.AppId)));
```

- [ ] **Step 2: Show the cover in the quick-add list template**

In `AddGameDialog.xaml`, the `SteamGamesList` currently uses `DisplayMemberPath="Display"` (:73). Replace `DisplayMemberPath` with an `ItemTemplate` that shows a small cover next to the text:

```xml
<ListView x:Name="SteamGamesList" SelectionMode="Multiple" MaxHeight="180"
          BorderBrush="{ThemeResource ControlElevationBorderBrush}" BorderThickness="1"
          SelectionChanged="OnSteamSelectionChanged">
    <ListView.ItemTemplate>
        <DataTemplate>
            <Grid ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Width="46" Height="22" CornerRadius="3"
                        Background="{ThemeResource ControlElevationBorderBrush}">
                    <Image Source="{Binding Cover}" Stretch="UniformToFill" />
                </Border>
                <TextBlock Grid.Column="1" Text="{Binding Display}" VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

- [ ] **Step 3: Expose `Cover` as an `ImageSource` on the row VM**

XAML binds to an `ImageSource`, not a path. Add a computed `Cover` to `SteamAddRow` (App-side conversion — never in Core):

```csharp
private sealed record SteamAddRow(GameInput Input, string Display, string? CoverPath)
{
    public Microsoft.UI.Xaml.Media.ImageSource? Cover =>
        string.IsNullOrEmpty(CoverPath) ? null
            : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(CoverPath));
}
```

(Missing art → `null` → the empty placeholder Border shows. Matches the `ModRowViewModel.Thumbnail` null-degrades pattern.)

- [ ] **Step 4: Build + smoke**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded.

Manual smoke: open Add Game → the "Quick add from Steam" list shows cover art beside each installed game (games without cached art show the empty placeholder). The game-switcher tile in `MainWindow` is a separate, smaller surface — if time allows, apply the same `ResolveCoverArtPath` → `BitmapImage` to the active-game header there; otherwise it's the first item of the Phase-2/Phase-3 follow-up. Keep Phase 1's art surface to the Add Game list to stay shippable.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/AddGameDialog.xaml src/ModManager.App/AddGameDialog.xaml.cs
git commit -m "feat(add-game): show local Steam cover art in the quick-add list"
```

---

## Task 8: Smoke checklist + full verification

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Add smoke entries**

Append to `docs/smoke-tests/pending.md`:

```markdown
## Richer Steam detection (v0.6.2)
- Add Game → pick a popular game installed on Steam (e.g. Cyberpunk 2077): Name, Engine, Mod folder, App ID, AND Game folder all fill; Add works without Browse. Expected: game registers in one step.
- Add Game → pick a popular game NOT installed: Game folder stays blank, Browse still works. Expected: no crash, manual path still possible.
- Add Game → "Quick add from Steam" list shows cover art per game; games with no cached art show the empty placeholder, not a broken image.
```

- [ ] **Step 2: Full suite + App build (green gate)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all pass, including `CorePurityTests`.
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): richer Steam detection checklist"
```

---

## Deferred to later phases (NOT in this plan)

- **`StateFlags` "fully-installed" gate** — verify the bitmask against a real not-fully-installed `.acf` first (verify-or-drop, per the spec). Parse + gate land together once verified.
- **`SizeOnDisk` / `LastUpdated` / `LastPlayed` parse** — land with their consumer (Phase 3 picker). `LastPlayed` is read live, never persisted.
- **Phase 2: build-id "game updated under your mods" warning** — persists `lastKnownSteamBuildId` on `GameEntry` (camelCase + round-trip test); comparator in Core; banner in App. The plan should pin the exact event that re-reads `buildId`.
- **Phase 3: enriched picker** — art + sort-by-last-played (live) + show-all.
- **GOG / Epic / Xbox** `IStoreLibrary` adapters — on demand.

---

## Self-Review

- **Spec coverage:** store seam (Task 5) ✓; widened parse — buildId now (Task 1), rest deferred with rationale ✓; Phase 1 folder-autofill (Task 6) ✓; cover art (Task 7) ✓; pure-Core split (Tasks 1-4 Core, 5-7 App) ✓; reversibility — all read-only, nothing persisted in Phase 1 ✓; camelCase — N/A this phase (no new persisted shape) ✓; StateFlags held verify-or-drop, explicitly deferred ✓.
- **Placeholder scan:** every code step has real code; no TBD/TODO. The one judgment call (MainWindow tile art in Task 7 Step 4) is scoped as optional-within-phase with a clear default, not a placeholder.
- **Type consistency:** `InstalledGame(StoreKind, AppId, Name, InstallDir){ BuildId }` used identically in Tasks 2/4/5/6; `AppManifest` 4th param `BuildId` (Task 1) feeds `InstalledGame.BuildId` (Task 5); `SteamArt.PickCover(IReadOnlyList<string>)` (Task 3) called in Task 5 Step 2; `InstalledGameMatch.ByAppId(list, appId)` (Task 4) called in Task 6 Step 2; `IStoreLibrary.ResolveCoverArtPath`/`InstalledGames`/`StoreKind` (Task 5) consumed in Tasks 6-7. Consistent.
