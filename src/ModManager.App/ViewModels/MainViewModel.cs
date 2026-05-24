using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using ModManager.App.Services;
using ModManager.Core;

namespace ModManager.App.ViewModels;

public sealed record GameOption(string Id, string Name);

/// <summary>
/// Orchestrates the shell over the proven Core: loads the active game's mods, toggles them
/// reversibly, applies MP/SP loadouts, fetches metadata, intakes drops, and launches. All
/// filesystem work delegates to Scanner; this VM only sequences and surfaces state.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly LauncherService _svc;
    private readonly ThemeService _themes;
    private GameContext? _ctx;
    private bool _suppressActiveSwitch;

    [ObservableProperty] private IReadOnlyList<Theme> themeOptions = Array.Empty<Theme>();
    [ObservableProperty] private Theme? selectedTheme;

    [ObservableProperty] private ObservableCollection<GameOption> games = new();
    [ObservableProperty] private GameOption? activeGame;
    [ObservableProperty] private ObservableCollection<ModRowViewModel> mods = new();
    [ObservableProperty] private string statusText = "No game registered.";
    [ObservableProperty] private string gameRootText = "";
    [ObservableProperty] private string activeMode = "all";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    private bool hasGame;

    [ObservableProperty] private bool isBusy;

    public Visibility GameVisibility => HasGame ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasGame ? Visibility.Collapsed : Visibility.Visible;

    public MainViewModel(LauncherService svc, ThemeService themes)
    {
        _svc = svc;
        _themes = themes;
        ThemeOptions = themes.Themes;
        SelectedTheme = themes.Default; // applies the default theme via OnSelectedThemeChanged
    }

    partial void OnSelectedThemeChanged(Theme? value)
    {
        if (value is not null) _themes.Apply(value);
    }

    public async Task LoadAsync()
    {
        var reg = _svc.LoadRegistry();
        _suppressActiveSwitch = true;
        Games.Clear();
        foreach (var g in reg.Games) Games.Add(new GameOption(g.Id, g.GameName));
        var active = Registry.GetActiveGame(reg);
        ActiveGame = active is null ? null : Games.FirstOrDefault(x => x.Id == active.Id);
        _suppressActiveSwitch = false;
        await ReloadModsAsync();
    }

    partial void OnActiveGameChanged(GameOption? value)
    {
        if (_suppressActiveSwitch || value is null) return;
        _svc.SetActiveGame(value.Id);
        _ = ReloadModsAsync();
    }

    private async Task ReloadModsAsync()
    {
        _ctx = _svc.ActiveContext();
        HasGame = _ctx is not null;
        if (_ctx is null)
        {
            Mods.Clear();
            GameRootText = "";
            StatusText = "No game registered. Add one in the wizard or the Electron app.";
            return;
        }
        IsBusy = true;
        try
        {
            await Scanner.MigrateDataDirAsync(_ctx);
            var list = await Scanner.ListWithClassAsync(_ctx);
            Mods = new ObservableCollection<ModRowViewModel>(list.Select(m => new ModRowViewModel(m)));
            GameRootText = _ctx.GameRoot;
            UpdateStatus();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    private void UpdateStatus() => StatusText = $"{Mods.Count(m => m.Enabled)} of {Mods.Count} enabled";

    /// <summary>Toggle one mod. The reversible disable/enable lives in Scanner; on failure the
    /// switch reverts and the error surfaces (never a silent half-disable).</summary>
    public async Task ToggleAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        row.IsBusy = true;
        try
        {
            if (row.Enabled) await Scanner.EnableModAsync(row.Mod.Name, _ctx);
            else await Scanner.DisableModAsync(row.Mod.Name, _ctx);
            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            row.Enabled = !row.Enabled; // revert the visual
            StatusText = e.Message;
        }
        finally { row.IsBusy = false; }
    }

    [RelayCommand]
    private Task AllOn() => BulkAsync(() => Scanner.SetAllModsAsync(true, _ctx!));

    [RelayCommand]
    private Task AllOff() => BulkAsync(() => Scanner.SetAllModsAsync(false, _ctx!));

    [RelayCommand]
    private Task SetMode(string mode)
    {
        ActiveMode = mode;
        return BulkAsync(() => Scanner.ApplyModeAsync(mode, _ctx!));
    }

    [RelayCommand]
    private Task Refresh() => ReloadModsAsync();

    [RelayCommand]
    private void Launch()
    {
        if (_ctx is null) return;
        if (!_svc.Launch(_ctx.Game)) StatusText = "No launch target configured for this game.";
    }

    [RelayCommand]
    private async Task FetchMetadata()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge);
            StatusText = r.GameId is null
                ? "Couldn't resolve this game on CurseForge."
                : $"Matched {r.Matched} of {r.Total} mods on CurseForge.";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Intake dropped/picked paths, then attach metadata (fingerprint, then name-search fallback).</summary>
    public async Task AddModsAsync(IReadOnlyList<string> paths)
    {
        if (_ctx is null || paths.Count == 0) return;
        IsBusy = true;
        try
        {
            var r = await Scanner.AddModsAsync(paths, _ctx);
            var identified = 0;
            if (r.Added.Count > 0)
            {
                StatusText = $"Added {r.Added.Count} — fetching metadata…";
                // Exact match first (fingerprint), then a name-search fallback — fingerprint
                // misses repacked/local files, so this is how a dropped mod still gets metadata.
                try { identified = (await Scanner.FingerprintIdentifyAsync(_ctx, _svc.CurseForge, r.Added)).Matched; }
                catch { /* best-effort; intake already succeeded */ }
                try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); }
                catch { /* best-effort */ }
            }
            StatusText = $"Added {r.Added.Count}, skipped {r.Skipped.Count}"
                + (identified > 0 ? $", identified {identified} on CurseForge" : "");
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Register a new game from the wizard, make it active, and load it.</summary>
    public async Task AddGameAsync(GameInput input)
    {
        IsBusy = true;
        try
        {
            var entry = _svc.AddGame(input);
            await LoadAsync();
            StatusText = $"Added {entry.GameName}.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Remove the active game from the launcher. Gated by a confirm dialog in the view.</summary>
    public async Task RemoveActiveGameAsync()
    {
        if (ActiveGame is null) return;
        IsBusy = true;
        try
        {
            _svc.RemoveGame(ActiveGame.Id);
            await LoadAsync();
            StatusText = "Removed game from the launcher.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Permanently uninstall a mod (deletes files). Gated by a confirm dialog in the view.</summary>
    public async Task UninstallAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            await Scanner.UninstallModAsync(row.Mod.Name, _ctx);
            StatusText = $"Uninstalled {row.DisplayName}.";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    private async Task BulkAsync(Func<Task> op)
    {
        if (_ctx is null) return;
        IsBusy = true;
        try { await op(); await ReloadModsAsync(); }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }
}
