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
    private readonly ModEngineService _me2;
    private readonly DirectInjectService _direct;
    private readonly ThemeService _themes;
    private readonly LudusaviService _ludu;
    private GameContext? _ctx;
    private bool _suppressActiveSwitch;

    // FromSoft games whose mods are driven by a Mod Engine 2 config (not filesystem scans).
    private bool ConfigBacked => _ctx is not null && _me2.IsConfigBacked(_ctx.Game);

    // FromSoft games without ME2: mods are direct-inject loose files (recognized + toggled by name).
    private bool DirectInjectBacked => _ctx is not null && !ConfigBacked && _direct.Applies(_ctx.Game);

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchHintVisibility))]
    private bool launchNeedsAttention;

    public Visibility LaunchHintVisibility => LaunchNeedsAttention ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoopHintVisibility))]
    private bool coopLauncherMissing;

    public Visibility CoopHintVisibility => CoopLauncherMissing ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOrderVisibility))]
    [NotifyPropertyChangedFor(nameof(NormalBarVisibility))]
    private bool isLoadOrderMode;

    public Visibility GameVisibility => HasGame ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasGame ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LoadOrderVisibility => IsLoadOrderMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalBarVisibility => IsLoadOrderMode ? Visibility.Collapsed : Visibility.Visible;

    public MainViewModel(LauncherService svc, ModEngineService me2, DirectInjectService direct, ThemeService themes, LudusaviService ludu)
    {
        _svc = svc;
        _me2 = me2;
        _direct = direct;
        _themes = themes;
        _ludu = ludu;
        ThemeOptions = themes.Themes;
        SelectedTheme = themes.Default; // applies the default theme via OnSelectedThemeChanged
    }

    partial void OnSelectedThemeChanged(Theme? value)
    {
        if (value is not null) _themes.Apply(value);
    }

    /// <summary>Refresh the theme list after an import and select (apply) the new one.</summary>
    public void OnThemeImported(Theme imported)
    {
        ThemeOptions = _themes.Themes;
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == imported.Id) ?? imported;
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
            // Three worlds: Mod Engine 2 games read their mods from the config; FromSoft games
            // without ME2 are direct-inject (loose files next to the exe) — toggled by name, never
            // deleted; everything else is a filesystem scan via the proven Scanner pipeline.
            IReadOnlyList<Mod> list;
            var directInject = DirectInjectBacked;
            if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
            else if (directInject) list = _direct.List(_ctx.Game);
            else list = await ReloadFromScannerAsync();

            // Direct-inject mods can be toggled (reversible move) but not uninstalled here.
            Mods = new ObservableCollection<ModRowViewModel>(
                list.Select(m => new ModRowViewModel(m, canToggle: true, canUninstall: !directInject)));
            GameRootText = _ctx.GameRoot;
            LaunchNeedsAttention = LaunchOptions.NeedsAttention(_ctx.Game.SteamAppId);
            CoopLauncherMissing = _direct.SeamlessNeedsLauncher(_ctx.Game);
            if (directInject)
                StatusText = list.Count > 0
                    ? $"Detected {list.Count} mod{(list.Count == 1 ? "" : "s")} — toggle to enable/disable (no Mod Engine 2)."
                    : "No Mod Engine 2 and no recognized mods found. Install Mod Engine 2 to manage folder mods here.";
            else UpdateStatus();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    private async Task<IReadOnlyList<Mod>> ReloadFromScannerAsync()
    {
        await Scanner.MigrateDataDirAsync(_ctx!);
        return await Scanner.ListWithClassAsync(_ctx!);
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
            if (ConfigBacked) _me2.SetEnabled(_ctx.Game, row.Mod.Name, row.Enabled);
            else if (DirectInjectBacked) _direct.SetEnabled(_ctx.Game, row.Mod.Name, row.Enabled);
            else if (row.Enabled) await Scanner.EnableModAsync(row.Mod.Name, _ctx);
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
    private Task AllOn() => SetAllAsync(true);

    [RelayCommand]
    private Task AllOff() => SetAllAsync(false);

    private Task SetAllAsync(bool on) => BulkAsync(() =>
    {
        if (ConfigBacked) { _me2.SetAll(_ctx!.Game, on); return Task.CompletedTask; }
        if (DirectInjectBacked)
        {
            foreach (var m in Mods.Where(m => m.Enabled != on)) _direct.SetEnabled(_ctx!.Game, m.Mod.Name, on);
            return Task.CompletedTask;
        }
        return Scanner.SetAllModsAsync(on, _ctx!);
    });

    [RelayCommand]
    private Task SetMode(string mode)
    {
        ActiveMode = mode;
        // No MP/SP split for Mod Engine 2 or direct-inject mods — the mode buttons are a no-op there.
        if (ConfigBacked || DirectInjectBacked) return Task.CompletedTask;
        return BulkAsync(() => Scanner.ApplyModeAsync(mode, _ctx!));
    }

    [RelayCommand]
    private Task Refresh() => ReloadModsAsync();

    /// <summary>Public reload hook for dialogs that change mod state (e.g. loading a profile).</summary>
    public Task RefreshAsync() => ReloadModsAsync();

    // ---------- inline load-order mode ----------

    /// <summary>Enter load-order mode: show only enabled mods, in saved order, numbered + draggable.</summary>
    public async Task EnterLoadOrderAsync()
    {
        if (_ctx is null || IsLoadOrderMode) return;
        if (DirectInjectBacked)
        {
            // Direct-inject mods load independently — there's no priority order to arrange.
            StatusText = "Load order doesn't apply to these mods — they load independently.";
            return;
        }
        List<ModRowViewModel> ordered;
        if (ConfigBacked)
        {
            // The config's array order IS the load order — keep enabled mods in their current order.
            ordered = Mods.Where(m => m.Enabled).ToList();
        }
        else
        {
            var orderKeys = await Scanner.GetLoadOrderAsync(_ctx);
            var byKey = Mods.Where(m => m.Enabled)
                .GroupBy(m => m.Mod.Name).ToDictionary(g => g.Key, g => g.First());
            ordered = orderKeys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        }
        foreach (var r in ordered) r.InLoadOrder = true;
        Mods = new ObservableCollection<ModRowViewModel>(ordered);
        Renumber();
        IsLoadOrderMode = true;
        StatusText = "Drag to reorder, or type a position. Top loads first. Apply when done.";
    }

    public async Task ApplyLoadOrderAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var order = Mods.Select(m => m.Mod.Name).ToList();
            if (ConfigBacked) _me2.Reorder(_ctx.Game, order);
            else await Scanner.ApplyLoadOrderAsync(_ctx, order);
            IsLoadOrderMode = false;
            await ReloadModsAsync();
            StatusText = "Load order applied.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    public async Task CancelLoadOrderAsync()
    {
        IsLoadOrderMode = false;
        await ReloadModsAsync();
    }

    /// <summary>Move a row to a 1-based position (type-to-jump) and renumber.</summary>
    public void MoveTo(ModRowViewModel row, int targetPosition)
    {
        var i = Mods.IndexOf(row);
        if (i < 0) return;
        var j = Math.Clamp(targetPosition - 1, 0, Mods.Count - 1);
        if (i == j) return;
        Mods.Move(i, j);
        Renumber();
    }

    /// <summary>Re-stamp 1-based positions after any reorder (drag or jump).</summary>
    public void Renumber()
    {
        for (var i = 0; i < Mods.Count; i++) Mods[i].OrderPosition = i + 1;
    }

    /// <summary>The active game's launch targets (modded / alt-launcher / vanilla) for the dropdown.</summary>
    public IReadOnlyList<LaunchTarget> LaunchTargets => _ctx?.Game.LaunchTargets ?? Array.Empty<LaunchTarget>();

    [RelayCommand]
    private void Launch()
    {
        if (_ctx is null) return;
        AutoBackupBeforeLaunch();
        try { if (!_svc.Launch(_ctx.Game)) StatusText = "No launch target configured for this game."; }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Run a specific launch target chosen from the dropdown.</summary>
    public void LaunchTargetExplicit(LaunchTarget target)
    {
        if (_ctx is null) return;
        AutoBackupBeforeLaunch();
        try { _svc.Launch(target, _ctx.Game.GameRoot); }
        catch (Exception e) { StatusText = e.Message; }
    }

    // When the game opts in, snapshot the save (auto) and prune before launching. Best-effort —
    // a backup failure surfaces but never blocks play.
    private void AutoBackupBeforeLaunch()
    {
        if (_ctx is null || !_ctx.Game.AutoBackupOnLaunch) return;
        var dir = _ctx.SaveDir;
        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;
        try
        {
            SaveManager.Backup(dir, _ctx.SavesDir, "before-launch", auto: true);
            SaveManager.Prune(_ctx.SavesDir, _ctx.Game.SaveAutoKeep ?? int.MaxValue);
        }
        catch (Exception e) { StatusText = "Auto-backup before launch failed: " + e.Message; }
    }

    /// <summary>The verified launch options for the active game (internal + external), for the dialog.</summary>
    public IReadOnlyList<LaunchOption> ActiveLaunchOptions => LaunchOptions.For(_ctx?.Game.SteamAppId);

    /// <summary>Current anti-cheat state for a toggle option on the active game.</summary>
    public AntiCheatState AntiCheatStateOf(LaunchOption opt)
    {
        var folder = DirectInjectService.PlayFolder(_ctx?.Game.GameRoot);
        return folder is null || opt.Bootstrapper is null
            ? AntiCheatState.Unsupported
            : AntiCheat.State(folder, opt.Bootstrapper);
    }

    /// <summary>Flip a game's anti-cheat (reversible swap); returns the resulting state.</summary>
    public AntiCheatState SetAntiCheat(LaunchOption opt, bool turnOn)
    {
        var folder = DirectInjectService.PlayFolder(_ctx?.Game.GameRoot);
        if (folder is null || opt.Bootstrapper is null || opt.RealExe is null) return AntiCheatState.Unsupported;
        try
        {
            if (turnOn) AntiCheat.Enable(folder, opt.Bootstrapper);
            else AntiCheat.Disable(folder, opt.Bootstrapper, opt.RealExe);
            StatusText = turnOn
                ? "Anti-cheat ON — Play launches normally (official online OK)."
                : "Anti-cheat OFF — press Play for mods. Avoid OFFICIAL online until you turn it back on (Seamless Co-op is fine).";
        }
        catch (Exception e) { StatusText = e.Message; }
        return AntiCheatStateOf(opt);
    }

    /// <summary>Run an internal launch option (the app starts the real exe directly).</summary>
    public void RunInternalOption(LaunchOption opt)
    {
        if (_ctx is null || opt.Exe is null) return;
        var root = _ctx.Game.GameRoot;
        var target = new LaunchTarget(opt.Title, "exe", System.IO.Path.Combine(root, opt.Exe))
        {
            Args = opt.Args,
            WorkingDir = opt.WorkingSubdir is null ? root : System.IO.Path.Combine(root, opt.WorkingSubdir),
        };
        LaunchTargetExplicit(target);
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
        if (ConfigBacked)
        {
            // ME2 mods are folders registered in the config — drop-to-install isn't wired yet.
            StatusText = "For Mod Engine 2 games, place the mod's folder under the ME2 'mod' folder, then add it in the config. Auto-install is coming.";
            return;
        }
        if (DirectInjectBacked)
        {
            // Direct-inject: extract/copy the drop into the game's exe folder, then re-detect so a
            // newly-installed launcher (Seamless / Mod Engine 2) surfaces its Play button immediately
            // — no manual re-scan. "Just install them" made literal.
            IsBusy = true;
            try
            {
                var r = _direct.Install(_ctx.Game, paths);
                if (r.Added.Count > 0) _svc.Redetect(_ctx.Game.Id); // pick up mod folders + launchers
                await ReloadModsAsync();                            // rebuilds context: refreshed list + Play targets
                var launcher = _ctx?.Game.LaunchTargets.Any(t => t.Kind == "exe") ?? false;
                StatusText = r.Added.Count > 0
                    ? $"Installed {r.Added.Count} file{(r.Added.Count == 1 ? "" : "s")}"
                      + (r.Skipped.Count > 0 ? $", skipped {r.Skipped.Count}" : "")
                      + (launcher ? " — open the Play menu to launch with mods." : ".")
                    : r.Skipped.Count > 0
                        ? $"Nothing installed — skipped {r.Skipped.Count} (already present or unsafe path)."
                        : "Nothing installable in that drop.";
            }
            catch (Exception e) { StatusText = e.Message; }
            finally { IsBusy = false; }
            return;
        }
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

    /// <summary>Register a new game from the wizard, make it active, and load it. When the wizard already
    /// resolved a save folder (the "Add with AI" flow), <paramref name="resolvedSaveDir"/> is used directly
    /// instead of re-running detection.</summary>
    public async Task AddGameAsync(GameInput input, string? resolvedSaveDir = null)
    {
        IsBusy = true;
        try
        {
            var entry = _svc.AddGame(input);
            // Prefer the wizard's already-resolved save folder; else find it (Ludusavi by Steam id, then heuristics).
            var saveDir = !string.IsNullOrEmpty(resolvedSaveDir)
                ? resolvedSaveDir
                : await SaveLocator.DetectAsync(_ludu, entry.GameName, entry.Engine, entry.GameRoot, entry.SteamAppId);
            if (saveDir is not null) _svc.SetSaveDir(entry.Id, saveDir);
            await LoadAsync();
            StatusText = $"Added {entry.GameName}.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Re-scan the active game for mod folders + launchers (Mod Engine 2 / Seamless Co-op).
    /// For games added before detection existed, or after a mod launcher was installed.</summary>
    public async Task RedetectActiveAsync()
    {
        if (ActiveGame is null) return;
        IsBusy = true;
        try
        {
            var g = _svc.Redetect(ActiveGame.Id);
            await ReloadModsAsync();
            var found = g?.LaunchTargets.Count ?? 0;
            StatusText = found > 0
                ? $"Re-scan done — {found} launch option{(found == 1 ? "" : "s")} found"
                + (g!.ModEngineConfig is not null ? ", Mod Engine 2 config linked." : ".")
                : "Re-scan done — no mod launchers found.";
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
            if (ConfigBacked) _me2.Remove(_ctx.Game, row.Mod.Name);
            else await Scanner.UninstallModAsync(row.Mod.Name, _ctx);
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
