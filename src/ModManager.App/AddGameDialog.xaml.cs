using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class AddGameDialog : ContentDialog
{
    private readonly IntPtr _hwnd;

    public sealed record EngineOption(string Key, string Label);

    public AddGameDialog(IntPtr hwnd, IReadOnlyList<SteamGame> steamGames)
    {
        InitializeComponent();
        _hwnd = hwnd;
        // No default selection — a wrong default reads as "auto-detected" when it isn't.
        EngineBox.ItemsSource = EnginePresets.Presets.Select(kv => new EngineOption(kv.Key, kv.Value.Label)).ToList();

        PopularGamesBox.ItemsSource = PopularGames.All;

        SteamGamesBox.ItemsSource = steamGames;
        if (steamGames.Count == 0) SteamGamesBox.PlaceholderText = "No installed Steam games detected";
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
    };
}
