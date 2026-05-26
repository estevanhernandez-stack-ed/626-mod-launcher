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
    private readonly NexusService _nexus;
    private GameContext? _ctx;
    private bool _suppressActiveSwitch;

    /// <summary>
    /// Shows the collision prompt and returns the rel-paths to replace, or null if cancelled. The
    /// view wires this (the dialog + XamlRoot live in the code-behind, not the VM). When unset,
    /// intake replaces nothing — new files still install, collisions are left untouched.
    /// </summary>
    public Func<IntakePlan, Task<ISet<string>?>>? ConfirmReplacements { get; set; }

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

    // MP-safety summary: how many enabled mods read as not-co-op-safe (Risky or SP-only). Non-blocking.
    private int MpRiskyEnabledCount => Mods.Count(m => m.Enabled && m.EffectiveMp is MpRisk.Risky or MpRisk.SpOnly);
    public Visibility MpWarningVisibility => MpRiskyEnabledCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string MpWarningText
    {
        get { var n = MpRiskyEnabledCount; return $"{n} enabled mod{(n == 1 ? "" : "s")} may not be co-op-safe"; }
    }
    private void NotifyMpWarning() { OnPropertyChanged(nameof(MpWarningVisibility)); OnPropertyChanged(nameof(MpWarningText)); }

    /// <summary>Set or clear (Auto = null) a mod's MP-compat override, persist it, refresh the badge + summary.</summary>
    public void SetMpOverride(ModRowViewModel row, MpRisk? value)
    {
        if (_ctx is null) return;
        try { MpCompatStore.SetOverride(_ctx.DataDir, row.Mod.Name, value); }
        catch (Exception e) { StatusText = e.Message; return; }
        row.MpOverride = value;
        NotifyMpWarning();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOrderVisibility))]
    [NotifyPropertyChangedFor(nameof(NormalBarVisibility))]
    private bool isLoadOrderMode;

    public Visibility GameVisibility => HasGame ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => HasGame ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LoadOrderVisibility => IsLoadOrderMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalBarVisibility => IsLoadOrderMode ? Visibility.Collapsed : Visibility.Visible;

    public MainViewModel(LauncherService svc, ModEngineService me2, DirectInjectService direct, ThemeService themes, LudusaviService ludu, NexusService nexus)
    {
        _svc = svc;
        _me2 = me2;
        _direct = direct;
        _themes = themes;
        _ludu = ludu;
        _nexus = nexus;
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
            // Order rows so variant-family members (same mod page / _Nx base) sit together, and mark
            // the members of a multi-variant family so the row shows a VARIANT chip. Toggles stay
            // per-row (the user enables as many as they want; disabling holds, never re-downloads).
            var mpOverrides = MpCompatStore.Load(_ctx.DataDir);
            var rows = new List<ModRowViewModel>();
            // A multi-variant family (e.g. Faster Ships 5x/10x/20x) collapses to ONE row whose levels
            // are inline toggle chips; a singleton renders as a normal row. Build in variant-group order;
            // OrderAndStampSections then orders + sections per GroupMode.
            foreach (var fam in VariantGroups.Group(list))
            {
                var rep = fam.Members[0]; // representative carries the row's name/description/metadata
                var folderAbs = rep.IsFolder
                    ? System.IO.Path.Combine(Scanner.LocByName(rep.Location, _ctx!).Abs, rep.Name)
                    : "";
                var options = fam.IsMulti
                    ? (IReadOnlyList<VariantOptionVM>)fam.Members
                        .Select(m => new VariantOptionVM(
                            m.Name,
                            string.IsNullOrEmpty(m.Variant) ? m.Name : m.Variant!.ToUpperInvariant(),
                            m.Enabled,
                            !m.ReadOnly || m.Loader is "ue4ss" or "bepinex"))
                        .ToList()
                    : System.Array.Empty<VariantOptionVM>();
                rows.Add(new ModRowViewModel(rep, canToggle: !rep.ReadOnly || rep.Loader is "ue4ss" or "bepinex", canUninstall: !directInject && !rep.ReadOnly)
                {
                    ReadmeFilePath = Scanner.ReadmePathFor(rep.Name, _ctx!),
                    MpOverride = mpOverrides.TryGetValue(rep.Name, out var o) ? o : null,
                    ModFolderAbs = folderAbs,
                    VariantOptions = options,
                });
            }
            OrderAndStampSections(rows);
            NotifyMpWarning();
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

    // View toggle: group the list by source (paks / UE4SS installed / bundled) or by MP-safety class.
    public IReadOnlyList<string> GroupModes { get; } = new[] { "By source", "By class", "By category" };

    [ObservableProperty] private string groupMode = "By source";
    partial void OnGroupModeChanged(string value)
    {
        if (Mods.Count > 0) OrderAndStampSections(Mods.ToList()); // re-group in place, no rescan
    }

    // Section key for a mod under the active GroupMode. Rank drives top-to-bottom order; Label is the
    // divider text. "By class" uses the MP-safety class (both/sp/mp) we track, not a content category.
    private (int Rank, string Label) SectionOf(Mod m)
    {
        if (GroupMode == "By category")
        {
            // UE4SS framework mods aren't on CF/Nexus (no category to fetch) — give them their own
            // bucket so they don't pile into UNCATEGORIZED next to truly unidentified mods.
            if (m.Builtin) return (8000, "UE4SS BUILT-IN");
            var c = string.IsNullOrWhiteSpace(m.Category) ? "UNCATEGORIZED" : m.Category!.Trim().ToUpperInvariant();
            var rank = string.Equals(c, "UNCATEGORIZED", StringComparison.Ordinal) ? int.MaxValue : 0;
            return (rank, c);
        }
        if (GroupMode == "By class")
            return (m.Class ?? "both").ToLowerInvariant() switch
            {
                "both" => (0, "WORKS IN MP & SP"),
                "sp" => (1, "SINGLE-PLAYER"),
                "mp" => (2, "MULTIPLAYER"),
                _ => (3, "UNCLASSIFIED"),
            };
        return m.Loader != "ue4ss" ? (0, "MODS")
            : m.Builtin ? (2, "BUNDLED WITH UE4SS")
            : (1, "UE4SS SCRIPTS");
    }

    // Order rows by the active grouping (stable OrderBy preserves variant adjacency within a section)
    // and stamp a divider on the first row of each block. Used by reload and the group-by toggle.
    private void OrderAndStampSections(IEnumerable<ModRowViewModel> rows)
    {
        var ordered = rows.OrderBy(r => SectionOf(r.Mod).Rank).ToList();
        string? prev = null;
        foreach (var r in ordered)
        {
            var label = SectionOf(r.Mod).Label;
            r.SectionHeader = label != prev ? label : null;
            prev = label;
        }
        Mods = new ObservableCollection<ModRowViewModel>(ordered);
    }

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
            else await Scanner.SetLoaderModEnabledAsync(row.Mod.Name, row.Enabled, _ctx);
            // Warn when toggling an owned UE4SS mod — manifest flip succeeded, but the managing
            // tool may overwrite it on its next deploy (mirrors the config edit-with-warning rule).
            var wasOwnedLoader = row.Mod.ReadOnly && row.Mod.Loader is "ue4ss" or "bepinex";
            await ReloadModsAsync();
            if (wasOwnedLoader && !string.IsNullOrEmpty(row.Mod.Managed))
                StatusText = $"Toggled {row.Mod.Name} via the loader — managed by {row.Mod.Managed.ToUpperInvariant()}, may be overwritten on its next deploy.";
        }
        catch (Exception e)
        {
            row.Enabled = !row.Enabled; // revert the visual
            StatusText = e.Message;
        }
        finally { row.IsBusy = false; }
    }

    /// <summary>Toggle one level of a multi-variant family — enable/disable that specific variant's
    /// files via the same gated path as the single toggle, then reload to refresh the chips.</summary>
    public async Task ToggleVariantAsync(VariantOptionVM opt, bool enable)
    {
        if (_ctx is null) return;
        try
        {
            if (enable)
            {
                // Single-select: only one level of a family runs at a time — turn the siblings off so
                // two levels never collide. (Turning the chosen one back off later leaves none active.)
                var list = await Scanner.BuildModListAsync(_ctx);
                var fam = VariantGroups.Group(list).FirstOrDefault(f => f.Members.Any(m => m.Name == opt.ModName));
                if (fam is not null)
                    foreach (var sib in fam.Members.Where(m => m.Name != opt.ModName && m.Enabled))
                        await Scanner.SetLoaderModEnabledAsync(sib.Name, false, _ctx);
            }
            await Scanner.SetLoaderModEnabledAsync(opt.ModName, enable, _ctx);
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
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

    /// <summary>True when any mod is enabled — the trigger for launch enforcement.</summary>
    public bool AnyModsEnabled => Mods.Any(m => m.Enabled);

    /// <summary>The required launcher resolved to a runnable exe target, or null when not set, the
    /// path resolves outside GameRoot (bad/manual value), or the exe is missing.</summary>
    public LaunchTarget? RequiredLauncherTarget()
    {
        if (_ctx is null || string.IsNullOrEmpty(_ctx.Game.RequiredLauncher)) return null;
        var root = _ctx.Game.GameRoot;
        var rel = _ctx.Game.RequiredLauncher!.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel));
        var rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null; // escaped GameRoot
        if (!System.IO.File.Exists(abs)) return null;                                   // not installed
        return new LaunchTarget(System.IO.Path.GetFileName(abs), "exe", abs) { WorkingDir = System.IO.Path.GetDirectoryName(abs) };
    }

    /// <summary>True when picking <paramref name="target"/> (a vanilla/steam launch) should confirm
    /// first because the game's required launcher is in force.</summary>
    public bool NeedsVanillaConfirm(LaunchTarget target)
        => _ctx is not null && LaunchGuard.NeedsVanillaConfirm(_ctx.Game, AnyModsEnabled, target);

    /// <summary>Surface the needs-launcher hint when the required launcher is set but not found.</summary>
    public void NotifyLauncherMissing()
    {
        CoopLauncherMissing = true;
        StatusText = "Required launcher not found — install it next to the game to play with mods.";
    }

    [RelayCommand]
    private void Launch()
    {
        if (_ctx is null) return;
        // Enforcement: with a required launcher and mods enabled, the launcher IS the default Play.
        if (LaunchGuard.RequiresLauncher(_ctx.Game, AnyModsEnabled))
        {
            var launcher = RequiredLauncherTarget();
            if (launcher is null) { NotifyLauncherMissing(); return; } // never launch a non-existent exe
            LaunchTargetExplicit(launcher);
            return;
        }
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
            // CF name-search over the installed mods. Nexus md5 identification can't run here: Nexus
            // matches the published-archive md5, and installed mods are already extracted (the archive
            // is gone) — Nexus identifies at DROP time instead (Md5IdentifyArchivesAsync on intake).
            // Vortex-deployed mods ARE identifiable here: their deployment manifest records the Nexus
            // modId, so we can fetch by id without needing the original archive.
            var r = await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge);
            var vtx = 0;
            if (_nexus.IsConnected)
            {
                try { vtx = (await Scanner.IdentifyVortexNexusAsync(_ctx, _nexus.Client!)).Matched; }
                catch { /* best-effort; CF result still stands */ }
            }
            await ReloadModsAsync();
            StatusText = r.GameId is null
                ? (vtx > 0 ? $"Filled {vtx} Vortex mod(s) from Nexus." : "Couldn't resolve this game on CurseForge.")
                : $"Matched {r.Matched} of {r.Total} on CurseForge" + (vtx > 0 ? $", +{vtx} from Vortex/Nexus." : ".");
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    // ---------- Nexus connection ----------

    public bool NexusConnected => _nexus.IsConnected;
    public string? NexusUser => _nexus.ConnectedUser;
    public bool NexusPremium => _nexus.ConnectedPremium;

    /// <summary>The connected account line, with a Premium/Free tag — null when not connected.</summary>
    public string? NexusAccountLine =>
        !_nexus.IsConnected ? null : $"{_nexus.ConnectedUser}{(_nexus.ConnectedPremium ? " (Premium)" : " (Free)")}";

    /// <summary>Re-validate the stored key to refresh the account name + premium flag (offline-safe).</summary>
    public Task RefreshNexusAsync() => _nexus.RefreshAsync();

    /// <summary>Backfill metadata for already-installed mods by md5-matching the user's downloaded
    /// Nexus ARCHIVES (the only thing with the hash Nexus indexes). Each archive's match fills the
    /// metadata for every installed mod that came from it.</summary>
    public async Task BackfillNexusAsync(IReadOnlyList<string> archives)
    {
        if (_ctx is null) return;
        if (!_nexus.IsConnected) { StatusText = "Connect Nexus first (toolbar -> Nexus)."; return; }
        if (string.IsNullOrWhiteSpace(_ctx.Game.NexusGameDomain)) { StatusText = "This game has no Nexus domain set."; return; }
        if (archives.Count == 0) { StatusText = "No .zip/.7z/.rar archives found in that folder."; return; }
        IsBusy = true;
        try
        {
            var n = (await Scanner.Md5IdentifyArchivesAsync(_ctx, _nexus.Client!, archives)).Matched;
            StatusText = n > 0
                ? $"Backfilled {n} mod{(n == 1 ? "" : "s")} from {archives.Count} Nexus archive(s)."
                : $"Scanned {archives.Count} archive(s) — no Nexus matches (must be the ORIGINAL Nexus archives for this game).";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Validate + store a pasted personal Nexus key (the user's own — never baked). Result via StatusText.</summary>
    public async Task<bool> ConnectNexusAsync(string apiKey)
    {
        try
        {
            var user = await _nexus.ConnectAsync(apiKey);
            StatusText = user is null
                ? "Nexus key rejected — check it on your account's API access page."
                : $"Connected to Nexus as {NexusAccountLine}.";
            return user is not null;
        }
        catch (Exception e) { StatusText = "Nexus connect failed: " + e.Message; return false; }
    }

    public void DisconnectNexus()
    {
        _nexus.Disconnect();
        StatusText = "Disconnected from Nexus.";
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
            // Direct-inject: plan the drop, confirm any collisions (replace keeps the old version,
            // revertible), then execute into the game's exe folder. Re-detect so a newly-installed
            // launcher (Seamless / Mod Engine 2) surfaces its Play button immediately — no manual
            // re-scan. "Just install them" made literal.
            IsBusy = true;
            try
            {
                var plan = _direct.Plan(_ctx.Game, paths);
                var chosen = await ConfirmReplacementsAsync(plan);
                if (chosen is null) { StatusText = "Update cancelled."; return; }
                var r = _direct.Execute(_ctx.Game, plan, chosen);
                if (r.Added.Count > 0 || r.Updated.Count > 0) _svc.Redetect(_ctx.Game.Id); // pick up mod folders + launchers
                await ReloadModsAsync();                                                    // rebuilds context: refreshed list + Play targets
                StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
                    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : ".");
            }
            catch (Exception e) { StatusText = e.Message; }
            finally { IsBusy = false; }
            return;
        }
        IsBusy = true;
        try
        {
            var plan = Scanner.PlanIntake(paths, _ctx);
            var chosen = await ConfirmReplacementsAsync(plan);
            if (chosen is null) { StatusText = "Update cancelled."; return; }
            var r = Scanner.ExecuteIntake(plan, chosen, _ctx);
            var identified = 0;
            var nexusIdentified = 0;
            if (r.Added.Count > 0)
            {
                // Exact match first — CF fingerprint, then Nexus md5 (catches Nexus-only / repacked
                // files the CF fingerprint misses) — then a name-search fallback. Exact wins over fuzzy.
                try { identified = (await Scanner.FingerprintIdentifyAsync(_ctx, _svc.CurseForge, r.Added)).Matched; }
                catch { /* best-effort; intake already succeeded */ }
                // Nexus matches the published-ARCHIVE md5 — hash the dropped zip(s), not the extracted files.
                try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, _nexus.Client!, paths)).Matched; }
                catch { /* best-effort; a Nexus miss / outage never fails intake */ }
                try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); }
                catch { /* best-effort */ }
            }
            StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
                + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
                + (identified > 0 ? $", identified {identified} on CurseForge" : "")
                + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "");
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Show the collision prompt for a plan and return the rel-paths to replace; null means
    /// the user cancelled. No collisions → replace nothing (adds-only); no view wired → same.</summary>
    private async Task<ISet<string>?> ConfirmReplacementsAsync(IntakePlan plan)
    {
        if (plan.Collisions.Count == 0) return new HashSet<string>();
        if (ConfirmReplacements is null) return new HashSet<string>();
        return await ConfirmReplacements(plan);
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

    // ---------- config cockpit ----------

    public sealed record CockpitConfigFile(string FileName, string Path, IReadOnlyList<ConfigEntry> Entries);

    public (IReadOnlyList<CockpitConfigFile> Configs, IReadOnlyList<LuaKeyBind> Keybinds, IReadOnlyList<LuaConsoleCommand> Commands)
        BuildCockpit(string modFolderAbs)
    {
        var configs = ModConfig.Discover(modFolderAbs)
            .Select(p => new CockpitConfigFile(System.IO.Path.GetFileName(p), p, ModConfig.ReadFile(p)))
            .ToList();
        var (binds, cmds) = LuaScan.ScanFolder(modFolderAbs);
        return (configs, binds, cmds);
    }

    /// <summary>Remap a Lua-hardcoded keybind: back up the source .lua, rewrite the one key token,
    /// write atomically. No-op (with a status note) if the rewrite finds no confident match.</summary>
    public async Task RemapKeyBindAsync(LuaKeyBind bind, string newKey)
    {
        if (_ctx is null || string.IsNullOrEmpty(bind.SourceFile) || string.IsNullOrWhiteSpace(newKey)) return;
        try
        {
            var lua = System.IO.File.ReadAllText(bind.SourceFile);
            var updated = LuaScan.RemapKeyBind(lua, bind.Key, bind.Modifiers, newKey.Trim());
            if (updated == lua) { StatusText = $"Couldn't find {bind.Key} to remap (left unchanged)."; return; }
            await Scanner.WriteModConfigAsync(bind.SourceFile, updated, _ctx); // reuse: backup-to-data-dir + atomic
            StatusText = $"Remapped {bind.Key} -> {newKey.Trim().ToUpperInvariant()}. Restart the mod/UE4SS to apply.";
        }
        catch (Exception e) { StatusText = $"Couldn't remap {bind.Key}: {e.Message}"; }
    }

    public async Task SaveConfigValueAsync(string configPath, string? section, string key, string value)
    {
        try
        {
            var content = System.IO.File.ReadAllText(configPath);
            var updated = ModConfig.SetValue(content, section, key, value);
            await Scanner.WriteModConfigAsync(configPath, updated, _ctx!);
            StatusText = $"Saved {key} in {System.IO.Path.GetFileName(configPath)}.";
        }
        catch (Exception e) { StatusText = $"Couldn't save {key}: {e.Message}"; }
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
