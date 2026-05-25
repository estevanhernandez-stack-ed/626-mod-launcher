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

    /// <summary>The save folder resolved during an "Add with AI" apply, or null if none was resolved.</summary>
    public string? ResolvedSaveDir => _resolvedSaveDir;

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

        SteamGamesBox.ItemsSource = steamGames;
        if (steamGames.Count == 0) SteamGamesBox.PlaceholderText = "No installed Steam games detected";
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

    // Pick a Steam game -> pre-fill name, folder, app id, and auto-detect the engine.
    private void OnSteamSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SteamGamesBox.SelectedItem is not SteamGame g) return;
        NameBox.Text = g.Name;
        FolderBox.Text = g.InstallDir;
        SteamBox.Text = g.AppId;
        ApplyDetectedEngine();
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
        NexusGameDomain = _appliedDraft?.NexusGameDomain,
    };
}
