using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ModManager.App.Frameworks;
using ModManager.App.Services;
using ModManager.App.Tools;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.Tools;

namespace ModManager.App.ViewModels;

public sealed record GameOption(string Id, string Name)
{
    // Local Steam cover-art path, resolved once at load; Cover builds the image on the UI thread when
    // the switcher renders it. Mirrors SteamAddRow.Cover — null degrades to the placeholder swatch.
    public string? CoverPath { get; init; }

    public ImageSource? Cover => string.IsNullOrEmpty(CoverPath)
        ? null
        : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(CoverPath));
}

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
    private readonly AvatarService _avatars;
    private readonly SteamService _steam;
    private readonly AppSettingsService _appSettings;
    private readonly NexusUpdatePoll _nexusPoll;
    // Dispatcher captured at VM construction (UI thread, because DI builds the VM during the
    // MainWindow ctor). Used to marshal cross-thread notifications — e.g. tool Process.Exited,
    // which fires on a thread-pool thread — back to the UI thread before touching VM state.
    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private GameContext? _ctx;
    private bool _suppressActiveSwitch;

    // Per-family last-active variant memory. Keyed by Mod.BaseTitle (the variant family's shared
    // name). Survives mod-list rebuilds so an off-then-on flip of the family switch restores the
    // variant the user had selected, not the first one. In-memory only - rebuilds reset to "first
    // variant" if the app restarts; persistence is a separate concern.
    private readonly Dictionary<string, string> _familyLastActive = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shows the collision prompt and returns the rel-paths to replace, or null if cancelled. The
    /// view wires this (the dialog + XamlRoot live in the code-behind, not the VM). When unset,
    /// intake replaces nothing — new files still install, collisions are left untouched.
    /// </summary>
    public Func<IntakePlan, Task<ISet<string>?>>? ConfirmReplacements { get; set; }

    /// <summary>
    /// Shows the ban-risk acknowledgment for a high-risk game and returns (proceed, dontWarnAgain).
    /// The view wires this (the dialog + XamlRoot live in the code-behind, not the VM). When unset
    /// the gate proceeds — the Core decision (<see cref="BanRiskRules.ShouldGateEnable"/>) only asks
    /// for it on a high-risk, un-acked game, so an unwired delegate degrades to no extra friction.
    /// </summary>
    public Func<string, Task<(bool proceed, bool dontWarnAgain)>>? ConfirmBanRiskEnable { get; set; }

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

    /// <summary>Framework dependencies the active game is missing — surfaced as a status banner.
    /// Refreshed at every <see cref="ReloadModsAsync"/>. Empty = nothing missing (banner hidden).</summary>
    public ObservableCollection<FrameworkDep> MissingFrameworks { get; } = new();

    /// <summary>Bound to the banner's Visibility — true when at least one framework is missing.</summary>
    public bool HasMissingFrameworks => MissingFrameworks.Count > 0;

    /// <summary>One-line summary for the banner ("Missing: UE4SS"). Multiple frameworks comma-joined.</summary>
    public string MissingFrameworksSummary => MissingFrameworks.Count == 0
        ? ""
        : "Missing: " + string.Join(", ", MissingFrameworks.Select(d => d.Name));

    /// <summary>Tools installed for the active game. Refreshed at every <see cref="ReloadModsAsync"/>.</summary>
    public ObservableCollection<ToolEntry> Tools { get; } = new();

    /// <summary>Catalog entries that apply to the active game but aren't installed. Surfaced as
    /// "Get it here" chips on the tools row.</summary>
    public ObservableCollection<KnownTool> MissingTools { get; } = new();

    /// <summary>Frameworks installed for the active game (UE4SS, ELM, ...) read from the per-game
    /// framework registry, each wrapped with its editable-config state. Surfaced as buttons next to
    /// Tools; the name shows a live "how to use" toast, the pencil edits the framework's settings INI.
    /// Refreshed every <see cref="ReloadModsAsync"/>.</summary>
    public ObservableCollection<FrameworkRowViewModel> FrameworkRows { get; } = new();

    public bool HasInstalledFrameworks => FrameworkRows.Count > 0;

    /// <summary>Active-game locations that are Vortex/MO2-owned and NOT yet taken over — drives the
    /// "Some folders are managed by Vortex" banner. Recomputed each ReloadModsAsync.</summary>
    public ObservableCollection<string> OwnedLocations { get; } = new();

    /// <summary>Active-game locations we took over but where a marker REAPPEARED (Vortex re-deployed).</summary>
    public ObservableCollection<string> ReDeployedLocations { get; } = new();

    public bool HasOwnedLocations => OwnedLocations.Count > 0;
    public bool HasReDeployedLocations => ReDeployedLocations.Count > 0;

    public Visibility OwnedBannerVisibility => HasOwnedLocations ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReDeployedBannerVisibility => HasReDeployedLocations ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Live "how to use" for an installed framework, read from its on-disk settings. The view
    /// calls this on a framework-button click and renders the lines in a toast.</summary>
    public static FrameworkUsageInfo FrameworkUsageFor(FrameworkInstallManifest m)
        => FrameworkUsage.Describe(m.FrameworkId, m.InstallPath);

    public bool HasTools => Tools.Count > 0;
    public bool HasMissingTools => MissingTools.Count > 0;
    public Visibility ToolsRowVisible => _ctx is not null ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Empty-state hint visibility for the tools row — collapsed when there's at least
    /// one installed tool or a "Get …" catalog chip showing.</summary>
    public Visibility ToolsEmptyHintVisibility => HasTools || HasMissingTools
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchHintVisibility))]
    private bool launchNeedsAttention;

    public Visibility LaunchHintVisibility => LaunchNeedsAttention ? Visibility.Visible : Visibility.Collapsed;

    // Steam updated this game since we last recorded its build — installed mods may need rechecking.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamBuildWarningVisibility))]
    private bool steamBuildChanged;

    [ObservableProperty] private string steamBuildMessage = "";

    // The live build to re-baseline to when the user dismisses the warning.
    private string? _pendingSteamBuild;

    public Visibility SteamBuildWarningVisibility => SteamBuildChanged ? Visibility.Visible : Visibility.Collapsed;

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

    public MainViewModel(LauncherService svc, ModEngineService me2, DirectInjectService direct, ThemeService themes, LudusaviService ludu, NexusService nexus, AvatarService avatars, SteamService steam, AppSettingsService appSettings, NexusUpdatePoll nexusPoll)
    {
        _svc = svc;
        _me2 = me2;
        _direct = direct;
        _themes = themes;
        _ludu = ludu;
        _nexus = nexus;
        _avatars = avatars;
        _steam = steam;
        _appSettings = appSettings;
        _nexusPoll = nexusPoll;
        ThemeOptions = themes.Themes;
        SelectedTheme = themes.Default; // applies the default theme via OnSelectedThemeChanged
    }

    // Segmented Loadout control: the selected segment tints with the theme accent; the others stay
    // transparent so the surrounding Border background shows through. Twin foregrounds keep contrast.
    // Inactive segments return the resource-backed ThemeInk brush directly so theme switches
    // propagate via the in-place color mutation in ThemeService.Set (no extra notify needed for
    // inactive). The active foreground is currently Black - readable on the default cyan accent;
    // a future text_on_accent theme slot would let this re-theme correctly on arbitrary accents.
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public Brush LoadoutAllBrush => SegmentBrushFor("all");
    public Brush LoadoutMpBrush  => SegmentBrushFor("mp");
    public Brush LoadoutSpBrush  => SegmentBrushFor("sp");
    public Brush LoadoutAllForeground => SegmentForegroundFor("all");
    public Brush LoadoutMpForeground  => SegmentForegroundFor("mp");
    public Brush LoadoutSpForeground  => SegmentForegroundFor("sp");

    private Brush SegmentBrushFor(string mode)
        => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
            ? (Application.Current.Resources["ThemeAccent"] as Brush ?? new SolidColorBrush(Colors.MediumPurple))
            : TransparentBrush;

    private Brush SegmentForegroundFor(string mode)
        => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Colors.Black)
            : (Application.Current.Resources["ThemeInk"] as Brush ?? new SolidColorBrush(Colors.White));

    partial void OnActiveModeChanged(string value)
    {
        NotifyLoadoutBrushes();
    }

    partial void OnSelectedThemeChanged(Theme? value)
    {
        if (value is not null) _themes.Apply(value);
        // Inactive-segment foreground uses the resource-backed ThemeInk brush, so its color tracks
        // the theme via ThemeService.Set's in-place mutation. The ACTIVE segment's brush is
        // ThemeAccent (also resource-backed) - same story. We still re-notify so any caller that
        // wraps the brush (binding helpers, etc.) sees a fresh reference, and so the active-mode
        // tint re-paints immediately on theme switch.
        NotifyLoadoutBrushes();
        OnPropertyChanged(nameof(SelectedThemeName));
    }

    /// <summary>The active theme's display name (or a placeholder when none is selected). Drives the
    /// THEME DropDownButton's content label.</summary>
    public string SelectedThemeName => SelectedTheme?.Name ?? "Theme";

    private void NotifyLoadoutBrushes()
    {
        OnPropertyChanged(nameof(LoadoutAllBrush));
        OnPropertyChanged(nameof(LoadoutMpBrush));
        OnPropertyChanged(nameof(LoadoutSpBrush));
        OnPropertyChanged(nameof(LoadoutAllForeground));
        OnPropertyChanged(nameof(LoadoutMpForeground));
        OnPropertyChanged(nameof(LoadoutSpForeground));
    }

    /// <summary>Refresh the theme list after an import and select (apply) the new one.</summary>
    public void OnThemeImported(Theme imported)
    {
        ThemeOptions = _themes.Themes;
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == imported.Id) ?? imported;
    }

    /// <summary>URI for the title-bar Image. Returns the user avatar if set; otherwise the bundled
    /// icon. Notified when the avatar changes (so the title bar swaps live without restart).</summary>
    public string AppIconSource => _avatars.HasAvatar
        ? new Uri(_avatars.AvatarPngPath).AbsoluteUri
        : "ms-appx:///Assets/icon.ico";

    public void NotifyAppIconChanged() => OnPropertyChanged(nameof(AppIconSource));

    /// <summary>Reload the theme list (a new derived theme may have just been imported), preserving
    /// the active selection where possible.</summary>
    public void RefreshThemes()
    {
        _themes.Reload();
        ThemeOptions = _themes.Themes;
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == SelectedTheme?.Id) ?? _themes.Default;
    }

    // LoadAsync rebuilds the games dropdown (Games.Clear + repopulate) and is NON-atomic — it awaits
    // mid-rebuild (ReloadModsAsync). Restore fires it TWICE: once from the RegistryChanged event the
    // orchestrator raises via its mid-operation Reload, and once explicitly after restore completes.
    // Two overlapping calls interleave their Clear/repopulate on the UI-bound Games collection and can
    // leave the dropdown with a partial set (live-smoke 2026-05-30: a restore showed only the active
    // game). Serialize: if a load is already in flight, flag one more pass and return; the running loop
    // re-runs after its await, reading the latest registry. The final pass always wins, clean.
    private bool _loading;
    private bool _loadPending;

    public async Task LoadAsync()
    {
        if (_loading) { _loadPending = true; return; }
        _loading = true;
        try
        {
            do
            {
                _loadPending = false;
                var reg = _svc.LoadRegistry();
                _suppressActiveSwitch = true;
                Games.Clear();
                var store = App.AppHost.Services.GetRequiredService<IStoreLibrary>();
                foreach (var g in reg.Games)
                    Games.Add(new GameOption(g.Id, g.GameName)
                    {
                        CoverPath = string.IsNullOrEmpty(g.SteamAppId) ? null : store.ResolveCoverArtPath(g.SteamAppId),
                    });
                var active = Registry.GetActiveGame(reg);
                ActiveGame = active is null ? null : Games.FirstOrDefault(x => x.Id == active.Id);
                _suppressActiveSwitch = false;
                await ReloadModsAsync();
            } while (_loadPending);
        }
        finally { _loading = false; }
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
            StatusText = "No game registered. Add one with + Game.";
            MissingFrameworks.Clear();
            OnPropertyChanged(nameof(HasMissingFrameworks));
            OnPropertyChanged(nameof(MissingFrameworksSummary));
            Tools.Clear();
            MissingTools.Clear();
            FrameworkRows.Clear();
            OwnedLocations.Clear();
            ReDeployedLocations.Clear();
            SteamBuildChanged = false; // collapse the build-update banner when no game is active
            OnPropertyChanged(nameof(HasTools));
            OnPropertyChanged(nameof(HasMissingTools));
            OnPropertyChanged(nameof(ToolsRowVisible));
            OnPropertyChanged(nameof(ToolsEmptyHintVisibility));
            OnPropertyChanged(nameof(HasInstalledFrameworks));
            OnPropertyChanged(nameof(HasOwnedLocations));
            OnPropertyChanged(nameof(HasReDeployedLocations));
            OnPropertyChanged(nameof(OwnedBannerVisibility));
            OnPropertyChanged(nameof(ReDeployedBannerVisibility));
            return;
        }
        IsBusy = true;
        try
        {
            // Three worlds: Mod Engine 2 games read their mods from the config; FromSoft games
            // without ME2 are direct-inject (loose files next to the exe) — toggled by name, never
            // deleted; everything else is a filesystem scan via the proven Scanner pipeline.
            var directInject = DirectInjectBacked;
            // Scanner-world only: migrate the data dir, then list, then persist the auto-seeded
            // classification — exactly the two writes the old scanner branch did. The shared
            // read-only resolver (used by the agent-access MCP too) performs neither.
            if (!ConfigBacked && !directInject)
                await Scanner.MigrateDataDirAsync(_ctx);
            // One read-only listing path shared with the MCP: dispatch by engine (ME2 / direct-inject /
            // scanner) + merge metadata.json. See ModManager.Core.ModListing.Resolve. The metadata
            // merge is load-bearing: without it, Nexus / CurseForge entries written by
            // Md5IdentifyArchivesAsync / RefreshMetadataByNameAsync never reach the displayed fromsoft rows.
            IReadOnlyList<Mod> list = ModListing.Resolve(_ctx.Game);
            if (!ConfigBacked && !directInject)
                Scanner.PersistClassification(_ctx, list);

            // Direct-inject mods can be toggled (reversible move) but not uninstalled here.
            // Order rows so variant-family members (same mod page / _Nx base) sit together, and mark
            // the members of a multi-variant family so the row shows a VARIANT chip. Toggles stay
            // per-row (the user enables as many as they want; disabling holds, never re-downloads).
            var mpOverrides = MpCompatStore.Load(_ctx.DataDir);
            // Refresh missing-framework state BEFORE building rows — the per-row chip reads from
            // MissingFrameworks at row-construction time. The notify pings further down keep the
            // banner binding fresh; this just lifts the source of truth to where rows see it.
            MissingFrameworks.Clear();
            foreach (var dep in FrameworkDeps.CheckPresent(_ctx))
                MissingFrameworks.Add(dep);
            // Load direct-inject mod config-path overrides once. The resolver consults these to
            // pick a user-chosen path over the catalog default when set. Empty overrides for the
            // common case (no per-user customization) — no disk hit if file missing.
            var directInjectOverrides = ModManager.Core.Catalog.DirectInjectConfigOverrides.Load(_ctx.DataDir);
            // Per-game metadata, loaded once for the loop. The endorse heart needs the Nexus mod id, which
            // lives on the persisted ModMeta (not the in-memory Mod), so each row resolves it from here via
            // the same deterministic resolver the endorse write uses — keeping the displayed key and the
            // written key in lockstep.
            var metaByKey = Scanner.LoadMetadata(_ctx);
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
                // .ini files for the row's pencil icon. Two branches:
                //   - Direct-inject rows (Location == "direct-inject"): no folderAbs to glob;
                //     pull from KnownDirectInjectMod.Catalog.ConfigPaths via the resolver, with
                //     per-user overrides applied. Resolver returns only paths that exist on disk
                //     so the icon stays hidden when the catalog default isn't installed.
                //   - Folder-tracked rows: existing recursive *.ini glob, capped at 20 so a
                //     pathological folder doesn't stall reload.
                IReadOnlyList<string> iniFiles = Array.Empty<string>();
                if (rep.Location == "direct-inject")
                {
                    iniFiles = ModManager.Core.Catalog.DirectInjectModConfigResolver
                        .Resolve(rep.Name, _ctx.GameRoot, directInjectOverrides);
                }
                else if (!string.IsNullOrEmpty(folderAbs) && Directory.Exists(folderAbs))
                {
                    try
                    {
                        iniFiles = Directory.EnumerateFiles(folderAbs, "*.ini", SearchOption.AllDirectories)
                            .Take(20)
                            .ToArray();
                    }
                    catch { /* leave empty on enumerate failure */ }
                }
                // ModId is a stable slug from the family display name — same row across reloads
                // gets the same INI-history bucket. Falls back to the mod's Name when DisplayName
                // would slug to empty (e.g. all-symbol titles).
                var displayName = !string.IsNullOrEmpty(rep.DisplayName) ? rep.DisplayName : rep.Name;
                var modId = Slugify(displayName);
                if (string.IsNullOrEmpty(modId)) modId = Slugify(rep.Name);
                var options = fam.IsMulti
                    ? (IReadOnlyList<VariantOptionVM>)fam.Members
                        .Select(m => new VariantOptionVM(
                            m.Name,
                            string.IsNullOrEmpty(m.Variant) ? m.Name : m.Variant!.ToUpperInvariant(),
                            m.Enabled,
                            !m.ReadOnly || m.Loader is "ue4ss" or "bepinex"))
                        .ToList()
                    : System.Array.Empty<VariantOptionVM>();
                // Row-level missing-framework chip. FromSoft has two candidates and each row only
                // needs ONE of them: folder mods need Mod Engine 2, direct-inject mods need Elden
                // Mod Loader. Critically there's NO cross-fallback — if a direct-inject row's ELM
                // is satisfied, we don't show "NEEDS Mod Engine 2" instead (direct-inject mods
                // don't load through ME2). Single-framework engines (UE4SS / BepInEx / SMAPI /
                // Forge-Fabric) just show whatever's first in MissingFrameworks.
                FrameworkDep? primaryMissing;
                if (_ctx.Game.Engine == "fromsoft")
                {
                    primaryMissing = rep.IsFolder
                        ? MissingFrameworks.FirstOrDefault(d => d.Name == "Mod Engine 2")
                        : MissingFrameworks.FirstOrDefault(d => d.Name == "Elden Mod Loader");
                }
                else
                {
                    primaryMissing = MissingFrameworks.FirstOrDefault();
                    // UE4SS is needed only by Lua/script mods + Blueprint LogicMods paks — not plain
                    // content paks (Witchfire, and ~mods/paks-root content mods generally). Drop the
                    // chip for a row that doesn't need it so we stop falsely flagging content paks.
                    if (primaryMissing?.Name == "UE4SS")
                    {
                        var locPath = Scanner.LocByName(rep.Location, _ctx!).Abs;
                        if (!FrameworkApplicability.ModNeedsUe4ss(rep, locPath))
                            primaryMissing = null;
                    }
                }
                // A direct-inject mod that brings its own proxy (Seamless ships ersc.dll, ReShade
                // ships its own) doesn't truly need Elden Mod Loader — soften the hint from red
                // "NEEDS" to amber "MAY NEED" so we don't drive an unnecessary loader install.
                var selfProvidesProxy = primaryMissing?.Name == "Elden Mod Loader"
                    && ModManager.Core.Catalog.KnownDirectInjectMod.Catalog.Any(
                        k => k.SelfProvidesProxy && (k.DisplayName == rep.Name || k.DisplayName == rep.Base));
                rows.Add(new ModRowViewModel(rep, canToggle: rep.IsLoader || !rep.ReadOnly || rep.Loader is "ue4ss" or "bepinex", canUninstall: !directInject && !rep.ReadOnly)
                {
                    ReadmeFilePath = Scanner.ReadmePathFor(rep.Name, _ctx!),
                    MpOverride = mpOverrides.TryGetValue(rep.Name, out var o) ? o : null,
                    ModFolderAbs = folderAbs,
                    IniFiles = iniFiles,
                    ModId = modId,
                    VariantOptions = options,
                    MissingFrameworkName = primaryMissing?.Name ?? "",
                    MissingFrameworkUrl = primaryMissing?.GetUrl,
                    MissingFrameworkNote = primaryMissing?.Note ?? "",
                    LoaderHintIsSoft = selfProvidesProxy,
                    // The endorse heart needs a resolved Nexus mod id (the write key) AND a live
                    // connection — both captured at row build, fresh every rescan, no per-row notify.
                    NexusModId = metaByKey.TryGetValue(rep.Name, out var repMeta)
                        ? NexusRefresh.ResolveModId(repMeta)
                        : null,
                    NexusConnected = _nexus.IsConnected,
                });
            }
            OrderAndStampSections(rows);
            NotifyMpWarning();
            GameRootText = _ctx.GameRoot;
            // LaunchOptions.NeedsAttention fires on Steam App ID alone — it doesn't know what's
            // installed. For Elden Ring, the only recommended option is the anti-cheat OFF swap,
            // which only matters for users WITHOUT Seamless Co-op. When Seamless is fully wired
            // (mod files + launcher both present), the user doesn't need the vanilla anti-cheat
            // toggle — Seamless brings its own bypass. Suppress the toolbar warning then.
            LaunchNeedsAttention = LaunchOptions.NeedsAttention(_ctx.Game.SteamAppId)
                && !_direct.SeamlessFullyInstalled(_ctx.Game);
            CoopLauncherMissing = _direct.SeamlessNeedsLauncher(_ctx.Game);

            // Build-id watch: warn when Steam updated this game since we last recorded its build. First sight
            // records the baseline silently; the pure comparator decides. _steam.InstalledGames() is a local
            // Steam scan (no network) and matches the active game by app id.
            var liveBuild = InstalledGameMatch.ByAppId(_steam.InstalledGames(), _ctx.Game.SteamAppId)?.BuildId;
            switch (SteamBuildCheck.Evaluate(_ctx.Game.LastKnownSteamBuildId, liveBuild))
            {
                case SteamBuildStatus.NoBaseline:
                    _svc.SetSteamBuildBaseline(_ctx.Game.Id, liveBuild);
                    _ctx.Game.LastKnownSteamBuildId = liveBuild;
                    SteamBuildChanged = false;
                    break;
                case SteamBuildStatus.Updated:
                    _pendingSteamBuild = liveBuild;
                    SteamBuildMessage = $"Steam updated {_ctx.Game.GameName} since you last modded it — your installed mods may need rechecking.";
                    SteamBuildChanged = true;
                    break;
                default: // Unchanged / Unknown
                    SteamBuildChanged = false;
                    break;
            }
            if (directInject)
                // Direct-inject IS a complete setup, not a missing-feature state. The earlier copy
                // read as "you don't have Mod Engine 2 (you should)" — which is wrong; for a
                // Seamless Co-op / EML stack, ME2 actively conflicts. Name what's running so the
                // user knows they're fine, and present ME2 as one path among others, not the goal.
                StatusText = list.Count > 0
                    ? $"Detected {list.Count} mod{(list.Count == 1 ? "" : "s")} — toggle to enable/disable. Loose-file install, no Mod Engine 2 needed."
                    : "No mods yet — drop a mod archive to install, or set up Mod Engine 2 for folder-based mods.";
            else UpdateStatus();
            // MissingFrameworks was refreshed above the row loop (the per-row chip reads from it);
            // these notifies keep the banner bindings in lockstep with the new collection contents.
            OnPropertyChanged(nameof(HasMissingFrameworks));
            OnPropertyChanged(nameof(MissingFrameworksSummary));

            // Refresh tools collection from the per-game registry. Malformed tools.json doesn't fail
            // the reload — leave the list empty and let the user fix or replace the file.
            Tools.Clear();
            try
            {
                foreach (var t in ToolRegistry.Load(_ctx.DataDir).Tools) Tools.Add(t);
            }
            catch (InvalidDataException) { /* malformed tools.json — leave empty */ }

            // Derive missing-tools: catalog entries that apply to this game but aren't installed yet.
            MissingTools.Clear();
            var installedIds = new HashSet<string>(Tools.Select(t => t.ToolId));
            foreach (var known in ToolCatalog.Catalog)
            {
                if (known.Engine != _ctx.Game.Engine) continue;
                if (known.SteamAppId != _ctx.Game.SteamAppId) continue;
                if (installedIds.Contains(known.ToolId)) continue;
                MissingTools.Add(known);
            }

            // Refresh installed frameworks from the per-game registry — surfaced as "how to use"
            // buttons next to Tools. Unreadable manifests are skipped by FrameworkRegistry.List.
            FrameworkRows.Clear();
            foreach (var fw in FrameworkRegistry.List(_ctx.DataDir)) FrameworkRows.Add(new FrameworkRowViewModel(fw));

            // Vortex/MO2 ownership posture per active-game location — drives the "managed by Vortex"
            // banner. Normalize with Path.GetFullPath so the taken-over membership check matches how
            // the Scanner/Core side stores the set (else a taken-over folder silently reads as owned).
            OwnedLocations.Clear();
            ReDeployedLocations.Clear();
            foreach (var loc in _ctx.Locations)
            {
                var res = ToolOwnership.Resolve(System.IO.Path.GetFullPath(loc.Abs), _ctx.TakenOver);
                if (res.State == OwnershipState.Owned) OwnedLocations.Add(loc.Abs);
                else if (res.State == OwnershipState.ReDeployed) ReDeployedLocations.Add(loc.Abs);
            }
            OnPropertyChanged(nameof(HasOwnedLocations));
            OnPropertyChanged(nameof(HasReDeployedLocations));
            OnPropertyChanged(nameof(OwnedBannerVisibility));
            OnPropertyChanged(nameof(ReDeployedBannerVisibility));

            OnPropertyChanged(nameof(HasTools));
            OnPropertyChanged(nameof(HasMissingTools));
            OnPropertyChanged(nameof(ToolsRowVisible));
            OnPropertyChanged(nameof(ToolsEmptyHintVisibility));
            OnPropertyChanged(nameof(HasInstalledFrameworks));
            // Toggling a mod (especially Seamless) may change which target the Launch button fires.
            // Re-publish the computed properties so the toolbar label tracks state without a manual
            // refresh. Fires after every Toggle / game switch / Redetect that lands in ReloadModsAsync.
            OnPropertyChanged(nameof(EffectiveLaunchTarget));
            OnPropertyChanged(nameof(LaunchButtonLabel));
            OnPropertyChanged(nameof(CurrentLaunchMode));
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }

        // Debounced Nexus auto-check (once per 24h per game, off the UI hot path). Fire-and-forget:
        // it polls Nexus by mod id for the active game, flags newer versions, and persists — then we
        // reload rows to surface UPDATE chips only if it actually changed something. Self-limiting via
        // the per-game stamp, so the per-toggle re-entry of ReloadModsAsync costs a stamp read + bail.
        // Every failure is swallowed inside MaybePollAsync — it can never break the session.
        if (_ctx is { } ctx) _ = AutoCheckNexusUpdatesAsync(ctx);
    }

    /// <summary>Fire-and-forget debounced Nexus auto-check launched at the tail of a game load. Runs on
    /// the thread-pool (off the UI hot path); if it persisted any newer-version data, it marshals a row
    /// reload back onto the UI thread — but only when the polled game is still the active one (the user
    /// may have switched games while the network call was in flight).</summary>
    private async Task AutoCheckNexusUpdatesAsync(GameContext ctx)
    {
        var changed = await _nexusPoll.MaybePollAsync(ctx, _nexus, _appSettings);
        if (!changed) return;

        void Reload()
        {
            // Don't clobber a different game the user switched to mid-poll.
            if (_ctx is null || !ReferenceEquals(_ctx, ctx)) return;
            _ = ReloadModsAsync();
        }

        if (_dispatcherQueue is { } dq) dq.TryEnqueue(Reload);
        else Reload();
    }

    private void UpdateStatus() => StatusText = $"{Mods.Count(m => m.Enabled)} of {Mods.Count} enabled";

    /// <summary>Suffix for the post-drop status line when the active game has a missing framework.
    /// Empty string when nothing's missing. The drop status line gets ". Heads up: this mod needs X
    /// — get it at &lt;url&gt;." appended so the user sees the gap the moment they drop.</summary>
    private string MissingFrameworkDropSuffix()
    {
        if (MissingFrameworks.Count == 0) return "";
        var dep = MissingFrameworks[0];
        // Trim the URL to a host-ish form so the status line stays readable. The persistent chip
        // carries the full clickable link; this is the just-dropped callout.
        var host = "";
        try { host = new Uri(dep.GetUrl).Host; } catch { host = dep.GetUrl; }
        return $". Heads up: this mod needs {dep.Name} — get it at {host}.";
    }

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
        // Mark the first row carrying a SectionHeader as the legend host. Only one ? button per render.
        var firstSection = ordered.FirstOrDefault(m => !string.IsNullOrEmpty(m.SectionHeader));
        if (firstSection is not null) firstSection.IsFirstSectionHeader = true;
        Mods = new ObservableCollection<ModRowViewModel>(ordered);
    }

    /// <summary>The single ban-risk enable gate every enable path consults. Resolves the active
    /// game's risk LIVE by Steam app id (so a feed raising risk protects an already-added game) and
    /// whether it's been acknowledged, then defers the policy to <see cref="BanRiskRules.ShouldGateEnable"/>.
    /// Returns true to proceed with the enable, false to abort (caller reverts the visual). On a
    /// high-risk, un-acked game it warns and waits for an explicit ack — it never auto-enables and
    /// never refuses (disabling is always one click away). Non-gated games proceed silently.</summary>
    private async Task<bool> GateBanRiskEnableAsync()
    {
        if (_ctx is null) return false;
        var level = BanRiskCatalog.ByAppId(_ctx.Game.SteamAppId);
        var acked = BanRiskAckStore.IsAcked(_ctx.DataDir, _ctx.Game.Id);
        if (!BanRiskRules.ShouldGateEnable(level, acked)) return true;
        if (ConfirmBanRiskEnable is null) return true; // unwired -> no extra friction (Core decision still owns policy)
        var (proceed, dontWarn) = await ConfirmBanRiskEnable(_ctx.Game.GameName);
        if (!proceed) return false;
        if (dontWarn) BanRiskAckStore.Ack(_ctx.DataDir, _ctx.Game.Id);
        return true;
    }

    /// <summary>Toggle one mod. The reversible disable/enable lives in Scanner; on failure the
    /// switch reverts and the error surfaces (never a silent half-disable).</summary>
    public async Task ToggleAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        // Ban-risk gate: only when this toggle is turning a row ON. Disabling is never gated
        // (getting safer needs no friction). On cancel, revert the visual exactly like the catch.
        if (row.Enabled && !await GateBanRiskEnableAsync()) { row.Enabled = false; return; }
        // A manual toggle leaves "clean vanilla" — clear the stash so CurrentMode reverts to Modded and
        // the launch button stops claiming "Play vanilla" while a mod is live again.
        VanillaStashStore.Clear(_ctx.DataDir);
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
        // Ban-risk gate before enabling a variant (the view already reflects the desired state; on
        // cancel we abort without touching files — the chip rebuild on the next reload corrects it).
        if (enable && !await GateBanRiskEnableAsync()) { await ReloadModsAsync(); return; }
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

    /// <summary>Toggle a variant family on or off. ON re-enables the LAST-active variant (remembered
    /// across rescans via <see cref="_familyLastActive"/>) or the first if none recorded. OFF disables
    /// every currently-enabled variant after recording which one was active. The variant CHIPS pick
    /// which variant is active when the family is on; this switch picks whether the family is on.</summary>
    public async Task ToggleFamilyAsync(ModRowViewModel row, bool on)
    {
        if (_ctx is null || !row.HasVariantOptions) return;
        // Ban-risk gate before turning a variant family ON (the family switch reflects the desired
        // state; on cancel we reload so the switch rebuilds from actual state — nothing enabled).
        if (on && !await GateBanRiskEnableAsync()) { await ReloadModsAsync(); return; }
        var familyKey = string.IsNullOrEmpty(row.Mod.BaseTitle) ? row.DisplayName : row.Mod.BaseTitle!;
        try
        {
            if (on)
            {
                if (row.VariantOptions.Any(v => v.Enabled)) return; // already on - no-op
                var target = _familyLastActive.TryGetValue(familyKey, out var remembered) ? remembered : null;
                target ??= row.VariantOptions.FirstOrDefault()?.ModName;
                if (target is null) return;
                await Scanner.SetLoaderModEnabledAsync(target, true, _ctx);
            }
            else
            {
                // Remember the active variant first so an off-then-on flip restores the user's choice.
                var active = row.VariantOptions.FirstOrDefault(v => v.Enabled);
                if (active is not null) _familyLastActive[familyKey] = active.ModName;
                foreach (var v in row.VariantOptions.Where(v => v.Enabled).ToList())
                    await Scanner.SetLoaderModEnabledAsync(v.ModName, false, _ctx);
            }
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Permanently uninstall every variant in a family. Gated by a confirm dialog in the
    /// view that names the count. Also clears the family's last-active memory so a future variant
    /// add doesn't auto-enable into a stale slot.</summary>
    public async Task UninstallFamilyAsync(ModRowViewModel row)
    {
        if (_ctx is null || !row.HasVariantOptions) return;
        IsBusy = true;
        try
        {
            var familyKey = string.IsNullOrEmpty(row.Mod.BaseTitle) ? row.DisplayName : row.Mod.BaseTitle!;
            foreach (var opt in row.VariantOptions.ToList())
            {
                if (ConfigBacked) _me2.Remove(_ctx.Game, opt.ModName);
                else await Scanner.UninstallModAsync(opt.ModName, _ctx);
            }
            _familyLastActive.Remove(familyKey);
            StatusText = $"Uninstalled {row.DisplayName} and {row.VariantOptions.Count} variant{(row.VariantOptions.Count == 1 ? "" : "s")}.";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task AllOn() => SetAllAsync(true);

    [RelayCommand]
    private Task AllOff() => SetAllAsync(false);

    private async Task SetAllAsync(bool on)
    {
        // Ban-risk gate ONCE before a bulk enable (never per-row, so no row can bypass it). A bulk
        // disable (on == false) is never gated — getting safer needs no friction.
        if (on && _ctx is not null && !await GateBanRiskEnableAsync()) return;
        await BulkAsync(() =>
        {
            // A bulk enable/disable is a manual state change too — clear the vanilla stash so the mode
            // reverts to Modded (mirrors the single ToggleAsync clear). BulkAsync already null-checked _ctx.
            VanillaStashStore.Clear(_ctx!.DataDir);
            if (ConfigBacked) { _me2.SetAll(_ctx!.Game, on); return Task.CompletedTask; }
            if (DirectInjectBacked)
            {
                foreach (var m in Mods.Where(m => m.Enabled != on)) _direct.SetEnabled(_ctx!.Game, m.Mod.Name, on);
                return Task.CompletedTask;
            }
            return Scanner.SetAllModsAsync(on, _ctx!);
        });
    }

    [RelayCommand]
    private async Task SetMode(string mode)
    {
        // No MP/SP split for Mod Engine 2 or direct-inject mods — the mode buttons are a no-op there.
        if (ConfigBacked || DirectInjectBacked) { ActiveMode = mode; return; }
        // Applying a mode enables the mods that match it — gate ONCE before the bulk apply. On cancel,
        // abort without changing the active mode (nothing was enabled).
        if (_ctx is not null && !await GateBanRiskEnableAsync()) return;
        ActiveMode = mode;
        await BulkAsync(() => Scanner.ApplyModeAsync(mode, _ctx!));
    }

    [RelayCommand]
    private Task Refresh() => ReloadModsAsync();

    [RelayCommand]
    private void DismissBuildWarning()
    {
        if (_ctx?.Game is null) return;
        _svc.SetSteamBuildBaseline(_ctx.Game.Id, _pendingSteamBuild);
        _ctx.Game.LastKnownSteamBuildId = _pendingSteamBuild;   // keep in-memory baseline in sync
        SteamBuildChanged = false;
    }

    /// <summary>Public reload hook for dialogs that change mod state (e.g. loading a profile).</summary>
    public Task RefreshAsync() => ReloadModsAsync();

    /// <summary>Public accessor for the active game's data dir — used by Tools dialogs to find
    /// <c>tools.json</c>. Returns an empty string when no game is bound (caller short-circuits).</summary>
    public string GameDataDirPublic() => _ctx?.DataDir ?? "";

    /// <summary>The active game context (null when no game). The App uses this for the on-block
    /// takeover dialog (to resolve a row's folder ownership). Read-only passthrough.</summary>
    public GameContext? ActiveContextPublic => _ctx;

    /// <summary>Take over one Vortex-owned folder, then rescan so its rows flip to managed.</summary>
    public async Task TakeOverFolderAsync(string folderAbs)
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = VortexTakeover.TakeOver(_ctx.DataDir, _ctx.GameRoot, folderAbs);
            StatusText = r.Success
                ? $"Took over {System.IO.Path.GetFileName(folderAbs.TrimEnd('\\', '/'))} — you manage it here now."
                : $"Couldn't take over the folder: {r.Error}";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Take over every Vortex-owned (or re-deployed) location for the ACTIVE game.</summary>
    public async Task TakeOverGameAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var targets = OwnedLocations.Concat(ReDeployedLocations).Distinct().ToList();
            var results = VortexTakeover.TakeOverGame(_ctx.DataDir, _ctx.GameRoot, targets);
            var ok = results.Count(x => x.Success);
            StatusText = $"Took over {ok} folder{(ok == 1 ? "" : "s")} for {_ctx.Game.GameName} — you manage them here now.";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

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
        foreach (var r in ordered) { r.InLoadOrder = true; r.IsFirstSectionHeader = false; }
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

    /// <summary>The launch target the primary Launch button will fire — state-aware. With Seamless
    /// Co-op fully installed on a FromSoft game, the Seamless launcher IS the modded launch path
    /// (its own bypass + private multiplayer), so default to it. Otherwise fall back to the registry's
    /// IsDefault target. The dropdown still exposes every target — this only picks the primary.</summary>
    public LaunchTarget? EffectiveLaunchTarget
    {
        get
        {
            if (_ctx is null) return null;
            if (_direct.SeamlessFullyInstalled(_ctx.Game))
            {
                var seamless = _ctx.Game.LaunchTargets.FirstOrDefault(t =>
                    string.Equals(t.Kind, "exe", StringComparison.OrdinalIgnoreCase)
                    && (t.Target ?? "").Contains("ersc_launcher", StringComparison.OrdinalIgnoreCase));
                if (seamless is not null) return seamless;
            }
            return LauncherService.DefaultTarget(_ctx.Game);
        }
    }

    /// <summary>The Launch button's label. Leads with the MODE (vanilla vs modded) so the word always
    /// means what it says, then appends the mechanism in parens (Steam, or the alt-launcher's name like
    /// Seamless Co-op / Mod Engine 2). The target's own free-text Label is NOT used directly — a game
    /// definition can carry a legacy "Play vanilla (Steam)" target label that would otherwise make a
    /// MODDED launch read "vanilla". Mode is the source of truth; the target only supplies the how.</summary>
    public string LaunchButtonLabel
    {
        get
        {
            var t = EffectiveLaunchTarget;
            var how = LaunchMechanismLabel(t);   // "Steam" | "<launcher>.exe name" | ""
            if (CurrentLaunchMode == LaunchMode.Vanilla)
                return string.IsNullOrEmpty(how) ? "▶ Play vanilla" : $"▶ Play vanilla ({how})";
            return string.IsNullOrEmpty(how) ? "▶ Play (modded)" : $"▶ Play modded ({how})";
        }
    }

    /// <summary>The launch MECHANISM for a target, mode-agnostic: "Steam" for a steam:// target, else
    /// the alt-launcher's display name (Seamless Co-op / Mod Engine 2) when the target label names one,
    /// else the exe's file name. Never returns the legacy "vanilla" wording — that's a MODE, set above.</summary>
    private static string LaunchMechanismLabel(LaunchTarget? t)
    {
        if (t is null) return "";
        if (string.Equals(t.Kind, "steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
        // exe target — prefer a recognizable launcher name from the label, else the exe file name.
        var label = t.Label ?? "";
        if (label.Contains("Seamless", StringComparison.OrdinalIgnoreCase)) return "Seamless Co-op";
        if (label.Contains("Mod Engine", StringComparison.OrdinalIgnoreCase)) return "Mod Engine 2";
        try { return System.IO.Path.GetFileName(t.Target); } catch { return ""; }
    }

    /// <summary>Dropdown wording for a per-target item: "Launch via Steam" / "Launch via Seamless Co-op".
    /// The per-target list is the MECHANISM picker (which way to start) — vanilla/modded is the separate
    /// top item — so these never echo a target's legacy mode-named label ("Play vanilla (Steam)").</summary>
    public string LaunchTargetMenuLabel(LaunchTarget t)
    {
        var how = LaunchMechanismLabel(t);
        return string.IsNullOrEmpty(how) ? (string.IsNullOrEmpty(t.Label) ? "Launch" : t.Label) : $"Launch via {how}";
    }

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

    /// <summary>True when picking <paramref name="target"/> (a vanilla/steam launch) should step aside
    /// first because enabled direct-inject DLLs would load into it and crash the vanilla start.</summary>
    public bool NeedsDirectInjectStepAside(LaunchTarget target)
        => _ctx is not null && LaunchGuard.NeedsDirectInjectStepAside(target, _direct.AnyActiveProxyDll(_ctx.Game));

    /// <summary>The launch mode read from on-disk state (a vanilla-stash means we stepped aside).</summary>
    public LaunchMode CurrentLaunchMode => _ctx is null ? LaunchMode.Modded : VanillaLaunch.CurrentMode(_ctx.DataDir);

    /// <summary>Build the real reversible-mechanism ops from the App services for the active game.</summary>
    private VanillaLaunchOps BuildVanillaOps()
    {
        var ctx = _ctx!;
        return new VanillaLaunchOps
        {
            // A variant FAMILY collapses several mods (FasterShips10 / _B / aaUltraFastShips) onto one
            // row; the active variant lives in the option chips, NOT the row's representative Mod. Read
            // the enabled variant members by their REAL name so we step aside the file that's actually
            // loading — using the representative's name would miss the active variant's .pak entirely.
            ActiveModRows = () => Mods.SelectMany(m => m.HasVariantOptions
                    ? m.VariantOptions.Where(v => v.Enabled && v.CanToggle)
                        .Select(v => new StashedModRow { Name = v.ModName, Location = m.Mod.Location })
                    : (m.Enabled && !m.Mod.ReadOnly)
                        ? new[] { new StashedModRow { Name = m.Mod.Name, Location = m.Mod.Location } }
                        : Enumerable.Empty<StashedModRow>())
                .ToList(),
            ActiveFrameworks = () => FrameworkRegistry.List(ctx.DataDir)
                .Where(f => !FrameworkRegistry.IsDisabled(ctx.DataDir, f.FrameworkId))
                .Select(f => f.FrameworkId).ToList(),
            ActiveDirectInjectProxies = () => _direct.ActiveProxyDlls(ctx.Game),
            DisableModRow = (name, _) => Scanner.DisableModAsync(name, ctx),
            EnableModRow = (name, _) => Scanner.EnableModAsync(name, ctx),
            DisableFramework = id => FrameworkRegistry.Disable(ctx.DataDir, id),
            EnableFramework = id => FrameworkRegistry.Enable(ctx.DataDir, id),
            DisableDirectInjectProxy = p => _direct.DisableProxy(ctx.Game, p),
            EnableDirectInjectProxy = p => _direct.EnableProxy(ctx.Game, p),
        };
    }

    /// <summary>Surface the needs-launcher hint when the required launcher is set but not found.</summary>
    public void NotifyLauncherMissing()
    {
        CoopLauncherMissing = true;
        StatusText = "Required launcher not found — install it next to the game to play with mods.";
    }

    [RelayCommand]
    private async Task Launch()
    {
        if (_ctx is null) return;
        // Enforcement: with a required launcher and mods enabled, the launcher IS the default Play.
        if (LaunchGuard.RequiresLauncher(_ctx.Game, AnyModsEnabled))
        {
            var launcher = RequiredLauncherTarget();
            if (launcher is null) { NotifyLauncherMissing(); return; } // never launch a non-existent exe
            await LaunchTargetExplicit(launcher);
            return;
        }
        // Use the state-aware effective target (e.g. Seamless when fully installed) so the primary
        // Launch matches what the button label promised. Fall back to the legacy LauncherService.Launch
        // path for games with no registered targets at all (steam:// / LaunchExe).
        var target = EffectiveLaunchTarget;
        if (target is not null) { await LaunchTargetExplicit(target); return; }
        AutoBackupBeforeLaunch();
        try { if (!_svc.Launch(_ctx.Game)) StatusText = "No launch target configured for this game."; }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Run a specific launch target (primary Launch button + dropdown both route here).
    /// Auto-backs up the save, then — for a Steam-DRM exe launcher with Steam closed — starts Steam
    /// and waits before launching, so the launch doesn't silently no-op (the DRM bootstrap needs the
    /// Steam client up). A steam:// target self-starts Steam, so it's not gated.</summary>
    public async Task LaunchTargetExplicit(LaunchTarget target)
    {
        if (_ctx is null) return;
        // Steam awareness: an exe launcher (Seamless's ersc_launcher.exe) on a Steam-DRM game
        // silently no-ops if Steam is closed. Auto-start Steam and wait (off the UI thread), or
        // surface a clear message instead of a dead click.
        if (LaunchGuard.NeedsSteamRunning(_ctx.Game, target) && !_steam.IsRunning())
        {
            StatusText = "Starting Steam…";
            var up = await Task.Run(() => _steam.EnsureRunning(TimeSpan.FromSeconds(20)));
            if (!up) { StatusText = "Couldn't start Steam — open Steam, then launch again."; return; }
        }
        AutoBackupBeforeLaunch();
        try { _svc.Launch(target, _ctx.Game.GameRoot); }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Play vanilla: step every active loader aside (reversible), refresh rows, then launch clean.</summary>
    public async Task StepAsideAndLaunchAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = await VanillaLaunch.StepAsideAsync(_ctx.DataDir, BuildVanillaOps());
            if (!r.Success) { StatusText = $"Couldn't switch to vanilla: {r.Error}"; return; }
            await ReloadModsAsync();
            StatusText = "Vanilla mode — mods stepped aside. Launching…";
            var target = EffectiveLaunchTarget;
            if (target is not null) await LaunchTargetExplicit(target);
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Play modded: restore exactly the stashed set, refresh rows, then launch with mods.</summary>
    public async Task RestoreAndLaunchAsync()
    {
        if (_ctx is null) return;
        IsBusy = true;
        try
        {
            var r = await VanillaLaunch.RestoreAsync(_ctx.DataDir, BuildVanillaOps());
            if (!r.Success) { StatusText = $"Couldn't restore mods: {r.Error}"; return; }
            await ReloadModsAsync();
            StatusText = "Modded mode — mods restored. Launching…";
            var target = EffectiveLaunchTarget;
            if (target is not null) await LaunchTargetExplicit(target);
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
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
                ? "Switched to ONLINE mode (anti-cheat on) — official multiplayer OK, file-based mods blocked."
                : "Switched to OFFLINE mode (anti-cheat off) — Play loads mods. Seamless Co-op online still works.";
        }
        catch (Exception e) { StatusText = e.Message; }
        return AntiCheatStateOf(opt);
    }

    /// <summary>Run an internal launch option (the app starts the real exe directly).</summary>
    public async Task RunInternalOption(LaunchOption opt)
    {
        if (_ctx is null || opt.Exe is null) return;
        var root = _ctx.Game.GameRoot;
        var target = new LaunchTarget(opt.Title, "exe", System.IO.Path.Combine(root, opt.Exe))
        {
            Args = opt.Args,
            WorkingDir = opt.WorkingSubdir is null ? root : System.IO.Path.Combine(root, opt.WorkingSubdir),
        };
        await LaunchTargetExplicit(target);
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

    /// <summary>Status dot for the Nexus toolbar button — accent-green when connected, danger-red
    /// when disconnected. The dot IS the affordance now (no separate ACCOUNT section label), so the
    /// state has to read at a glance. Resource-backed brushes so theme switches propagate via
    /// ThemeService.Set's in-place color mutation.</summary>
    public Brush NexusStatusBrush => NexusConnected
        ? ((Brush)Application.Current.Resources["ThemeAccent"])
        : ((Brush)Application.Current.Resources["ThemeDanger"]);

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
        if (string.IsNullOrWhiteSpace(NexusDomains.Effective(_ctx.Game))) { StatusText = "This game has no Nexus domain set."; return; }
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

    /// <summary>Manual "Refresh Nexus stats": poll Nexus <em>by mod id</em> (no archive needed) over
    /// every identified mod in the active game — refreshing endorsements / downloads / availability and
    /// capturing the upstream current version (which drives the UPDATE chip). The installed version is
    /// preserved (it's the "what you have" side of the compare). Throttled + 429-aware via
    /// <see cref="NexusRefresh.RefreshAllAsync"/>: a rate limit stops the sweep and reports partial
    /// progress instead of thrashing. The id-resolution is deterministic, so refreshed metas are mapped
    /// back to their on-disk keys by re-resolving the id, then persisted in one atomic batch.</summary>
    public async Task RefreshNexusStatsAsync()
    {
        if (_ctx is null) return;
        if (!_nexus.IsConnected) { StatusText = "Connect Nexus first (toolbar -> Nexus)."; return; }
        var domain = NexusDomains.Effective(_ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) { StatusText = "This game has no Nexus domain set."; return; }

        // key -> meta for the rows we can resolve a Nexus id for. RefreshAllAsync skips the rest with
        // no network call; we map results back to keys by re-resolving the (deterministic) id below.
        var byKey = Scanner.LoadMetadata(_ctx);
        var identified = byKey.Where(kv => NexusRefresh.ResolveModId(kv.Value) is not null).ToList();
        if (identified.Count == 0) { StatusText = "No Nexus-identified mods to refresh — backfill metadata first."; return; }

        IsBusy = true;
        try
        {
            // Small inter-call delay, well under the burst ceiling; RefreshAllAsync applies it between
            // (not before) calls so a one-item sweep pays nothing.
            var result = await NexusRefresh.RefreshAllAsync(
                identified.Select(kv => kv.Value), domain!, _nexus.Client!,
                throttle: () => System.Threading.Tasks.Task.Delay(120));

            if (result.Updated.Count > 0)
            {
                // Re-resolve each refreshed meta's id back to its on-disk key (id-resolution is
                // deterministic and identity fields survive the refresh, so the lookup is exact).
                var keyById = new Dictionary<int, string>();
                foreach (var kv in identified)
                    if (NexusRefresh.ResolveModId(kv.Value) is { } id)
                        keyById[id] = kv.Key;

                var writes = new List<(string, ModMeta)>();
                foreach (var meta in result.Updated)
                    if (NexusRefresh.ResolveModId(meta) is { } id && keyById.TryGetValue(id, out var key))
                        writes.Add((key, meta));

                Scanner.WriteManyMeta(_ctx, writes);
                await ReloadModsAsync();
            }

            StatusText = result.RateLimited
                ? "Nexus rate limit reached — try again later."
                : $"Refreshed {result.Refreshed} mod{(result.Refreshed == 1 ? "" : "s")}, {result.UpdatesAvailable} update{(result.UpdatesAvailable == 1 ? "" : "s")} available.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>One-click endorse ⇄ abstain for a Nexus-identified row — the give-back half of the
    /// Nexus loop, honors-the-builders and never automatic (one user click per write). Picks the
    /// direction from the row's current <see cref="Mod.Endorsed"/> (endorsed → abstain, else endorse),
    /// version-stamps the POST with the installed version (falling back to the upstream latest), and
    /// only flips the heart + persists when Nexus accepts the write — a refusal (not downloaded / too
    /// soon / any other precondition) shows the API's friendly reason in the status line and leaves the
    /// row honest (no optimistic flip). A 429 degrades to a rate-limit line; nothing throws to the UI.</summary>
    public async Task ToggleEndorseAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        if (!_nexus.IsConnected) { StatusText = "Connect Nexus first (toolbar -> Nexus)."; return; }
        var domain = NexusDomains.Effective(_ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) { StatusText = "This game has no Nexus domain set."; return; }

        // The endorse key is the Nexus mod id. The in-memory Mod doesn't carry it (it lives in the
        // ModMeta entry), so resolve it off the row's metadata via the same deterministic resolver the
        // refresh sweep uses — and reuse that meta instance for the write so all other enrichment is
        // preserved.
        var meta = Scanner.LoadMetadata(_ctx).TryGetValue(row.Mod.Name, out var existing) ? existing : null;
        if (meta is null || NexusRefresh.ResolveModId(meta) is not int modId)
        {
            StatusText = "This mod isn't identified on Nexus yet.";
            return;
        }

        var action = row.Mod.Endorsed == true ? EndorseAction.Abstain : EndorseAction.Endorse;
        var version = row.Mod.Version ?? row.Mod.NexusLatestVersion ?? "";
        var name = row.DisplayName;

        IsBusy = true;
        try
        {
            var outcome = await _nexus.Client!.EndorseAsync(domain!, modId, version, action);
            if (outcome.Refused)
            {
                // The row stays honest — no flip on a precondition refusal; the API tells the user why.
                StatusText = outcome.Message ?? "Nexus declined the endorsement.";
                return;
            }

            // Persist Endorsed onto the existing metadata entry (mutate-in-place) so the rest of the
            // mod's enrichment — title, credit, NexusModId — survives the write. Endorsed is persisted
            // user intent, so it must outlive a rescan.
            row.Mod.Endorsed = action == EndorseAction.Endorse;
            meta.Endorsed = row.Mod.Endorsed;
            Scanner.WriteOneMeta(_ctx, row.Mod.Name, meta);
            row.NotifyEndorseChanged();

            StatusText = action == EndorseAction.Endorse
                ? $"Endorsed \"{name}\" on Nexus."
                : $"Retracted endorsement for \"{name}\".";
        }
        catch (NexusRateLimitException)
        {
            StatusText = "Nexus rate limit reached — try again later.";
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Manual-match escape hatch: user pastes a Nexus or CurseForge URL for a row whose
    /// auto-identify didn't land. Parse → fetch metadata from the named provider → write it against
    /// this mod's key with IsManual=true so future rescans can't clobber it. Result via StatusText.</summary>
    public async Task<bool> ManualMatchAsync(ModRowViewModel row, string url)
    {
        if (_ctx is null) return false;
        var parts = ModSiteUrl.Parse(url);
        if (parts is null)
        {
            StatusText = "That doesn't look like a Nexus or CurseForge mod URL.";
            return false;
        }

        try
        {
            ModMeta? hit = null;
            switch (parts.Provider)
            {
                case ModSiteProvider.Nexus:
                    if (!_nexus.IsConnected)
                    {
                        StatusText = "Connect Nexus first (Settings → Nexus Mods).";
                        return false;
                    }
                    hit = await _nexus.Client!.GetModAsync(parts.GameKey, int.Parse(parts.ModRef));
                    break;

                case ModSiteProvider.CurseForge:
                    // The CF client needs a numeric gameId. Use the active game's registered CF id.
                    // If the game has no CurseforgeGameId yet, the user has to set it in Add Game /
                    // Settings first — we don't try to resolve a slug → gameId without the registry hint.
                    if (_ctx.Game.CurseforgeGameId is not int gameId)
                    {
                        StatusText = "This game has no CurseForge ID registered — set it in the game's registry first.";
                        return false;
                    }
                    hit = await Scanner.LookupCurseForgeSlugAsync(_svc.CurseForge, gameId, parts.ModRef);
                    break;
            }

            if (hit is null)
            {
                StatusText = $"Couldn't find that mod on {parts.Provider}.";
                return false;
            }
            hit.IsManual = true;
            Scanner.WriteOneMeta(_ctx, row.Mod.Name, hit);
            await ReloadModsAsync();
            StatusText = $"Matched \"{row.DisplayName}\" to {hit.Title ?? "the pasted URL"}.";
            return true;
        }
        catch (Exception e) { StatusText = e.Message; return false; }
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
            OnPropertyChanged(nameof(NexusConnected));
            OnPropertyChanged(nameof(NexusStatusBrush));
            return user is not null;
        }
        catch (Exception e) { StatusText = "Nexus connect failed: " + e.Message; return false; }
    }

    public void DisconnectNexus()
    {
        _nexus.Disconnect();
        StatusText = "Disconnected from Nexus.";
        OnPropertyChanged(nameof(NexusConnected));
        OnPropertyChanged(nameof(NexusStatusBrush));
    }

    /// <summary>Intake dropped/picked paths, then attach metadata (fingerprint, then name-search fallback).</summary>
    public async Task AddModsAsync(IReadOnlyList<string> paths)
    {
        if (_ctx is null || paths.Count == 0) return;

        // Pre-check 0 (engine-agnostic): framework intake. KnownFramework.Classify scopes by
        // engine + SteamAppId internally, so this is a no-op for games whose engine doesn't
        // ship any catalog-recognized framework. Catalog match -> confirmation dialog -> install
        // via FrameworkInstaller (game root, with backup snapshot). Looks-like-framework ->
        // feedback nudge then fall through to the engine-specific intake (or cancel).
        var frameworkOutcome = await TryInstallFrameworksAsync(paths);
        paths = frameworkOutcome.Remaining;
        if (paths.Count == 0)
        {
            // Everything dropped was a framework (or got cancelled). Surface results + return.
            if (frameworkOutcome.StatusParts.Count > 0)
                StatusText = string.Join(". ", frameworkOutcome.StatusParts) + ".";
            if (frameworkOutcome.AnyInstalled) await ReloadModsAsync();
            return;
        }

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

                // Identify what just got installed — same chain the regular intake branch uses. Direct-inject
                // mods are named from DirectInject.Catalog (e.g. "Seamless Co-op"); Md5IdentifyArchivesAsync's
                // fromsoft branch maps the archive's md5 → Nexus → those catalog names. Best-effort: a Nexus
                // miss / outage / unreachable CF proxy never breaks the install that already succeeded.
                var identified = 0;
                var nexusIdentified = 0;
                if (r.Added.Count > 0 || r.Updated.Count > 0)
                {
                    try { identified = (await Scanner.FingerprintIdentifyAsync(_ctx, _svc.CurseForge, r.Added.Concat(r.Updated))).Matched; }
                    catch { }
                    try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, _nexus.Client!, paths)).Matched; }
                    catch { }
                    try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); }
                    catch { }
                    if (identified > 0 || nexusIdentified > 0) await ReloadModsAsync();
                }

                StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
                    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : ".")
                    + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
                    + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "")
                    + MissingFrameworkDropSuffix();
            }
            catch (Exception e) { StatusText = e.Message; }
            finally { IsBusy = false; }
            return;
        }
        IsBusy = true;
        try
        {
            // Pre-check 1: save/world-mod drops. Routes detected zips to SaveModFlow and carves
            // them out so the regular intake doesn't try to classify their non-pak contents.
            var remaining = paths.ToList();
            var savedCount = 0;
            var saveSkipReasons = new List<string>();
            if (!string.IsNullOrEmpty(_ctx.SaveDir))
            {
                var saveTypeExts = GameProfiles.Resolve(_ctx.Game.Engine, _ctx.Game.SteamAppId)
                    .SaveTypes.Select(t => t.Extension).ToList();
                var verdicts = SaveModFlow.TryHandleDrops(
                    remaining, saveTypeExts,
                    saveProfilesDir: _ctx.SaveDir!,
                    snapshotsDir: _ctx.SavesDir,
                    dataDir: _ctx.DataDir,
                    saveModPath: _ctx.Game.SaveModPath,
                    forbidden: _ctx.Game.SaveModForbidden);
                foreach (var v in verdicts)
                {
                    if (v.Outcome == SaveModDropOutcome.Installed) { savedCount++; remaining.Remove(v.SourcePath); }
                    else if (v.Outcome == SaveModDropOutcome.Failed)
                    { saveSkipReasons.Add($"{Path.GetFileName(v.SourcePath)}: {v.Reason}"); remaining.Remove(v.SourcePath); }
                }
            }

            // Pre-check 2: UE4SS Lua-mod drops. When the launcher OWNS the UE4SS install (it's in the
            // framework registry), install the mod into ue4ss\Mods — validate-then-extract, reversible,
            // re-rooting a version-wrapped archive. When UE4SS isn't ours (e.g. Vortex owns it, or it
            // isn't installed), fall back to clear guidance instead of writing into a folder we don't own.
            // Either way the matched archives are carved out so regular intake doesn't skip every Lua entry.
            var luaInstalled = new List<string>();    // installed into ue4ss\Mods (we own UE4SS)
            var luaInstalledSources = new List<(string ArchivePath, string ModName)>(); // for post-install metadata identify
            var luaNeedsManual = new List<string>();   // detected but not ours to install
            var luaFailures = new List<string>();
            var archiveReader = new SharpCompressArchiveReader();
            var ownedUe4ss = FrameworkRegistry.List(_ctx.DataDir)
                .FirstOrDefault(m => string.Equals(m.FrameworkId, "ue4ss", StringComparison.OrdinalIgnoreCase));
            var ue4ssModsDir = ownedUe4ss is null ? null : Path.Combine(ownedUe4ss.InstallPath, "ue4ss", "Mods");
            remaining = remaining.Where(p =>
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return true;
                var lower = p.ToLowerInvariant();
                if (!Intake.ArchiveExtensions.Any(a => lower.EndsWith(a))) return true;
                try
                {
                    using (var arch = archiveReader.Open(p))
                        if (!Ue4ssLuaDetect.Detect(arch.EntryNames).IsLuaMod) return true;  // not a Lua mod — leave for intake

                    if (ue4ssModsDir is not null)
                    {
                        try
                        {
                            var res = Ue4ssLuaInstaller.Install(p, ue4ssModsDir, archiveReader);
                            luaInstalled.Add(res.ModName);
                            // Remember the source archive so we can md5-identify metadata for it after the
                            // loop (the sync Where-lambda can't await). The archive is still on disk here.
                            luaInstalledSources.Add((p, res.ModName));
                        }
                        catch (Exception ex) { luaFailures.Add($"{Path.GetFileName(p)}: {ex.Message}"); }
                    }
                    else
                    {
                        using var arch = archiveReader.Open(p);
                        var v = Ue4ssLuaDetect.Detect(arch.EntryNames);
                        luaNeedsManual.Add(v.ModFolderName ?? Path.GetFileNameWithoutExtension(p));
                    }
                    return false; // carved out of regular intake
                }
                catch { return true; }
            }).ToList();

            // Identify metadata for each just-installed Lua mod by md5-matching its source archive against
            // Nexus, bound under the mod-folder key the row uses. Lua mods are carved out before the regular
            // intake's identify pass (which is pak-keyed), so this is where they get their title/author/links
            // — no manual backfill needed. Best-effort: a miss or no-Nexus connection just leaves the row bare.
            if (luaInstalledSources.Count > 0 && _nexus.IsConnected)
                foreach (var (src, modName) in luaInstalledSources)
                {
                    try { await Ue4ssLuaInstaller.IdentifyMetadataAsync(_ctx, _nexus.Client!, src, modName); }
                    catch { /* best-effort; install already succeeded */ }
                }

            // Pre-check 3: tool drops. ToolDetector.Classify routes recognized utility archives
            // (e.g. WSE save editor) through ToolIntake — extracted under <DataDir>/tools/<id>/ and
            // registered in tools.json. Mod-shape archives short-circuit back to Mod and stay in
            // `remaining`; tool installs are carved out so PlanIntake doesn't classify their .exe /
            // .ps1 contents as mods. The Tools collection itself lands in Task 8.
            var installedTools = new List<ToolEntry>();
            var ambiguousRunnables = new Dictionary<string, IReadOnlyList<string>>();
            var toolFailures = new List<string>();
            remaining = remaining.Where(p =>
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return true;
                var lower = p.ToLowerInvariant();
                if (!Intake.ArchiveExtensions.Any(a => lower.EndsWith(a))) return true;
                try
                {
                    var (cls, known) = ToolDetector.Classify(p, _ctx!.Game.Engine ?? "", _ctx.Game.SteamAppId ?? "");
                    if (cls != ToolClassification.Tool) return true;
                    var result = ToolIntake.Install(p, _ctx.DataDir, known);
                    installedTools.Add(result.Entry);
                    if (result.Candidates.Count > 0)
                        ambiguousRunnables[result.Entry.ToolId] = result.Candidates;
                    return false; // carved out — don't run through mod intake
                }
                catch (Exception ex)
                {
                    toolFailures.Add($"{Path.GetFileName(p)}: {ex.Message}");
                    return false; // tool install failed; don't fall back to mod intake for an exe-only zip
                }
            }).ToList();

            var plan = Scanner.PlanIntake(remaining, _ctx);
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
                try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, _nexus.Client!, remaining)).Matched; }
                catch { /* best-effort; a Nexus miss / outage never fails intake */ }
                try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); }
                catch { /* best-effort */ }
            }

            // Assemble a single status line that surfaces every outcome - save-mod installs first,
            // any save-mod failures with their reasons, UE4SS Lua detections second, tool installs
            // third, then the regular intake's add/update/skip counts.
            var statusParts = new List<string>();
            if (savedCount > 0) statusParts.Add($"Installed {savedCount} save-mod world{(savedCount == 1 ? "" : "s")}");
            foreach (var reason in saveSkipReasons) statusParts.Add(reason);
            if (luaInstalled.Count > 0)
                statusParts.Add($"Installed {string.Join(", ", luaInstalled)} into UE4SS Mods");
            foreach (var fail in luaFailures) statusParts.Add($"UE4SS Lua install failed: {fail}");
            if (luaNeedsManual.Count > 0)
                statusParts.Add($"{string.Join(", ", luaNeedsManual)} {(luaNeedsManual.Count == 1 ? "is a" : "are")} UE4SS Lua mod{(luaNeedsManual.Count == 1 ? "" : "s")} — install UE4SS first, or drop into ue4ss\\Mods yourself");
            foreach (var t in installedTools)
                statusParts.Add($"Installed {t.DisplayName} as a tool for {_ctx.Game.GameName}");
            foreach (var fail in toolFailures) statusParts.Add($"Tool install failed: {fail}");
            statusParts.Add($"updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}");
            StatusText = string.Join(". ", statusParts)
                + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
                + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
                + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "")
                + MissingFrameworkDropSuffix();
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Launch a registered tool. When the tool edits saves, take an explicit (non-auto)
    /// snapshot FIRST — a snapshot failure (no save folder, disk full, etc.) blocks the launch so
    /// we never run an editor against unprotected saves. The status line surfaces the snapshot
    /// label on exit so the user can find it in Saves → Snapshots if they need to revert.</summary>
    public async Task LaunchToolAsync(ToolEntry entry)
    {
        try
        {
            ToolLauncher.Launch(
                entry,
                snapshot: entry.EditsSaves ? () => SnapshotSavesForTool(entry) : null,
                onExit: snapLabel =>
                {
                    // Process.Exited fires on a thread-pool thread — direct property writes here
                    // would crash the UI. Marshal back to the dispatcher captured at VM ctor.
                    void Update()
                    {
                        StatusText = snapLabel is null
                            ? $"{entry.DisplayName} closed."
                            : $"{entry.DisplayName} closed. Snapshot saved as '{snapLabel}'.";
                    }
                    if (_dispatcherQueue is not null) _dispatcherQueue.TryEnqueue(Update);
                    else Update();
                });

            StatusText = entry.EditsSaves
                ? $"Snapshotting save before launching {entry.DisplayName}…"
                : $"Launching {entry.DisplayName}…";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't launch {entry.DisplayName}: {ex.Message}";
        }
        await Task.CompletedTask;
    }

    /// <summary>Open a file picker for a tool archive and route it through the regular drop pipeline.
    /// ToolDetector.Classify carves tool archives out of the mod intake path automatically, so the
    /// same <see cref="AddModsAsync"/> entry-point handles tool installs just like a drag-drop.</summary>
    public async Task PromptAddToolAsync()
    {
        var window = App.MainWindow;
        if (window is null)
        {
            StatusText = "Couldn't open the picker — main window not ready yet.";
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".7z");
        picker.FileTypeFilter.Add(".rar");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await AddModsAsync(new[] { file.Path });
        }
    }

    /// <summary>Snapshot the active save folder before a save-editing tool starts. Uses the same
    /// SaveManager primitive as the Saves dialog (non-auto label so the user can find it), labelled
    /// with the tool's display name + a wall-clock stamp.</summary>
    private string SnapshotSavesForTool(ToolEntry tool)
    {
        if (_ctx is null) throw new InvalidOperationException("No active game.");
        if (string.IsNullOrEmpty(_ctx.SaveDir))
            throw new InvalidOperationException("No save folder configured — set one in Saves first.");
        var label = $"before-{tool.DisplayName.Replace(' ', '-')}-{DateTime.Now:yyyy-MM-dd-HHmm}";
        var snap = SaveManager.Backup(_ctx.SaveDir, _ctx.SavesDir, label, auto: false);
        return snap.Label;
    }

    /// <summary>What <see cref="TryInstallFrameworksAsync"/> returns: the list of paths NOT
    /// consumed by framework intake (caller's existing branches handle these), the status-line
    /// snippets to surface, and whether anything was actually installed (so the caller can
    /// trigger a reload).</summary>
    private sealed record FrameworkPrecheckOutcome(
        IReadOnlyList<string> Remaining,
        IReadOnlyList<string> StatusParts,
        bool AnyInstalled);

    /// <summary>
    /// Drop-pipeline Pre-check 0: detect + install catalog-known frameworks before the
    /// engine-specific intake. For each dropped archive: peek its entries, run KnownFramework
    /// .Classify, show the confirmation dialog on a hit, the unrecognized-nudge on
    /// looks-like-framework, otherwise leave it for the caller's branches.
    /// </summary>
    private async Task<FrameworkPrecheckOutcome> TryInstallFrameworksAsync(IReadOnlyList<string> paths)
    {
        if (_ctx is null) return new FrameworkPrecheckOutcome(paths, Array.Empty<string>(), false);
        var remaining = new List<string>();
        var statusParts = new List<string>();
        bool anyInstalled = false;

        foreach (var src in paths)
        {
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) { remaining.Add(src); continue; }
            var lower = src.ToLowerInvariant();
            if (!Intake.ArchiveExtensions.Any(a => lower.EndsWith(a))) { remaining.Add(src); continue; }

            IReadOnlyList<string>? zipEntries = null;
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(src);
                zipEntries = zip.Entries.Select(e => e.FullName).ToList();
            }
            catch { /* can't peek — let the regular intake try */ }
            if (zipEntries is null) { remaining.Add(src); continue; }

            var classify = KnownFramework.Classify(zipEntries, _ctx.Game.Engine ?? "", _ctx.Game.SteamAppId);
            if (classify.Match is not null)
            {
                var fileNames = zipEntries
                    .Select(e => e.Replace('\\', '/'))
                    .Where(e => !e.EndsWith("/", StringComparison.Ordinal))
                    .ToList();
                // Resolve the symbolic InstallRoot ("PlayFolder", "GameRoot") to the actual
                // absolute path the installer will use. Two reasons: (1) the dialog has to show
                // the user the TRUTH about where files land — "ELDEN RING" hides the \Game
                // suffix and confused F2's first smoke; (2) the overwrite-check has to look in
                // the same place the installer will write, or it'll miss / falsely report
                // existing files.
                // ue-pak frameworks (UE4SS) resolve a project-relative root from the game's mod
                // locations (e.g. R5/Binaries/Win64); ELM's GameRoot/PlayFolder ignore this arg.
                var relPaths = _ctx.Game.ModLocations.Select(l => l.Path).ToList();
                var resolvedInstallRoot = FrameworkInstaller.ResolveInstallRoot(
                    classify.Match.InstallRoot, _ctx.GameRoot, relPaths);
                if (resolvedInstallRoot is null)
                {
                    // No project subfolder resolved — render the same refusal Install would, instead
                    // of dereferencing null in the overwrite-preview.
                    statusParts.Add(
                        $"Couldn't install {classify.Match.DisplayName}: no project subfolder found in " +
                        "the game's mod locations. Re-scan the game's mod folders and try again.");
                    continue;
                }
                var willOverwrite = fileNames
                    .Where(e => File.Exists(Path.Combine(resolvedInstallRoot, e)))
                    .ToList();

                var dlg = new FrameworkInstallDialog(classify.Match, fileNames, willOverwrite, resolvedInstallRoot)
                { XamlRoot = App.MainWindow!.Content.XamlRoot };
                var result = await dlg.ShowAsync();
                if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    statusParts.Add($"Skipped {classify.Match.DisplayName} install");
                    continue;
                }

                try
                {
                    var r = FrameworkInstaller.Install(src, classify.Match, _ctx.GameRoot, _ctx.DataDir, relPaths);
                    // Report the real install location, not a hardcoded "game root" — UE4SS lands under
                    // <project>/Binaries/Win64, and saying "game root" there is a lie.
                    statusParts.Add($"Installed {classify.Match.DisplayName} ({r.InstalledFiles.Count} file(s) to {r.InstallPath})");
                    anyInstalled = true;
                }
                catch (Exception ex)
                {
                    statusParts.Add($"Couldn't install {classify.Match.DisplayName}: {ex.Message}");
                }
                continue;
            }

            if (classify.LooksLikeFramework)
            {
                var nudge = new FrameworkUnrecognizedNudgeDialog(Path.GetFileName(src))
                { XamlRoot = App.MainWindow!.Content.XamlRoot };
                var result = await nudge.ShowAsync();
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
                {
                    // Cancel — drop this archive entirely.
                    statusParts.Add($"Skipped {Path.GetFileName(src)} (looked like a framework)");
                    continue;
                }
                // Primary ("Continue as mod") or Secondary ("Open feedback link") — fall through
                // to the regular mod intake. Secondary already launched the URL via the dialog.
                remaining.Add(src);
                continue;
            }

            remaining.Add(src);
        }

        return new FrameworkPrecheckOutcome(remaining, statusParts, anyInstalled);
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
                : await SaveLocator.DetectAsync(_ludu, entry.GameName, entry.Engine, entry.GameRoot, entry.SteamAppId, _steam.CurrentUserId64());
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

    /// <summary>Slug a display name into a filesystem-safe id (used as the INI-history bucket
    /// directory name). Lowercases, replaces non-alphanumerics with '-', collapses dashes, trims.</summary>
    private static string Slugify(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-');
        var s = new string(chars.ToArray());
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Trim('-');
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
