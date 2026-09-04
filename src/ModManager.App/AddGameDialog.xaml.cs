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

    // Store library, used to resolve locally-cached cover art for the Steam quick-add rows.
    private readonly IStoreLibrary _store = App.AppHost.Services.GetRequiredService<IStoreLibrary>();

    // Installed store games, kept so a popular-game pick can auto-fill the folder we already resolved.
    private readonly IReadOnlyList<InstalledGame> _installedGames;

    // Resolved save folder from an "Add with AI" apply, stashed for the register path to persist.
    private string? _resolvedSaveDir;

    // The applied agent profile, stashed so BuildInput can carry fields that have no visible wizard
    // control (windowTitle / fileExtensions / groupingRule / curseforgeGameId). Null on manual add.
    private GameDefinitionDraft? _appliedDraft;

    /// <summary>The manifest id of a game the user PICKED from the catalogue, as opposed to typed.
    ///
    /// <para>It is carried separately from the visible fields for the same reason <see
    /// cref="_appliedDraft"/> is: there is no control for it, and inferring it from the name box is
    /// precisely the bug this exists to fix. Null on a manual add, which leaves
    /// <c>BuildGameEntry</c> deriving the id from the name exactly as before.</para></summary>
    private string? _pickedManifestId;

    // Approved batch rows, populated by OnApplyBatch and consumed by MainWindow's register loop on
    // Primary. Empty in the single-game flow.
    private readonly List<(GameInput Input, string? ResolvedSaveDir)> _batchApproved = new();

    private sealed record BatchRowVM(string Headline, string Detail);

    // One checkable Steam quick-add row: the ready-to-register input + the engine-tagged display label
    // + the resolved local cover-art path (null when no art is cached). Cover converts the path to an
    // ImageSource App-side — never in Core — mirroring ModRowViewModel.Thumbnail's null-degrades pattern.
    private sealed record SteamAddRow(GameInput Input, string Display, string? CoverPath)
    {
        public Microsoft.UI.Xaml.Media.ImageSource? Cover =>
            string.IsNullOrEmpty(CoverPath) ? null
                : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(CoverPath));
    }

    // One installed game whose engine we couldn't detect — surfaced as a "Set up" row (art + name +
    // a button that pre-fills the manual form so the user picks the engine).
    private sealed record SteamSetupRow(string AppId, string Name, string? CoverPath, string InstallDir)
    {
        public Microsoft.UI.Xaml.Media.ImageSource? Cover =>
            string.IsNullOrEmpty(CoverPath) ? null
                : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(CoverPath));
    }

    /// <summary>The save folder resolved during an "Add with AI" apply, or null if none was resolved.</summary>
    public string? ResolvedSaveDir => _resolvedSaveDir;

    /// <summary>The approved batch inputs, or an empty list if batch mode wasn't used.</summary>
    public IReadOnlyList<(GameInput Input, string? ResolvedSaveDir)> BatchApproved => _batchApproved;

    public sealed record EngineOption(string Key, string Label);

    public AddGameDialog(IntPtr hwnd, IReadOnlyList<InstalledGame> steamGames)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        _hwnd = hwnd;
        _installedGames = steamGames;
        // No default selection — a wrong default reads as "auto-detected" when it isn't.
        EngineBox.ItemsSource = EnginePresets.Presets.Select(kv => new EngineOption(kv.Key, kv.Value.Label)).ToList();

        // Everything curated is offered, but the games on THIS machine come first — the list is 116
        // entries and a user is looking for one of the handful they own. Ranking rather than filtering
        // is deliberate: a game we cannot detect (anything not sold on Steam, which has no install
        // signal we can read) is merely lower down instead of absent, so a detection miss costs a
        // scroll rather than the capability.
        var installedAppIds = _installedGames.Select(g => g.AppId).ToHashSet(StringComparer.Ordinal);
        PopularGamesBox.ItemsSource = PopularGames.All
            .OrderByDescending(g => g.SteamAppId is not null && installedAppIds.Contains(g.SteamAppId))
            .ToList();

        // Save-root enum values for the picker. Set in code, not XAML (literal-bool/SelectedItem-in-markup
        // parse gotcha on this WinUI build).
        SaveRootBox.ItemsSource = GameDefinitionImport.SaveRoots;

        // Quick-add: plan each installed Steam game for one-click auto-add. Detectable-engine games
        // become checkable rows; undetectable ones are listed for manual setup (we never guess an engine).
        var addable = new List<SteamAddRow>();
        var manual = new List<SteamSetupRow>();
        foreach (var g in steamGames)
        {
            var plan = SteamGameImport.Plan(
                new SteamImportCandidate(g.AppId, g.Name, g.InstallDir),
                EngineScan.Detect(g.InstallDir));
            if (plan.Addable && plan.Input is not null)
            {
                var label = EnginePresets.Presets.TryGetValue(plan.Engine!, out var p) ? p.Label : plan.Engine!;
                addable.Add(new SteamAddRow(plan.Input, $"{g.Name}  ·  {label}", _store.ResolveCoverArtPath(g.AppId)));
            }
            else
            {
                manual.Add(new SteamSetupRow(g.AppId, g.Name, _store.ResolveCoverArtPath(g.AppId), g.InstallDir));
            }
        }
        _steamRows = addable;
        SteamGamesList.ItemsSource = _steamRows;
        if (addable.Count == 0) SteamEmptyNote.Visibility = Visibility.Visible;
        if (manual.Count > 0)
        {
            SteamSetupList.ItemsSource = manual;
            SteamSetupHeader.Visibility = Visibility.Visible;
            SteamSetupList.Visibility = Visibility.Visible;
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
        pkg.SetText(GameDefinitionPrompt.Build(AiGameNameBox.Text));
        Clipboard.SetContent(pkg);
        ShowProfileStatus("Prompt copied — run it in your agent, then paste the JSON back here.", "ThemeAccent");
    }

    // Parse + validate the pasted JSON, resolve it to machine paths, and pre-fill the wizard fields.
    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        var result = GameDefinitionImport.Load(AiJsonBox.Text ?? "");
        if (result.Draft is null)
        {
            ShowProfileStatus(string.Join("  ", result.Errors), "ThemeDanger");
            return;
        }
        var d = result.Draft;
        _appliedDraft = d; // stash so BuildInput can carry fields with no visible control

        // The draft names a game of its own; a pick made before it cannot follow it onto that game.
        // Compared rather than assumed, same as OnSteamSetup: a profile for the game already picked
        // keeps its id. A draft with no steamAppId resolves to null and clears, which is the safe way
        // to be wrong.
        if (_pickedManifestId is not null
            && _pickedManifestId != ManifestIdLookup.BySteamAppId(d.SteamAppId))
            _pickedManifestId = null;

        // Resolve + verify on disk (read-only). No browse attempted here — pass null so Steam detection runs.
        var resolver = App.AppHost.Services.GetRequiredService<GameDefinitionResolver>();
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
        var picked = BatchSteamList.SelectedItems.Cast<InstalledGame>().ToList();
        if (picked.Count == 0)
        {
            ShowBatchStatus("Pick at least one Steam game first.", "ThemeDanger");
            return;
        }
        var pkg = new DataPackage();
        pkg.SetText(GameDefinitionPrompt.BuildMany(picked.Select(g => g.Name).ToList()));
        Clipboard.SetContent(pkg);
        ShowBatchStatus($"Batch prompt copied for {picked.Count} games — run it, paste the array back, then Apply all.", "ThemeAccent");
    }

    // Validate the pasted JSON array, resolve each, render a per-row preview, and collect the
    // approved inputs. Primary commits them.
    private async void OnApplyBatch(object sender, RoutedEventArgs e)
    {
        _batchApproved.Clear();
        var results = GameDefinitionImport.LoadMany(BatchJsonBox.Text ?? "");
        if (results.Count == 0)
        {
            ShowBatchStatus("Empty array — nothing to apply.", "ThemeDanger");
            BatchResultsList.ItemsSource = Array.Empty<BatchRowVM>();
            return;
        }

        var picked = BatchSteamList.SelectedItems.Cast<InstalledGame>().ToList();
        var resolver = App.AppHost.Services.GetRequiredService<GameDefinitionResolver>();
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
                Id = ManifestIdLookup.BySteamAppId(r.Draft.SteamAppId),
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

    // Pick a curated game -> pre-fill name, engine, mod folder, and app id, plus the game folder
    // when the game is installed on Steam (matched by app id; left blank otherwise so the user can
    // Browse). Manual entry still works unchanged.
    private void OnPopularSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PopularGamesBox.SelectedItem is not PopularGame g) return;

        // Record WHICH game was picked before touching any text box. The name box is about to be
        // overwritten with the display name, and Slugify of that name is not reliably this game's id.
        _pickedManifestId = g.Id;

        // Fill the plain fields FIRST and unconditionally — these can't throw, so the pre-fill always
        // lands. (Regression guard: a throw while selecting the engine combo below used to abort this
        // handler right after the name was set, leaving the mod folder + app id blank and the dialog
        // looking dead. There is no app-level unhandled-exception sink, so the throw was swallowed.)
        NameBox.Text = g.Name;
        ModPathBox.Text = g.ModPath;
        SteamBox.Text = g.SteamAppId;

        // We already parsed this game's install folder from Steam — fill it so the pick is one step from
        // Add instead of making the user Browse to a path we know. Editable; user can still change it.
        if (InstalledGameMatch.ByAppId(_installedGames, g.SteamAppId) is { } installed)
            FolderBox.Text = installed.InstallDir;

        // Select the matching engine LAST, and deferred off this SelectionChanged tick. Mutating one
        // combo's selection from inside another combo's SelectionChanged is exactly the re-entrancy
        // WinUI can throw on; deferring runs it cleanly after this event returns. Any failure is
        // logged + swallowed so it can never blank the fields above. Re-assert the game-specific mod
        // path after selecting, since that selection fires OnEngineChanged which seeds the engine
        // preset's default path.
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if ((EngineBox.ItemsSource as IEnumerable<EngineOption>)?.FirstOrDefault(o => o.Key == g.Engine) is { } match)
                {
                    EngineBox.SelectedItem = match;
                    ModPathBox.Text = g.ModPath;                  // game-specific path wins over the preset default
                    EngineHint.Visibility = Visibility.Collapsed; // a curated pick, not folder auto-detection
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"popular-pick '{g.Id}' engine-select", ex);
            }
        });
    }

    // "Set up" on an engine-undetected game: pre-fill the manual form (name + folder + app id) and
    // let the user pick the engine. The dialog's Add then registers it via the normal manual path.
    private void OnSteamSetup(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SteamSetupRow row) return;

        // Clear only what belongs to a DIFFERENT game. Setting up the same game's row - to pull its
        // Steam-resolved folder after applying a profile - is a legitimate order, and blanket-clearing
        // silently dropped that profile's fields. What must never survive is a pick, a draft, or a
        // resolved save dir that refers to some other game: Scanner re-joins by id on every scan (a
        // stale id is an ongoing wrong join, not a one-off), and a stale save dir points THIS game's
        // save editor / restore points / save-mod install at ANOTHER game's save tree.
        if (!string.Equals(_appliedDraft?.SteamAppId, row.AppId, StringComparison.Ordinal))
        {
            _appliedDraft = null;
            _resolvedSaveDir = null;   // resolved for the OTHER game; SetSaveDir would point this one at its saves
        }

        // A game in the SET-UP list is one the launcher could not identify, so a curated id picked
        // earlier cannot legitimately belong to it. Compared rather than assumed, so the reason is
        // visible: if this row ever did resolve to the picked game, the id would rightly survive.
        if (_pickedManifestId is not null
            && _pickedManifestId != ManifestIdLookup.BySteamAppId(row.AppId))
            _pickedManifestId = null;

        NameBox.Text = row.Name;
        FolderBox.Text = row.InstallDir;
        SteamBox.Text = row.AppId;
        ApplyDetectedEngine();   // runs the standard auto-detect; these rows are here precisely because detection missed, so it reliably leaves the placeholder for the user to pick
        // The manual form is below this list in the scroll, so the pre-fill lands off-screen and reads
        // as "nothing happened." Bring it into view and focus the engine picker — the next action.
        EngineBox.StartBringIntoView();
        EngineBox.Focus(FocusState.Programmatic);
    }

    // Filter over the detected list (F-026). Checked games survive filtering: the checked SET
    // is the source of truth, the ListView selection is just its visible projection.
    private List<SteamAddRow> _steamRows = new();
    private readonly HashSet<SteamAddRow> _steamChecked = new();
    private bool _steamRelisting;

    private void OnSteamFilterChanged(object sender, TextChangedEventArgs e)
    {
        var q = SteamFilterBox.Text.Trim();
        var view = q.Length == 0
            ? _steamRows
            : _steamRows.Where(r => r.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _steamRelisting = true;
        try
        {
            SteamGamesList.ItemsSource = view;
            foreach (var r in view) if (_steamChecked.Contains(r)) SteamGamesList.SelectedItems.Add(r);
        }
        finally { _steamRelisting = false; }
        UpdateSteamSelectionUi();
    }

    // React as the user checks Steam games. The dialog's Add button commits the CHECKED SET —
    // a game checked, then filtered out of view, still adds (the status line names it).
    private void OnSteamSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_steamRelisting) return; // programmatic re-list churn, not a user action
        foreach (var r in e.AddedItems.OfType<SteamAddRow>()) _steamChecked.Add(r);
        foreach (var r in e.RemovedItems.OfType<SteamAddRow>()) _steamChecked.Remove(r);
        UpdateSteamSelectionUi();
    }

    private void UpdateSteamSelectionUi()
    {
        var picked = _steamChecked.ToList();
        var n = picked.Count;

        // When Steam games are checked, the manual single-game form is irrelevant — each game
        // registers under its OWN Steam name (OnPrimary never reads NameBox in this path). Collapse the
        // manual form + the popular-game picker so the dialog doesn't read as "pick games AND fill in a
        // name." Unchecking all brings the manual form back. This is the fix for the "why is there a
        // name field / what about two games" confusion: the field disappears and we name every game.
        ManualFormPanel.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        PopularGamesBox.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (n == 0)
        {
            SteamSelectionStatus.Visibility = Visibility.Collapsed;
            PrimaryButtonText = "Add";
        }
        else
        {
            SteamSelectionStatus.Text = "Adding: " + string.Join(", ", picked.Select(p => p.Input.Name));
            SteamSelectionStatus.Visibility = Visibility.Visible;
            PrimaryButtonText = n == 1 ? "Add 1 game" : $"Add {n} games";
        }
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
            RequestGameLink.Visibility = Visibility.Collapsed;
        }
        else
        {
            EngineHint.Visibility = Visibility.Collapsed;
            RequestGameLink.Visibility = Visibility.Visible;
        }
    }

    // Open a prefilled GitHub issue against the 626-game-manifest feed repo.
    // Mirrors the FrameworkUnrecognizedNudgeDialog open-URL pattern.
    private void OnRequestGame(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;   // name is the one required field
        var engineKey = (EngineBox.SelectedItem as EngineOption)?.Key;   // null when unselected -> "Not sure"
        var modPath = ModPathBox.Text?.Trim();
        var notes = string.IsNullOrWhiteSpace(modPath) ? null : $"Mod path: {modPath}";
        var url = ModManager.Core.GameRequestUrl.Build(name, SteamBox.Text?.Trim(), engineKey, notes);
        if (ModManager.Core.SafeUrl.IsHttpUrl(url))
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineBox.SelectedItem is EngineOption opt && EnginePresets.Presets.TryGetValue(opt.Key, out var preset))
        {
            ModPathBox.Text = preset.ModPath;
            // The user now has an engine — manual pick counts as "resolved", so retire the request nudge.
            // ApplyDetectedEngine still owns showing it (so it never flashes before a name/folder exists).
            RequestGameLink.Visibility = Visibility.Collapsed;
        }
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
        var steamPicked = _steamChecked.ToList();
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
        // Set only when the user PICKED a catalogued game; null when they typed one, which leaves
        // BuildGameEntry deriving the id from the name as it always has.
        Id = _pickedManifestId,
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
