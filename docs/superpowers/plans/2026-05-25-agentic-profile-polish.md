# Agentic Profile Polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — it hangs building the WinUI App).

**Goal:** Close two gaps in the shipped "Add with AI" wizard: (1) the prompt doesn't ask the agent for `nexusGameDomain` even though the rest of the pipeline already carries it; (2) the Steam picker is single-select, so you can only onboard one game per agent round-trip.

**Architecture:** Both changes stay in their natural seams. Item 1 is a one-line addition to `GameProfilePrompt.Build` plus a pinning test — the data path (`Draft → Input → Entry`) already forwards `NexusGameDomain` end-to-end. Item 2 adds a sibling `GameProfilePrompt.BuildMany(IReadOnlyList<string>)` plus a `GameProfileImport.LoadMany(string)` that returns `IReadOnlyList<ProfileImportResult>` from a JSON array; the dialog grows a parallel "Batch from Steam" expander whose Apply iterates `LoadMany` results into the existing single-game registration path, one row at a time. **No mutation of the existing single-game flow** — batch is sugar on top.

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit.

---

## Task 1: Prompt asks for `nexusGameDomain`

**Files:**
- Modify: `src/ModManager.Core/GameProfilePrompt.cs`
- Test: `tests/ModManager.Tests/GameProfilePromptTests.cs`

- [ ] **Step 1: Write the failing test**

Add this `[Fact]` to `GameProfilePromptTests`:

```csharp
[Fact]
public void Build_asks_for_nexusGameDomain()
{
    var p = GameProfilePrompt.Build("Cyberpunk 2077");
    Assert.Contains("nexusGameDomain", p);
    // The contract: a Nexus URL domain (the slug), not a numeric id.
    Assert.Contains("domain", p, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Build_asks_for_nexusGameDomain"`
Expected: FAIL — `Build()` output does not contain `"nexusGameDomain"`.

- [ ] **Step 3: Add the field to the prompt**

In `src/ModManager.Core/GameProfilePrompt.cs`, after the `curseforgeGameId` line and before the closing `\n\nRules:` line, insert:

```csharp
"  curseforgeGameId (number, optional),\n" +
"  nexusGameDomain (string, the Nexus URL slug like 'cyberpunk2077' - not a numeric id; optional).\n\n" +
```

(Replace the existing `curseforgeGameId` terminator `.` with `,` and add the new line.)

- [ ] **Step 4: Run test to verify it passes (and the contract test still passes)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfilePromptTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/GameProfilePrompt.cs tests/ModManager.Tests/GameProfilePromptTests.cs
git commit -m "feat: agentic-profile prompt asks for nexusGameDomain"
```

---

## Task 2: `GameProfileImport.LoadMany` for JSON arrays

**Files:**
- Modify: `src/ModManager.Core/GameProfileImport.cs`
- Test: `tests/ModManager.Tests/GameProfileImportTests.cs`

The single-game `Load` accepts one JSON object. Batch mode needs a sibling that accepts a JSON array and returns one `ProfileImportResult` per element — so a single bad row doesn't block the rest. Mirrors how the existing single-row validation already separates `Draft` from `Errors`.

- [ ] **Step 1: Write the failing tests**

Add to `GameProfileImportTests`:

```csharp
[Fact]
public void LoadMany_parses_each_element_with_its_own_result()
{
    var json = """
    [
      { "name":"A","engine":"bethesda","saveRoot":"AppData","saveSubPath":"A" },
      { "name":"B","engine":"frostbite","saveRoot":"AppData","saveSubPath":"B" }
    ]
    """;
    var results = GameProfileImport.LoadMany(json);
    Assert.Equal(2, results.Count);
    Assert.Empty(results[0].Errors);
    Assert.Equal("A", results[0].Draft!.Name);
    Assert.NotEmpty(results[1].Errors); // frostbite is not an engine preset
    Assert.Null(results[1].Draft);
}

[Fact]
public void LoadMany_rejects_non_array_root_with_one_error()
{
    var results = GameProfileImport.LoadMany("""{ "name":"X" }""");
    Assert.Single(results);
    Assert.Null(results[0].Draft);
    Assert.Contains(results[0].Errors, e => e.Contains("array", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void LoadMany_rejects_bad_json_with_one_error()
{
    var results = GameProfileImport.LoadMany("[ not json");
    Assert.Single(results);
    Assert.Null(results[0].Draft);
    Assert.NotEmpty(results[0].Errors);
}

[Fact]
public void LoadMany_returns_empty_for_an_empty_array()
{
    var results = GameProfileImport.LoadMany("[]");
    Assert.Empty(results);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LoadMany"`
Expected: FAIL — `GameProfileImport.LoadMany` does not exist.

- [ ] **Step 3: Implement LoadMany**

Add to `src/ModManager.Core/GameProfileImport.cs` inside the `GameProfileImport` class:

```csharp
/// <summary>Parses + validates a JSON array of profiles. One result per element (good or bad);
/// a single bad row doesn't poison the rest. Returns a single error result if the root is not
/// a valid JSON array.</summary>
public static IReadOnlyList<ProfileImportResult> LoadMany(string json)
{
    JsonElement root;
    try
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new[] { new ProfileImportResult(null, new[] { "Expected a JSON array of profiles." }) };
        // Clone each element to its own owned JSON string before the doc is disposed.
        var elements = doc.RootElement.EnumerateArray().Select(e => e.GetRawText()).ToList();
        return elements.Select(Load).ToList();
    }
    catch (JsonException e)
    {
        return new[] { new ProfileImportResult(null, new[] { "Not valid JSON: " + e.Message }) };
    }
}
```

Add `using System.Text.Json;` is already imported.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfileImportTests"`
Expected: PASS (all existing + 4 new).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/GameProfileImport.cs tests/ModManager.Tests/GameProfileImportTests.cs
git commit -m "feat: GameProfileImport.LoadMany for JSON-array batch profiles"
```

---

## Task 3: `GameProfilePrompt.BuildMany` for a batched ask

**Files:**
- Modify: `src/ModManager.Core/GameProfilePrompt.cs`
- Test: `tests/ModManager.Tests/GameProfilePromptTests.cs`

One agent round-trip should onboard N games. The batched prompt asks for a JSON **array**, in the same order as the names, with the same per-game contract.

- [ ] **Step 1: Write the failing tests**

Add to `GameProfilePromptTests`:

```csharp
[Fact]
public void BuildMany_pins_the_contract_for_a_list_of_games()
{
    var p = GameProfilePrompt.BuildMany(new[] { "Cyberpunk 2077", "Phasmophobia" });
    Assert.Contains("Cyberpunk 2077", p);
    Assert.Contains("Phasmophobia", p);
    Assert.Contains("JSON array", p);
    Assert.Contains("same order", p); // order pinned so the user can map rows back
    Assert.Contains("nexusGameDomain", p); // inherits the single-game contract
    Assert.Contains("saveRoot", p);
    Assert.DoesNotContain("```", p);
}

[Fact]
public void BuildMany_rejects_empty_list()
{
    Assert.Throws<ArgumentException>(() => GameProfilePrompt.BuildMany(Array.Empty<string>()));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BuildMany"`
Expected: FAIL — `BuildMany` does not exist.

- [ ] **Step 3: Implement BuildMany**

Add to `src/ModManager.Core/GameProfilePrompt.cs`:

```csharp
public static string BuildMany(IReadOnlyList<string> gameNames)
{
    if (gameNames is null || gameNames.Count == 0)
        throw new ArgumentException("At least one game name is required.", nameof(gameNames));

    var engines = string.Join(", ", EnginePresets.Presets.Keys);
    var saveRoots = string.Join(", ", GameProfileImport.SaveRoots);
    var games = string.Join("\n", gameNames.Select((n, i) => $"  {i + 1}. {n.Trim()}"));
    return
        "You are filling registration profiles for multiple PC games at once.\n\n" +
        "Games (return one JSON object per game, in the same order):\n" +
        games + "\n\n" +
        "Return ONLY a single JSON array - no prose, no markdown fences. Each element is a profile\n" +
        "object using STRUCTURED, RELATIVE values only - NEVER an absolute machine path. The app\n" +
        "resolves real paths.\n\n" +
        "Per-element fields:\n" +
        "  name (string),\n" +
        $"  engine (one of: {engines}),\n" +
        "  windowTitle (string, optional),\n" +
        "  steamAppId (string of digits, optional),\n" +
        "  modPath (string, relative to the install folder; optional - omit to use the engine default),\n" +
        "  fileExtensions (array of strings, optional), groupingRule (string, optional),\n" +
        $"  saveRoot (one of: {saveRoots}),\n" +
        "  saveSubPath (string, relative path under saveRoot),\n" +
        "  requiredLauncher (string, relative path to the launcher exe that must be used when modded; optional),\n" +
        "  curseforgeGameId (number, optional),\n" +
        "  nexusGameDomain (string, the Nexus URL slug like 'cyberpunk2077' - not a numeric id; optional).\n\n" +
        "Rules: valid JSON array only; same order as the list above; engine and saveRoot must be from\n" +
        "the lists; every path relative.";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameProfilePromptTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/GameProfilePrompt.cs tests/ModManager.Tests/GameProfilePromptTests.cs
git commit -m "feat: GameProfilePrompt.BuildMany for batched agent ask"
```

---

## Task 4: Wire batch UX into `AddGameDialog`

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml`
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (only if the registration loop lives there — see Step 3)

This is UI plumbing — no unit tests against WinUI dialogs in this repo (consistent with the existing dialog). The core logic (BuildMany / LoadMany) is already tested; the dialog just composes them.

### Step 4.1: XAML — new "Batch from Steam" expander

- [ ] **Step 1: Add the batch expander to `AddGameDialog.xaml`**

Insert this `<Expander>` immediately AFTER the existing `<Expander x:Name="AiExpander" ...>` block and BEFORE the `<ComboBox x:Name="PopularGamesBox" ...>`:

```xml
<Expander x:Name="BatchExpander" HorizontalAlignment="Stretch" Header="Batch from Steam (multiple games)">
    <StackPanel Spacing="8" Width="400">
        <TextBlock TextWrapping="Wrap" Opacity="0.75" FontSize="12"
                   Text="Pick the Steam games you want to onboard, copy the batch prompt into your agent, paste the JSON array back, then Apply all. Profiles you approve are registered; the rest are skipped." />
        <ListView x:Name="BatchSteamList" SelectionMode="Multiple" Height="160"
                  BorderBrush="{ThemeResource ControlElevationBorderBrush}" BorderThickness="1">
            <ListView.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Name}" />
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
        <Button Content="Copy batch prompt" Click="OnCopyBatchPrompt" />
        <TextBox x:Name="BatchJsonBox" Header="Agent JSON array" AcceptsReturn="True" Height="120" TextWrapping="Wrap"
                 PlaceholderText="Paste the JSON array the agent returned" />
        <Button Content="Apply all" Click="OnApplyBatch" />
        <TextBlock x:Name="BatchStatus" Visibility="Collapsed" TextWrapping="Wrap" FontSize="12" />
        <ItemsControl x:Name="BatchResultsList" Margin="0,4,0,0">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border BorderBrush="{ThemeResource ControlElevationBorderBrush}" BorderThickness="1"
                            CornerRadius="4" Padding="8" Margin="0,0,0,4">
                        <StackPanel Spacing="2">
                            <TextBlock Text="{Binding Headline}" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Detail}" Opacity="0.75" FontSize="12" TextWrapping="Wrap" />
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Expander>
```

### Step 4.2: Code-behind — the batch handlers

- [ ] **Step 2: Add batch fields + setup in the constructor**

In `AddGameDialog.xaml.cs`, after the existing `private GameProfileDraft? _appliedDraft;` field, add:

```csharp
// The Steam games offered for batch — the dialog owns the list so the batch list mirrors
// the single-game dropdown without re-running the SteamService scan.
private readonly IReadOnlyList<SteamGame> _steamGames;

private sealed record BatchRowVM(string Headline, string Detail);
```

In the constructor, after `SteamGamesBox.ItemsSource = steamGames;`, add:

```csharp
_steamGames = steamGames;
BatchSteamList.ItemsSource = steamGames;
if (steamGames.Count == 0) BatchExpander.IsEnabled = false;
```

- [ ] **Step 3: Implement OnCopyBatchPrompt**

Add to `AddGameDialog.xaml.cs`:

```csharp
// Build a batched prompt from the picked Steam games. Empty selection -> a status nudge, no copy.
private void OnCopyBatchPrompt(object sender, RoutedEventArgs e)
{
    var picked = BatchSteamList.SelectedItems.Cast<SteamGame>().ToList();
    if (picked.Count == 0)
    {
        ShowBatchStatus("Pick at least one Steam game first.", "ThemeDanger");
        return;
    }
    var pkg = new DataPackage();
    pkg.SetText(GameProfilePrompt.BuildMany(picked.Select(g => g.Name).ToList()));
    Clipboard.SetContent(pkg);
    ShowBatchStatus($"Batch prompt copied for {picked.Count} games — run it, paste the array back, then Apply all.", "ThemeAccent");
}
```

- [ ] **Step 4: Implement OnApplyBatch — preview only, no register yet**

The Primary button still controls registration to keep the existing single-game guarantee. Batch mode collects approved drafts; the dialog's primary action commits them.

Add a backing field at class scope:

```csharp
// Per-row results of the batch apply. Populated by OnApplyBatch; consumed by GetBatchApprovedInputs
// after the user closes with Primary. Empty when the user didn't use batch mode.
private readonly List<(GameInput Input, string? ResolvedSaveDir)> _batchApproved = new();
```

Add the handler:

```csharp
// Validate the pasted JSON array, resolve each, render a per-row preview, and collect the
// approved inputs. The Primary button will register them.
private async void OnApplyBatch(object sender, RoutedEventArgs e)
{
    _batchApproved.Clear();
    var results = GameProfileImport.LoadMany(BatchJsonBox.Text ?? "");
    if (results.Count == 0)
    {
        ShowBatchStatus("Empty array — nothing to apply.", "ThemeDanger");
        BatchResultsList.ItemsSource = Array.Empty<BatchRowVM>();
        return;
    }

    var picked = BatchSteamList.SelectedItems.Cast<SteamGame>().ToList();
    var resolver = App.AppHost.Services.GetRequiredService<GameProfileResolver>();
    var rows = new List<BatchRowVM>();
    int ok = 0;

    for (int i = 0; i < results.Count; i++)
    {
        var r = results[i];
        var name = r.Draft?.Name ?? (i < picked.Count ? picked[i].Name : $"#{i + 1}");
        if (r.Draft is null)
        {
            rows.Add(new BatchRowVM($"SKIPPED  {name}", string.Join("  ", r.Errors)));
            continue;
        }
        // Same resolve flow the single-game Apply uses. browsedGameRoot null -> Steam detection.
        var resolved = await resolver.ResolveAsync(r.Draft, browsedGameRoot: null);
        var summary = string.Join("   ", resolved.Checks.Select(c =>
            $"{(c.Status == ResolveStatus.Pass ? "OK" : "!")} {c.Label}"));

        // Assemble a GameInput from the draft (matches BuildInput's mapping when an _appliedDraft is in play).
        var input = new GameInput
        {
            Name = r.Draft.Name!,
            Engine = r.Draft.Engine!,
            GameRoot = resolved.GameRoot ?? "",
            ModPath = r.Draft.ModPath,
            SteamAppId = r.Draft.SteamAppId,
            SaveRoot = r.Draft.SaveRoot,
            SaveSubPath = r.Draft.SaveSubPath,
            RequiredLauncher = r.Draft.RequiredLauncher,
            WindowTitle = r.Draft.WindowTitle,
            FileExtensions = r.Draft.FileExtensions,
            GroupingRule = r.Draft.GroupingRule,
            CurseforgeGameId = r.Draft.CurseforgeGameId,
            SaveModPath = r.Draft.SaveModPath,
            SaveModForbidden = r.Draft.SaveModForbidden,
            NexusGameDomain = r.Draft.NexusGameDomain,
        };

        if (string.IsNullOrEmpty(input.GameRoot))
        {
            rows.Add(new BatchRowVM($"NEEDS FOLDER  {name}", $"Could not resolve install folder. {summary}"));
            continue;
        }

        _batchApproved.Add((input, resolved.SaveDir));
        rows.Add(new BatchRowVM($"READY  {name}", summary));
        ok++;
    }

    BatchResultsList.ItemsSource = rows;
    ShowBatchStatus($"{ok} of {results.Count} ready to register. Click Add to commit; cancel to abandon.",
        ok == 0 ? "ThemeDanger" : "ThemeAccent");
}

private void ShowBatchStatus(string message, string brushKey)
{
    BatchStatus.Text = message;
    if (Application.Current.Resources.TryGetValue(brushKey, out var v) && v is Brush b) BatchStatus.Foreground = b;
    BatchStatus.Visibility = Visibility.Visible;
}

/// <summary>The approved batch inputs, or an empty list if batch mode wasn't used.</summary>
public IReadOnlyList<(GameInput Input, string? ResolvedSaveDir)> BatchApproved => _batchApproved;
```

### Step 4.3: Primary-button + register loop

The current `OnPrimary` validates Name/Folder/Engine on the single-game form. Batch mode needs a different gate: if `_batchApproved` is non-empty, the wizard's single-form fields aren't required.

- [ ] **Step 5: Relax `OnPrimary` for batch mode**

Replace the existing `OnPrimary` with:

```csharp
private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
{
    // Batch mode: at least one approved row passes; the single-form fields are irrelevant.
    if (_batchApproved.Count > 0) return;

    if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(FolderBox.Text)
        || EngineBox.SelectedItem is not EngineOption)
    {
        ErrorText.Visibility = Visibility.Visible;
        args.Cancel = true; // keep the dialog open
    }
}
```

- [ ] **Step 6: Register approved batch rows in `MainWindow.OnAddGame`**

Update `src/ModManager.App/MainWindow.xaml.cs` `OnAddGame` to handle batch results:

```csharp
private async void OnAddGame(object sender, RoutedEventArgs e)
{
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    var steamGames = App.AppHost.Services.GetRequiredService<Services.SteamService>().InstalledGames();
    var dialog = new AddGameDialog(hwnd, steamGames) { XamlRoot = Content.XamlRoot };
    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

    // Batch mode wins when there's at least one approved row — register them in order and skip
    // the single-form path. Otherwise the existing single-game flow applies.
    if (dialog.BatchApproved.Count > 0)
    {
        foreach (var (input, resolvedSaveDir) in dialog.BatchApproved)
            await ViewModel.AddGameAsync(input, resolvedSaveDir);
        return;
    }

    await ViewModel.AddGameAsync(dialog.BuildInput(), dialog.ResolvedSaveDir);
}
```

- [ ] **Step 7: Build the App project**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: ALL PASS (no Core regressions).

- [ ] **Step 9: Commit**

```bash
git add src/ModManager.App/AddGameDialog.xaml src/ModManager.App/AddGameDialog.xaml.cs src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat: batch add games from Steam (multi-select + JSON-array agent round-trip)"
```

---

## Self-Review Notes

- **Spec coverage:** Item 1 covered by Task 1 (prompt field + test). Item 2 covered by Tasks 2 (LoadMany), 3 (BuildMany), and 4 (dialog wire-up + register loop).
- **End-to-end NexusGameDomain:** Already wired pre-existing — verified in `GameProfileDraft` (record field), `GameInput` (referenced in `BuildInput`), `EnginePresets.BuildGameEntry` (line 82 `entry.NexusGameDomain = input.NexusGameDomain`). The plan adds only the prompt-side ask; the consumer is already ready.
- **Batch isolation:** `_batchApproved` stays empty unless `OnApplyBatch` populates it. Single-game flow is untouched (`BuildInput` and `ResolvedSaveDir` remain the sole path for non-batch use).
- **Reversibility:** Batch register loops `AddGameAsync` one row at a time. If one fails, the prior succeed and the user sees them in the registry — no "all or nothing" trap.
- **Type consistency:** `GameProfileImport.LoadMany` returns `IReadOnlyList<ProfileImportResult>`, matching the existing `ProfileImportResult` shape used by single-game `Load`.
