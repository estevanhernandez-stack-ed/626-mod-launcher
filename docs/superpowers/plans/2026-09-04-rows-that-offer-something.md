# Rows That Offer Something Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The updates list opens the mod's Nexus page, and Settings shows the holding folders left behind by games you removed, with a way to look inside, copy them out, or delete them.

**Architecture:** Two independent features sharing one release. Part 1 extracts a URL builder into Core and adds a bound, visibility-gated button to an existing row template. Part 2 adds a pure orphan-detection function plus a thin walking layer in Core, and a new Settings section over them. Every behaviour change starts as a failing xUnit test in Core; the App layer is markup plus handlers.

**Tech Stack:** .NET 10, C#, WinUI 3, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-04-a-row-that-tells-you-something-offers-the-thing-to-do-design.md`

## Global Constraints

- `src/ModManager.Core/` takes **no WinUI or WinRT types**. `CorePurityTests` enforces it. `System.IO` is fine and already used throughout Core.
- Nullable enabled; `TreatWarningsAsErrors` is **true** in Core, false in App.
- **Never run bare `dotnet test` or `dotnet build` at the repo root** — the WinUI project hangs it. Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`.
- **After any XAML edit, delete `src/ModManager.App/obj` before building**, and never build while the app is running. Stale generated code makes the app die at `Connect()` with an `InvalidCastException`.
- Every interactive control ships with an `AutomationProperties.AutomationId` in the same commit. Templated rows bind the id off a **stable key, never display copy** (`.claude/rules/automation-ids.md`).
- Filled danger only inside a confirm dialog, with element-scoped `ButtonBackgroundPointerOver` and `ButtonBackgroundPressed` (`.claude/rules/vsm-danger-buttons.md`).
- On-disk JSON is camelCase. Nothing here writes JSON, but the rule stands if you add any.
- Voice for user-facing copy: builder-to-builder, second person, sentence case. No corporate speak, no emoji.
- Conventional commits: `feat(area)`, `fix(area)`, `docs(area)`, `test(area)`.

## File Structure

| Path | Responsibility | Task |
|---|---|---|
| `src/ModManager.Core/Nexus/NexusModPage.cs` | **Create.** The one definition of a Nexus mod-page URL. | 1 |
| `src/ModManager.Core/Transport/SaveBundle.cs` | **Modify** ~line 334. Repoint at `NexusModPage`. | 1 |
| `tests/ModManager.Tests/Nexus/NexusModPageTests.cs` | **Create.** | 1 |
| `src/ModManager.App/UpdatesView.xaml.cs` | **Modify.** `UpdateRow` gains three members; the view gains a click handler. | 2 |
| `src/ModManager.App/UpdatesView.xaml` | **Modify** the row `DataTemplate`. | 2 |
| `src/ModManager.Core/LeftoverHoldings.cs` | **Create.** Pure `Orphans`, then `Find` that walks. | 3 |
| `tests/ModManager.Tests/LeftoverHoldingsTests.cs` | **Create.** | 3 |
| `src/ModManager.App/SettingsDialog.xaml` | **Modify.** New section between Restore points and Reset. | 4 |
| `src/ModManager.App/SettingsDialog.xaml.cs` | **Modify.** `LeftoverRow`, load, and three handlers. | 4 |
| `docs/smoke-tests/pending.md` | **Modify.** Two cases. | 5 |

---

### Task 1: The Nexus mod-page URL, defined once

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusModPage.cs`
- Create: `tests/ModManager.Tests/Nexus/NexusModPageTests.cs`
- Modify: `src/ModManager.Core/Transport/SaveBundle.cs` (the method containing `https://www.nexusmods.com/{nexusDomain}/mods/{id}`, around line 334)

**Interfaces:**
- Consumes: nothing.
- Produces: `ModManager.Core.Nexus.NexusModPage.Url(string? nexusDomain, int? modId) → string?`. Task 2 calls it.

- [ ] **Step 1: Write the failing test**

Create `tests/ModManager.Tests/Nexus/NexusModPageTests.cs`:

```csharp
using ModManager.Core.Nexus;

namespace ModManager.Tests.Nexus;

public class NexusModPageTests
{
    [Fact]
    public void A_domain_and_an_id_make_the_mod_page()
        => Assert.Equal("https://www.nexusmods.com/windrose/mods/153",
                        NexusModPage.Url("windrose", 153));

    // Null rather than a half-built link. A domain with no id is the game's whole mod list, which is
    // not what a row naming one mod promised; an id with no domain cannot be addressed at all. The
    // caller uses null to decide whether to show the button, so a wrong non-null here becomes a
    // button that goes somewhere the user did not ask for.
    [Theory]
    [InlineData(null, 153)]
    [InlineData("", 153)]
    [InlineData("   ", 153)]
    [InlineData("windrose", null)]
    [InlineData("windrose", 0)]
    [InlineData("windrose", -1)]
    public void Anything_missing_or_meaningless_yields_null(string? domain, int? modId)
        => Assert.Null(NexusModPage.Url(domain, modId));
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~NexusModPage"`
Expected: FAIL to compile — `NexusModPage` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/ModManager.Core/Nexus/NexusModPage.cs`:

```csharp
namespace ModManager.Core.Nexus;

/// <summary>
/// Where a mod lives on Nexus. One definition, because the shape was already written out inline in
/// SaveBundle and a second copy is a second thing to get wrong when Nexus changes a path.
/// </summary>
public static class NexusModPage
{
    /// <summary>The mod's page, or null when we cannot name the mod. Both parts are required: a
    /// domain with no id is the game's whole mod list, which is not what a row naming one mod
    /// promised.</summary>
    public static string? Url(string? nexusDomain, int? modId)
        => string.IsNullOrWhiteSpace(nexusDomain) || modId is not > 0
            ? null
            : $"https://www.nexusmods.com/{nexusDomain}/mods/{modId}";
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~NexusModPage"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Repoint SaveBundle at it**

Find the method in `src/ModManager.Core/Transport/SaveBundle.cs` whose body ends with

```csharp
        return $"https://www.nexusmods.com/{nexusDomain}/mods/{id}";
```

and replace **only that return statement** with a call to `NexusModPage.Url(...)`, adding
`using ModManager.Core.Nexus;` to the file's usings.

The method's existing signature and null-guards stay exactly as they are. If its declared return type
is non-nullable `string`, do **not** change the signature — coalesce instead (`?? ""` or whatever the
surrounding code already does for the empty case), and keep every existing early-return. Read the
whole method before editing: the point of this step is that its observable behaviour is unchanged.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS. Every pre-existing `SaveBundle` test still green — that is the proof the repoint was
behaviour-neutral. If a `SaveBundle` test fails, the coalescing in step 5 is wrong; fix step 5, do not
edit the test.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusModPage.cs tests/ModManager.Tests/Nexus/NexusModPageTests.cs src/ModManager.Core/Transport/SaveBundle.cs
git commit -m "feat(nexus): one definition of a mod-page url"
```

---

### Task 2: The updates list opens the mod page

**Files:**
- Modify: `src/ModManager.App/UpdatesView.xaml.cs` (the `UpdateRow` class near the top, and the view's handler region)
- Modify: `src/ModManager.App/UpdatesView.xaml` (the `DataTemplate` with `x:DataType="local:UpdateRow"`, around lines 88–108)

**Interfaces:**
- Consumes: `NexusModPage.Url` from Task 1.
- Produces: nothing later tasks use.

**Context you need.** `UpdateRow` wraps a `PendingUpdateGroup` and exposes `Group`, a private
`Pending => Group.Primary`, `RowAutomationId`, `ModName`, `FilesText`, `FilesVisibility` and
`VersionText`. `PendingUpdate` carries `NexusModId` (an `int?`) and `NexusDomain` (a `string?`). The
App opens URLs with `Windows.System.Launcher.LaunchUriAsync(new Uri(url))` — see
`src/ModManager.App/Tools/ToolsPanel.xaml.cs:68`.

- [ ] **Step 1: Add the three members to `UpdateRow`**

In `src/ModManager.App/UpdatesView.xaml.cs`, inside `UpdateRow`, after `VersionText`:

```csharp
    /// <summary>The mod's page, or null when this row cannot name a mod on Nexus. A row can be built
    /// from the name index or from a source that is not Nexus, and those have nothing to link to.</summary>
    public string? ModPageUrl => NexusModPage.Url(Pending.NexusDomain, Pending.NexusModId);

    /// <summary>Hidden, not greyed. A greyed button invites a hover looking for a tooltip explaining
    /// the refusal, and there is nothing useful to say beyond "we do not know which mod this is" —
    /// which the row already implies by having no version arrow.</summary>
    public Visibility GetVisibility => ModPageUrl is null ? Visibility.Collapsed : Visibility.Visible;

    public string GetAutomationName => $"Get {ModName}";
```

Add `using ModManager.Core.Nexus;` to the file's usings.

- [ ] **Step 2: Add the button to the row template**

In `src/ModManager.App/UpdatesView.xaml`, the row `Grid` currently has two columns (`*` and `Auto`),
with the version `TextBlock` in column 1. Add a third `Auto` column and put the button in it, so the
version text keeps its place:

```xml
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
```

and immediately after the version `TextBlock` (which stays in `Grid.Column="1"`):

```xml
                                                <Button Grid.Column="2" Content="Get update"
                                                        AutomationProperties.Name="{x:Bind GetAutomationName}"
                                                        Visibility="{x:Bind GetVisibility}"
                                                        Tag="{x:Bind ModPageUrl}"
                                                        Click="OnGetUpdate"
                                                        VerticalAlignment="Center" />
```

- [ ] **Step 3: Add the handler**

In `src/ModManager.App/UpdatesView.xaml.cs`, beside the existing `OnOpenGame` handler:

```csharp
    // Tag carries the resolved url rather than the row, so a row that could not build one cannot
    // reach here at all — the button it would have been on is collapsed.
    private void OnGetUpdate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string url && !string.IsNullOrEmpty(url))
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }
```

- [ ] **Step 4: Build clean**

```bash
rm -rf src/ModManager.App/obj
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0
```

Expected: `0 Error(s)`. Do not build with the app running.

- [ ] **Step 5: Run the Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, unchanged count from Task 1.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/UpdatesView.xaml src/ModManager.App/UpdatesView.xaml.cs
git commit -m "feat(updates): a row with an update offers the page to get it from"
```

---

### Task 3: Find the holding folders nothing owns

**Files:**
- Create: `src/ModManager.Core/LeftoverHoldings.cs`
- Create: `tests/ModManager.Tests/LeftoverHoldingsTests.cs`

**Interfaces:**
- Consumes: `Scanner.DataDirForGame(GameEntry)` — returns `<library>/_626mods/<id>`.
- Produces, both used by Task 4:
  - `LeftoverHoldings.Orphans(IEnumerable<string> registeredIds, IEnumerable<string> folderNames) → IReadOnlyList<string>`
  - `LeftoverHoldings.Find(IReadOnlyList<GameEntry> registered) → IReadOnlyList<LeftoverHolding>`
  - `record LeftoverHolding(string Path, string FolderName, int FileCount, long Bytes, IReadOnlyList<string> TopLevelNames)`

**Context you need.** `GameEntry` has `Id` and `GameRoot` as settable strings and an optional
`DataDir`. `Scanner.DataDirForGame` returns the per-game folder; its **parent** is the `_626mods`
root. The suite creates real temp directories via `TestSupport.TempDir("prefix-")` — see
`tests/ModManager.Tests/ArchiveModKeysForTests.cs:15` for the idiom.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/LeftoverHoldingsTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class LeftoverHoldingsTests
{
    [Fact]
    public void A_folder_matching_a_registered_game_is_not_an_orphan()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose" }));

    [Fact]
    public void A_folder_matching_no_registered_game_is_an_orphan()
        => Assert.Equal(new[] { "demonologist" },
                        LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose", "demonologist" }));

    // Ids are slugs and the folder is named from one, but a case difference between the registry and
    // the disk must never make a live game look abandoned — that is the one mistake here that offers
    // to delete files still in use.
    [Fact]
    public void Case_does_not_make_a_registered_game_look_orphaned()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "Windrose" }, new[] { "windrose" }));

    [Fact]
    public void Find_describes_an_orphan_and_leaves_a_registered_game_out()
    {
        var lib = TestSupport.TempDir("leftovers-");
        var gameRoot = Path.Combine(lib, "steamapps", "common", "Windrose");
        Directory.CreateDirectory(gameRoot);

        var holdings = Path.Combine(lib, "_626mods");
        Directory.CreateDirectory(Path.Combine(holdings, "windrose"));
        var orphan = Path.Combine(holdings, "demonologist");
        Directory.CreateDirectory(Path.Combine(orphan, "SomeMod"));
        File.WriteAllText(Path.Combine(orphan, "SomeMod", "a.pak"), "0123456789");
        File.WriteAllText(Path.Combine(orphan, "profiles.json"), "{}");

        var found = LeftoverHoldings.Find(new[]
        {
            new GameEntry { Id = "windrose", GameName = "Windrose", GameRoot = gameRoot },
        });

        var one = Assert.Single(found);
        Assert.Equal("demonologist", one.FolderName);
        Assert.Equal(2, one.FileCount);                       // counts the whole tree, not just the top
        Assert.Equal(12, one.Bytes);
        // It is NOT only mods — the folder holds profiles and metadata too, and the UI has to be able
        // to say so rather than call it all "mods".
        Assert.Contains("SomeMod", one.TopLevelNames);
        Assert.Contains("profiles.json", one.TopLevelNames);
    }

    // The accepted blind spot, pinned so it is a decision rather than a surprise: roots come from the
    // registered games. A library whose every game has been removed is a library nothing points at.
    // The alternative is walking drives for a folder name, which is how a tool offers to delete a
    // directory it merely recognised.
    [Fact]
    public void A_root_no_registered_game_points_at_is_never_scanned()
    {
        var lib = TestSupport.TempDir("leftovers-unseen-");
        Directory.CreateDirectory(Path.Combine(lib, "_626mods", "demonologist"));

        Assert.Empty(LeftoverHoldings.Find(Array.Empty<GameEntry>()));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LeftoverHoldings"`
Expected: FAIL to compile — `LeftoverHoldings` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/ModManager.Core/LeftoverHoldings.cs`:

```csharp
namespace ModManager.Core;

/// <summary>One holding folder that belongs to no registered game, described well enough for a user
/// to decide what to do with it. TopLevelNames matters because the folder is NOT only mods — it also
/// holds profiles, classification and metadata, and a UI that calls it "mods" invites someone to
/// delete a profile they wanted.</summary>
public sealed record LeftoverHolding(string Path, string FolderName, int FileCount, long Bytes,
                                     IReadOnlyList<string> TopLevelNames);

/// <summary>
/// The holding folders left behind when a game is removed from the launcher. Disabling a mod moves
/// its files to <c>&lt;library&gt;/_626mods/&lt;game-id&gt;/</c>; removing the game leaves that folder
/// referenced by nothing and shown nowhere, which sits badly next to a promise to keep your files.
/// </summary>
public static class LeftoverHoldings
{
    /// <summary>Pure: which folder names belong to no registered game. The whole judgment, with no
    /// filesystem in it.</summary>
    public static IReadOnlyList<string> Orphans(
        IEnumerable<string> registeredIds, IEnumerable<string> folderNames)
    {
        var known = new HashSet<string>(registeredIds, StringComparer.OrdinalIgnoreCase);
        return folderNames.Where(n => !known.Contains(n)).ToList();
    }

    /// <summary>Walks the holding roots the registered games point at and describes what Orphans
    /// picked out. Roots come from the games themselves, never from scanning drives — so a folder
    /// this app did not create cannot appear here.</summary>
    public static IReadOnlyList<LeftoverHolding> Find(IReadOnlyList<GameEntry> registered)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in registered)
        {
            var parent = System.IO.Path.GetDirectoryName(Scanner.DataDirForGame(g));
            if (!string.IsNullOrEmpty(parent)) roots.Add(parent);
        }

        var ids = registered.Select(g => g.Id).Where(id => !string.IsNullOrEmpty(id));
        var found = new List<LeftoverHolding>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            var names = Directory.GetDirectories(root)
                .Select(System.IO.Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            foreach (var name in Orphans(ids, names))
            {
                var path = System.IO.Path.Combine(root, name);
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                var top = Directory.GetFileSystemEntries(path)
                    .Select(System.IO.Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                found.Add(new LeftoverHolding(
                    path, name, files.Length,
                    files.Sum(f => new FileInfo(f).Length), top));
            }
        }

        return found.OrderBy(h => h.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
```

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LeftoverHoldings"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole suite, including the purity guard**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS. `CorePurityTests` green — this file touches only `System.IO`.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/LeftoverHoldings.cs tests/ModManager.Tests/LeftoverHoldingsTests.cs
git commit -m "feat(core): find the holding folders no registered game owns"
```

---

### Task 4: The Settings section

**Files:**
- Modify: `src/ModManager.App/SettingsDialog.xaml` — new section between the *Restore points* `StackPanel` and the *Reset* one, with a `Border` divider between, matching the existing rhythm.
- Modify: `src/ModManager.App/SettingsDialog.xaml.cs` — a `LeftoverRow` record beside `RestorePointRow` (around line 25), a loader beside the restore-points loader (around line 845), and three handlers.

**Interfaces:**
- Consumes: `LeftoverHoldings.Find` and `LeftoverHolding` from Task 3.
- Produces: nothing later tasks use.

**Context you need.** `RestorePointRow` (`SettingsDialog.xaml.cs:25`) is the template to copy — a
`sealed record` with a `RowAutomationId` and one `*AutomationName` per button, pre-formatting its
display strings so the XAML binds plain strings with no converter. Sections carry
`AutomationProperties.AutomationId="SettingsGroup.<name>"` and an `AutomationProperties.Name`. Rows go
in an `ItemsRepeater` with a `StackLayout`, and an empty-state `TextBlock` sits above it, collapsed by
default — see `NoRestorePointsText`.

- [ ] **Step 1: Add the row type**

In `src/ModManager.App/SettingsDialog.xaml.cs`, beside `RestorePointRow`:

```csharp
/// <summary>One row in Settings → Leftover mod folders. Detail pre-formats the count, size and what
/// is actually inside, so the template binds plain strings. Names come off the FOLDER NAME, which is
/// a stable key, never off display copy.</summary>
public sealed record LeftoverRow(string Path, string FolderName, string Detail)
{
    public string RowAutomationId => $"Leftover.{FolderName}";
    public string ShowAutomationName => $"Show files for {FolderName}";
    public string CopyAutomationName => $"Save a copy of {FolderName}";
    public string RemoveAutomationName => $"Remove {FolderName}";
}
```

- [ ] **Step 2: Add the markup**

In `src/ModManager.App/SettingsDialog.xaml`, after the *Restore points* `StackPanel` closes and after
a `Border` divider matching the ones around it:

```xml
            <!-- ============ Leftover mod folders ============
                 Between Restore points and Reset, so the dialog reads: back it up, undo it, tidy it,
                 reset it. These folders hold DISABLED MODS, PROFILES AND METADATA, not only mods —
                 the copy says so, because "leftover mods" is the wording that gets someone to remove
                 a profile they wanted. -->
            <StackPanel AutomationProperties.AutomationId="SettingsGroup.leftovers"
                        AutomationProperties.Name="Leftover mod folders" Spacing="{StaticResource SpaceM}">
                <TextBlock Text="Leftover mod folders" FontWeight="SemiBold" FontSize="{StaticResource RowTitleFontSize}" />
                <TextBlock TextWrapping="Wrap" Foreground="{StaticResource ThemeInkSoft}" FontSize="{StaticResource BodyFontSize}"
                           Text="Folders holding the disabled mods, profiles and settings of games you no longer have here. Nothing has been deleted — look inside, keep a copy, or clear one out. Only folders sitting beside games you still have can be listed." />
                <TextBlock x:Name="NoLeftoversText" AutomationProperties.AutomationId="LeftoversEmptyText"
                           Text="Nothing left over."
                           Foreground="{StaticResource ThemeInkDim}" FontSize="{StaticResource BodyFontSize}" Visibility="Collapsed" />
                <ItemsRepeater x:Name="LeftoversList">
                    <ItemsRepeater.Layout>
                        <StackLayout Orientation="Vertical" Spacing="8" />
                    </ItemsRepeater.Layout>
                    <ItemsRepeater.ItemTemplate>
                        <DataTemplate x:DataType="local:LeftoverRow">
                            <Grid AutomationProperties.AutomationId="{x:Bind RowAutomationId}" ColumnSpacing="8">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0" VerticalAlignment="Center">
                                    <TextBlock Foreground="{StaticResource ThemeInk}" Text="{x:Bind FolderName}" FontWeight="SemiBold" />
                                    <TextBlock Text="{x:Bind Detail}"
                                               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                               FontSize="{StaticResource BodyFontSize}" TextWrapping="Wrap" />
                                </StackPanel>
                                <Button AutomationProperties.Name="{x:Bind ShowAutomationName}" Grid.Column="1"
                                        Content="Show files" Tag="{x:Bind Path}" Click="OnShowLeftover" VerticalAlignment="Center" />
                                <!-- Save a copy sits BEFORE Remove deliberately: the one irreversible
                                     action is last, and is the only one styled as danger. -->
                                <Button AutomationProperties.Name="{x:Bind CopyAutomationName}" Grid.Column="2"
                                        Content="Save a copy…" Tag="{x:Bind Path}" Click="OnCopyLeftover" VerticalAlignment="Center" />
                                <!-- Outlined danger, expressed inline. There is NO named danger button
                                     style in this app — grep confirms it; LibraryView.xaml:164 does
                                     the same thing the same way. Do not invent a style resource. -->
                                <Button AutomationProperties.Name="{x:Bind RemoveAutomationName}" Grid.Column="3"
                                        Content="Remove" Tag="{x:Bind Path}" Click="OnRemoveLeftover" VerticalAlignment="Center"
                                        Background="Transparent" BorderThickness="1"
                                        BorderBrush="{StaticResource ThemeDanger}"
                                        Foreground="{StaticResource ThemeDanger}" />
                            </Grid>
                        </DataTemplate>
                    </ItemsRepeater.ItemTemplate>
                </ItemsRepeater>
                <TextBlock x:Name="LeftoverStatusText" AutomationProperties.AutomationId="LeftoverStatusText"
                           AutomationProperties.LiveSetting="Polite"
                           Foreground="{StaticResource ThemeInkSoft}" FontSize="{StaticResource BodyFontSize}"
                           TextWrapping="Wrap" IsTextSelectionEnabled="True" />
            </StackPanel>
```

**Check `DangerOutlineButtonStyle` exists** before using that name — grep the App's resource
dictionaries for the outlined-danger style the design language already defines. If it is named
something else, use the real name. If no such style exists, leave the `Style` off entirely and say so
in your report rather than inventing one; a plain button is honest, a wrong style name fails the build.

- [ ] **Step 3: Load the rows**

Beside the restore-points loader in `SettingsDialog.xaml.cs`, add a method called from the same place
that populates the other sections:

```csharp
    private void LoadLeftovers()
    {
        var rows = LeftoverHoldings.Find(_registry.Games)
            .Select(h => new LeftoverRow(h.Path, h.FolderName, DescribeLeftover(h)))
            .ToList();

        LeftoversList.ItemsSource = rows;
        NoLeftoversText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Names what is actually in there. A bare file count invites "it's only two files" on a folder
    // whose two files are a profile and a mod the user spent an evening on.
    private static string DescribeLeftover(LeftoverHolding h)
    {
        var size = h.Bytes >= 1048576 ? $"{h.Bytes / 1048576.0:0.#} MB"
                 : h.Bytes >= 1024    ? $"{h.Bytes / 1024.0:0.#} KB"
                 : $"{h.Bytes} bytes";
        var what = h.TopLevelNames.Count == 0 ? "empty" : string.Join(", ", h.TopLevelNames.Take(4))
                 + (h.TopLevelNames.Count > 4 ? $", and {h.TopLevelNames.Count - 4} more" : "");
        return $"{h.FileCount} file{(h.FileCount == 1 ? "" : "s")}, {size} — {what}";
    }
```

**Use whatever the file already calls the registry.** Grep the class for how the other loaders reach
the registered games (`_registry.Games`, a field, or a property) and match it. Call `LoadLeftovers()`
from the same method that calls the restore-points loader.

- [ ] **Step 4: Add the three handlers**

```csharp
    private void OnShowLeftover(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path && Directory.Exists(path))
            _ = Windows.System.Launcher.LaunchFolderPathAsync(path);
    }

    private async void OnCopyLeftover(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !Directory.Exists(path)) return;

        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");
        var dest = await picker.PickSingleFolderAsync();
        if (dest is null) return;

        // The WHOLE folder, never a filtered subset. Deciding for the user which parts of their own
        // data are worth keeping is the judgment this feature exists to stop making.
        try
        {
            var target = Path.Combine(dest.Path, Path.GetFileName(path));
            CopyTree(path, target);
            LeftoverStatusText.Text = $"Copied to {target}. Nothing on this machine changed.";
        }
        catch (Exception ex)
        {
            LeftoverStatusText.Text = $"Could not copy: {ex.Message}";
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    private async void OnRemoveLeftover(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !Directory.Exists(path)) return;

        var name = Path.GetFileName(path);
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;

        // Names the folder and what goes with it, never "this item". This is the app's own delete
        // path, so the confirm has to be specific enough that a wrong click is the user's, not ours.
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Remove {name}?",
            Content = $"Deletes {files} file{(files == 1 ? "" : "s")} permanently, including any "
                    + "profiles and settings in that folder. This cannot be undone.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Keep it",
            DefaultButton = ContentDialogButton.Close,
        };
        DialogTheming.ApplyDangerPrimary(confirm);   // see step 5

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            Directory.Delete(path, recursive: true);
            LeftoverStatusText.Text = $"Removed {name}.";
            LoadLeftovers();
        }
        catch (Exception ex)
        {
            LeftoverStatusText.Text = $"Could not remove {name}: {ex.Message}";
        }
    }
```

**Match the file's existing idioms** for the window handle (`_hwnd` or whatever it is called here) and
for how other dialogs in this file are constructed and shown. Grep before assuming.

- [ ] **Step 5: Wire the filled-danger confirm**

`DialogTheming.ApplyDangerPrimary` in the snippet above is a **placeholder name**. Read
`src/ModManager.App/SafeClearDialog.xaml.cs` — the reference implementation named in
`.claude/rules/vsm-danger-buttons.md` — and use whatever mechanism it actually uses, which hooks the
**Title content's `Loaded`** (never `Opened`) and element-scopes the state keys on the
`PrimaryButton` template part with the SAME live brush instances `ThemeService.Apply` mutates:

```csharp
button.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
button.Resources["ButtonBackgroundPressed"]     = res["ThemeDanger"];
button.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
button.Resources["ButtonForegroundPressed"]     = res["ThemeBg"];
```

Never new brushes — they freeze the colour at injection time. If a shared helper already exists,
reuse it; if `SafeClearDialog` does it inline, extracting a small shared helper is welcome, and
`SafeClearDialog` must keep working identically if you do.

- [ ] **Step 6: Build clean**

```bash
rm -rf src/ModManager.App/obj
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0
```

Expected: `0 Error(s)`.

- [ ] **Step 7: Run the Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, unchanged from Task 3.

- [ ] **Step 8: Commit**

```bash
git add src/ModManager.App/SettingsDialog.xaml src/ModManager.App/SettingsDialog.xaml.cs
git commit -m "feat(settings): the mod folders games left behind, with a way out of each"
```

---

### Task 5: The smoke cases

**Files:**
- Modify: `docs/smoke-tests/pending.md` (append two `##` sections at the end, matching the existing heading and prose style)

**Interfaces:** consumes nothing, produces nothing. Documentation only.

- [ ] **Step 1: Write the updates case**

Append a section titled `## An update row offers the page to get it from`, covering:

1. Open the updates screen with at least one pending update. Each row with a known Nexus mod shows a
   **Get update** button; clicking it opens that mod's page in the browser.
2. A row the launcher could not match to a Nexus mod shows **no button at all** — not a greyed one.
   State that this is the deliberate shape and why: a greyed button invites a hover looking for an
   explanation that does not exist.
3. The version text keeps its position; the button is a third column, not a replacement.

- [ ] **Step 2: Write the leftovers case**

Append a section titled `## The mod folders a removed game left behind`, covering:

1. Settings shows a **Leftover mod folders** section between Restore points and Reset, listing exactly
   the folders under `_626mods` roots that no registered game owns. On the maintainer's machine as of
   2026-09-04 that is seven: `demonologist`, `phasmophobia`, `ready-or-not`, `repo`,
   `captain-of-industry`, `schedule-i`, `marvel-s-spider-man-2-2`.
2. **No registered game ever appears.** Fifteen are registered; none of their folders are listed.
3. Each row names its file count, its size, and what is actually inside — a row must not read as
   "mods" when the folder also holds profiles and settings.
4. **Show files** opens the folder and changes nothing.
5. **Save a copy…** writes the whole tree elsewhere and changes nothing on this machine. Verify
   byte-for-byte with an independent hash of both trees.
6. **Remove** asks first, names the folder and its file count, and defaults to keeping it.
   **Exercise this on a throwaway folder you created for the test, never on one of the real seven** —
   those are the maintainer's files and what to do with each is his call.
7. The section states that it can only list folders sitting beside games you still have.

Include the standard note that the real seven are not fixtures.

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): an update row that opens, and folders a removed game left"
```
