# Identify Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace six overlapping identify/metadata menu actions with one "Identify my mods…" that runs every evidence tier automatically, plus an Advanced submenu keeping the individual passes.

**Architecture:** A new `MainViewModel.IdentifyMyModsAsync` sequences four passes (sweep → md5 → fill-blanks → name search) over the existing, already-tested Core primitives. Proposal-building is split out of `DiscoverExistingModsAsync` so it can be composed. The two existing review dialogs merge into one two-section dialog that takes both proposal lists as separate typed collections — a presentation merge, not a data-model merge, so both write paths and their tests stay untouched.

**Tech Stack:** .NET 10, C# (nullable enabled, warnings-as-errors), WinUI 3, xUnit.

## Global Constraints

- Target projects explicitly. **Never** run bare `dotnet build`/`dotnet test` at the repo root — the WinUI project hangs it. Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.
- **Close the running app before building.** Check with `tasklist //FI "IMAGENAME eq ModManager.App.exe"`; a build over a running app fails on file locks, and building after a XAML edit while it runs produces torn codegen.
- After any `.xaml` edit, delete `src/ModManager.App/obj` and `src/ModManager.App/bin` before rebuilding.
- Metadata-only writes. No file is moved, renamed, or deleted by any pass in this plan.
- No WinUI/WinRT types in `src/ModManager.Core/` — `CorePurityTests` enforces it.
- UI copy: sentence case, no emoji, periods at the end of microcopy, em-dashes fine. No "empower/leverage/seamlessly/unlock".
- Any new computed visibility property ships with its `OnPropertyChanged` sites. See Task 6.

---

### Task 1: Core — weaker evidence must not overwrite stronger within one run

**Files:**
- Modify: `src/ModManager.Core/LooseMods/LooseIdentify.cs`
- Test: `tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `LooseIdentify.ExcludeKeys(IReadOnlyList<(string ModKey, SourceSearchHit Hit)> approved, IEnumerable<string> alreadyWritten) -> IReadOnlyList<(string ModKey, SourceSearchHit Hit)>`

**Why:** In the unified run, the md5 pass and the name-search pass can both resolve the same mod key. An archive's real write keys come from its CONTENTS and are only resolved at apply time (`Scanner.ArchiveModKeysFor`), so the propose-time filters cannot see the collision. md5 is exact; a name search is a guess. The run applies md5 first and the name-search pass must skip anything md5 just wrote.

- [ ] **Step 1: Write the failing test**

Add to `tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs`, before the closing brace:

```csharp
    // ---- ExcludeKeys: the strong pass wins inside a single run ----

    // An archive's md5 write keys resolve only at apply time, so a name-search proposal for the
    // same row survives every propose-time filter. Applying md5 first and filtering here is what
    // stops a guess from overwriting an exact match.
    [Fact]
    public void Keys_already_written_by_a_stronger_pass_are_dropped()
    {
        var approved = new[]
        {
            ("EquipmentEx", Hit("Equipment-EX", 1)),
            ("GoneAway", Hit("Gone Away", 2)),
        };

        var kept = LooseIdentify.ExcludeKeys(approved, new[] { "EquipmentEx" });

        var one = Assert.Single(kept);
        Assert.Equal("GoneAway", one.ModKey);
    }

    [Fact]
    public void Key_exclusion_ignores_case()
    {
        var approved = new[] { ("EquipmentEx", Hit("Equipment-EX", 1)) };

        Assert.Empty(LooseIdentify.ExcludeKeys(approved, new[] { "equipmentex" }));
    }

    [Fact]
    public void Excluding_against_nothing_keeps_every_approved_pair()
    {
        var approved = new[] { ("A", Hit("A", 1)), ("B", Hit("B", 2)) };

        Assert.Equal(2, LooseIdentify.ExcludeKeys(approved, Array.Empty<string>()).Count);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LooseIdentifyTests"`
Expected: FAIL — `error CS0117: 'LooseIdentify' does not contain a definition for 'ExcludeKeys'`

- [ ] **Step 3: Write minimal implementation**

Add to `src/ModManager.Core/LooseMods/LooseIdentify.cs`, inside the `LooseIdentify` class:

```csharp
    /// <summary>Drop approved name-search pairs whose key a STRONGER pass already wrote in this
    /// run. An archive's md5 write keys come from its contents and resolve only at apply time
    /// (<c>Scanner.ArchiveModKeysFor</c>), so a name-search proposal for the same row clears every
    /// propose-time filter. The run applies md5 first and calls this before applying name-search
    /// results — an exact hash match must never be replaced by a name guess.</summary>
    public static IReadOnlyList<(string ModKey, SourceSearchHit Hit)> ExcludeKeys(
        IReadOnlyList<(string ModKey, SourceSearchHit Hit)> approved, IEnumerable<string> alreadyWritten)
    {
        // Built here rather than taken as a set so a case-sensitive caller collection cannot
        // silently defeat the exclusion — mod keys are compared case-insensitively everywhere else.
        var written = new HashSet<string>(alreadyWritten, StringComparer.OrdinalIgnoreCase);
        return approved.Where(a => !written.Contains(a.ModKey)).ToList();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LooseIdentifyTests"`
Expected: PASS, 19 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/LooseMods/LooseIdentify.cs tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs
git commit -m "feat(identify): let a stronger pass block a weaker one inside a single run"
```

---

### Task 2: Split discovery proposal-building from its review/apply

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`DiscoverExistingModsAsync`, around line 2200)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `private async Task<IReadOnlyList<AdoptionProposal>> BuildDiscoveryProposalsAsync(GameContext ctx, CancellationToken ct)` — the sweep + three-tier classification with NO review and NO write.

**Why:** The unified run needs discovery's proposals without discovery's dialog, because there is now one dialog for the whole run. This task is a pure refactor — no behavior change, so `DiscoverExistingModsAsync` must still work identically for the Advanced path.

- [ ] **Step 1: Extract the proposal-building body**

In `MainViewModel.cs`, `DiscoverExistingModsAsync` currently sweeps, filters, runs the tiers, then calls `ReviewDiscoveries`. Move everything from the `skipFolders` setup down to (but NOT including) the `ReviewDiscoveries` call into a new private method:

```csharp
    /// <summary>Sweep + classify + tier-match, stopping BEFORE review. Split out of
    /// <see cref="DiscoverExistingModsAsync"/> so the unified identify run can compose discovery
    /// with the other passes behind a single review dialog. Writes nothing.</summary>
    private async Task<IReadOnlyList<AdoptionProposal>> BuildDiscoveryProposalsAsync(
        GameContext ctx, CancellationToken ct)
    {
        // ... the existing body, verbatim, from the skipFolders list through the tier loop ...
        // Return the proposals list instead of calling ReviewDiscoveries.
    }
```

Keep every existing comment. The `ct` parameter is checked in the tier loop only — add `if (ct.IsCancellationRequested) break;` as the first line of the `foreach` over candidates.

- [ ] **Step 2: Reduce `DiscoverExistingModsAsync` to a caller**

```csharp
    public async Task DiscoverExistingModsAsync(bool auto)
    {
        if (_ctx is null) return;
        var ctx = _ctx!;

        var proposals = await BuildDiscoveryProposalsAsync(ctx, CancellationToken.None);
        if (proposals.Count == 0)
        {
            if (!auto) StatusText = "No unmanaged mods found in this game's folder.";
            return;
        }
        if (ReviewDiscoveries is null) return; // unwired view -> nothing adopted
        var approved = await ReviewDiscoveries(proposals);
        await ApplyDiscoveriesAsync(approved, proposals.Count, ctx);
    }
```

If the existing apply logic is currently inline at the end of `DiscoverExistingModsAsync`, extract it to `ApplyDiscoveriesAsync(IReadOnlyList<AdoptionProposal> approved, int proposalCount, GameContext ctx)` verbatim, returning the set of keys it wrote:

```csharp
    /// <summary>Persist approved adoptions. Returns the mod keys actually written, so the unified
    /// run can stop a weaker later pass from overwriting them (see LooseIdentify.ExcludeKeys).</summary>
    private async Task<IReadOnlyList<string>> ApplyDiscoveriesAsync(
        IReadOnlyList<AdoptionProposal> approved, int proposalCount, GameContext ctx)
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 Errors.

- [ ] **Step 4: Run the suite (no regressions in Core)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "refactor(viewmodel): split discovery proposal-building from review and apply"
```

---

### Task 3: The merged two-section review dialog

**Files:**
- Create: `src/ModManager.App/IdentifyReviewDialog.xaml`
- Create: `src/ModManager.App/IdentifyReviewDialog.xaml.cs`
- Test: none (WinUI dialogs are not unit-testable; covered by Task 7's smoke entries)

**Interfaces:**
- Consumes: `AdoptionProposal` (Core), `LooseIdentifyProposal` (Core).
- Produces:
  - `IdentifyReviewDialog(IReadOnlyList<AdoptionProposal> newToList, IReadOnlyList<LooseIdentifyProposal> nowIdentified)`
  - `IReadOnlyList<AdoptionProposal> ApprovedAdoptions()`
  - `IReadOnlyList<(string ModKey, SourceSearchHit Hit)> ApprovedIdentifications()`

**Why:** One run, one dialog. Two typed lists rather than a merged proposal type, so both existing apply paths keep working unchanged.

**Design constraint carried from the spec:** the fill-blanks pass does NOT appear here. Only identity claims are reviewable.

- [ ] **Step 1: Create the XAML**

Create `src/ModManager.App/IdentifyReviewDialog.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.IdentifyReviewDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ModManager.App"
    PrimaryButtonText="Apply"
    CloseButtonText="Cancel"
    DefaultButton="Close"
    AutomationProperties.Name="Identify my mods"
    PrimaryButtonClick="OnApply">

    <ContentDialog.Title>
        <StackPanel Spacing="6">
            <Border Height="3" Background="{StaticResource ThemeAccent}" Margin="-24,0,-24,4"
                    AutomationProperties.AccessibilityView="Raw" />
            <TextBlock Text="LIBRARY // IDENTIFY" FontFamily="{StaticResource MonoFontFamily}"
                       FontSize="{StaticResource TagFontSize}" CharacterSpacing="80"
                       AutomationProperties.AccessibilityView="Raw"
                       Foreground="{StaticResource ThemeInkDim}" />
            <TextBlock Text="Identify my mods" FontSize="{StaticResource ViewTitleFontSize}" FontWeight="SemiBold" />
        </StackPanel>
    </ContentDialog.Title>

    <ScrollViewer MaxHeight="480" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Spacing="{StaticResource SpaceM}" Width="460">
            <TextBlock TextWrapping="Wrap" Foreground="{StaticResource ThemeInkSoft}"
                       FontSize="{StaticResource BodyFontSize}"
                       Text="Here's what we found. Nothing is saved until you apply, and no file is moved either way." />

            <TextBlock x:Name="NewHeader" Text="NEW TO YOUR LIST" FontFamily="{StaticResource MonoFontFamily}"
                       FontSize="{StaticResource TagFontSize}" CharacterSpacing="80"
                       Foreground="{StaticResource ThemeInkDim}" />
            <ItemsControl x:Name="NewList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="local:IdentifyReviewRow">
                        <Grid ColumnSpacing="{StaticResource SpaceS}" Padding="0,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <CheckBox Grid.Column="0" IsChecked="{x:Bind Approve, Mode=TwoWay}"
                                      AutomationProperties.Name="{x:Bind Headline}" VerticalAlignment="Top"
                                      Click="OnRowClick" />
                            <StackPanel Grid.Column="1" Spacing="2">
                                <TextBlock Text="{x:Bind Headline}" TextWrapping="Wrap"
                                           Foreground="{StaticResource ThemeInk}" />
                                <TextBlock Text="{x:Bind Detail}" TextWrapping="Wrap"
                                           FontSize="{StaticResource MetaFontSize}"
                                           Foreground="{StaticResource ThemeInkDim}" />
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock x:Name="IdentifiedHeader" Text="NOW IDENTIFIED" FontFamily="{StaticResource MonoFontFamily}"
                       FontSize="{StaticResource TagFontSize}" CharacterSpacing="80"
                       Foreground="{StaticResource ThemeInkDim}" />
            <ItemsControl x:Name="IdentifiedList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="local:IdentifyReviewRow">
                        <Grid ColumnSpacing="{StaticResource SpaceS}" Padding="0,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <CheckBox Grid.Column="0" IsChecked="{x:Bind Approve, Mode=TwoWay}"
                                      AutomationProperties.Name="{x:Bind Headline}" VerticalAlignment="Top"
                                      Visibility="{x:Bind CheckboxVisibility}" Click="OnRowClick" />
                            <StackPanel Grid.Column="1" Spacing="2">
                                <TextBlock Text="{x:Bind Headline}" TextWrapping="Wrap"
                                           Foreground="{StaticResource ThemeInk}" />
                                <TextBlock Text="{x:Bind Detail}" TextWrapping="Wrap"
                                           FontSize="{StaticResource MetaFontSize}"
                                           Foreground="{StaticResource ThemeInkDim}"
                                           Visibility="{x:Bind DetailVisibility}" />
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</ContentDialog>
```

- [ ] **Step 2: Create the code-behind**

Create `src/ModManager.App/IdentifyReviewDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Discovery;
using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// The single review for a whole "Identify my mods" run. Two sections: things not previously in the
/// mod list at all, and existing rows we can now name. Apply is the ONLY write path; Cancel writes
/// nothing.
///
/// <para>What is NOT here is deliberate: the run's fill-blanks pass (fetching a description and
/// cover art for a row whose Nexus mod id we already hold) never appears. That is not a claim about
/// WHICH mod something is — it is detail about an identity already established — and listing a
/// hundred rows of "we added a description" would bury the handful that need real judgment. The
/// rule for anything added to this dialog later: approve identity, do not approve detail.</para>
/// </summary>
public sealed partial class IdentifyReviewDialog : ContentDialog
{
    private readonly List<IdentifyReviewRow> _new = new();
    private readonly List<IdentifyReviewRow> _identified = new();

    public IdentifyReviewDialog(
        IReadOnlyList<AdoptionProposal> newToList,
        IReadOnlyList<LooseIdentifyProposal> nowIdentified)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this);

        foreach (var p in newToList)
        {
            var identified = p.Evidence != AdoptionEvidence.None;
            var loader = p.Candidate.Kind == DiscoveryKind.ProxyLoader;
            _new.Add(new IdentifyReviewRow
            {
                Adoption = p,
                Headline = (identified, loader) switch
                {
                    (true, _) => $"{p.Candidate.FileName} — {p.Title}",
                    (false, true) => $"{p.Candidate.FileName} — mod loader",
                    _ => $"{p.Candidate.FileName} — not identified",
                },
                Detail = (p.Evidence, loader) switch
                {
                    (AdoptionEvidence.Md5, _) => $"Exact match by file hash. {p.Candidate.RelativePath}",
                    (AdoptionEvidence.NameIndex, _) => $"Matched by name{(p.Author is null ? "" : $" · by {p.Author}")}. {p.Candidate.RelativePath}",
                    (_, true) => $"Found at {p.Candidate.RelativePath}. This is the loader other mods ride on, not a mod itself.",
                    _ => $"Found at {p.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                Approve = identified,
            });
        }

        foreach (var p in nowIdentified)
        {
            _identified.Add(p.Match is null
                ? new IdentifyReviewRow { ModKey = p.ModKey, Headline = $"{p.CleanQuery} — no confident match" }
                : new IdentifyReviewRow
                {
                    ModKey = p.ModKey,
                    Hit = p.Match,
                    Approve = true,
                    Headline = $"{p.CleanQuery} → {p.Match.Name}"
                               + (string.IsNullOrWhiteSpace(p.Match.Author) ? "" : $" · by {p.Match.Author}"),
                    Detail = TrimSummary(p.Match.Summary),
                });
        }

        NewList.ItemsSource = _new;
        IdentifiedList.ItemsSource = _identified;
        // A section with nothing in it is noise — hide its header and its list together.
        NewHeader.Visibility = NewList.Visibility = _new.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdentifiedHeader.Visibility = IdentifiedList.Visibility = _identified.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncPrimary();
    }

    public IReadOnlyList<AdoptionProposal> ApprovedAdoptions()
        => _new.Where(r => r.Approve && r.Adoption is not null).Select(r => r.Adoption!).ToList();

    public IReadOnlyList<(string ModKey, SourceSearchHit Hit)> ApprovedIdentifications()
        => _identified.Where(r => r.Approve && r.Hit is not null).Select(r => (r.ModKey, r.Hit!)).ToList();

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args) { /* results read via the Approved* methods */ }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is IdentifyReviewRow row) row.Approve = cb.IsChecked == true;
        SyncPrimary();
    }

    // One count across BOTH sections — the button promises exactly what Apply will write.
    private void SyncPrimary()
    {
        var n = ApprovedAdoptions().Count + ApprovedIdentifications().Count;
        PrimaryButtonText = $"Apply {n} change{(n == 1 ? "" : "s")}";
        IsPrimaryButtonEnabled = n > 0;
    }

    private static string TrimSummary(string? summary)
    {
        var s = (summary ?? "").Trim();
        return s.Length <= 160 ? s : s[..159].TrimEnd() + "…";
    }
}

/// <summary>
/// One reviewable row. Top-level (not nested) to match the rest of the App's x:Bind DataTemplate
/// rows — nesting it and referencing it via x:DataType="local:Outer+Row" compiles to an invalid
/// generic-type-argument cast in the generated bindings; the '+' nested-type separator is CLR
/// metadata syntax, not C#.
/// </summary>
public sealed class IdentifyReviewRow
{
    public AdoptionProposal? Adoption { get; init; }   // set for "new to your list" rows
    public string ModKey { get; init; } = "";          // set for "now identified" rows
    public SourceSearchHit? Hit { get; init; }         // set for "now identified" rows
    public string Headline { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Approve { get; set; }

    // An unmatched row has nothing to approve — show the line, drop the checkbox.
    public Visibility CheckboxVisibility => Adoption is not null || Hit is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
}
```

- [ ] **Step 3: Clean and build**

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```
Expected: 0 Errors.

- [ ] **Step 4: Check the design-law tests still pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DesignLawTests"`
Expected: PASS. If `Copy_law_no_dead_xaml_strings` flags `NewHeader`/`IdentifiedHeader`, that is a genuine hit — those carry literal `Text` and are only re-assigned for `Visibility`, not `Text`, so it should not fire. If it does, verify no code path assigns `.Text` on them.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/IdentifyReviewDialog.xaml src/ModManager.App/IdentifyReviewDialog.xaml.cs
git commit -m "feat(dialog): one two-section review for a whole identify run"
```

---

### Task 4: The unified run

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/ModManager.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `BuildDiscoveryProposalsAsync` + `ApplyDiscoveriesAsync` (Task 2), `LooseIdentify.ExcludeKeys` (Task 1), `IdentifyReviewDialog` (Task 3).
- Produces:
  - `MainViewModel.ReviewIdentifyRun` — `Func<IReadOnlyList<AdoptionProposal>, IReadOnlyList<LooseIdentifyProposal>, Task<(IReadOnlyList<AdoptionProposal> Adoptions, IReadOnlyList<(string ModKey, SourceSearchHit Hit)> Identifications)>>?`
  - `MainViewModel.IdentifyMyModsAsync(string? downloadsFolder)`

- [ ] **Step 1: Add the review delegate**

Next to the existing `ReviewDiscoveries` property (~line 113):

```csharp
    /// <summary>Set by the view to show the unified review. Returns what the user approved in each
    /// section. Null (unwired view) means the run proposes and writes nothing.</summary>
    public Func<IReadOnlyList<AdoptionProposal>, IReadOnlyList<LooseIdentifyProposal>,
        Task<(IReadOnlyList<AdoptionProposal> Adoptions, IReadOnlyList<(string ModKey, SourceSearchHit Hit)> Identifications)>>? ReviewIdentifyRun { get; set; }
```

- [ ] **Step 2: Add the orchestration**

```csharp
    /// <summary>
    /// The whole identify ladder behind one action. Passes run best-evidence-first; only identity
    /// claims reach the review dialog.
    ///
    /// <para>APPLY ORDER IS LOAD-BEARING. Adoptions land first because an archive resolved by md5 is
    /// an exact match, and its real write keys are only known after approval
    /// (<c>Scanner.ArchiveModKeysFor</c>) — so a name-search proposal for the same row clears every
    /// propose-time filter. Applying md5 first and filtering the name-search results through
    /// <see cref="LooseIdentify.ExcludeKeys"/> is what stops a guess from overwriting a hash.</para>
    /// </summary>
    public async Task IdentifyMyModsAsync(string? downloadsFolder)
    {
        if (_ctx is null) return;
        var ctx = _ctx!;

        IsBusy = true;
        using var cts = new CancellationTokenSource();
        _longOpCts = cts;
        IsCancellable = true;
        try
        {
            // Pass 1 + 2: sweep the game folder and md5 what it found. Already tiered internally.
            StatusText = "Looking through this game's folder…";
            var adoptions = await BuildDiscoveryProposalsAsync(ctx, cts.Token);

            // Pass 2b: the downloads folder the user pointed us at, if any. Exact matches.
            if (!string.IsNullOrWhiteSpace(downloadsFolder) && !cts.IsCancellationRequested)
            {
                StatusText = "Matching your downloads folder…";
                adoptions = await AddDownloadsFolderMatchesAsync(adoptions, downloadsFolder!, ctx, cts.Token);
            }

            // Pass 3: fill blanks on rows we already identified. NOT reviewable — we already know
            // which mod these are; this only retrieves detail about it.
            if (!cts.IsCancellationRequested) await FillMissingDetailsAsync(ctx, cts.Token);

            // Pass 4: name-search whatever is still unnamed.
            IReadOnlyList<LooseIdentifyProposal> identifications = Array.Empty<LooseIdentifyProposal>();
            if (!cts.IsCancellationRequested)
                identifications = await ProposeLooseIdentifyAsync() ?? Array.Empty<LooseIdentifyProposal>();

            if (adoptions.Count == 0 && identifications.Count == 0)
            {
                StatusText = cts.IsCancellationRequested
                    ? "Stopped. Nothing was changed."
                    : "Everything in this game's folder is already in your list and identified.";
                return;
            }

            if (ReviewIdentifyRun is null) return; // unwired view -> nothing written
            var (approvedAdoptions, approvedIdentifications) = await ReviewIdentifyRun(adoptions, identifications);

            // Strongest first — see the apply-order note above.
            var written = await ApplyDiscoveriesAsync(approvedAdoptions, adoptions.Count, ctx);
            var safeIdentifications = LooseIdentify.ExcludeKeys(approvedIdentifications, written);
            await ApplyLooseIdentifyAsync(safeIdentifications, identifications.Count);
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsCancellable = false; _longOpCts = null; IsBusy = false; }
    }
```

- [ ] **Step 3: Extract the two helper passes**

`AddDownloadsFolderMatchesAsync` reuses the md5 logic currently in `MainWindow.xaml.cs`'s `OnNexusBackfill` (enumerate `.zip`/`.7z`/`.rar` recursively, md5-identify each, build `AdoptionProposal.FromMd5`). Move that enumeration + identify into the VM verbatim and have `OnNexusBackfill` call it too, so there is one implementation.

`FillMissingDetailsAsync` is the body of the existing `EnrichMetadataAsync` minus its own busy-state and status handling (the run owns those). Have `EnrichMetadataAsync` become a thin wrapper that sets busy state and calls it, so the Advanced entry keeps working.

- [ ] **Step 4: Wire the dialog in the view**

In `MainWindow.xaml.cs`, next to the existing `ViewModel.ReviewDiscoveries = ...` assignment (~line 89):

```csharp
        ViewModel.ReviewIdentifyRun = async (adoptions, identifications) =>
        {
            var dialog = new IdentifyReviewDialog(adoptions, identifications) { XamlRoot = Content.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return (Array.Empty<AdoptionProposal>(), Array.Empty<(string, SourceSearchHit)>());
            return (dialog.ApprovedAdoptions(), dialog.ApprovedIdentifications());
        };
```

- [ ] **Step 5: Add the entry handler with the up-front folder prompt**

```csharp
    // One prompt before anything runs — the downloads folder is the only pass that needs input,
    // and asking mid-run would interrupt a sweep the user is watching.
    private async void OnIdentifyMyMods(object sender, RoutedEventArgs e)
    {
        var ask = new ContentDialog
        {
            Title = "Also check a downloads folder?",
            Content = "If you have a folder of downloaded mod archives, we can match them exactly by file hash. "
                      + "Otherwise we'll match by name, which is a good guess but still a guess.",
            PrimaryButtonText = "Choose folder",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(ask);

        string? folder = null;
        if (await ask.ShowAsync() == ContentDialogResult.Primary)
        {
            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            folder = (await picker.PickSingleFolderAsync())?.Path;
        }

        await ViewModel.IdentifyMyModsAsync(folder);
    }
```

- [ ] **Step 6: Clean, build, test**

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```
Expected: 0 Errors; 0 failed tests.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat(identify): run the whole ladder behind one action and one review"
```

---

### Task 5: Restructure the menu

**Files:**
- Modify: `src/ModManager.App/MainWindow.xaml` (lines 84-127)

**Interfaces:**
- Consumes: `OnIdentifyMyMods` (Task 4).
- Produces: the final menu shape.

- [ ] **Step 1: Replace the six items**

Replace everything from the `Backfill metadata from Nexus archives…` item through the `Fetch metadata for all mods` item with:

```xml
                            <MenuFlyoutItem Text="Identify my mods…" Click="OnIdentifyMyMods"
                                            ToolTipService.ToolTip="Find mods that aren't in your list yet, and name the ones we haven't identified. Nothing is moved.">
                                <MenuFlyoutItem.Icon>
                                    <FontIcon Glyph="&#xE721;" />
                                </MenuFlyoutItem.Icon>
                            </MenuFlyoutItem>
                            <MenuFlyoutItem Text="Re-detect launchers &amp; frameworks" Click="OnRedetect"
                                            ToolTipService.ToolTip="Re-check which mod loaders and launch options this game has">
                                <MenuFlyoutItem.Icon>
                                    <FontIcon Glyph="&#xE72C;" />
                                </MenuFlyoutItem.Icon>
                            </MenuFlyoutItem>
                            <MenuFlyoutSeparator />
                            <MenuFlyoutSubItem Text="Advanced">
                                <MenuFlyoutItem Text="Match against my downloads folder…" Click="OnNexusBackfill"
                                                ToolTipService.ToolTip="Match installed mods against a folder of downloaded archives, by file hash" />
                                <MenuFlyoutItem Text="Refresh details from Nexus" Click="OnEnrichMetadata"
                                                ToolTipService.ToolTip="Re-fetch descriptions and cover art for mods we've already identified" />
                                <MenuFlyoutItem Text="Check CurseForge" Command="{x:Bind ViewModel.FetchMetadataCommand}"
                                                ToolTipService.ToolTip="Name-search CurseForge for every mod (best-effort)" />
                            </MenuFlyoutSubItem>
```

Note: no `Visibility` bindings on the Advanced items. Per the spec's *guard, don't hide*, their existing precondition guards do the talking.

- [ ] **Step 2: Delete the now-unreferenced handler**

`OnFindExistingMods` and `OnLooseIdentify` are no longer bound from the menu. Keep `OnFindExistingMods` — the first-add auto-sweep still calls `DiscoverExistingModsAsync`. Delete `OnLooseIdentify` and the `LooseIdentifyVisibility`/`LooseIdentifyAvailable` properties plus their four `OnPropertyChanged` sites ONLY if nothing else references them; verify with:

```bash
grep -rn "OnLooseIdentify\|LooseIdentifyVisibility\|LooseIdentifyAvailable" src/
```
If the grep returns only the declarations, remove them. If anything else binds them, leave them.

- [ ] **Step 3: Clean, build, test**

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```
Expected: 0 Errors; 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(ui): six identify actions become one plus an Advanced submenu"
```

---

### Task 6: Guard the downloads-folder entry before it opens a picker

**Files:**
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (`OnNexusBackfill`)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing new.

**Why:** `OnNexusBackfill` opens the folder picker as its first action. A signed-out user can browse to a downloads folder and only then discover nothing can be matched. Per *guard, don't hide*, the item stays visible and explains itself — but it must explain BEFORE costing the user a file-picker round trip.

- [ ] **Step 1: Add the precondition**

As the first lines of `OnNexusBackfill`, before the `FolderPicker` is constructed:

```csharp
        // Explain before costing the user a picker round-trip. md5 matching is a Nexus call; with
        // no connection there is nothing a folder of archives could be matched against.
        if (!ViewModel.NexusActionsAvailable)
        {
            ViewModel.StatusText = "Connect Nexus first (toolbar -> Nexus).";
            return;
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 Errors. If `NexusActionsAvailable` or `StatusText` is not public on the VM, make it public (both are already bound from XAML, so they are public).

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml.cs
git commit -m "fix(ui): explain the Nexus precondition before opening the folder picker"
```

---

### Task 7: Smoke checklist

**Files:**
- Modify: `docs/smoke-tests/pending.md`

**Why:** Every behavior in this plan lives in the App layer, which the test project cannot reach. These entries are written to catch silent failure — the class of bug this surface has already produced twice (a menu item that built fine but rendered collapsed; a feature that ran clean and wrote metadata nowhere).

- [ ] **Step 1: Append the section**

```markdown
## feat/identify-consolidation — one action instead of six

**Shipped:** The More menu's six identify/metadata actions collapse to `Identify my mods…` plus an
Advanced submenu (`Match against my downloads folder…`, `Refresh details from Nexus`,
`Check CurseForge`). `Re-scan mods & launchers` is now `Re-detect launchers & frameworks`. One run
does sweep -> md5 -> fill-blanks -> name search, behind one review dialog with two sections.

**Smoke needed:**

1. **The menu item is actually there.** Open More on a game with Nexus connected AND on one with
   Nexus signed out. EXPECT: `Identify my mods…` visible in BOTH; every Advanced item visible in
   both. This surface has already shipped a computed-visibility item that built fine and rendered
   permanently collapsed — absence is the regression to look for.
2. **Guards explain rather than hide.** Signed out, click each Advanced item. EXPECT: a status line
   naming the fix ("Connect Nexus first (toolbar -> Nexus).") — and for
   `Match against my downloads folder…`, EXPECT that message with NO file picker appearing first.
3. **Both sections render, and the count is the sum.** On Cyberpunk (194 mods, ~98 identified), run
   `Identify my mods…` and choose a downloads folder. EXPECT both `NEW TO YOUR LIST` and
   `NOW IDENTIFIED` populated; the primary button reads `Apply N changes` where N equals the total
   checked across BOTH sections; unchecking in either section decrements it; it disables at zero.
4. **An empty section disappears.** Run it again immediately. EXPECT the second run has little or
   nothing to propose, and any section with zero rows hides its header too (no orphan
   `NOW IDENTIFIED` heading over empty space).
5. **Fill-blanks does NOT appear in the dialog.** EXPECT no row anywhere reading like "added a
   description". Descriptions and cover art should simply BE there on the rows afterwards, with the
   count reported only in the status line.
6. **Stop works mid-run and keeps what finished.** Start a run on Cyberpunk, hit Stop during the
   `Searching Nexus — N of M…` phase. EXPECT the run ends, the review dialog still opens with what
   completed (or a status line saying nothing finished), and the mod list is unchanged until Apply.
7. **Nothing moved.** After applying, confirm the game folder listing and file timestamps are
   unchanged. Metadata-only is the law; the first file move is still the user's first toggle.
8. **The Advanced passes still work individually.** Run each one on its own and confirm it does its
   single job and reports it.
```

- [ ] **Step 2: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): identify-consolidation checklist"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| One primary action running the ladder | 4, 5 |
| Advanced submenu keeps individual passes | 5 |
| Downloads folder offered once up front | 4 (Step 5) |
| One review dialog | 3, 4 |
| Discovery folded in | 2, 4 |
| Two sections, identity claims only | 3 |
| Fill-blanks excluded from review | 3 (documented), 4 (Pass 3 outside review), 7 (smoke 5) |
| `Re-detect launchers & frameworks` rename | 5 |
| Guard, don't hide | 5 (no Visibility bindings), 6, 7 (smoke 2) |
| `OnNexusBackfill` needs a guard | 6 |
| Metadata-only, review-gated, no downgrade | 1 (ExcludeKeys), 4 (apply order), 7 (smoke 7) |

No gaps.

**Placeholder scan:** none — every code step carries real code. Task 2 and Task 4 Step 3 move existing bodies rather than quoting them in full; both name the exact source method and the exact resulting signature, which is the information the implementer lacks.

**Type consistency:** `IdentifyReviewDialog(IReadOnlyList<AdoptionProposal>, IReadOnlyList<LooseIdentifyProposal>)` in Task 3 matches the `ReviewIdentifyRun` delegate shape in Task 4. `ApplyDiscoveriesAsync` returns `IReadOnlyList<string>` (Task 2) and is consumed as the `alreadyWritten` argument to `LooseIdentify.ExcludeKeys` (Task 1) in Task 4. `ApprovedIdentifications()` returns `IReadOnlyList<(string ModKey, SourceSearchHit Hit)>`, matching `ApplyLooseIdentifyAsync`'s existing parameter type.

**One risk called out for the implementer:** Task 2 is a pure refactor of a long, heavily-commented method. Preserve every existing comment verbatim — they document non-obvious invariants (the base-pak guard, the archive-key derivation, the md5 budget cap) that are not restated in this plan.
