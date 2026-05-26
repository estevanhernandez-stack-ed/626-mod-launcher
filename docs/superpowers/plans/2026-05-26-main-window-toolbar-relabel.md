# Main-window Toolbar Relabel + Regroup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Steps use checkbox (`- [ ]`) syntax. Build command: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (no Core tests should change — this is a UI labeling pass over already-wired commands).

**Goal:** Execute the four-piece approved design in [docs/superpowers/specs/2026-05-25-main-window-toolbar-relabel-design.md](docs/superpowers/specs/2026-05-25-main-window-toolbar-relabel-design.md): a labeling + regrouping pass over [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) and a small VM property addition for Nexus + first-section-header flags. Zero functional change to any command — every button still does what it does today.

**Architecture:** Six commits in one PR. Five XAML edits (title-bar overflow, command bar, per-row glyph labels, chip tooltips, segmented Loadout) plus one VM-side property pair (`NexusStatusBrush` + `IsFirstSectionHeader`). The existing implicit divider-based grouping at [MainWindow.xaml:102](src/ModManager.App/MainWindow.xaml#L102) and [:106](src/ModManager.App/MainWindow.xaml#L106) becomes explicit with group labels above each cluster, structured as a 2-row inner grid inside the existing command-bar `Grid Row=1`.

**Tech Stack:** WinUI 3 (XAML + Segoe MDL2 glyphs), .NET 10, CommunityToolkit.Mvvm.

---

## File Structure

| File | What changes |
| --- | --- |
| [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) | Title-bar `⋯` MenuFlyout gains two items; command bar replaced with labeled grid; per-row buttons gain subscript labels; chip tooltips added; section-header `?` button added. |
| [src/ModManager.App/MainWindow.xaml.cs](src/ModManager.App/MainWindow.xaml.cs) | `OnShowChipGlossary` handler for the section-header `?`; one `ContentDialog` for the glossary popup. |
| [src/ModManager.App/ViewModels/MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs) | New `NexusStatusBrush` computed property + `NexusConnectedChanged` notification wiring (already-existing `NexusConnected` is the source of truth). |
| [src/ModManager.App/ViewModels/ModRowViewModel.cs](src/ModManager.App/ViewModels/ModRowViewModel.cs) | New `IsFirstSectionHeader` bool + `FirstSectionHelpVisibility` (Visibility computed). Set during list rebuild in MainViewModel. |

No Core changes. No new test cases (Core tests should pass unchanged — verify after each commit).

---

## Task 1: Title bar — `⋯` absorbs Fetch metadata + + Theme

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (title-bar `⋯` MenuFlyout at lines 43–77; command bar at lines 108 and 157)

- [ ] **Step 1: Add Fetch metadata + + Theme to the `⋯` MenuFlyout**

Find the `MenuFlyout` block starting at line 46. Insert two new `MenuFlyoutItem` entries before the `<MenuFlyoutSeparator />` at line 69 (the separator before "Remove this game"), like this:

```xml
<MenuFlyoutItem Text="Fetch metadata for all mods" Command="{x:Bind ViewModel.FetchMetadataCommand}"
                ToolTipService.ToolTip="Re-fetch metadata from CurseForge for every mod (best-effort).">
    <MenuFlyoutItem.Icon>
        <FontIcon Glyph="&#xE896;" />
    </MenuFlyoutItem.Icon>
</MenuFlyoutItem>
<MenuFlyoutItem Text="Generate a theme with AI…" Click="OnNewTheme"
                ToolTipService.ToolTip="Open the theme generator.">
    <MenuFlyoutItem.Icon>
        <FontIcon Glyph="&#xE790;" />
    </MenuFlyoutItem.Icon>
</MenuFlyoutItem>
<MenuFlyoutSeparator />
```

The existing handler `OnNewTheme` at line 157 stays — the new `MenuFlyoutItem` reaches the same code-behind. `FetchMetadataCommand` is already on the VM.

- [ ] **Step 2: Remove the two buttons from the command bar**

Delete line 108 (the `<Button Content="Fetch metadata" .../>`) and line 157 (the `<Button Content="+ Theme" .../>`).

- [ ] **Step 3: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED (file-copy errors are OK if the app is running — that's the locked-DLL pattern noted in `memory/portable-build-file-lock.md`).

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml
git commit -m "feat(ui): move Fetch metadata + Generate theme into the ... menu"
```

---

## Task 2: Command bar — restructure as a 2-row inner grid with group labels

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (lines 90–113 + 149–158)

This task replaces the single horizontal StackPanel of toolbar buttons with a 2-row inner Grid: row 0 holds small uppercase group labels above the clusters, row 1 holds the controls. Group dividers move into the inner grid. The segmented Loadout control comes in Task 3 — this task keeps the three buttons as-is, just adds the wrapping structure + labels + relabels everything.

- [ ] **Step 1: Replace the inner StackPanel (lines 97–113) with a 2-row inner grid**

Replace the entire `<StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="6" Visibility="{x:Bind ViewModel.NormalBarVisibility, Mode=OneWay}">` block (lines 97–113) with:

```xml
<Grid Grid.Column="0" Visibility="{x:Bind ViewModel.NormalBarVisibility, Mode=OneWay}">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>
    <!-- Row 0: group labels -->
    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="6" Margin="0,0,0,2">
        <TextBlock Text="LIST"     Width="68" Opacity="0.45" FontSize="10" CharacterSpacing="80" />
        <TextBlock Text="TOGGLE"   Width="160" Opacity="0.45" FontSize="10" CharacterSpacing="80" Margin="6,0,0,0" />
        <TextBlock Text="LOADOUT"  Width="148" Opacity="0.45" FontSize="10" CharacterSpacing="80" Margin="6,0,0,0" />
        <TextBlock Text="MODS"     Width="100" Opacity="0.45" FontSize="10" CharacterSpacing="80" Margin="6,0,0,0" />
        <TextBlock Text="LIBRARY"  Opacity="0.45" FontSize="10" CharacterSpacing="80" Margin="6,0,0,0" />
    </StackPanel>
    <!-- Row 1: controls (existing buttons, relabeled) -->
    <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="6">
        <Button Content="↻ Rescan" Command="{x:Bind ViewModel.RefreshCommand}"
                ToolTipService.ToolTip="Re-scan the game folder for new or removed mods" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" Margin="4,6" />
        <Button Content="Enable all" Command="{x:Bind ViewModel.AllOnCommand}" />
        <Button Content="Disable all" Command="{x:Bind ViewModel.AllOffCommand}" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" Margin="4,6" />
        <Button Content="All" Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="all" />
        <Button Content="MP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="mp" />
        <Button Content="SP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="sp" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" Margin="4,6" />
        <Button Content="+ Add mods" Click="OnAddMods" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" Margin="4,6" />
        <Button Content="Reorder" Click="OnUnlockLoadOrder" ToolTipService.ToolTip="Drag rows to set the load order" />
        <Button Content="Profiles" Click="OnProfiles" ToolTipService.ToolTip="Saved loadouts" />
        <Button Content="Saves" Click="OnSaves" ToolTipService.ToolTip="Save snapshots + installed save mods" />
        <ProgressRing IsActive="{x:Bind ViewModel.IsBusy, Mode=OneWay}" Width="18" Height="18" VerticalAlignment="Center" />
    </StackPanel>
</Grid>
```

Notes:

- `↻ Rescan` is plain text — the arrow character is part of the label.
- The fixed-width `TextBlock` group labels are sized to roughly align with their button clusters. If empirical alignment is off after build, adjust widths by 4–8px increments.
- `Reorder` keeps `OnUnlockLoadOrder` as its handler — same code, just a new label.

- [ ] **Step 2: Add labels above the right cluster (View + Account)**

Find the existing right-cluster `<StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">` (currently at line 123) and wrap it the same way — a 2-row Grid with the labels row above. Replace its open tag and contents — keep the children (CoopHint, MpWarning, LaunchHint, GroupModes ComboBox, Themes ComboBox, Nexus Button — but DROP the now-moved `+ Theme` button):

```xml
<Grid Grid.Column="1">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>
    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="6" Margin="0,0,0,2" HorizontalAlignment="Right">
        <TextBlock Text="VIEW"    Width="248" Opacity="0.45" FontSize="10" CharacterSpacing="80" TextAlignment="Right" />
        <TextBlock Text="ACCOUNT" Width="80"  Opacity="0.45" FontSize="10" CharacterSpacing="80" TextAlignment="Right" Margin="6,0,0,0" />
    </StackPanel>
    <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
        <!-- Existing CoopHint / MpWarning / LaunchHint blocks stay HERE unchanged (they live before the View cluster). -->
        <!-- ... existing CoopHint, MpWarning, LaunchHint ... -->
        <ComboBox MinWidth="120" ToolTipService.ToolTip="Group the mod list"
                  ItemsSource="{x:Bind ViewModel.GroupModes}"
                  SelectedItem="{x:Bind ViewModel.GroupMode, Mode=TwoWay}" />
        <ComboBox MinWidth="120" ToolTipService.ToolTip="Theme"
                  ItemsSource="{x:Bind ViewModel.ThemeOptions, Mode=OneWay}"
                  SelectedItem="{x:Bind ViewModel.SelectedTheme, Mode=TwoWay}"
                  DisplayMemberPath="Name" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" Margin="4,6" />
        <Button Click="OnNexus" ToolTipService.ToolTip="Connect your Nexus Mods account (per-user API key) for metadata + md5 mod ID">
            <StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                <Ellipse Width="6" Height="6" Fill="{x:Bind ViewModel.NexusStatusBrush, Mode=OneWay}" />
                <TextBlock Text="Nexus" />
            </StackPanel>
        </Button>
    </StackPanel>
</Grid>
```

The CoopHint / MpWarning / LaunchHint / +Theme blocks need to be kept where they were structurally — when porting, paste their existing XAML into the `<!-- ... existing ... -->` slot (drop `+ Theme`).

- [ ] **Step 3: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

If the inner-grid restructure introduces alignment glitches, that's expected — Task 3 (segmented control) will reorganize the Loadout cluster anyway. The build needs to succeed though.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml
git commit -m "feat(ui): command bar gains group labels + verb-first button labels"
```

---

## Task 3: Loadout segmented control

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (the three `All`/`MP`/`SP` buttons from Task 2)

WinUI has no built-in segmented control, so we build one: a Border with three Buttons inside, each tinted by the active mode. Selection state is computed by comparing `ViewModel.ActiveMode` to each segment's parameter — the existing `ActiveMode` `[ObservableProperty]` on MainViewModel ([line 50](src/ModManager.App/ViewModels/MainViewModel.cs#L50)) drives it.

- [ ] **Step 1: Replace the three Loadout buttons with the segmented control**

In the Row-1 StackPanel from Task 2, find the three buttons:

```xml
<Button Content="All" Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="all" />
<Button Content="MP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="mp" />
<Button Content="SP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="sp" />
```

Replace with this segmented control:

```xml
<Border CornerRadius="3" BorderThickness="1" BorderBrush="{StaticResource ThemeBorder}"
        Background="{StaticResource ThemePanel}">
    <StackPanel Orientation="Horizontal">
        <Button Content="All" Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="all"
                BorderThickness="0" CornerRadius="0" MinWidth="44"
                Background="{x:Bind ViewModel.LoadoutAllBrush, Mode=OneWay}"
                Foreground="{x:Bind ViewModel.LoadoutAllForeground, Mode=OneWay}" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" />
        <Button Content="MP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="mp"
                BorderThickness="0" CornerRadius="0" MinWidth="44"
                Background="{x:Bind ViewModel.LoadoutMpBrush, Mode=OneWay}"
                Foreground="{x:Bind ViewModel.LoadoutMpForeground, Mode=OneWay}" />
        <shapes:Rectangle Width="1" Fill="{StaticResource ThemeBorder}" />
        <Button Content="SP"  Command="{x:Bind ViewModel.SetModeCommand}" CommandParameter="sp"
                BorderThickness="0" CornerRadius="0" MinWidth="44"
                Background="{x:Bind ViewModel.LoadoutSpBrush, Mode=OneWay}"
                Foreground="{x:Bind ViewModel.LoadoutSpForeground, Mode=OneWay}" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Add the six brush properties to MainViewModel**

Open [src/ModManager.App/ViewModels/MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs). Near the other view-model brush properties (e.g. around `MpBadgeBrush`), add:

```csharp
// Segmented Loadout control: the selected segment tints with the theme accent; the others stay
// transparent so the surrounding Border background shows through. Twin foregrounds keep contrast.
public Brush LoadoutAllBrush => SegmentBrushFor("all");
public Brush LoadoutMpBrush  => SegmentBrushFor("mp");
public Brush LoadoutSpBrush  => SegmentBrushFor("sp");
public Brush LoadoutAllForeground => SegmentForegroundFor("all");
public Brush LoadoutMpForeground  => SegmentForegroundFor("mp");
public Brush LoadoutSpForeground  => SegmentForegroundFor("sp");

private Brush SegmentBrushFor(string mode)
    => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
        ? (Application.Current.Resources["ThemeAccent"] as Brush ?? new SolidColorBrush(Colors.MediumPurple))
        : new SolidColorBrush(Colors.Transparent);

private Brush SegmentForegroundFor(string mode)
    => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
        ? new SolidColorBrush(Colors.Black)
        : (Application.Current.Resources["ThemeText"] as Brush ?? new SolidColorBrush(Colors.White));
```

Imports likely already present: `Microsoft.UI.Xaml.Media` (Brush, SolidColorBrush), `Windows.UI` (Colors). Add `using Windows.UI;` if missing.

- [ ] **Step 3: Notify when ActiveMode changes**

In the same file, find the auto-generated `OnActiveModeChanged` partial method hook (CommunityToolkit MVVM creates a `partial void OnActiveModeChanged(string value)` slot for the `[ObservableProperty] activeMode` field). If a body exists, append the notify calls; otherwise add the body:

```csharp
partial void OnActiveModeChanged(string value)
{
    OnPropertyChanged(nameof(LoadoutAllBrush));
    OnPropertyChanged(nameof(LoadoutMpBrush));
    OnPropertyChanged(nameof(LoadoutSpBrush));
    OnPropertyChanged(nameof(LoadoutAllForeground));
    OnPropertyChanged(nameof(LoadoutMpForeground));
    OnPropertyChanged(nameof(LoadoutSpForeground));
}
```

If a `partial void OnActiveModeChanged(string value)` body already exists with other logic, just add the six `OnPropertyChanged` calls at the end of it.

- [ ] **Step 4: Build the App + verify the segmented control highlights the active mode**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Run the test suite (no regressions expected — only UI changes)**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: 625 passing (same as PR #29 baseline).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(ui): segmented Loadout control (All / MP / SP) with active-tint"
```

---

## Task 4: Per-row glyphs gain 9px subscript labels

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (mod-row DataTemplate at lines 285–310)

Each FontIcon-only Button (readme at line 286, cockpit at line 292, uninstall at line 305) gets a tiny TextBlock label under the glyph. The Button content becomes a vertical StackPanel: FontIcon on top, label on bottom.

- [ ] **Step 1: Replace the readme button content**

Find the Button at line 286 (`Click="OnShowReadme"`). Replace its `<FontIcon Glyph="&#xE8A5;" FontSize="14" />` child with:

```xml
<StackPanel Orientation="Vertical" HorizontalAlignment="Center" Spacing="0">
    <FontIcon Glyph="&#xE8A5;" FontSize="14" HorizontalAlignment="Center" />
    <TextBlock Text="readme" FontSize="9" Opacity="0.55" HorizontalAlignment="Center" />
</StackPanel>
```

- [ ] **Step 2: Replace the cockpit (config) button content**

Find the Button at line 292 (`Click="OnShowCockpit"`). Replace its `<FontIcon Glyph="&#xE713;" FontSize="14" />` child with:

```xml
<StackPanel Orientation="Vertical" HorizontalAlignment="Center" Spacing="0">
    <FontIcon Glyph="&#xE713;" FontSize="14" HorizontalAlignment="Center" />
    <TextBlock Text="config" FontSize="9" Opacity="0.55" HorizontalAlignment="Center" />
</StackPanel>
```

- [ ] **Step 3: Replace the uninstall button content**

Find the Button at line 305 (`Click="OnUninstall"`). Replace its `<FontIcon Glyph="&#xE74D;" FontSize="14" Foreground="{StaticResource ThemeDanger}" />` child with:

```xml
<StackPanel Orientation="Vertical" HorizontalAlignment="Center" Spacing="0">
    <FontIcon Glyph="&#xE74D;" FontSize="14" Foreground="{StaticResource ThemeDanger}" HorizontalAlignment="Center" />
    <TextBlock Text="uninstall" FontSize="9" Opacity="0.55" HorizontalAlignment="Center" />
</StackPanel>
```

- [ ] **Step 4: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml
git commit -m "feat(ui): per-row glyphs get readme / config / uninstall subscript labels"
```

---

## Task 5: Chip hover tooltips

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (chip cluster at lines 243–282)

Add `ToolTipService.ToolTip` to every chip. The class chip and variant chip are dynamic-text (their content changes by row) — those need binding-based tooltips. The fixed chips (`Managed`, `UE4SS BUILT-IN`, `VARIANT`, `MP?`) already have tooltips or can receive a static one.

- [ ] **Step 1: Add a tooltip to the class chip (line 260)**

The class chip currently has no tooltip. Add one bound to a new computed property `ClassChipTooltip`:

```xml
<Border Background="{StaticResource ThemePanel}" CornerRadius="3" Padding="6,2"
        ToolTipService.ToolTip="{x:Bind ClassChipTooltip}">
    <TextBlock Text="{x:Bind ClassChip}" FontFamily="Consolas" FontSize="11" />
</Border>
```

- [ ] **Step 2: Add ClassChipTooltip to ModRowViewModel**

Open [src/ModManager.App/ViewModels/ModRowViewModel.cs](src/ModManager.App/ViewModels/ModRowViewModel.cs). Add near the other chip-related properties:

```csharp
/// <summary>Human-friendly explainer for the class chip (BOTH/SP/MP/etc.). Used as a hover
/// tooltip; the chip text itself stays terse.</summary>
public string ClassChipTooltip => ClassChip switch
{
    "BOTH" => "This mod is active in both your SP and MP loadouts.",
    "SP"   => "This mod is active only in your single-player loadout.",
    "MP"   => "This mod is active only in your multiplayer loadout.",
    "OFF"  => "This mod is not in any loadout — currently disabled.",
    _      => $"Mod class: {ClassChip}",
};
```

- [ ] **Step 3: Add a tooltip to the variant chip (line 263)**

```xml
<Border Background="{StaticResource ThemePanel}" CornerRadius="3" Padding="6,2"
        Visibility="{x:Bind VariantVisibility}"
        ToolTipService.ToolTip="Active variant of this mod. Click another in the family to switch.">
    <TextBlock Text="{x:Bind VariantChip}" FontFamily="Consolas" FontSize="11" />
</Border>
```

- [ ] **Step 4: Add a tooltip to the MP-safety badge (line 268)**

The MP-safety button already has a tooltip ("Multiplayer safety — click to set"). Replace it with the more specific four-state explainer:

```xml
ToolTipService.ToolTip="{x:Bind MpBadgeTooltip, Mode=OneWay}"
```

Add the corresponding property to ModRowViewModel:

```csharp
public string MpBadgeTooltip => MpBadge switch
{
    "MP-SAFE"  => "Author or verified-safe list says this works in MP. Click to override.",
    "MP-RISKY" => "Flagged as risky in MP (anti-cheat / desync). Click to override.",
    "SP-ONLY"  => "Marked SP-only — not in your MP loadout. Click to override.",
    "MP?"      => "No MP stance claimed. Click to set MP-safe, MP-risky, or SP-only.",
    _          => "Multiplayer safety — click to set.",
};
```

- [ ] **Step 5: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/ViewModels/ModRowViewModel.cs
git commit -m "feat(ui): per-chip hover tooltips with state-specific explainers"
```

---

## Task 6: Section-header `?` glossary popup

**Files:**

- Modify: [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) (section-header TextBlock at line 172)
- Modify: [src/ModManager.App/MainWindow.xaml.cs](src/ModManager.App/MainWindow.xaml.cs) (new `OnShowChipGlossary` handler)
- Modify: [src/ModManager.App/ViewModels/ModRowViewModel.cs](src/ModManager.App/ViewModels/ModRowViewModel.cs) (new `IsFirstSectionHeader` + `FirstSectionHelpVisibility`)
- Modify: [src/ModManager.App/ViewModels/MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs) (flag the first sectioned row during list rebuild)

The `?` button is anchored to the FIRST rendered section header (top of the list, regardless of which group-by is active). It opens a simple ContentDialog listing every chip + glyph used on the list.

- [ ] **Step 1: Add the two new flags to ModRowViewModel**

Open [ModRowViewModel.cs](src/ModManager.App/ViewModels/ModRowViewModel.cs). Near the existing `SectionHeader` property:

```csharp
/// <summary>True for the topmost row that carries a SectionHeader. Drives the one-time chip-legend
/// affordance — only the first header shows the "?" button to avoid noise across group-by views.</summary>
public bool IsFirstSectionHeader { get; set; }
public Visibility FirstSectionHelpVisibility =>
    IsFirstSectionHeader && !string.IsNullOrEmpty(SectionHeader) ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 2: Set IsFirstSectionHeader during list rebuild in MainViewModel**

Open [MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs). Locate the method that rebuilds `Mods` (search for `Mods.Clear()` or the section-header-assignment loop). After the loop that sets `SectionHeader` on each row, add:

```csharp
// Mark the first row carrying a SectionHeader as the legend host.
var firstSection = Mods.FirstOrDefault(m => !string.IsNullOrEmpty(m.SectionHeader));
if (firstSection is not null) firstSection.IsFirstSectionHeader = true;
```

If a private helper `AssignSectionHeaders` (or similar) already exists, put the snippet at the end of it.

- [ ] **Step 3: Modify the section header in the XAML to be a horizontal Grid with the `?` button**

In [MainWindow.xaml](src/ModManager.App/MainWindow.xaml), find the existing section-header TextBlock at line 172:

```xml
<TextBlock Text="{x:Bind SectionHeader}" Visibility="{x:Bind SectionHeaderVisibility, Mode=OneWay}"
           FontWeight="SemiBold" FontSize="12" CharacterSpacing="80"
           Foreground="{StaticResource ThemeAccent}" Margin="4,16,0,4" />
```

Replace with:

```xml
<Grid Visibility="{x:Bind SectionHeaderVisibility, Mode=OneWay}" Margin="4,16,4,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="{x:Bind SectionHeader}"
               FontWeight="SemiBold" FontSize="12" CharacterSpacing="80"
               Foreground="{StaticResource ThemeAccent}" />
    <Button Grid.Column="1" Click="OnShowChipGlossary" Padding="6,0" MinWidth="22" Height="20"
            Background="{StaticResource ThemePanel}" CornerRadius="10" BorderThickness="0"
            ToolTipService.ToolTip="What do the chips and icons mean?"
            Visibility="{x:Bind FirstSectionHelpVisibility, Mode=OneWay}">
        <TextBlock Text="?" FontSize="11" Foreground="{StaticResource ThemeAccent}" />
    </Button>
</Grid>
```

- [ ] **Step 4: Add OnShowChipGlossary to the code-behind**

Open [MainWindow.xaml.cs](src/ModManager.App/MainWindow.xaml.cs). Add the handler (near the other dialog-opening handlers):

```csharp
private async void OnShowChipGlossary(object sender, RoutedEventArgs e)
{
    var content = new StackPanel { Spacing = 8 };
    void Add(string chip, string explain)
    {
        var row = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Background = (Application.Current.Resources["ThemePanel"] as Brush) ?? new SolidColorBrush(Microsoft.UI.Colors.DimGray),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = chip, FontFamily = new FontFamily("Consolas"), FontSize = 11, MinWidth = 56, TextAlignment = TextAlignment.Center },
        };
        row.Children.Add(pill);
        row.Children.Add(new TextBlock { Text = explain, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(row);
    }
    Add("BOTH",     "Active in both your SP and MP loadouts.");
    Add("SP",       "Active only in your single-player loadout.");
    Add("MP",       "Active only in your multiplayer loadout.");
    Add("OFF",      "Currently disabled — not in any loadout.");
    Add("MP-SAFE",  "Author or verified-safe list says this works in MP.");
    Add("MP-RISKY", "Flagged risky in MP (anti-cheat / desync). Use with care.");
    Add("MP?",      "No MP stance claimed. Right-click the badge to set one.");
    Add("3x / 10x", "Active level of a variant family. Click another in the family to switch.");
    Add("VARIANT",  "One of several variants of the same mod — pick whichever fits.");
    Add("📄 readme",   "Open the mod's bundled readme.");
    Add("⚙ config",    "Open the config cockpit (UE4SS keybinds + settings).");
    Add("🗑 uninstall", "Permanently remove the mod from disk.");

    var dialog = new ContentDialog
    {
        Title = "What do these mean?",
        CloseButtonText = "Got it",
        DefaultButton = ContentDialogButton.Close,
        Content = new ScrollViewer { Content = content, MaxHeight = 420 },
        XamlRoot = Content.XamlRoot,
    };
    await dialog.ShowAsync();
}
```

Imports likely already present (`Microsoft.UI.Xaml`, `Microsoft.UI.Xaml.Controls`, `Microsoft.UI.Xaml.Media`). Add `Microsoft.UI.Text` if needed for FontFamily.

- [ ] **Step 5: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Run the test suite (no regressions)**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: 625 passing.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs \
       src/ModManager.App/ViewModels/ModRowViewModel.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(ui): first section header gets a '?' chip-glossary popup"
```

---

## Task 7: Nexus status dot via VM property

**Files:**

- Modify: [src/ModManager.App/ViewModels/MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs)

The XAML (Task 2 Step 2) binds the Ellipse's Fill to `NexusStatusBrush`. The VM needs to expose it and notify on `NexusConnected` changes.

- [ ] **Step 1: Add NexusStatusBrush to MainViewModel**

Near the existing `NexusConnected` getter at [line 569](src/ModManager.App/ViewModels/MainViewModel.cs#L569):

```csharp
/// <summary>Status dot for the Nexus toolbar button — accent-green when authed, muted gray otherwise.</summary>
public Brush NexusStatusBrush => NexusConnected
    ? ((Application.Current.Resources["ThemeAccent"] as Brush) ?? new SolidColorBrush(Microsoft.UI.Colors.MediumSpringGreen))
    : new SolidColorBrush(Microsoft.UI.Colors.Gray);
```

- [ ] **Step 2: Notify NexusStatusBrush on NexusConnected changes**

`NexusConnected` is a computed getter, not an `[ObservableProperty]`. Find where the VM currently raises `OnPropertyChanged(nameof(NexusConnected))` (search the file). Wherever that line appears, append:

```csharp
OnPropertyChanged(nameof(NexusStatusBrush));
```

If the existing notification site already raises a batch of Nexus-related properties, add `NexusStatusBrush` to that batch.

- [ ] **Step 3: Build + run tests**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: BUILD SUCCEEDED, 625 tests passing.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(ui): Nexus button status dot (accent when connected, muted when not)"
```

---

## Smoke checklist (manual — WinUI has no UI tests)

After all six tasks, run the app and walk through:

- [ ] Title-bar `⋯` menu shows "Fetch metadata for all mods" and "Generate a theme with AI…" (and the previous items).
- [ ] Command bar has 5 group labels visible: LIST · TOGGLE · LOADOUT · MODS · LIBRARY.
- [ ] Right cluster has 2 group labels: VIEW · ACCOUNT.
- [ ] `Rescan` button reads "↻ Rescan", clicking it triggers RefreshCommand.
- [ ] `Enable all` / `Disable all` buttons work (replace `All On` / `All Off`).
- [ ] Loadout segmented control: All / MP / SP highlights the active one in accent; clicking switches.
- [ ] `+ Add mods` (was `+ Add`) opens the file picker.
- [ ] `Reorder` (was `Unlock load order`) enters reorder mode; Apply/Cancel works as before.
- [ ] `Profiles` and `Saves` open their dialogs.
- [ ] Theme dropdown still works; Nexus button shows a green dot when authed, gray when not.
- [ ] Mod-row glyphs: readme / config / uninstall each show their 9px subscript label, click still works.
- [ ] Hover any chip → state-specific explainer tooltip.
- [ ] First section header shows a `?` button → opens the chip-glossary popup; subsequent headers don't show it.

---

## Self-Review Notes

- **Spec coverage:** Task 1 = §1 (title bar overflow). Tasks 2 + 3 + 7 = §2 (command bar relabel + segmented Loadout + Nexus dot). Task 4 = §3 (per-row glyphs). Tasks 5 + 6 = §4 (chip tooltips + first-section-header `?`).
- **Risk:** Low — labeling + grouping only, no command rewires. Existing handlers (`OnNewTheme`, `OnUnlockLoadOrder`, `OnAddMods`, etc.) reach the same code from their new labels.
- **No Core changes / no test changes.** Every existing test should pass through unaffected. `dotnet test` at 625 passing is the baseline.
- **Type consistency:** `LoadoutAllBrush` / `LoadoutMpBrush` / `LoadoutSpBrush` + the two foreground siblings all use the same `SegmentBrushFor` / `SegmentForegroundFor` helpers. `ClassChipTooltip` switches on `ClassChip` strings — keep the cases aligned if `ClassChip`'s set ever grows.
- **The `Reorder` rename** is the only judgment-call change; its handler `OnUnlockLoadOrder` stays. The button's behavior is unchanged.
