# Steam show-all picker — Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make every installed Steam game visible + actionable in Add Game, ordered by recently-played. Engine-detected games stay one-click checkable; engine-undetected games (Marvel Rivals, Helldivers, etc.) become "Set up" rows (art + name + a button that pre-fills the manual form) instead of today's plain "engine not detected: X, Y" text.

**Architecture:** Parse `lastPlayed` from the appmanifest (read live, never persisted) and sort `InstalledGames()` recently-played-first via a pure Core helper. The dialog's two groups inherit that order. In the App, replace the `SteamManualNote` TextBlock with a `SteamSetupList` of rows whose "Set up" button reuses the existing manual-form pre-fill path.

**Tech Stack:** .NET 10, C#, WinUI 3 (App), xUnit (Core). Pure-Core / thin-App (`CorePurityTests`).

**Design decision (approved):** show-all rework + recently-played-first; `lastPlayed` read live, never written to disk (per the privacy call). Undetected games get a "Set up" affordance (pre-filled manual form) — the interim cover for Marvel Rivals until the engine-detection backlog lands.

**Spec:** [docs/superpowers/specs/2026-06-14-richer-steam-detection-design.md](../specs/2026-06-14-richer-steam-detection-design.md) (Phase 3).

---

## File Structure

**Modify (Core):**
- `src/ModManager.Core/SteamParse.cs` — parse `LastPlayed` into `AppManifest`.
- `src/ModManager.Core/InstalledGame.cs` — add `LastPlayed` (string?).

**Create (Core):**
- `src/ModManager.Core/InstalledGameSort.cs` — pure recently-played-first ordering.

**Modify (App):**
- `src/ModManager.App/Services/SteamService.cs` — set `LastPlayed`; order via the new sort.
- `src/ModManager.App/AddGameDialog.xaml.cs` — `SteamSetupRow`; ctor builds the undetected list; `OnSteamSetup` handler.
- `src/ModManager.App/AddGameDialog.xaml` — replace `SteamManualNote` with `SteamSetupList`.

**Test:**
- `tests/ModManager.Tests/SteamParseTests.cs` — `LastPlayed` parse.
- `tests/ModManager.Tests/InstalledGameSortTests.cs` — ordering.

---

## Task 1: Parse `LastPlayed` + carry it on `InstalledGame`

**Files:**
- Modify: `src/ModManager.Core/SteamParse.cs`, `src/ModManager.Core/InstalledGame.cs`, `src/ModManager.App/Services/SteamService.cs`
- Test: `tests/ModManager.Tests/SteamParseTests.cs`

- [ ] **Step 1: Failing test** — add to `SteamParseTests.cs`:

```csharp
[Fact]
public void ParseAppManifest_extracts_lastPlayed_and_null_when_absent()
{
    Assert.Equal("1777760600", SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" \"LastPlayed\" \"1777760600\" }").LastPlayed);
    Assert.Null(SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" }").LastPlayed);
}
```

- [ ] **Step 2: Run — expect FAIL** (`AppManifest` has no `LastPlayed`):

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~lastPlayed"`

- [ ] **Step 3: Implement.** In `SteamParse.cs`, add the regex next to `StateFlagsKeyRe`:

```csharp
[GeneratedRegex("\"LastPlayed\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
private static partial Regex LastPlayedKeyRe();
```

Extend `ParseAppManifest`'s return (add as the trailing arg):

```csharp
            StateFlagsKeyRe().Match(s) is { Success: true } sf ? sf.Groups[1].Value : null,
            LastPlayedKeyRe().Match(s) is { Success: true } lp ? lp.Groups[1].Value : null);
```

Extend the record:

```csharp
public sealed record AppManifest(string? AppId, string? Name, string? InstallDir, string? BuildId = null, string? StateFlags = null, string? LastPlayed = null);
```

In `InstalledGame.cs`, add the field (note: read live, never persisted):

```csharp
    /// <summary>Steam's last-played unix timestamp (seconds), or null. Read live for recently-played
    /// ordering; never persisted (behavioral data stays off disk).</summary>
    public string? LastPlayed { get; init; }
```

In `SteamService.InstalledGames()`, carry it on the constructed game:

```csharp
                        games.Add(new InstalledGame("steam", m.AppId, m.Name!, full) { BuildId = m.BuildId, LastPlayed = m.LastPlayed });
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ParseAppManifest"`

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SteamParse.cs src/ModManager.Core/InstalledGame.cs src/ModManager.App/Services/SteamService.cs tests/ModManager.Tests/SteamParseTests.cs
git commit -m "feat(steam): parse lastPlayed (live, never persisted) onto InstalledGame"
```

---

## Task 2: Pure recently-played-first ordering

**Files:**
- Create: `src/ModManager.Core/InstalledGameSort.cs`
- Modify: `src/ModManager.App/Services/SteamService.cs`
- Test: `tests/ModManager.Tests/InstalledGameSortTests.cs`

- [ ] **Step 1: Failing test** — `tests/ModManager.Tests/InstalledGameSortTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class InstalledGameSortTests
{
    private static InstalledGame G(string name, string? lastPlayed)
        => new("steam", name, name, $@"C:\{name}") { LastPlayed = lastPlayed };

    [Fact]
    public void Orders_most_recently_played_first()
    {
        var sorted = InstalledGameSort.RecentlyPlayedFirst(new[] { G("Old", "100"), G("New", "900"), G("Mid", "500") });
        Assert.Equal(new[] { "New", "Mid", "Old" }, sorted.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void Never_played_or_unparseable_fall_last_then_alphabetical()
    {
        var sorted = InstalledGameSort.RecentlyPlayedFirst(new[] { G("Zeb", null), G("Played", "100"), G("Abe", "notanumber") });
        Assert.Equal(new[] { "Played", "Abe", "Zeb" }, sorted.Select(g => g.Name).ToArray());
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`InstalledGameSort` missing):

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InstalledGameSort"`

- [ ] **Step 3: Implement** — `src/ModManager.Core/InstalledGameSort.cs`:

```csharp
using System.Globalization;

namespace ModManager.Core;

/// <summary>Pure ordering for the installed-games picker: most-recently-played first (by the live
/// Steam lastPlayed timestamp), with never-played / unparseable games last, then alphabetical.</summary>
public static class InstalledGameSort
{
    public static IReadOnlyList<InstalledGame> RecentlyPlayedFirst(IReadOnlyList<InstalledGame> games)
        => games
            .OrderByDescending(g => long.TryParse(g.LastPlayed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : long.MinValue)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
```

- [ ] **Step 4: Use it in `SteamService.InstalledGames()`** — replace the final return's `OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList()` with:

```csharp
        return InstalledGameSort.RecentlyPlayedFirst(games);
```

- [ ] **Step 5: Run — expect PASS** (sort tests + full Core suite):

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/InstalledGameSort.cs src/ModManager.App/Services/SteamService.cs tests/ModManager.Tests/InstalledGameSortTests.cs
git commit -m "feat(steam): order installed games recently-played-first"
```

---

## Task 3: `SteamSetupRow` + ctor builds undetected rows + `OnSteamSetup`

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs`

Read the file first. The ctor (~line 53-91) builds `addable` (`List<SteamAddRow>`) + `manual` (`List<string>`); `SteamAddRow` (~line 29-43) has a computed `Cover`.

- [ ] **Step 1: Add the row VM** next to `SteamAddRow`:

```csharp
// One installed game whose engine we couldn't detect — surfaced as a "Set up" row (art + name +
// a button that pre-fills the manual form so the user picks the engine).
private sealed record SteamSetupRow(string AppId, string Name, string? CoverPath, string InstallDir)
{
    public Microsoft.UI.Xaml.Media.ImageSource? Cover =>
        string.IsNullOrEmpty(CoverPath) ? null
            : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(CoverPath));
}
```

- [ ] **Step 2: Build setup rows in the ctor.** Change `var manual = new List<string>();` to `var manual = new List<SteamSetupRow>();`, and in the `else` branch replace `manual.Add(g.Name);` with:

```csharp
                manual.Add(new SteamSetupRow(g.AppId, g.Name, _store.ResolveCoverArtPath(g.AppId), g.InstallDir));
```

Replace the `if (manual.Count > 0) { SteamManualNote.Text = ...; SteamManualNote.Visibility = Visible; }` block with:

```csharp
        if (manual.Count > 0)
        {
            SteamSetupList.ItemsSource = manual;
            SteamSetupHeader.Visibility = Visibility.Visible;
            SteamSetupList.Visibility = Visibility.Visible;
        }
```

- [ ] **Step 3: Add the handler.** Pre-fill the manual form from the row (mirrors `OnPopularSelected`'s field-fill):

```csharp
// "Set up" on an engine-undetected game: pre-fill the manual form (name + folder + app id) and
// let the user pick the engine. The dialog's Add then registers it via the normal manual path.
private void OnSteamSetup(object sender, RoutedEventArgs e)
{
    if ((sender as FrameworkElement)?.DataContext is not SteamSetupRow row) return;
    NameBox.Text = row.Name;
    FolderBox.Text = row.InstallDir;
    SteamBox.Text = row.AppId;
    ApplyDetectedEngine();   // sets engine if we can tell; leaves the placeholder for the user otherwise
}
```

- [ ] **Step 4: Build to verify** (after Task 4's XAML lands the `SteamSetupList`/`SteamSetupHeader` names; this step's build will fail until then — expected. Confirm at Task 4):

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`

- [ ] **Step 5: Commit** (with Task 4, since the XAML names are co-dependent):

Deferred — commit together with Task 4 (the .cs references `SteamSetupList`/`SteamSetupHeader` which Task 4 creates).

---

## Task 4: Replace `SteamManualNote` with `SteamSetupList`

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml`

The current markup (~line 93-94) is `<TextBlock x:Name="SteamManualNote" Visibility="Collapsed" ... />`.

- [ ] **Step 1: Replace it** with a header + a setup list (mirror the `SteamGamesList` art template; `SelectionMode="None"`; a per-row "Set up" button):

```xml
            <TextBlock x:Name="SteamSetupHeader" Visibility="Collapsed" Margin="0,6,0,0"
                       Text="Set up (engine not detected)" FontWeight="SemiBold" FontSize="12" Opacity="0.85" />
            <ListView x:Name="SteamSetupList" Visibility="Collapsed" SelectionMode="None" MaxHeight="160"
                      BorderBrush="{ThemeResource ControlElevationBorderBrush}" BorderThickness="1">
                <ListView.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnSpacing="8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Border Grid.Column="0" Width="46" Height="22" CornerRadius="3"
                                    Background="{ThemeResource ControlElevationBorderBrush}">
                                <Image Source="{Binding Cover}" Stretch="UniformToFill" />
                            </Border>
                            <TextBlock Grid.Column="1" Text="{Binding Name}" VerticalAlignment="Center" />
                            <Button Grid.Column="2" Content="Set up" Click="OnSteamSetup" />
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
```

(The `Button`'s `DataContext` is the row, so `OnSteamSetup` reads `(sender as FrameworkElement).DataContext as SteamSetupRow`.)

- [ ] **Step 2: Build to verify** (Task 3 + 4 together compile):

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit Tasks 3 + 4 together**

```bash
git add src/ModManager.App/AddGameDialog.xaml.cs src/ModManager.App/AddGameDialog.xaml
git commit -m "feat(add-game): show-all picker — engine-undetected games get Set up rows"
```

---

## Task 5: Smoke checklist + full verification

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Append**

```markdown
## Steam show-all picker (Phase 3)
- Add Game → the quick-add list is ordered most-recently-played first (your recent games at the top).
- Engine-undetected games (Marvel Rivals, Helldivers 2) now appear as "Set up" rows with art, not a plain text note. Clicking "Set up" pre-fills the manual form (name, game folder, app id) and leaves the engine for you to pick; Add then registers it.
- A fully-detected game still one-click-adds via the checkable list unchanged.
```

- [ ] **Step 2: Full gate**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (all pass, incl. CorePurityTests)
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (0 errors)

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): Steam show-all picker checklist"
```

---

## Self-Review

- **Spec coverage:** lastPlayed parse (Task 1) ✓; recently-played sort (Task 2) ✓; show-all undetected "Set up" rows (Tasks 3-4) ✓; detected one-click list unchanged ✓.
- **Privacy:** `lastPlayed` parsed + read live for ordering, never written to disk (no new persisted field). ✓
- **Pure-Core:** parse + sort in Core (tested); IO + dialog in App. `CorePurityTests` covers it.
- **Reversibility:** read-only; nothing persisted. ✓
- **Type consistency:** `AppManifest.LastPlayed` (Task 1) → `InstalledGame.LastPlayed` (Task 1) → `InstalledGameSort.RecentlyPlayedFirst` (Task 2) → consumed via `InstalledGames()` order. `SteamSetupRow` (Task 3) bound by `SteamSetupList` (Task 4); `OnSteamSetup` reads it. Tasks 3 + 4 commit together (the .cs ↔ XAML names are co-dependent).
- **Placeholders:** none — Core code complete; App tasks carry concrete code + the grounded ctor/XAML anchors.
