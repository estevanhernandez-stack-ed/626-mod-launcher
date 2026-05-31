using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModManager.App.Services;
using ModManager.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class AddGameDialog : ContentDialog
{
    private readonly IntPtr _hwnd;

    // Resolved save folder from an "Add with AI" apply, stashed for the register path to persist.
    private string? _resolvedSaveDir;

    // The applied agent profile, stashed so BuildInput can carry fields that have no visible wizard
    // control (windowTitle / fileExtensions / groupingRule / curseforgeGameId). Null on manual add.
    private GameProfileDraft? _appliedDraft;

    // Approved batch rows, populated by OnApplyBatch and consumed by MainWindow's register loop on
    // Primary. Empty in the single-game flow.
    private readonly List<(GameInput Input, string? ResolvedSaveDir)> _batchApproved = new();

    private sealed record BatchRowVM(string Headline, string Detail);

    // One checkable Steam quick-add row: the ready-to-register input + the engine-tagged display label.
    private sealed record SteamAddRow(GameInput Input, string Display);

    /// <summary>The save folder resolved during an "Add with AI" apply, or null if none was resolved.</summary>
    public string? ResolvedSaveDir => _resolvedSaveDir;

    /// <summary>The approved batch inputs, or an empty list if batch mode wasn't used.</summary>
    public IReadOnlyList<(GameInput Input, string? ResolvedSaveDir)> BatchApproved => _batchApproved;

    public sealed record EngineOption(string Key, string Label);

    public AddGameDialog(IntPtr hwnd, IReadOnlyList<SteamGame> steamGames)
    {
        InitializeComponent();
        _hwnd = hwnd;
        // No default selection — a wrong default reads as "auto-detected" when it isn't.
        EngineBox.ItemsSource = EnginePresets.Presets.Select(kv => new EngineOption(kv.Key, kv.Value.Label)).ToList();

        PopularGamesBox.ItemsSource = PopularGames.All;

        // Save-root enum values for the picker. Set in code, not XAML (literal-bool/SelectedItem-in-markup
        // parse gotcha on this WinUI build).
        SaveRootBox.ItemsSource = GameProfileImport.SaveRoots;

        // Quick-add: plan each installed Steam game for one-click auto-add. Detectable-engine games
        // become checkable rows; undetectable ones are listed for manual setup (we never guess an engine).
        var addable = new List<SteamAddRow>();
        var manual = new List<string>();
        foreach (var g in steamGames)
        {
            var plan = SteamGameImport.Plan(
                new SteamImportCandidate(g.AppId, g.Name, g.InstallDir),
                EngineScan.Detect(g.InstallDir));
            if (plan.Addable && plan.Input is not null)
            {
                var label = EnginePresets.Presets.TryGetValue(plan.Engine!, out var p) ? p.Label : plan.Engine!;
                addable.Add(new SteamAddRow(plan.Input, $"{g.Name}  ·  {label}"));
            }
            else
            {
                manual.Add(g.Name);
            }
        }
        SteamGamesList.ItemsSource = addable;
        if (addable.Count == 0) SteamEmptyNote.Visibility = Visibility.Visible;
        if (manual.Count > 0)
        {
            SteamManualNote.Text = $"Add manually (engine not detected): {string.Join(", ", manual)}";
            SteamManualNote.Visibility = Visibility.Visible;
        }

        // Batch picker mirrors the single-game Steam list. Disable the expander when there's nothing
        // installed - the batched ask is meaningless without targets.
        BatchSteamList.ItemsSource = steamGames;
        if (steamGames.Count == 0) BatchExpander.IsEnabled = false;
    }

    // Copy the agent prompt for the typed game name to the clipboard. Mirrors NewThemeDialog's flow.
    private void OnCopyProfilePrompt(object sender, RoutedEventArgs e)
    {
        var pkg = new DataPackage();
        pkg.SetText(GameProfilePrompt.Build(AiGameNameBox.Text));
        Clipboard.SetContent(pkg);
        ShowProfileStatus("Prompt copied — run it in your agent, then paste the JSON back here.", "ThemeAccent");
    }

    // Parse + validate the pasted JSON, resolve it to machine paths, and pre-fill the wizard fields.
    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        var result = GameProfileImport.Load(AiJsonBox.Text ?? "");
        if (result.Draft is null)
        {
            ShowProfileStatus(string.Join("  ", result.Errors), "ThemeDanger");
            return;
        }
        var d = result.Draft;
        _appliedDraft = d; // stash so BuildInput can carry fields with no visible control

        // Resolve + verify on disk (read-only). No browse attempted here — pass null so Steam detection runs.
        var resolver = App.AppHost.Services.GetRequiredService<GameProfileResolver>();
        var resolved = await resolver.ResolveAsync(d, browsedGameRoot: null);

        // Pre-fill the familiar wizard fields.
        NameBox.Text = d.Name ?? "";
        SelectEngine(d.Engine);                 // selects the matching EngineBox item (fires OnEngineChanged)
        if (!string.IsNullOrEmpty(d.ModPath)) ModPathBox.Text = d.ModPath;
        if (!string.IsNullOrEmpty(d.SteamAppId)) SteamBox.Text = d.SteamAppId;
        SaveRootBox.SelectedItem = d.SaveRoot;  // SaveRoots are strings, so the enum value round-trips
        SaveSubPathBox.Text = d.SaveSubPath ?? "";
        RequiredLauncherBox.Text = d.RequiredLauncher ?? "";
        if (!string.IsNullOrEmpty(resolved.GameRoot)) FolderBox.Text = resolved.GameRoot; // resolved install path

        // Show the pass/warn/missing checks inline.
        var summary = string.Join("   ", resolved.Checks.Select(c =>
            $"{(c.Status == ResolveStatus.Pass ? "OK" : "!")} {c.Label}"));
        ShowProfileStatus($"Profile applied. {summary}", "ThemeAccent");
        _resolvedSaveDir = resolved.SaveDir; // stash for register
    }

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

    // Validate the pasted JSON array, resolve each, render a per-row preview, and collect the
    // approved inputs. Primary commits them.
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

            // Assemble a GameInput from the draft - mirrors BuildInput's mapping when an _appliedDraft
            // is in play, so the batch path carries the same fields the single-game path does.
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

    // Select the EngineBox item whose key matches — same mechanism the popular-games quick-pick uses.
    private void SelectEngine(string? engineKey)
    {
        if (string.IsNullOrEmpty(engineKey)) return;
        if ((EngineBox.ItemsSource as IEnumerable<EngineOption>)?.FirstOrDefault(o => o.Key == engineKey) is { } match)
        {
            EngineBox.SelectedItem = match;
            EngineHint.Visibility = Visibility.Collapsed; // applied from a profile, not folder auto-detection
        }
    }

    private void ShowProfileStatus(string message, string brushKey)
    {
        ProfileStatus.Text = message;
        if (Application.Current.Resources.TryGetValue(brushKey, out var v) && v is Brush b) ProfileStatus.Foreground = b;
        ProfileStatus.Visibility = Visibility.Visible;
    }

    // Pick a curated game -> pre-fill name, engine, mod folder, and app id. Leaves the game
    // folder for the user to point at their install. Manual entry still works unchanged.
    private void OnPopularSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PopularGamesBox.SelectedItem is not PopularGame g) return;
        NameBox.Text = g.Name;
        // Select the matching engine option. This fires OnEngineChanged, which seeds ModPathBox
        // from the engine preset's default — we then override with the game-specific path below.
        if ((EngineBox.ItemsSource as IEnumerable<EngineOption>)?.FirstOrDefault(o => o.Key == g.Engine) is { } match)
        {
            EngineBox.SelectedItem = match;
            EngineHint.Visibility = Visibility.Collapsed; // this is a curated pick, not folder auto-detection
        }
        ModPathBox.Text = g.ModPath;
        SteamBox.Text = g.SteamAppId;
    }

    // Live count as the user checks Steam games. The dialog's Add button commits the selection.
    private void OnSteamSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var n = SteamGamesList.SelectedItems.Count;
        SteamSelectionStatus.Text = n == 0 ? "" : $"{n} game{(n == 1 ? "" : "s")} selected — click Add to register.";
        SteamSelectionStatus.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // Probe the chosen folder and preselect the engine if we can tell. Leaves it on the
    // "Select engine…" placeholder when we can't — so a guess never masquerades as detection.
    private void ApplyDetectedEngine()
    {
        // Steam App ID is the most reliable signal (catches proprietary engines like FromSoft's
        // that have no folder signature); fall back to scanning the game folder.
        var key = KnownEngines.ByAppId(SteamBox.Text) ?? EngineScan.Detect(FolderBox.Text);
        var match = key is null
            ? null
            : (EngineBox.ItemsSource as IEnumerable<EngineOption>)?.FirstOrDefault(o => o.Key == key);
        if (match is not null)
        {
            EngineBox.SelectedItem = match;
            EngineHint.Visibility = Visibility.Visible;
        }
        else
        {
            EngineHint.Visibility = Visibility.Collapsed;
        }
    }

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineBox.SelectedItem is EngineOption opt && EnginePresets.Presets.TryGetValue(opt.Key, out var preset))
            ModPathBox.Text = preset.ModPath;
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        FolderBox.Text = folder.Path;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = folder.Name;
        ApplyDetectedEngine();
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Steam quick-add wins: any checked Steam games auto-register through the same BatchApproved
        // loop MainWindow uses for the AI flow. saveDir null -> AddGameAsync resolves it (Ludusavi etc.).
        var steamPicked = SteamGamesList.SelectedItems.Cast<SteamAddRow>().ToList();
        if (steamPicked.Count > 0)
        {
            foreach (var row in steamPicked) _batchApproved.Add((row.Input, null));
            return; // close; MainWindow registers every BatchApproved entry
        }

        // AI batch mode: approved rows from the profile flow take precedence over the single-form fields.
        if (_batchApproved.Count > 0) return;

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(FolderBox.Text)
            || EngineBox.SelectedItem is not EngineOption)
        {
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true; // keep the dialog open
        }
    }

    /// <summary>The assembled input — call only after a Primary result (validation has passed).</summary>
    public GameInput BuildInput() => new()
    {
        Name = NameBox.Text.Trim(),
        Engine = (EngineBox.SelectedItem as EngineOption)?.Key ?? "custom",
        GameRoot = FolderBox.Text.Trim(),
        ModPath = string.IsNullOrWhiteSpace(ModPathBox.Text) ? null : ModPathBox.Text.Trim(),
        SteamAppId = string.IsNullOrWhiteSpace(SteamBox.Text) ? null : SteamBox.Text.Trim(),
        SaveRoot = SaveRootBox.SelectedItem as string,
        SaveSubPath = string.IsNullOrWhiteSpace(SaveSubPathBox.Text) ? null : SaveSubPathBox.Text.Trim(),
        RequiredLauncher = string.IsNullOrWhiteSpace(RequiredLauncherBox.Text) ? null : RequiredLauncherBox.Text.Trim(),
        // No visible wizard control for these — carry the applied agent profile's values through so
        // they reach the registry instead of being silently dropped. Null on manual add (no draft),
        // which leaves the preset defaults to apply in BuildGameEntry.
        WindowTitle = _appliedDraft?.WindowTitle,
        FileExtensions = _appliedDraft?.FileExtensions,
        GroupingRule = _appliedDraft?.GroupingRule,
        CurseforgeGameId = _appliedDraft?.CurseforgeGameId,
        SaveModPath = _appliedDraft?.SaveModPath,
        SaveModForbidden = _appliedDraft?.SaveModForbidden,
        NexusGameDomain = _appliedDraft?.NexusGameDomain,
    };
}
