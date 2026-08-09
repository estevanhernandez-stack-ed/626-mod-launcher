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
using ModManager.Core.Discovery;
using ModManager.Core.Frameworks;
using ModManager.Core.Loaders;
using ModManager.Core.LooseMods;
using ModManager.Core.Nexus;
using ModManager.Core.Plugins;
using ModManager.Core.Tools;
using ModManager.Plugins.Abstractions;

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

/// <summary>App-side view row for a detected mod loader. Wraps <see cref="DetectedLoader"/> into a
/// bindable shape the XAML DataTemplate can address without a Core reference in the template.
/// <see cref="BanSafe"/> drives an optional tooltip hint in the XAML.</summary>
public sealed record DetectedLoaderRow(string DisplayName, string LauncherPath, bool BanSafe);

/// <summary>One entry in the safe-loader list shown inside the ban-risk gate dialog.
/// <see cref="LauncherPath"/> is non-null when the loader is already installed in the play folder —
/// the dialog shows a "Launch {DisplayName}" button. Null means not installed — the dialog shows a
/// "Get {DisplayName}" button that opens <see cref="GetUrl"/>.</summary>
public sealed record BanSafeLoaderOption(string DisplayName, string? LauncherPath, string GetUrl);

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
    private readonly NexusOAuthService _oauth;
    private readonly AvatarService _avatars;
    private readonly SteamService _steam;
    private readonly AppSettingsService _appSettings;
    private readonly NexusUpdatePoll _nexusPoll;
    private readonly ModSourceRegistry _sources;
    private readonly Services.DiscoveryScanService _discovery;
    private readonly Services.ModNameIndexSource _nameIndex;
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
    /// The safe-loader list is passed so the dialog can surface "Launch / Get" options — installed
    /// loaders get a Process.Start button; catalog-only loaders get a Get-it-here link.
    /// </summary>
    public Func<string, IReadOnlyList<BanSafeLoaderOption>, Task<(bool proceed, bool dontWarnAgain)>>? ConfirmBanRiskEnable { get; set; }

    /// <summary>
    /// Shows the loader-disable warning for a loose-root loader row (a proxy like dinput8.dll — the
    /// DLL every ASI plugin loads through) and returns true to proceed with the disable. The view
    /// wires this (the dialog + XamlRoot live in the code-behind, not the VM). Warn-and-proceed,
    /// never a hard block; when unset the disable proceeds without extra friction. Mirrors the
    /// <see cref="ConfirmBanRiskEnable"/> delegate pattern.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmLooseLoaderDisable { get; set; }

    /// <summary>
    /// Shows the review-before-adopt dialog for a discovery sweep's proposals and returns the
    /// approved subset (empty on Cancel). The view wires this (dialog + XamlRoot live in the
    /// code-behind, not the VM) — exactly the <see cref="ConfirmBanRiskEnable"/> pattern. When unset,
    /// <see cref="DiscoverExistingModsAsync"/> stops after building proposals: nothing is adopted.
    /// </summary>
    public Func<IReadOnlyList<AdoptionProposal>, Task<IReadOnlyList<AdoptionProposal>>>? ReviewDiscoveries { get; set; }

    /// <summary>Set by the view to show the unified review. Returns what the user approved in each
    /// section. Null (unwired view) means the run proposes and writes nothing.</summary>
    public Func<IReadOnlyList<AdoptionProposal>, IReadOnlyList<LooseIdentifyProposal>,
        Task<(IReadOnlyList<AdoptionProposal> Adoptions, IReadOnlyList<(string ModKey, SourceSearchHit Hit)> Identifications)>>? ReviewIdentifyRun { get; set; }

    // FromSoft games whose mods are driven by a Mod Engine 2 config (not filesystem scans).
    private bool ConfigBacked => _ctx is not null && _me2.IsConfigBacked(_ctx.Game);

    // FromSoft games without ME2: mods are direct-inject loose files (recognized + toggled by name).
    private bool DirectInjectBacked => _ctx is not null && !ConfigBacked && _direct.Applies(_ctx.Game);

    // Loose-root (decima) games: mods sit as loose files in the GAME ROOT (catalog + by-nature
    // detection), toggled by name through the same reversible DirectInject move machinery with
    // <dataDir>/loose-disabled as the holding root. Never scanner-world — without this lane a
    // toggle falls through to Scanner.SetLoaderModEnabledAsync and silently no-ops. Routes on the
    // resolved context's form via the ONE predicate (LooseRootListing.Applies) — the same dispatch
    // ModListing.Resolve consults, so the toggle lane and the listing can never disagree.
    private bool LooseRootBacked => _ctx is not null && !ConfigBacked && LooseRootService.Applies(_ctx);

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

    /// <summary>Mod loaders detected in the active game's play folder (e.g. Mod Engine 2,
    /// Seamless Co-op). Refreshed at every <see cref="ReloadModsAsync"/>. Surfaced as
    /// "Launch via X" buttons in the tools bar — one-click to start the loader instead of the
    /// base game. Ban-safe loaders are the preferred play path on games with anti-cheat.</summary>
    public ObservableCollection<DetectedLoaderRow> Loaders { get; } = new();

    public bool HasLoaders => Loaders.Count > 0;

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

    /// <summary>True while a long operation is running that the user is allowed to stop. Drives the
    /// Stop button beside the busy ring. Separate from <see cref="IsBusy"/> because most busy work
    /// is a quick, atomic file op that must NOT be interruptible mid-flight — only a long,
    /// read-only, resumable run (the Nexus name-search sweep) offers Stop.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelVisibility))]
    private bool isCancellable;

    public Visibility CancelVisibility => IsCancellable ? Visibility.Visible : Visibility.Collapsed;

    private CancellationTokenSource? _longOpCts;

    /// <summary>The one long operation this window can be running. The DECISION — held, by whom,
    /// and what to say when a second one asks — lives in Core where it is tested; what stays here is
    /// only the WinUI state that decision governs.</summary>
    private readonly LongOperationSlot _longOp = new();

    /// <summary>Keeps an answer readable while a progress counter is running. See
    /// <see cref="StatusHold"/> — a refusal written once loses to a ticker written constantly.</summary>
    private readonly StatusHold _statusHold = new();

    /// <summary>How long an answer owns the line. Long enough to read a sentence without feeling
    /// stuck; the progress counter picks straight back up afterwards.</summary>
    private static readonly TimeSpan AnswerHold = TimeSpan.FromSeconds(4);

    /// <summary>A progress tick. Yields to an answer the user is still reading.</summary>
    private void AmbientStatus(string text)
    {
        if (_statusHold.AmbientAllowed) StatusText = text;
    }

    /// <summary>An answer to something the user just did. Holds the line against ambient ticks.</summary>
    private void AnswerStatus(string text)
    {
        StatusText = text;
        _statusHold.Hold(AnswerHold);
    }

    /// <summary>
    /// Take the slot along with the busy ring, the Stop button, and the cancellation source, or
    /// refuse and say what is in the way.
    ///
    /// <para>The slot is claimed LAST, after everything that can throw. <see cref="IsBusy"/> and
    /// <see cref="IsCancellable"/> are <c>[ObservableProperty]</c> setters that raise PropertyChanged
    /// synchronously into x:Bind handlers; a throw there with the slot already taken would strand it
    /// for the session and silently disable every long action until restart. Claimed last, a throw
    /// leaves it free.</para>
    /// </summary>
    private bool TryBeginLongOp(CancellationTokenSource cts, string what)
    {
        if (RefuseIfLongOpRunning()) return false;
        IsBusy = true;
        _longOpCts = cts;
        IsCancellable = true;
        _longOp.TryClaim(what);
        // A run actually starting supersedes whatever answer was on the line — including a refusal
        // from a moment ago, which is now stale news about a slot that just changed hands.
        _statusHold.Clear();
        return true;
    }

    /// <summary>Release the slot and the state it governs. Belongs in the <c>finally</c> of whatever
    /// claimed it, so every exit — normal, cancelled, or thrown — hands it back.</summary>
    private void EndLongOp()
    {
        _longOp.Release();
        IsCancellable = false;
        _longOpCts = null;
        IsBusy = false;
    }

    /// <summary>
    /// Refuse a second long operation, naming the one already running.
    ///
    /// <para>Public because a caller may need to refuse BEFORE it asks the user anything. "Identify
    /// my mods…" opens a downloads-folder prompt first, and <see cref="TryBeginLongOp"/> only runs
    /// once that answer comes back — so without this the user answers a modal and is then told it
    /// was never going to run. <see cref="TryBeginLongOp"/> remains the authority and re-checks.</para>
    /// </summary>
    public bool RefuseIfLongOpRunning()
    {
        if (!_longOp.IsHeld) return false;
        // An ANSWER, not chatter: the run being refused is writing its own counter several times a
        // second, and without the hold it erases this before it can be read — which is precisely how
        // a working guard reads as a dead click.
        AnswerStatus(_longOp.RefusalMessage);
        return true;
    }

    /// <summary>Stop the running long operation. Safe at any moment: the run it cancels writes
    /// nothing on its own — whatever finished is still handed to the review dialog for approval.</summary>
    public void CancelLongOperation()
    {
        var cts = _longOpCts;
        if (cts is null) return;
        StatusText = "Stopping…";
        // The run that owns this source can finish and dispose it between the read above and the
        // call below — Stop losing that race must be a no-op, not an error on screen. Only the
        // disposed case is absorbed; anything else still surfaces.
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

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
    private int MpRiskyEnabledCount => _allRows.Count(m => m.Enabled && m.EffectiveMp is MpRisk.Risky or MpRisk.SpOnly);
    public Visibility MpWarningVisibility => MpRiskyEnabledCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string MpWarningText
    {
        get { var n = MpRiskyEnabledCount; return $"{n} enabled mod{(n == 1 ? "" : "s")} may not be co-op-safe"; }
    }
    private void NotifyMpWarning() { OnPropertyChanged(nameof(MpWarningVisibility)); OnPropertyChanged(nameof(MpWarningText)); }

    // Game-level ban-risk banner: resolved live by Steam app id from EffectiveManifest (via
    // BanRiskCatalog), distinct from the per-mod co-op-desync MpWarning above. Shows for high and
    // medium; stays visible even after the enable gate is acked (the risk is never hidden) and
    // covers the dropped-live-pak case the gate can't see. Recomputed on the same notify as
    // MpWarning when the active game changes.
    public Visibility BanRiskWarningVisibility =>
        BanRiskCatalog.ByAppId(_ctx?.Game.SteamAppId) >= GameBanRisk.Medium ? Visibility.Visible : Visibility.Collapsed;
    public string BanRiskWarningText => "This game uses anti-cheat — enabling mods for online play can get your account banned.";
    private void NotifyBanRiskWarning() { OnPropertyChanged(nameof(BanRiskWarningVisibility)); OnPropertyChanged(nameof(BanRiskWarningText)); }

    /// <summary>Set or clear (Auto = null) a mod's MP-compat override, persist it, refresh the badge + summary.</summary>
    public void SetMpOverride(ModRowViewModel row, MpRisk? value)
    {
        if (_ctx is null) return;
        try { MpCompatStore.SetOverride(_ctx.DataDir, row.Mod.Name, value); }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); return; }
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

    public MainViewModel(LauncherService svc, ModEngineService me2, DirectInjectService direct, ThemeService themes, LudusaviService ludu, NexusService nexus, NexusOAuthService oauth, AvatarService avatars, SteamService steam, AppSettingsService appSettings, NexusUpdatePoll nexusPoll, ModSourceRegistry sources, Services.DiscoveryScanService discovery, Services.ModNameIndexSource nameIndex)
    {
        _svc = svc;
        _me2 = me2;
        _direct = direct;
        _themes = themes;
        _ludu = ludu;
        _nexus = nexus;
        _oauth = oauth;
        _avatars = avatars;
        _steam = steam;
        _appSettings = appSettings;
        _nexusPoll = nexusPoll;
        _sources = sources;
        _discovery = discovery;
        _nameIndex = nameIndex;
        ThemeOptions = themes.Themes;
        // Restore the user's saved pick (F-080); Default (the flagship) covers first-run, a
        // cleared setting, and a saved id whose theme has since been deleted. The restore gate
        // keeps this path from PERSISTING: first-run must not pin the current default as if the
        // user chose it, and a transiently unreadable user theme must not overwrite a good saved
        // id with the fallback (B4.5 review catch). Only real picks save.
        _restoringTheme = true;
        try { SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == appSettings.ThemeId) ?? themes.Default; }
        finally { _restoringTheme = false; }
    }

    private bool _restoringTheme;

    // Segmented Loadout control: the selected segment tints with the theme accent; the others stay
    // transparent so the surrounding Border background shows through. Twin foregrounds keep contrast.
    // Inactive segments return the resource-backed ThemeInk brush directly so theme switches
    // propagate via the in-place color mutation in ThemeService.Set (no extra notify needed for
    // inactive). The active foreground is the resource-backed ThemeBg brush (F-078) — the same
    // bg-on-accent convention every other accent fill uses, re-themed live like ThemeInk.
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public Brush LoadoutAllBrush => SegmentBrushFor("all");
    public Brush LoadoutMpBrush  => SegmentBrushFor("mp");
    public Brush LoadoutSpBrush  => SegmentBrushFor("sp");
    public Brush LoadoutAllForeground => SegmentForegroundFor("all");
    public Brush LoadoutMpForeground  => SegmentForegroundFor("mp");
    public Brush LoadoutSpForeground  => SegmentForegroundFor("sp");

    private Brush SegmentBrushFor(string mode)
        => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
            ? (Brush)Application.Current.Resources["ThemeAccent"]
            : TransparentBrush;

    private Brush SegmentForegroundFor(string mode)
        => string.Equals(ActiveMode, mode, StringComparison.OrdinalIgnoreCase)
            ? (Brush)Application.Current.Resources["ThemeBg"]
            : (Brush)Application.Current.Resources["ThemeInk"];

    partial void OnActiveModeChanged(string value)
    {
        NotifyLoadoutBrushes();
    }

    partial void OnSelectedThemeChanged(Theme? value)
    {
        if (value is not null) _themes.Apply(value);
        if (value is not null && !_restoringTheme) _appSettings.SetThemeId(value.Id); // F-080: real picks survive restart

        // Warn-on-APPLY (F-076): the import-time advisory only fires once; a stored low-contrast
        // theme applied later was silent. Recomputed from the theme itself (derive, don't persist —
        // Este's call), advisory-only, and skipped during the startup restore to keep launch quiet.
        if (value is not null && !_restoringTheme)
        {
            var contrast = ModManager.Core.Themes.ContrastReport(value);
            if (contrast.Count > 0)
                StatusText = $"{value.Name} applied. Readability heads-up: {contrast[0]}"
                    + (contrast.Count > 1 ? $" (+{contrast.Count - 1} more pair{(contrast.Count > 2 ? "s" : "")})" : "");
        }
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
        // Reload() rebuilds instances, so this reassign ALWAYS fires the setter even when the id
        // is unchanged — gate it like the startup restore or every Settings close re-persists and
        // re-warns as if the user picked a theme (B5-B8 review, S3).
        _restoringTheme = true;
        try { SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == SelectedTheme?.Id) ?? _themes.Default; }
        finally { _restoringTheme = false; }
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
        // A filter typed for one game must not pre-narrow the next game's first render (F-061).
        // Backing-field clear: the property setter would run FilterRows over the OUTGOING game's
        // rows for one wasted render. The notify still empties the TwoWay-bound box.
        modFilterText = "";
        OnPropertyChanged(nameof(ModFilterText));
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
            _allRows = Array.Empty<ModRowViewModel>(); // no ghost rows through the filter (F-015/S7)
            ModFilterText = "";
            GameRootText = "";
            StatusText = "No game registered. Add one with + Game.";
            MissingFrameworks.Clear();
            OnPropertyChanged(nameof(HasMissingFrameworks));
            OnPropertyChanged(nameof(MissingFrameworksSummary));
            Tools.Clear();
            MissingTools.Clear();
            Loaders.Clear();
            FrameworkRows.Clear();
            OwnedLocations.Clear();
            ReDeployedLocations.Clear();
            SteamBuildChanged = false; // collapse the build-update banner when no game is active
            OnPropertyChanged(nameof(HasTools));
            OnPropertyChanged(nameof(HasMissingTools));
            OnPropertyChanged(nameof(HasLoaders));
            OnPropertyChanged(nameof(ToolsRowVisible));
            OnPropertyChanged(nameof(ToolsEmptyHintVisibility));
            OnPropertyChanged(nameof(HasInstalledFrameworks));
            OnPropertyChanged(nameof(HasOwnedLocations));
            OnPropertyChanged(nameof(HasReDeployedLocations));
            OnPropertyChanged(nameof(OwnedBannerVisibility));
            OnPropertyChanged(nameof(ReDeployedBannerVisibility));
            OnPropertyChanged(nameof(CatalogAvailable));
            OnPropertyChanged(nameof(CatalogVisibility));
            OnPropertyChanged(nameof(CatalogBrowseAvailable));
            OnPropertyChanged(nameof(CatalogBrowseVisibility));
            OnPropertyChanged(nameof(CatalogDetailAvailable));
            OnPropertyChanged(nameof(CatalogDetailVisibility));
            OnPropertyChanged(nameof(CatalogActionsAvailable));
            OnPropertyChanged(nameof(CatalogActionsVisibility));
            return;
        }
        // Save/restore, never a bare `false`. This reload is routinely NESTED inside a longer
        // operation that raised the ring itself — the unified identify run reloads between passes,
        // and so does every apply that writes metadata. A nested callee must not lower a ring it
        // did not raise: doing so left the run's longest await (the pass-4 name search) rendering
        // as idle. Restoring the prior value makes a reload transparent to whatever composes it,
        // which is why this belongs here rather than as a re-assert at each call site.
        var wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            // Four worlds: Mod Engine 2 games read their mods from the config; FromSoft games
            // without ME2 are direct-inject (loose files next to the exe) — toggled by name, never
            // deleted; loose-root (decima) games list from the game root via LooseRootListing;
            // everything else is a filesystem scan via the proven Scanner pipeline.
            var directInject = DirectInjectBacked;
            var looseRoot = LooseRootBacked;
            // Scanner-world only: migrate the data dir, then list, then persist the auto-seeded
            // classification — exactly the two writes the old scanner branch did. The shared
            // read-only resolver (used by the agent-access MCP too) performs neither. Loose-root
            // rows aren't scanner rows — persisting a classification for them would seed garbage
            // entries keyed by detector names.
            if (!ConfigBacked && !directInject && !looseRoot)
                await Scanner.MigrateDataDirAsync(_ctx);
            // One read-only listing path shared with the MCP: dispatch by engine (ME2 / direct-inject /
            // scanner) + merge metadata.json. See ModManager.Core.ModListing.Resolve. The metadata
            // merge is load-bearing: without it, Nexus / CurseForge entries written by
            // Md5IdentifyArchivesAsync / RefreshMetadataByNameAsync never reach the displayed fromsoft rows.
            IReadOnlyList<Mod> list = ModListing.Resolve(_ctx.Game);
            if (!ConfigBacked && !directInject && !looseRoot)
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
                // Loose-root rows: never uninstallable here (we never delete loose files in the
                // game root — same law as direct-inject), and the disabled-but-unrestorable
                // sentinel (corrupt/missing __626mod.json sidecar) can't be toggled — there's
                // nothing safe to restore, so its switch renders disabled. A loose-root row honors
                // ReadOnly strictly (a Vortex/MO2-owned root stays read-only until takeover — the
                // scanner world's semantics; loose rows have no loader-manifest escape), with NO
                // IsLoader bypass: that escape stays only for the fromsoft direct-inject lane.
                var unrestorable = LooseRootListing.IsUnrestorable(rep);
                var looseRow = rep.Location == LooseRootListing.LooseRootLocation;
                rows.Add(new ModRowViewModel(rep,
                    canToggle: !unrestorable && (looseRow
                        ? !rep.ReadOnly
                        : rep.IsLoader || !rep.ReadOnly || rep.Loader is "ue4ss" or "bepinex"),
                    canUninstall: !directInject && !looseRoot && !rep.ReadOnly)
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
                    // The endorse heart needs a resolved Nexus mod id (the write key), a live connection,
                    // AND a loaded Nexus source plugin (the heart routes through IModSource.SetEndorsedAsync,
                    // so it's absent on the STORE flavor / zero-plugins path). All captured at row build,
                    // fresh every rescan, no per-row notify.
                    NexusModId = metaByKey.TryGetValue(rep.Name, out var repMeta)
                        ? NexusRefresh.ResolveModId(repMeta)
                        : null,
                    NexusConnected = NexusActionsAvailable,
                });
            }
            OrderAndStampSections(rows);
            NotifyMpWarning();
            NotifyBanRiskWarning();
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

            // Detect mod loaders installed in the play folder (Mod Engine 2, Seamless Co-op, …)
            // and surface them as "Launch via X" buttons in the tools bar. LoaderScan.Detect is pure
            // File.Exists — no I/O beyond that. On ban-risk games, these are the primary safe path.
            Loaders.Clear();
            var pf = DirectInjectService.PlayFolder(_ctx.Game.GameRoot);
            foreach (var d in LoaderScan.Detect(pf, _ctx.Game.Engine, _ctx.Game.SteamAppId))
                Loaders.Add(new DetectedLoaderRow(d.Loader.DisplayName, d.LauncherPath, d.Loader.BanSafe));
            OnPropertyChanged(nameof(HasLoaders));

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
            // The catalog surfaces gate on the Nexus connection plus the active game's domain, and a
            // game switch changes both; recompute them on every row rebuild too, or the buttons never
            // appear on switch.
            OnPropertyChanged(nameof(CatalogAvailable));
            OnPropertyChanged(nameof(CatalogVisibility));
            OnPropertyChanged(nameof(CatalogBrowseAvailable));
            OnPropertyChanged(nameof(CatalogBrowseVisibility));
            OnPropertyChanged(nameof(CatalogDetailAvailable));
            OnPropertyChanged(nameof(CatalogDetailVisibility));
            OnPropertyChanged(nameof(CatalogActionsAvailable));
            OnPropertyChanged(nameof(CatalogActionsVisibility));
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = wasBusy; }

        // Debounced Nexus auto-check (once per 24h per game, off the UI hot path). Fire-and-forget:
        // it polls Nexus by mod id for the active game, flags newer versions, and persists — then we
        // reload rows to surface UPDATE chips only if it actually changed something. Self-limiting via
        // the per-game stamp, so the per-toggle re-entry of ReloadModsAsync costs a stamp read + bail.
        // Every failure is swallowed inside MaybePollAsync — it can never break the session.
        if (_ctx is { } ctx)
        {
            _ = AutoCheckNexusUpdatesAsync(ctx);
            // Same debounce shape, different payload: seed the per-game Nexus name index so the
            // discovery sweep's tier-2 match actually has something to match against. Task 7 shipped
            // SeedAsync with no caller ever gating or calling it — ModNameIndexSource.MaybeSeedAsync
            // closes that gap using the exact NexusPollStamp mechanism MaybePollAsync uses, gated on
            // the SAME AutoCheckModUpdates setting MaybePollAsync checks (a user who turned auto-check
            // off shouldn't still get up to 10 catalog requests per game per day for this).
            _ = _nameIndex.MaybeSeedAsync(ctx.DataDir, ctx.Game.Id, NexusDomains.Effective(ctx.Game), _nexus.IsConnected, _appSettings.AutoCheckModUpdates, NexusSource);
        }
    }

    /// <summary>Fire-and-forget debounced Nexus auto-check launched at the tail of a game load. Runs on
    /// the thread-pool (off the UI hot path); if it persisted any newer-version data, it marshals a row
    /// reload back onto the UI thread — but only when the polled game is still the active one (the user
    /// may have switched games while the network call was in flight).</summary>
    private async Task AutoCheckNexusUpdatesAsync(GameContext ctx)
    {
        var changed = await _nexusPoll.MaybePollAsync(ctx, NexusSource, _nexus, _appSettings);
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

    private void UpdateStatus() => StatusText = $"{_allRows.Count(m => m.Enabled)} of {_allRows.Count} enabled";

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
        // Re-group from the STATE list, never the filtered render list — regrouping while a
        // filter is typed must not collapse _allRows (OrderAndStampSections reassigns it), or
        // Disable-all / play-vanilla silently act on the visible subset (F-015 close-out fix).
        if (_allRows.Count > 0) OrderAndStampSections(_allRows.ToList()); // re-group in place, no rescan
    }

    // Loose-root rows group by mod NATURE (their Class carries the detector's kind), not by the
    // scanner taxonomies — fixed category order: plugins, then shader/addon packages, then the
    // loader proxies the plugins depend on. ReShade's catalog kind is "graphics"; it belongs with
    // shaders. The disabled-but-unrestorable sentinel sinks to its own bottom section so a
    // held-but-orphaned mod stays visible, never mixed in as a healthy row. Applies in every
    // GroupMode — the scanner groupings (source / MP class / CF category) are meaningless here.
    private static (int Rank, string Label)? LooseRootSectionOf(Mod m)
    {
        if (LooseRootListing.IsUnrestorable(m)) return (3, "UNRESTORABLE");
        if (m.Location != LooseRootListing.LooseRootLocation) return null;
        return (m.Class ?? "").ToLowerInvariant() switch
        {
            "loader" => (2, "LOADERS"),
            "shaders" or "graphics" => (1, "SHADERS"),
            _ => (0, "PLUGINS"),
        };
    }

    // Section key for a mod under the active GroupMode. Rank drives top-to-bottom order; Label is the
    // divider text. "By class" uses the MP-safety class (both/sp/mp) we track, not a content category.
    private (int Rank, string Label) SectionOf(Mod m)
    {
        if (LooseRootSectionOf(m) is { } loose) return loose;
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
        // Stable OrderBy preserves variant adjacency within a section. The ThenBy keys every
        // non-loose row to "" (equal keys keep original order, so scanner rows are untouched) and
        // sorts loose-root rows by display name within their category — category-then-name.
        var ordered = rows
            .OrderBy(r => SectionOf(r.Mod).Rank)
            .ThenBy(r => LooseRootSectionOf(r.Mod) is null ? "" : r.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? prev = null;
        foreach (var r in ordered)
        {
            var label = SectionOf(r.Mod).Label;
            r.SectionHeader = label != prev ? label : null;
            prev = label;
        }
        _allRows = ordered;
        Mods = new ObservableCollection<ModRowViewModel>(FilterRows(ordered));
    }

    // Find-by-name over the loaded rows (vibe-glow F-015). The predicate is Core (ModSearch).
    // THE RULE: Mods is the RENDER list; _allRows is the STATE list. Every write/safety/status
    // path (load order, play-vanilla step-aside, launch guard, enable-all, MP warnings,
    // loose-identify) reads _allRows — a typed filter must never narrow a file op.
    private IReadOnlyList<ModRowViewModel> _allRows = Array.Empty<ModRowViewModel>();

    [ObservableProperty] private string modFilterText = "";

    partial void OnModFilterTextChanged(string value)
        => Mods = new ObservableCollection<ModRowViewModel>(FilterRows(_allRows));

    // Zero-match empty state (F-059): a blank list reads as broken; name the query instead.
    [ObservableProperty] private string filterEmptyText = "";
    [ObservableProperty] private Visibility filterEmptyVisibility = Visibility.Collapsed;

    private List<ModRowViewModel> FilterRows(IEnumerable<ModRowViewModel> rows)
    {
        var all = rows.ToList();
        var visible = all.Where(r => ModSearch.Matches(r.DisplayName, r.Mod.Author, r.FileTag, ModFilterText)).ToList();
        var filteredToNothing = visible.Count == 0 && all.Count > 0 && !string.IsNullOrWhiteSpace(ModFilterText);
        FilterEmptyText = filteredToNothing ? $"No mods match \"{ModFilterText.Trim()}\"." : "";
        FilterEmptyVisibility = filteredToNothing ? Visibility.Visible : Visibility.Collapsed;
        // Re-stamp section dividers + the legend host over the VISIBLE sequence — a filtered-out
        // first row must not take its section header or the ? glossary button with it.
        string? prev = null;
        foreach (var r in visible)
        {
            var label = SectionOf(r.Mod).Label;
            r.SectionHeader = label != prev ? label : null;
            r.IsFirstSectionHeader = false;
            prev = label;
        }
        var first = visible.FirstOrDefault(m => !string.IsNullOrEmpty(m.SectionHeader));
        if (first is not null) first.IsFirstSectionHeader = true;
        return visible;
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

        // Build the safe-loader list: for each ban-safe loader that applies to this game, check if
        // it's already installed in the play folder (LauncherPath non-null) or just in the catalog
        // (LauncherPath null → "Get it here"). Pure File.Exists inside Detect — no extra I/O.
        var pf = DirectInjectService.PlayFolder(_ctx.Game.GameRoot);
        var detected = LoaderScan.Detect(pf, _ctx.Game.Engine, _ctx.Game.SteamAppId)
            .ToDictionary(d => d.Loader.LoaderId, StringComparer.Ordinal);
        var options = LoaderScan.BanSafeFor(_ctx.Game.Engine, _ctx.Game.SteamAppId)
            .Select(l => new BanSafeLoaderOption(
                l.DisplayName,
                detected.TryGetValue(l.LoaderId, out var det) ? det.LauncherPath : null,
                l.GetUrl))
            .ToList();

        var (proceed, dontWarn) = await ConfirmBanRiskEnable(_ctx.Game.GameName, options);
        if (!proceed) return false;
        if (dontWarn) BanRiskAckStore.Ack(_ctx.DataDir, _ctx.Game.Id);
        return true;
    }

    /// <summary>The loose-root loader-disable gate. The VM owns the policy trigger (disabling a
    /// loose-root row whose kind flagged it a loader — <see cref="Mod.IsLoader"/>); the view owns
    /// the dialog via <see cref="ConfirmLooseLoaderDisable"/>. Returns true to proceed with the
    /// disable, false to abort (caller reverts the visual, nothing touched disk). Unwired ->
    /// proceed (no extra friction). Warn-and-proceed, never a hard block.</summary>
    private async Task<bool> GateLooseLoaderDisableAsync(ModRowViewModel row)
    {
        if (ConfirmLooseLoaderDisable is null) return true;
        return await ConfirmLooseLoaderDisable(row.DisplayName);
    }

    /// <summary>Toggle one mod. The reversible disable/enable lives in Scanner; on failure the
    /// switch reverts and the error surfaces (never a silent half-disable).</summary>
    public async Task ToggleAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        if (row.IsBusy) return; // reentrancy guard — a mid-flight toggle ignores further flips (F-016)
        // Ban-risk gate: only when this toggle is turning a row ON. Disabling is never gated
        // (getting safer needs no friction). On cancel, revert the visual exactly like the catch.
        if (row.Enabled && !await GateBanRiskEnableAsync()) { row.Enabled = false; return; }
        // Loader-disable gate: turning OFF a loose-root loader row (the proxy DLL every ASI plugin
        // loads through) warns first — disabling it disables every plugin that injects through it.
        // Warn-and-proceed, never a hard block. On cancel, nothing touched disk; reload so the
        // switch rebuilds from actual state (mirrors the variant-family cancel path).
        if (!row.Enabled && LooseRootBacked && row.Mod.IsLoader && !await GateLooseLoaderDisableAsync(row))
        { row.Enabled = true; await ReloadModsAsync(); return; }
        // A manual toggle leaves "clean vanilla" — clear the stash so CurrentMode reverts to Modded and
        // the launch button stops claiming "Play vanilla" while a mod is live again.
        VanillaStashStore.Clear(_ctx.DataDir);
        row.IsBusy = true;
        try
        {
            if (ConfigBacked) _me2.SetEnabled(_ctx.Game, row.Mod.Name, row.Enabled);
            else if (DirectInjectBacked) _direct.SetEnabled(_ctx.Game, row.Mod.Name, row.Enabled);
            else if (LooseRootBacked) LooseRootService.SetEnabled(_ctx.Game, row.Mod.Name, row.Enabled);
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
            StatusText = ErrorRemedy.Describe(e);
        }
        finally { row.IsBusy = false; }
    }

    /// <summary>Toggle one level of a multi-variant family — enable/disable that specific variant's
    /// files via the same gated path as the single toggle, then reload to refresh the chips.</summary>
    public async Task ToggleVariantAsync(VariantOptionVM opt, bool enable)
    {
        if (_ctx is null) return;
        var owner = _allRows.FirstOrDefault(r => r.VariantOptions.Any(v => v.ModName == opt.ModName));
        if (owner?.IsBusy == true) return; // reentrancy guard (F-016) — chips share the row's busy state
        // Ban-risk gate before enabling a variant (the view already reflects the desired state; on
        // cancel we abort without touching files — the chip rebuild on the next reload corrects it).
        if (enable && !await GateBanRiskEnableAsync()) { await ReloadModsAsync(); return; }
        if (owner is not null) owner.IsBusy = true;
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { if (owner is not null) owner.IsBusy = false; }
    }

    /// <summary>Toggle a variant family on or off. ON re-enables the LAST-active variant (remembered
    /// across rescans via <see cref="_familyLastActive"/>) or the first if none recorded. OFF disables
    /// every currently-enabled variant after recording which one was active. The variant CHIPS pick
    /// which variant is active when the family is on; this switch picks whether the family is on.</summary>
    public async Task ToggleFamilyAsync(ModRowViewModel row, bool on)
    {
        if (_ctx is null || !row.HasVariantOptions) return;
        if (row.IsBusy) return; // reentrancy guard (F-016) — same net as the single toggle
        // Ban-risk gate before turning a variant family ON (the family switch reflects the desired
        // state; on cancel we reload so the switch rebuilds from actual state — nothing enabled).
        if (on && !await GateBanRiskEnableAsync()) { await ReloadModsAsync(); return; }
        var familyKey = string.IsNullOrEmpty(row.Mod.BaseTitle) ? row.DisplayName : row.Mod.BaseTitle!;
        row.IsBusy = true;
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { row.IsBusy = false; }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
                foreach (var m in _allRows.Where(m => m.Enabled != on)) _direct.SetEnabled(_ctx!.Game, m.Mod.Name, on);
                return Task.CompletedTask;
            }
            if (LooseRootBacked)
            {
                // Same per-row lane as direct-inject. CanToggle filters out the unrestorable
                // sentinel (nothing safe to restore); the explicit ReadOnly skip keeps a
                // Vortex/MO2-owned root untouched by bulk ops (read-only until takeover — the
                // scanner world's bulk guard, made explicit here). A bulk disable includes the
                // loader row WITHOUT the per-row loader warning: the user asked for everything
                // off, so "disabling the loader disables the plugins" is the requested outcome.
                foreach (var m in _allRows.Where(m => m.CanToggle && !m.Mod.ReadOnly && m.Enabled != on))
                    LooseRootService.SetEnabled(_ctx!.Game, m.Mod.Name, on);
                return Task.CompletedTask;
            }
            return Scanner.SetAllModsAsync(on, _ctx!);
        });
    }

    [RelayCommand]
    private async Task SetMode(string mode)
    {
        // No MP/SP split for Mod Engine 2, direct-inject, or loose-root mods — the mode buttons are
        // a no-op there (Scanner.ApplyModeAsync must never run against a non-scanner world).
        if (ConfigBacked || DirectInjectBacked || LooseRootBacked) { ActiveMode = mode; return; }
        // Applying a mode enables the mods that match it — gate ONCE before the bulk apply. On cancel,
        // abort without changing the active mode (nothing was enabled).
        if (_ctx is not null && !await GateBanRiskEnableAsync()) return;
        ActiveMode = mode;
        await BulkAsync(() => Scanner.ApplyModeAsync(mode, _ctx!));
    }

    // The one toolbar "Refresh": re-scan the mod list, then — when Nexus is connected — refresh Nexus
    // stats (endorsements / downloads / update flags). The Nexus step is skipped silently on Store or
    // when disconnected (guarded here so it doesn't surface a misleading "Nexus unavailable" status),
    // so Refresh is always useful on every flavor.
    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadModsAsync();
        if (NexusUserFeaturesAvailable)
            await RefreshNexusStatsAsync();
    }

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

    /// <summary>Apply a saved profile through the ban-risk gate. Loading a profile is a bulk enable
    /// (it can flip mods ON), so it goes through <see cref="GateBanRiskEnableAsync"/> ONCE before
    /// <see cref="Scanner.LoadProfileAsync"/> touches anything — on cancel nothing is enabled and the
    /// caller is told it didn't apply. Returns true if the profile was applied. The dialog routes
    /// here instead of calling Scanner directly so no profile-apply path bypasses the gate.</summary>
    public async Task<bool> LoadProfileAsync(string name)
    {
        if (_ctx is null) return false;
        if (!await GateBanRiskEnableAsync()) return false; // un-acked high-risk + cancel -> enable nothing
        await Scanner.LoadProfileAsync(name, _ctx);
        return true;
    }

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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = false; }
    }

    // ---------- inline load-order mode ----------

    /// <summary>Enter load-order mode: show only enabled mods, in saved order, numbered + draggable.</summary>
    public async Task EnterLoadOrderAsync()
    {
        if (_ctx is null || IsLoadOrderMode) return;
        if (DirectInjectBacked || LooseRootBacked)
        {
            // Direct-inject and loose-root mods load independently — no priority order to arrange.
            StatusText = "Load order doesn't apply to these mods — they load independently.";
            return;
        }
        List<ModRowViewModel> ordered;
        if (ConfigBacked)
        {
            // The config's array order IS the load order — keep enabled mods in their current order.
            ordered = _allRows.Where(m => m.Enabled).ToList();
        }
        else
        {
            var orderKeys = await Scanner.GetLoadOrderAsync(_ctx);
            var byKey = _allRows.Where(m => m.Enabled)
                .GroupBy(m => m.Mod.Name).ToDictionary(g => g.Key, g => g.First());
            ordered = orderKeys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        }
        foreach (var r in ordered) { r.InLoadOrder = true; r.IsFirstSectionHeader = false; }
        Mods = new ObservableCollection<ModRowViewModel>(ordered);
        // Direct Mods assign bypasses FilterRows — drop any zero-match overlay so it can't sit
        // on top of the load-order list (B4.5 review catch).
        FilterEmptyText = "";
        FilterEmptyVisibility = Visibility.Collapsed;
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
    public bool AnyModsEnabled => _allRows.Any(m => m.Enabled);

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
                return string.IsNullOrEmpty(how) ? "Play vanilla" : $"Play vanilla ({how})";
            return string.IsNullOrEmpty(how) ? "Play (modded)" : $"Play modded ({how})";
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
            ActiveModRows = () => _allRows.SelectMany(m => m.HasVariantOptions
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
            // Loose-root rows step aside through their own reversible lane (the DirectInject move
            // to <dataDir>/loose-disabled) — Scanner has no idea these rows exist, so routing them
            // to Scanner.Disable/EnableModAsync would silently no-op and the "vanilla" launch would
            // still load every loose mod. Routed by the row's Location, which the stash records, so
            // Restore replays through the same lane the step-aside used. The loose-root loader row
            // (dinput8) is an ActiveModRow like any other — decima needs no separate proxy lane.
            DisableModRow = (name, location) =>
            {
                if (location == LooseRootListing.LooseRootLocation)
                { LooseRootService.SetEnabled(ctx.Game, name, false); return Task.CompletedTask; }
                return Scanner.DisableModAsync(name, ctx);
            },
            EnableModRow = (name, location) =>
            {
                if (location == LooseRootListing.LooseRootLocation)
                { LooseRootService.SetEnabled(ctx.Game, name, true); return Task.CompletedTask; }
                return Scanner.EnableModAsync(name, ctx);
            },
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
        try
        {
            if (!_svc.Launch(_ctx.Game)) StatusText = "No launch target configured for this game.";
            else StampLaunch();
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        try { _svc.Launch(target, _ctx.Game.GameRoot); StampLaunch(); }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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

    // Stamp the recency signal after a successful launch: GameEntry.LastLaunchedUtc + an append to the
    // own-launch log (LauncherService.StampLaunch). Never touches the launch mechanism itself, and a
    // stamping failure is non-fatal — recency just degrades to the Steam source next load.
    private void StampLaunch()
    {
        if (_ctx is null) return;
        try { _svc.StampLaunch(_ctx.Game.Id); }
        catch { /* recency degrades to Steam; never block or report on a launch that already happened */ }
    }

    /// <summary>The verified launch options for the active game (internal + external), for the dialog.</summary>
    public IReadOnlyList<LaunchOption> ActiveLaunchOptions => LaunchOptions.For(_ctx?.Game.SteamAppId);

#if FULL
    // FULL flavor only — the EAC-disable toggle is stripped from the sealed Store SKU. AntiCheat (the
    // Core mechanism) is absent from the Store Core binary, and LaunchOptions.For filters the toggle
    // option out for Store, so these call sites compile out cleanly.
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        return AntiCheatStateOf(opt);
    }
#endif

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
                try { vtx = (await Scanner.IdentifyVortexNexusAsync(_ctx, NexusSource)).Matched; }
                catch { /* best-effort; CF result still stands */ }
            }
            await ReloadModsAsync();
            StatusText = r.GameId is null
                ? (vtx > 0 ? $"Filled {vtx} Vortex mod(s) from Nexus." : "Couldn't resolve this game on CurseForge.")
                : $"Matched {r.Matched} of {r.Total} on CurseForge" + (vtx > 0 ? $", +{vtx} from Vortex/Nexus." : ".");
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = false; }
    }

    // ---------- Nexus connection ----------

    public bool NexusConnected => _nexus.IsConnected;

    /// <summary>The loaded Nexus mod source from the shared <see cref="ModSourceRegistry"/> — the plugin's
    /// <see cref="IModSource"/> when the FULL flavor loaded one, null on the STORE flavor / zero-plugins
    /// path. Every user-facing Nexus action (endorse heart, "Refresh Nexus stats", the update poll) routes
    /// through this instead of Core's <c>NexusClient</c>, as does scan-time md5-identify (<c>Scanner</c>'s
    /// <c>Md5Identify*</c> / <c>IdentifyVortexNexusAsync</c> + <c>Ue4ssLuaInstaller.IdentifyMetadataAsync</c>,
    /// rewired in B2a). When null the surfaces are absent and identify no-ops — the app stays a complete
    /// product without them (the zero-plugins invariant).
    /// <para>As of B2b-1 every read also routes here: the library-wide endorse-heart sync + windowed
    /// update poll (<c>NexusRefresh.RefreshAllAsync</c>) and the manual-URL match all go through this
    /// <c>IModSource</c>. Core's <c>NexusClient</c> has zero live callers left.</para></summary>
    private IModSource? NexusSource => _sources.ById("nexus");

    /// <summary>True when the user-facing Nexus surfaces should be shown: a Nexus source is loaded AND the
    /// account is connected. Drives the "Refresh Nexus stats" menu item visibility and the endorse heart.
    /// On the STORE flavor the registry is empty, so this is false and the surfaces are absent.</summary>
    public bool NexusActionsAvailable => NexusSource is not null && _nexus.IsConnected;
    public Visibility NexusActionsVisibility => NexusActionsAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The dark-window gate for USER-SCOPED Nexus features (endorse, md5 identify/backfill,
    /// refresh-stats, loose-identify apply). False when the OAuth client_id hasn't been delivered yet
    /// (secure sign-in not finalized) or the account isn't connected. The unauthenticated GraphQL
    /// search (loose-identify propose) is never routed through this — it stays available regardless.</summary>
    public bool NexusUserFeaturesAvailable =>
        NexusAuthGate.CanUseUserScopedFeatures(_oauth.Config.IsConfigured, _nexus.IsConnected);

    /// <summary>True once the OAuth client_id has been delivered (secure sign-in configured). False = the
    /// dark window — the Settings "Connect Nexus account" button is disabled with a "finalizing" note.</summary>
    public bool NexusSignInConfigured => _oauth.Config.IsConfigured;

    /// <summary>
    /// The one line for "you are not signed in to Nexus".
    ///
    /// <para>Three different phrasings shipped, and they did not merely differ in wording — they
    /// gave different DIRECTIONS. Four sites said "(toolbar -> Nexus)" and the only place a user can
    /// actually connect is the Settings dialog's "Connect Nexus account" button, so following that
    /// message led somewhere with nothing to click. A remedy that names the wrong place is worse
    /// than no remedy: it spends the user's trust before it fails them.</para>
    ///
    /// <para>One constant, so a pre-check and the operation it guards cannot drift apart — the
    /// property those two must agree on is now that they are the same string.</para>
    /// </summary>
    private const string NexusNotConnectedMessage =
        "Nexus isn't connected. Connect your account in Settings → Nexus Mods.";

    /// <summary>Status message for a gated user-scoped action — distinguishes the dark window
    /// ("finalizing sign-in") from a plain disconnected state so the copy never tells the user to
    /// "connect" when connecting isn't yet possible.</summary>
    private string NexusUnavailableMessage =>
        !_oauth.Config.IsConfigured
            ? "Secure sign-in is being finalized with Nexus."
            : "Connect your Nexus account first (Settings → Nexus Mods).";

    /// <summary>True when the launcher discarded a pre-OAuth API key on load — the shell shows a one-time
    /// reconnect notice on startup.</summary>
    public bool NexusLegacyKeyDiscarded => _nexus.LegacyKeyWasDiscarded;

    /// <summary>Whether the active game resolves a Nexus domain (stored, or by Steam app id). The
    /// window consults this before running loose-identify so the no-domain case gets a clear
    /// message dialog instead of a silent no-op.</summary>
    public bool ActiveGameHasNexusDomain =>
        _ctx is not null && !string.IsNullOrWhiteSpace(NexusDomains.Effective(_ctx.Game));

    /// <summary>Catalog browse is available on the FULL build when the loaded Nexus source supports
    /// IModCatalog and the active game resolves a Nexus domain. On STORE / no-plugin / older plugin the
    /// source isn't IModCatalog, so this is false and the menu item is absent. The capability check IS the
    /// flavor gate — no #if FULL.</summary>
    public bool CatalogAvailable =>
        NexusActionsAvailable && NexusSource is IModCatalog && ActiveGameHasNexusDomain;
    public Visibility CatalogVisibility => CatalogAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Adult-excluded Nexus catalog search for the active game. Self-timeouts (~10s) so a hung
    /// request can't wedge the dialog; never throws (empty list on any failure). Adult exclusion is
    /// server-side in the plugin — the launcher receives only clean hits.</summary>
    public async Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string query)
    {
        if (_ctx is null || NexusSource is not IModCatalog catalog) return System.Array.Empty<SourceSearchHit>();
        var domain = NexusDomains.Effective(_ctx.Game);
        // A blank query is intentional: it's the default catalog view (the plugin returns the game's
        // most-endorsed listing). Only a missing domain short-circuits to empty.
        if (string.IsNullOrWhiteSpace(domain))
            return System.Array.Empty<SourceSearchHit>();
        try
        {
            var search = catalog.SearchCatalogAsync(domain, query);
            var done = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (done != search) return System.Array.Empty<SourceSearchHit>();
            var hits = await search.ConfigureAwait(false);
            // Growth is free (design intent, Task 7's ModNameIndexSource.Grow) — these hits were
            // already fetched for the search view; fold them into the per-game name index so a later
            // discovery sweep can identify an extracted copy without a network round-trip.
            if (hits.Count > 0 && _ctx is { } ctx) _nameIndex.Grow(ctx.DataDir, hits);
            return hits;
        }
        catch { return System.Array.Empty<SourceSearchHit>(); }
    }

    /// <summary>Rich catalog browse (cards, sort views, category filter, paging, per-user badges) —
    /// available when the loaded plugin implements the Phase 1 capability. Older plugins fall back to the
    /// simpler <see cref="CatalogAvailable"/> path, and STORE/no-plugin leaves both false.</summary>
    public bool CatalogBrowseAvailable =>
        NexusActionsAvailable && NexusSource is IModCatalogBrowse && ActiveGameHasNexusDomain;
    public Visibility CatalogBrowseVisibility => CatalogBrowseAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fetch one catalog page for the active game. Self-timeouts (~10s) so a hung request can't
    /// wedge the view; never throws (empty page on any failure).</summary>
    public async Task<CatalogPage> BrowseCatalogAsync(
        string? text, CatalogSort sort, string? category, int offset, int count = 20)
    {
        if (_ctx is null || NexusSource is not IModCatalogBrowse browse) return CatalogPage.Empty;
        var domain = NexusDomains.Effective(_ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) return CatalogPage.Empty;
        try
        {
            var call = browse.BrowseCatalogAsync(new CatalogQuery(domain!, text, sort, category, offset, count));
            var done = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (done != call) return CatalogPage.Empty;
            var page = await call.ConfigureAwait(false);
            // Growth is free — these hits were already fetched for the browse view; fold them into the
            // per-game name index so a later discovery sweep can identify an extracted copy for free.
            if (page.Hits.Count > 0 && _ctx is { } ctx) _nameIndex.Grow(ctx.DataDir, page.Hits);
            return page;
        }
        catch { return CatalogPage.Empty; }
    }

    /// <summary>Mod detail (description, art, stats, requirements, viewer state) is available when the
    /// loaded plugin implements the Phase 2 capability. Older plugins (Phase 1 browse-only, or none) leave
    /// this false, so a card click on those builds simply doesn't try to open a dead dialog.</summary>
    public bool CatalogDetailAvailable => NexusActionsAvailable && NexusSource is IModCatalogDetail;
    public Visibility CatalogDetailVisibility => CatalogDetailAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Endorse/track are available when the loaded plugin implements the Phase 2 actions
    /// capability. Both mutate the user's real Nexus account — <see cref="SetEndorsedAsync"/> and
    /// <see cref="SetTrackedAsync"/> below are plain pass-throughs meant to fire only from an explicit UI
    /// click, never from an init/refresh/reload path.</summary>
    public bool CatalogActionsAvailable => NexusActionsAvailable && NexusSource is IModCatalogActions;
    public Visibility CatalogActionsVisibility => CatalogActionsAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fetch one mod's detail for the active game. Self-timeouts (~10s) so a hung request can't
    /// wedge the dialog; never throws (null on any failure).</summary>
    public async Task<CatalogDetail?> GetModDetailAsync(int gameId, int modId)
    {
        if (NexusSource is not IModCatalogDetail detail) return null;
        try
        {
            var call = detail.GetModDetailAsync(gameId, modId);
            var done = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            return done == call ? await call.ConfigureAwait(false) : null;
        }
        catch { return null; }
    }

    /// <summary>Endorse/un-endorse a mod on the user's real Nexus account. A plain pass-through to the
    /// plugin — call only in direct response to an explicit UI click, never from an init/refresh/reload
    /// path. Never throws (false on any failure, so the UI can revert its optimistic toggle).</summary>
    public async Task<bool> SetEndorsedAsync(string uid, bool endorsed)
    {
        if (NexusSource is not IModCatalogActions actions) return false;
        try { return await actions.SetEndorsedAsync(uid, endorsed).ConfigureAwait(false); }
        catch { return false; }
    }

    /// <summary>Track/untrack a mod on the user's real Nexus account. A plain pass-through to the plugin —
    /// call only in direct response to an explicit UI click, never from an init/refresh/reload path. Never
    /// throws (false on any failure, so the UI can revert its optimistic toggle).</summary>
    public async Task<bool> SetTrackedAsync(string uid, bool tracked)
    {
        if (NexusSource is not IModCatalogActions actions) return false;
        try { return await actions.SetTrackedAsync(uid, tracked).ConfigureAwait(false); }
        catch { return false; }
    }

#if FULL
    /// <summary>The off-Store plugin feed, stashed on wire-up so <see cref="ConnectNexusAsync"/> can trigger
    /// the consented first-install fetch after an OAuth connect without reaching back into the DI container.
    /// FULL-only — the type itself is absent from the STORE build.</summary>
    private PluginFeedSource? _feed;

    /// <summary>Subscribe to the off-Store feed's hot-load signal so the first-ever Nexus connect lights
    /// up the Nexus surfaces immediately. Without this, <see cref="NexusActionsAvailable"/> was evaluated
    /// while the registry was still empty (no plugin) and stayed false until the next rescan / game switch.
    /// The event fires on a background thread, so the handler marshals to the UI thread (the same
    /// <see cref="DispatcherQueue"/> the VM uses elsewhere) before re-notifying + reloading rows. Wired
    /// from the MainWindow ctor — keeps the shared ctor untouched and absent from the STORE build.</summary>
    public void WirePluginFeed(PluginFeedSource feed)
    {
        _feed = feed;
        feed.PluginLoaded += (_, _) =>
        {
            async void Apply()
            {
                // async void (DispatcherQueue callback) — never let an exception escape the UI thread.
                try
                {
                    OnPropertyChanged(nameof(NexusActionsAvailable));
                    OnPropertyChanged(nameof(NexusActionsVisibility));
                    OnPropertyChanged(nameof(CatalogAvailable));
                    OnPropertyChanged(nameof(CatalogVisibility));
                    OnPropertyChanged(nameof(CatalogBrowseAvailable));
                    OnPropertyChanged(nameof(CatalogBrowseVisibility));
                    OnPropertyChanged(nameof(CatalogDetailAvailable));
                    OnPropertyChanged(nameof(CatalogDetailVisibility));
                    OnPropertyChanged(nameof(CatalogActionsAvailable));
                    OnPropertyChanged(nameof(CatalogActionsVisibility));
                    // Re-detect + reload rows so per-row Nexus state reflects the now-loaded plugin.
                    // RedetectActiveAsync re-runs the scan with the registered source; it calls
                    // ReloadModsAsync internally (which fires the auto-check poll).
                    await RedetectActiveAsync();
                    // Re-identify what CAN be identified post-install: Vortex-deployed Nexus mods carry the
                    // modId in their manifest (no archive needed). This backfills the ids so the stats sweep
                    // below fills their hearts on hot-load — without it, mods that predate the plugin (and so
                    // were never identified through a then-null source) would stay dark until the next manual
                    // backfill. Best-effort. (Mods raw-dropped before the plugin existed have no recoverable
                    // md5 — the archive is gone — so those genuinely need the manual "Fetch metadata" or a
                    // re-drop; that's an inherent limit of post-extract identify, not a bug.)
                    if (_ctx is not null && NexusSource is { } src)
                    {
                        try { await Scanner.IdentifyVortexNexusAsync(_ctx, src); } catch { /* best-effort identify */ }
                    }
                    // RefreshNexusStatsAsync runs the full RefreshAllAsync sweep — including the one
                    // bulk GetUserEndorsementsAsync → ApplyEndorsements pass that fills hearts — then reloads.
                    // Not debounced: the hot-load event fires exactly once per install and the hearts must be
                    // live immediately, not gated behind 24h.
                    await RefreshNexusStatsAsync();
                }
                catch (Exception ex) { StatusText = ErrorRemedy.Describe(ex); }
            }
            if (_dispatcherQueue is { } dq) dq.TryEnqueue(Apply);
            else Apply();
        };
    }
#endif

    /// <summary>Status dot for the Nexus toolbar button — accent-green when connected, danger-red
    /// when disconnected. The dot IS the affordance now (no separate ACCOUNT section label), so the
    /// state has to read at a glance. Resource-backed brushes so theme switches propagate via
    /// ThemeService.Set's in-place color mutation.</summary>
    public Brush NexusStatusBrush => NexusConnected
        ? ((Brush)Application.Current.Resources["ThemeAccent"])
        : ((Brush)Application.Current.Resources["ThemeDanger"]);

    public string? NexusUser => _nexus.ConnectedUser;
    public bool NexusPremium => _nexus.ConnectedPremium;

    /// <summary>Label for the title-bar account chip. It deliberately does NOT say "Nexus" while
    /// connected: the Browse Nexus action button sits in the same bar, and two controls both reading
    /// "Nexus" (one a status jump-link, one a browse action) reads as a repeat. Connected, the chip is
    /// about WHO you are signed in as, so it shows the account name. Disconnected, it is a call to
    /// action — and the Browse button is hidden in that state, so nothing is duplicated either way.</summary>
    public string NexusChipLabel =>
        !_nexus.IsConnected ? "Connect Nexus"
        : string.IsNullOrWhiteSpace(_nexus.ConnectedUser) ? "Nexus account"
        : _nexus.ConnectedUser!;

    /// <summary>Tooltip for the account chip — carries the Premium/Free tag the compact label drops.</summary>
    public string NexusChipTooltip =>
        !_nexus.IsConnected
            ? "Connect your Nexus Mods account — click to sign in from Settings."
            : $"Signed in to Nexus Mods as {NexusAccountLine} — click to manage in Settings.";

    /// <summary>The connected account line, with a Premium/Free tag — null when not connected.</summary>
    public string? NexusAccountLine =>
        !_nexus.IsConnected ? null : $"{_nexus.ConnectedUser}{(_nexus.ConnectedPremium ? " (Premium)" : " (Free)")}";

    /// <summary>Re-fetch the connected account's identity (name + premium) under the current OAuth bearer,
    /// offline-safe. NOT the token-refresh delegate (<see cref="NexusService.RefreshAsync"/>, which takes a
    /// refresh token) — this re-hits validate.json to refresh the display name + premium tag.</summary>
    public Task RefreshNexusAsync() => _oauth.RefreshIdentityAsync();

    /// <summary>Precondition check for the downloads-folder backfill, run BEFORE the folder picker
    /// opens. Mirrors <see cref="BackfillNexusAsync"/>'s own chain in the same order and with the
    /// same wording — a check that drifts from the operation it guards is worse than no check,
    /// because the user is told one thing and then fails on another. Writes the reason and returns
    /// false when the operation cannot run; the caller simply returns. The first check is deliberately
    /// narrow (source null only) because disconnected and dark-window states have more accurate messages
    /// on the next line via <see cref="NexusUserFeaturesAvailable"/>.</summary>
    public bool CanBackfillFromDownloads()
    {
        if (NexusSource is null) { StatusText = NexusNotConnectedMessage; return false; }
        if (!NexusUserFeaturesAvailable) { StatusText = NexusUnavailableMessage; return false; }
        if (!ActiveGameHasNexusDomain) { StatusText = "This game has no Nexus domain set."; return false; }
        return true;
    }

    /// <summary>Backfill metadata for already-installed mods by md5-matching the user's downloaded
    /// Nexus ARCHIVES (the only thing with the hash Nexus indexes). Each archive's match fills the
    /// metadata for every installed mod that came from it.</summary>
    public async Task BackfillNexusAsync(IReadOnlyList<string> archives)
    {
        if (_ctx is null) return;
        if (NexusSource is not { } source) { StatusText = NexusNotConnectedMessage; return; }
        if (!NexusUserFeaturesAvailable) { StatusText = NexusUnavailableMessage; return; }
        if (string.IsNullOrWhiteSpace(NexusDomains.Effective(_ctx.Game))) { StatusText = "This game has no Nexus domain set."; return; }
        if (archives.Count == 0) { StatusText = "No .zip/.7z/.rar archives found in that folder."; return; }
        IsBusy = true;
        try
        {
            var n = (await Scanner.Md5IdentifyArchivesAsync(_ctx, source, archives)).Matched;
            StatusText = n > 0
                ? $"Backfilled {n} mod{(n == 1 ? "" : "s")} from {archives.Count} Nexus archive(s)."
                : $"Scanned {archives.Count} archive(s) — no Nexus matches (must be the ORIGINAL Nexus archives for this game).";
            await ReloadModsAsync();
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = false; }
    }

    /// <summary>Manual "Refresh Nexus stats": poll Nexus <em>by mod id</em> (no archive needed) over
    /// every identified mod in the active game — refreshing endorsements / downloads / availability and
    /// capturing the upstream current version (which drives the UPDATE chip). The installed version is
    /// preserved (it's the "what you have" side of the compare).
    ///
    /// <para><b>Routes through the loaded Nexus <see cref="IModSource"/> plugin</b> (resolved from the
    /// shared <see cref="ModSourceRegistry"/>) — not Core's <c>NexusClient</c>. The whole sweep is
    /// delegated to <see cref="NexusRefresh.RefreshAllAsync"/>, which fetches each identified mod by id
    /// (selective <c>Overlay</c> — stats + upstream version only, never the manual match's title), then
    /// runs <em>one</em> bulk <see cref="IModSource.GetUserEndorsementsAsync"/> to sync hearts
    /// library-wide (so an endorsement made on the website reflects here, not just the ones toggled in
    /// the launcher). The selective overlay preserves the persisted heart; the bulk sync is the only
    /// writer and is best-effort. A small inter-call delay throttles the sweep; a 429 stops it and
    /// reports partial progress (the rate-limit note). When no plugin is loaded (STORE flavor /
    /// zero-plugins) the source is null and the action is absent.</para></summary>
    public async Task RefreshNexusStatsAsync()
    {
        if (_ctx is null) return;
        if (NexusSource is not { } source) { StatusText = NexusNotConnectedMessage; return; }
        if (!NexusUserFeaturesAvailable) { StatusText = NexusUnavailableMessage; return; }
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
            // (not before) calls so a one-item sweep pays nothing. The sweep also runs the one bulk
            // GetUserEndorsementsAsync -> ApplyEndorsements pass that syncs hearts library-wide.
            var result = await NexusRefresh.RefreshAllAsync(
                identified.Select(kv => kv.Value), domain!, source,
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = false; }
    }

    /// <summary>The Advanced menu's entry into <see cref="FillMissingDetailsAsync"/> — it owns the
    /// busy ring, the Stop button, and the cancellation source, which the unified identify run owns
    /// for itself when it composes the same pass. The pass itself lives in one place only.</summary>
    public async Task EnrichMetadataAsync()
    {
        if (_ctx is null) return;
        var ctx = _ctx!;

        using var cts = new CancellationTokenSource();
        if (!TryBeginLongOp(cts, "Getting details")) return;
        try { await FillMissingDetailsAsync(ctx, cts.Token); }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { EndLongOp(); }
    }

    /// <summary>
    /// Fill in descriptions and cover art for rows we identified but never fully described.
    ///
    /// <para>Needed because the two paths that PRODUCE an identification both leave these holes: a
    /// name search returns no full description, and once a row is identified nothing revisits it —
    /// <c>LooseIdentify.Candidates</c> skips identified rows by design, and the background update
    /// poll only ever looks at mods with a NEW FILE upstream. A three-year-old mod would sit named
    /// and blank forever. This sweep asks Nexus by mod id, which returns the full metadata, and
    /// <c>NexusRefresh.Overlay</c> fills only the holes — anything already known is kept.</para>
    ///
    /// <para>NEVER PRODUCES A REVIEWABLE ROW, and that is the point. Every row it touches already
    /// resolves to a Nexus mod id, so this is detail about an identity that is already established,
    /// not a claim about WHICH mod something is. It runs unconditionally inside the unified run and
    /// reports only through the status line — nothing it does reaches
    /// <see cref="ReviewIdentifyRun"/>. Returns the number of rows filled so a caller can say so.</para>
    /// </summary>
    private async Task<int> FillMissingDetailsAsync(GameContext ctx, CancellationToken ct)
    {
        if (NexusSource is not { } source) { StatusText = NexusNotConnectedMessage; return 0; }
        if (!_nexus.IsConnected) { StatusText = NexusNotConnectedMessage; return 0; }
        var domain = NexusDomains.Effective(ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) { StatusText = "This game has no Nexus domain set."; return 0; }

        var byKey = Scanner.LoadMetadata(ctx);
        var candidates = NexusRefresh.SelectEnrichmentCandidates(byKey.Values);
        if (candidates.Count == 0) { StatusText = "Every identified mod already has its details."; return 0; }

        var progress = new Progress<NexusRefreshProgress>(p =>
            AmbientStatus($"Getting details from Nexus — {p.Completed} of {p.Total}…"));

        var result = await NexusRefresh.RefreshAllAsync(
            candidates, domain!, source, throttle: () => Task.Delay(120), progress: progress, ct: ct);

        // Map each refreshed meta back to its on-disk key by re-resolving the (deterministic)
        // id — identity fields survive the refresh, so the lookup is exact. Same route the
        // background poll uses; never a second persistence path.
        var keyById = new Dictionary<int, string>();
        foreach (var kv in byKey)
            if (NexusRefresh.ResolveModId(kv.Value) is { } id)
                keyById[id] = kv.Key;

        var writes = new List<(string, ModMeta)>();
        foreach (var meta in result.Updated)
            if (NexusRefresh.ResolveModId(meta) is { } id && keyById.TryGetValue(id, out var key))
                writes.Add((key, meta));

        if (writes.Count > 0)
        {
            Scanner.WriteManyMeta(ctx, writes);
            await ReloadModsAsync();
        }

        StatusText = (result.RateLimited, ct.IsCancellationRequested) switch
        {
            (true, _) => $"Nexus rate-limited us after {writes.Count} of {candidates.Count}. Run it again later to finish.",
            (_, true) => $"Stopped after {writes.Count} of {candidates.Count}. Run it again to finish the rest.",
            _ when writes.Count == 0 => "Nexus had no extra details for these mods.",
            _ => $"Filled in details for {writes.Count} mod{(writes.Count == 1 ? "" : "s")}.",
        };
        return writes.Count;
    }

    /// <summary>Name-search identify, step 1 of 2 (review-first): gather the unidentified rows in
    /// ANY location — not just loose-root (<see cref="LooseIdentify.Candidates"/> — loaders, manual
    /// matches, and already-identified rows are excluded), name-search Nexus per row via the loaded
    /// <see cref="IModTextSearch"/> source, and return the proposals for the review dialog. This is
    /// the pass itself, minus the busy/Stop bookkeeping, so a composing caller can run it under its
    /// own token. Null = gated out (the status line explains, including "no loose mods need
    /// identifying"). NOTHING is written here — the only write path is
    /// <see cref="ApplyLooseIdentifyAsync"/> with the rows the user checked.
    /// <paramref name="progress"/> is supplied by the caller so each entry point can word its own
    /// progress line; build it on the UI thread (Progress&lt;T&gt; captures the current
    /// SynchronizationContext, which is what keeps StatusText a UI-thread-only write while the
    /// search workers run concurrently).</summary>
    /// <summary>
    /// Give the swept candidates the same name search the existing rows get.
    ///
    /// <para>Returns the proposal list with every unidentified, non-loader candidate that matched
    /// upgraded from <see cref="AdoptionEvidence.None"/> to <see cref="AdoptionEvidence.NameSearch"/>.
    /// A miss leaves the proposal exactly as it was — still adoptable, still listed, because visible
    /// and unnamed beats invisible.</para>
    ///
    /// <para>Gated exactly like the row search: no source, no connection, or no domain means the
    /// proposals come back untouched rather than annotated with a guess.</para>
    /// </summary>
    private async Task<IReadOnlyList<AdoptionProposal>> NameSweptCandidatesAsync(
        IReadOnlyList<AdoptionProposal> proposals, GameContext ctx, CancellationToken ct)
    {
        var worth = AdoptionProposal.WorthSearching(proposals).ToList();
        if (worth.Count == 0) return proposals;
        if (NexusSource is not IModTextSearch search || !_nexus.IsConnected) return proposals;
        var domain = NexusDomains.Effective(ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) return proposals;

        var named = new Dictionary<string, SourceSearchHit>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        foreach (var p in worth)
        {
            if (ct.IsCancellationRequested) break;
            AmbientStatus($"Naming what we found — {++done} of {worth.Count}…");

            var query = NameMatch.CleanModName(p.Candidate.FileName);
            try
            {
                // Same ladder as the row search: the precise name first, widening only when nothing
                // came back. Scoring always against the FULL query, so retrieval widens and
                // acceptance does not.
                foreach (var rung in NameMatch.QueryLadder(query))
                {
                    if (ct.IsCancellationRequested) break;
                    var hits = await search.SearchAsync(domain!, rung);
                    if (NameMatch.PickBestMatch(query, hits, h => h.Name) is { } hit)
                    { named[p.Candidate.RelativePath] = hit; break; }
                }
            }
            catch (SourceRateLimitException) { break; } // throttled: stop asking, keep what we have
            catch { /* this candidate stays unnamed; the rest still get their turn */ }
        }

        return named.Count == 0
            ? proposals
            : proposals.Select(p => named.TryGetValue(p.Candidate.RelativePath, out var hit)
                    ? AdoptionProposal.FromSearch(p.Candidate, hit)
                    : p)
                .ToList();
    }

    private async Task<IReadOnlyList<LooseIdentifyProposal>?> SearchUnnamedRowsAsync(
        GameContext ctx, IProgress<LooseIdentifyProgress> progress, CancellationToken ct)
    {
        if (NexusSource is not IModTextSearch search) { StatusText = NexusNotConnectedMessage; return null; }
        if (!_nexus.IsConnected) { StatusText = NexusNotConnectedMessage; return null; }
        var domain = NexusDomains.Effective(ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) { StatusText = "This game has no Nexus domain set."; return null; }

        var candidates = LooseIdentify.Candidates(_allRows.Select(r => r.Mod).ToList(), Scanner.LoadMetadata(ctx));
        if (candidates.Count == 0) { StatusText = "No loose mods need identifying."; return null; }

        // The search delegate still self-timeouts per call: a hung Nexus request yields "no
        // hits" for that row after ~10s instead of stalling one of the workers forever. The
        // abandoned call gets its fault observed on completion so a late failure never
        // surfaces as an unobserved-task exception. Cancellation stops NEW rows immediately;
        // rows already in flight finish (or time out), so Stop settles within ~10s worst case.
        // Core's CleanQuery passes through untouched — that's NameMatch's contract, not noise
        // for the App to re-clean.
        var rateLimited = false;
        var proposals = await LooseIdentify.ProposeAsync(candidates, async query =>
        {
            var call = search.SearchAsync(domain!, query);
            if (await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10))) == call)
                return await call;
            _ = call.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return Array.Empty<SourceSearchHit>();
        }, LooseIdentify.DefaultConcurrency, progress, ct, onRateLimited: () => rateLimited = true);

        if (rateLimited)
        {
            // Say WHY. Every row we never reached would otherwise be listed as "no confident match",
            // which reads as a finding about those mods rather than a fact about the connection.
            StatusText = proposals.Count == 0
                ? "Nexus rate-limited us before anything could be searched. Try again later."
                : $"Nexus rate-limited us after {proposals.Count} of {candidates.Count}. Review what was found, then run it again later for the rest.";
            return proposals.Count == 0 ? null : proposals;
        }

        if (proposals.Count == 0)
        {
            // Stopped before anything finished — say so rather than opening an empty dialog.
            StatusText = ct.IsCancellationRequested
                ? "Stopped before anything was searched. Nothing changed."
                : "No matches found on Nexus for these mods.";
            return null;
        }

        if (ct.IsCancellationRequested)
            StatusText = $"Stopped after {proposals.Count} of {candidates.Count}. Review what was found, or run it again for the rest.";
        return proposals;
    }

    /// <summary>Loose-root name-search identify, step 2 of 2: persist ONLY the user-approved pairs.
    /// Each approved hit merges over the row's existing metadata entry via
    /// <see cref="Scanner.MergeMeta"/> — hit wins per field, existing enrichment (InstalledUtc,
    /// description, image, downloads) survives, and a manual match locks and comes back untouched —
    /// then the whole batch lands in one atomic <see cref="Scanner.WriteManyMeta"/> write. Never a
    /// raw overwrite: <see cref="LooseIdentify.ToMeta"/> returns a fresh ModMeta, so replacing the
    /// entry would wipe unrelated fields.
    ///
    /// <para>Returns the number of rows actually written — 0 for every refusal (no context, the
    /// user-features gate, an empty batch, a throw). A composing caller needs that to tell "named 7
    /// mods" from "the write was refused and the reason is already on the status line"; it must not
    /// infer success from the size of the batch it handed in.</para></summary>
    public async Task<int> ApplyLooseIdentifyAsync(IReadOnlyList<(string ModKey, SourceSearchHit Hit)> approved, int proposalCount)
    {
        if (_ctx is null) return 0;
        // Search/propose stays open (unauthenticated GraphQL), but WRITING the identification is a
        // user-scoped action — gate the apply so the dark window never commits a match.
        if (!NexusUserFeaturesAvailable) { StatusText = NexusUnavailableMessage; return 0; }
        if (approved.Count == 0) { StatusText = "No matches approved — nothing written."; return 0; }
        // Save/restore for the same reason ReloadModsAsync does it: this apply is the tail of the
        // unified identify run, which raised the ring itself. A nested callee must not lower a ring
        // it did not raise.
        var wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            var existing = Scanner.LoadMetadata(_ctx);
            var writes = approved
                .Select(a => (a.ModKey, Scanner.MergeMeta(existing.GetValueOrDefault(a.ModKey) ?? new ModMeta(), LooseIdentify.ToMeta(a.Hit))))
                .ToList();
            Scanner.WriteManyMeta(_ctx, writes);
            await ReloadModsAsync();
            StatusText = $"Identified {approved.Count} of {proposalCount} loose mod{(proposalCount == 1 ? "" : "s")}.";
            return writes.Count;
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); return 0; }
        finally { IsBusy = wasBusy; }
    }

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

        // The longest run in the app, behind a menu item nothing disables — the busy ring is 18px and
        // easy to miss, so a second click is the expected mistake, not the exotic one. Two runs in
        // flight would hand Stop only the second one's token, let the second's finally clear the busy
        // and Stop state out from under the first, and end with both reaching ShowAsync — where the
        // second throws "Only a single ContentDialog can be open at any time" and a whole run's
        // proposals are discarded silently. Refuse the second run instead. Not a lock: this is the UI
        // thread, and the only writer.
        var ctx = _ctx!;

        using var cts = new CancellationTokenSource();
        if (!TryBeginLongOp(cts, "Identify")) return;
        try
        {
            // Pass 1 + 2: sweep the game folder and md5 what it found. Already tiered internally.
            StatusText = "Looking through this game's folder…";
            var adoptions = await BuildDiscoveryProposalsAsync(ctx, cts.Token);

            // Pass 2b: the downloads folder the user pointed us at, if any. Exact matches.
            // Its own outcome comes back as a note rather than a status write, because four more
            // passes will overwrite the status line before the user reads any of it — a user who
            // took the trouble to pick a folder has to be told what it contributed, at the end.
            string? downloadsNote = null;
            if (!string.IsNullOrWhiteSpace(downloadsFolder) && !cts.IsCancellationRequested)
            {
                StatusText = "Matching your downloads folder…";
                (adoptions, downloadsNote) =
                    await AddDownloadsFolderMatchesAsync(adoptions, downloadsFolder!, ctx, cts.Token);
            }

            // Pass 3: fill blanks on rows we already identified. NOT reviewable — we already know
            // which mod these are; this only retrieves detail about it. It writes its partial batch
            // on cancellation, so `filled` is carried into EVERY terminal line below, cancelled or
            // not — a run that filled eight rows and then stopped must never report "nothing".
            var filled = 0;
            if (!cts.IsCancellationRequested) filled = await FillMissingDetailsAsync(ctx, cts.Token);

            // Pass 4: name-search whatever is still unnamed. Called directly at the pass level
            // (SearchUnnamedRowsAsync), under THIS run's own token — a wrapper that owned its own
            // busy/Stop state would void this run's busy ring for everything after it and steer
            // Stop at a token this run never checks.
            var searchProgress = new Progress<LooseIdentifyProgress>(p =>
                AmbientStatus($"Searching Nexus for names — {p.Completed} of {p.Total}…"));
            IReadOnlyList<LooseIdentifyProposal> identifications = Array.Empty<LooseIdentifyProposal>();
            // Null means the pass gated out or found nothing, and it has already written the SPECIFIC
            // reason (not connected / no domain / no loose mods need identifying / no matches). Keep
            // those words: the run must not answer "why is my list unchanged?" with "everything is
            // already identified", a claim it never got far enough to test.
            string? searchNote = null;
            if (!cts.IsCancellationRequested)
            {
                var found = await SearchUnnamedRowsAsync(ctx, searchProgress, cts.Token);
                if (found is null) searchNote = StatusText;
                else identifications = found;

                // Swept files are not rows yet, so the pass above — which searches _allRows — never
                // sees them. Without this they are proposed as "not identified", adopted, become
                // rows, and only a SECOND run names them, with nothing telling the user that a
                // second run was worth doing. Same tier, same ladder, same review gate.
                if (!cts.IsCancellationRequested)
                    adoptions = await NameSweptCandidatesAsync(adoptions, ctx, cts.Token);
            }

            var stopped = cts.IsCancellationRequested;

            if (adoptions.Count == 0 && identifications.Count == 0)
            {
                StatusText = IdentifyRunReport.Summarize(new IdentifyRunOutcome
                {
                    Filled = filled,
                    Stopped = stopped,
                    DownloadsNote = downloadsNote,
                    NothingHappenedLine = searchNote,
                });
                return;
            }

            if (ReviewIdentifyRun is null) return; // unwired view -> nothing written

            // Stop pressed mid-sweep still opens the review — whatever settled is real and worth
            // approving — but the line behind the dialog must not still read "Stopping…".
            if (stopped)
                StatusText = "Stopped early. Review what was found, or run it again for the rest.";

            var (approvedAdoptions, approvedIdentifications) = await ReviewIdentifyRun(adoptions, identifications);

            // Strongest first — see the apply-order note above.
            var written = await ApplyDiscoveriesAsync(approvedAdoptions, adoptions.Count, ctx);

            // Approvals went in and no key came out. The apply distinguishes three different reasons
            // for that (the archive maps to nothing installed yet / every key was already identified
            // / neither) and has put the right one on the status line. Capture it — the zero-key case
            // is the downloads-folder NORM, so a user who just approved "Adopt 3 mods" would
            // otherwise be told "Nothing was changed." with the explanation deleted.
            string? adoptionNote = null;
            if (approvedAdoptions.Count > 0 && written.Count == 0) adoptionNote = StatusText;

            var safeIdentifications = LooseIdentify.ExcludeKeys(approvedIdentifications, written);
            var dropped = approvedIdentifications.Count - safeIdentifications.Count;

            // Only call the name-search apply when it has something to write. Standalone it reports
            // "No matches approved — nothing written." on an empty batch, which is right for the
            // Advanced entry but would overwrite the adoption result the user just earned here.
            string? identifyNote = null;
            var named = 0;
            if (safeIdentifications.Count > 0)
            {
                named = await ApplyLooseIdentifyAsync(safeIdentifications, identifications.Count);
                // Same shape as the adoption capture above: a batch went in and nothing came out, so
                // the apply refused (signed out, or it threw) and its reason is the useful part.
                // Carried into the summary rather than returned early — bailing here would drop the
                // adoptions and fills this run already wrote, which breaks the very rule the
                // composer exists to enforce.
                if (named == 0) identifyNote = StatusText;
            }

            StatusText = IdentifyRunReport.Summarize(new IdentifyRunOutcome
            {
                Adopted = written.Count,
                Named = named,
                Filled = filled,
                DroppedNameMatches = dropped,
                Stopped = stopped,
                AdoptionNote = adoptionNote,
                IdentifyNote = identifyNote,
                DownloadsNote = downloadsNote,
            });
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { EndLongOp(); }
    }

    /// <summary>Pass 2b of the unified run: md5 the archives in a user-chosen downloads folder
    /// against Nexus and fold the exact matches into the adoption proposals. Exact evidence, so
    /// these ride the same reviewable path as the sweep's own md5 tier — nothing is written here.
    ///
    /// <para>The candidate's <c>RelativePath</c> carries the archive's ABSOLUTE path on purpose: a
    /// downloads folder normally sits outside the game root (often on another drive), so there is no
    /// relative form to give it. Both places that resolve a candidate back to disk —
    /// <c>DiscoveryScanService.Md5Of</c> and <see cref="DiscoveryWriteKeysAsync"/> — go through
    /// <c>Path.Combine(root, RelativePath)</c>, which hands back an already-rooted second argument
    /// unchanged. These candidates never reach <c>DiscoverySweep</c>, whose skip/mod-path matching is
    /// the only code that actually requires the relative form.</para></summary>
    private async Task<(IReadOnlyList<AdoptionProposal> Proposals, string? Note)> AddDownloadsFolderMatchesAsync(
        IReadOnlyList<AdoptionProposal> existing, string folder, GameContext ctx, CancellationToken ct)
    {
        // Same gate as the sweep's tier 1: no plugin, signed out, or no domain all collapse to
        // "md5 unavailable". Report it as a note and leave the other passes to carry the run.
        var domain = NexusDomains.Effective(ctx.Game);
        if (NexusSource is not { } source || !_nexus.IsConnected || string.IsNullOrWhiteSpace(domain))
            return (existing, "That downloads folder was skipped — matching by file hash needs Nexus connected.");

        // Never throws: the folder was picked by the user and can be gone, unreadable, or on a drive
        // that went away between the picker and here. An empty list reads as "nothing to match" and
        // the rest of the run stands, which is what this pass's whole degrade-gracefully shape is for.
        var archives = EnumerateDownloadArchives(folder);
        if (archives.Count == 0)
            return (existing, "No .zip/.7z/.rar archives were readable in that downloads folder.");

        // Never propose the same archive twice. A downloads folder pointed INSIDE the game root was
        // already walked by pass 1, and the sweep's own copy carries the relative path the review
        // dialog reads better. Filename is the right identity — it is the same file either way.
        var seen = existing.Select(p => p.Candidate.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<AdoptionProposal>();
        var checkedCount = 0;
        // Set from the cap BREAK, never from "folder size > cap". Duplicates already claimed by the
        // game-folder sweep are skipped below without charging the budget, so a 400-archive folder
        // sitting inside the game root can run to completion having checked 40 — telling that user
        // to "check the rest" would point at a rest that does not exist.
        var truncated = false;
        foreach (var path in archives)
        {
            if (ct.IsCancellationRequested) break;
            if (checkedCount >= DownloadsMd5Cap) { truncated = true; break; }

            var fileName = Path.GetFileName(path);
            if (!seen.Add(fileName)) continue;
            checkedCount++;

            AmbientStatus($"Matching your downloads folder — {checkedCount} of {Math.Min(archives.Count, DownloadsMd5Cap)}…");
            var candidate = new DiscoveryCandidate(path, fileName, DiscoveryKind.Archive);
            var md5 = await Task.Run(() => _discovery.Md5Of(ctx.GameRoot, candidate));
            if (md5 is null) continue;

            try
            {
                var identify = await source.IdentifyByHashAsync(domain!, md5);
                if (identify is not null) added.Add(AdoptionProposal.FromMd5(candidate, identify));
            }
            catch { /* a miss / outage never blocks the run — the other passes still stand */ }
        }

        // Wording and every "should we even say this" branch live in Core behind tests — see
        // IdentifyRunReport.DownloadsFolderNote.
        var note = IdentifyRunReport.DownloadsFolderNote(
            archivesFound: archives.Count, checkedCount: checkedCount, matched: added.Count,
            truncated: truncated, stopped: ct.IsCancellationRequested);

        return (added.Count == 0 ? existing : existing.Concat(added).ToList(), note);
    }

    /// <summary>The archives inside a user-chosen downloads folder. One implementation, shared by
    /// the unified run's downloads pass and the Advanced "match against my downloads folder" entry
    /// in the window, so the two can never disagree about what counts as an archive.
    ///
    /// <para>Never throws. The folder came from a picker and can be deleted, unmounted, or
    /// permission-denied by the time we walk it; both callers treat an empty list as "nothing to
    /// match" and carry on, and one of them is an <c>async void</c> event handler where an escaping
    /// exception would take the process down.</para></summary>
    public static IReadOnlyList<string> EnumerateDownloadArchives(string folder)
    {
        try
        {
            // Recurse — a downloads folder usually nests archives in per-mod subfolders.
            // IgnoreInaccessible so one locked subfolder can't throw away everything already
            // found — the same posture DiscoveryScanService.Walk takes on the game folder.
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            return Directory.GetFiles(folder, "*.*", options)
                .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        // The ROOT being gone / unreadable / malformed still throws past IgnoreInaccessible.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    // Nexus md5 lookups are network calls against a per-day budget (2500/day). A pathological
    // Downloads-folder-in-the-game-root case could otherwise turn one sweep into hundreds of calls;
    // cap the archive md5 tier per run regardless of trigger (auto or manual) — candidates past the
    // cap simply fall to tier 2 (name index) / tier 3 (unidentified), never blocked or dropped.
    private const int DiscoveryMd5TierCap = 25;

    // The unified run's downloads-folder pass is the same archive-md5 tier against the same day
    // budget, so it caps too — "regardless of trigger" above means this one as well. It gets a
    // bigger number because the gesture is different: the sweep stumbles onto whatever archives
    // happen to sit in a game folder, while here the user deliberately pointed at a folder OF
    // archives and expects them checked. 100 is ~4% of the day budget per click. The cap is never
    // silent — AddDownloadsFolderMatchesAsync reports how many of how many it actually checked,
    // because a truncated run that says nothing reads as a complete one.
    private const int DownloadsMd5Cap = 100;

    /// <summary>Sweep this game's folder for mods the launcher didn't install, identify what we can,
    /// and offer them for adoption. READ-ONLY until the user approves — adoption writes METADATA
    /// ONLY, never a file (the first move is the user's first toggle, through the existing
    /// move-to-holding path). Best evidence first: an archive candidate's Nexus md5 (exact,
    /// authoritative) beats a per-game name-index hit, which beats "found, unidentified" — still
    /// listed and adoptable, because visible-but-unnamed beats invisible. Never re-proposes a mod
    /// the launcher already manages and already identified (manual match, a Nexus id, or any prior
    /// source confidence) — the feature promises mods the launcher didn't install, not a second
    /// opinion on the ones it did.
    /// <paramref name="auto"/> true = the silent first-add run (says nothing when it finds nothing);
    /// false = the "Find existing mods" menu item, which always reports back.</summary>
    public async Task DiscoverExistingModsAsync(bool auto)
    {
        if (_ctx is null) return;
        var ctx = _ctx!;

        // BuildDiscoveryProposalsAsync sets StatusText itself for every "nothing found" reason,
        // gated on auto exactly as this method used to gate it inline before the split.
        var proposals = await BuildDiscoveryProposalsAsync(ctx, CancellationToken.None, auto);
        if (proposals.Count == 0) return;

        if (ReviewDiscoveries is null) return; // unwired view -> nothing adopted, but the sweep itself still ran
        var approved = await ReviewDiscoveries(proposals);

        // ApplyDiscoveriesAsync sets StatusText itself for the write outcome, same auto gating.
        await ApplyDiscoveriesAsync(approved, proposals.Count, ctx, auto);
    }

    /// <summary>Sweep + classify + tier-match, stopping BEFORE review. Split out of
    /// <see cref="DiscoverExistingModsAsync"/> so the unified identify run can compose discovery
    /// with the other passes behind a single review dialog. Writes nothing.
    /// <paramref name="auto"/> mirrors <see cref="DiscoverExistingModsAsync"/>'s parameter of the
    /// same name and gates the same "nothing found" status lines below; it defaults to false so a
    /// caller composing a bigger run (with no "auto" concept of its own, e.g. the unified identify
    /// run) always gets the status line instead of silent no-ops.</summary>
    private async Task<IReadOnlyList<AdoptionProposal>> BuildDiscoveryProposalsAsync(
        GameContext ctx, CancellationToken ct, bool auto = false)
    {
        // Skip the launcher's own holding folders plus anything another manager has taken over.
        // ctx.TakenOver is already the resolved, loaded set (Scanner.GameContext loads it once from
        // taken-over.json) — reuse it instead of re-reading the file. It's ABSOLUTE paths
        // (TakenOverStore.Add(dataDir, folderAbs)), but DiscoverySweep's skip-matching is RELATIVE to
        // the swept root, so each entry is rebased against GameRoot via RelativeToGameRoot (also
        // drops a path that resolves outside the root, or onto another drive entirely).
        var skipFolders = new List<string> { "_626mods", "loose-disabled", "disabled" };
        foreach (var takenOverAbs in ctx.TakenOver)
            if (RelativeToGameRoot(takenOverAbs, ctx.GameRoot) is { } rel)
                skipFolders.Add(rel);

        // Sweep EVERY configured mod location, not just the first — ModLocator.Detect persists all
        // existing candidate folders, so a UE4SS game can have both ~mods AND LogicMods at once.
        // Hand-installed Blueprint mods sitting only in LogicMods would otherwise stay invisible,
        // exactly the case this feature exists to catch. Each location's path can itself be
        // absolute (Scanner.GameContext resolves it that way when Path.IsPathRooted), so rebase
        // each one the same way as the taken-over folders. PaksRoot flags the loader-less UE-pak
        // form (ModLocation.Form == "paks-root", e.g. Witchfire) where the mod folder IS
        // Content/Paks itself — DiscoverySweep uses it to refuse the game's own shipped paks
        // (PakClassifier.IsBaseGamePak), the one property this feature must never violate.
        var modPaths = new List<DiscoverySweepModPath>();
        foreach (var loc in ctx.Game.ModLocations)
            if (RelativeToGameRoot(loc.Path, ctx.GameRoot) is { } rel)
                modPaths.Add(new DiscoverySweepModPath(rel, loc.Form == "paks-root"));

        var options = new DiscoverySweepOptions(
            ModPaths: modPaths,
            // The RAW registry entry, not a preset lookup and not ctx.Exts: the manifest ships
            // per-game overrides (e.g. Cyberpunk 2077 -> ["archive"], not the "custom" preset's
            // ["pak"]), and ctx.Exts is normalized empty->["pak"] (Scanner.cs), which would make
            // EngineShaped fire wrongly for fromsoft's genuinely-empty, folder-based extension list.
            // ctx.Game.FileExtensions is the exact value ctx.FileRe (and therefore Scanner.ModKeyFor)
            // is built from — using anything else would let the sweep and the key formula disagree.
            EngineExtensions: ctx.Game.FileExtensions,
            SkipFolders: skipFolders);

        // DiscoveryScanService.Sweep walks the whole game folder synchronously — keep it off the UI
        // thread so a large, years-old install can't freeze the window.
        var candidates = await Task.Run(() => _discovery.Sweep(ctx.GameRoot, options));
        if (candidates.Count == 0)
        {
            if (!auto) StatusText = "No unmanaged mods found in this game's folder.";
            return Array.Empty<AdoptionProposal>();
        }

        // Never re-propose a candidate whose best-guess key is already identified (manual match, a
        // Nexus id, or any prior source confidence) — mirrors LooseIdentify.Candidates' exact rule
        // for exactly the same reason: a name-index hit on an already md5-identified row would
        // downgrade SourceConfidence from "md5" to "nameSearch" and could point endorse/update-check
        // at the wrong mod page. The pre-filter key is a best guess (the real archive-contents key
        // isn't known until identify + ArchiveModKeysFor below), which is fine — it only needs to
        // catch the common EngineShaped case; a false negative here just means the tier logic below
        // re-derives the same "already identified" outcome a step later, never a wrong write.
        // File space -> mod-key space. The sweep finds FILES; the launcher lists MODS, and one mod
        // is routinely several files (a UE mod ships pak + ucas + utoc, which Scanner folds onto a
        // single key — see the outMap/mod.Files.Add grouping in Scanner's pak scan). Collapsing
        // here is what makes the review dialog's "Adopt N mods" count mods instead of files.
        candidates = DiscoverySweep.Deduplicate(candidates, c => DiscoveryBestGuessKey(c, ctx));

        // Never re-offer a mod the launcher ALREADY lists. Adoption's promise is "this lists it so
        // you can turn it on and off"; for a row that already exists and already toggles, that
        // promise is met, and re-proposing it made a years-old install look like the sweep wanted
        // to re-add the whole mod list. Naming an already-listed-but-unnamed row is a real job, but
        // it belongs to LooseIdentify — which now reaches these rows on every game shape.
        //
        // EngineShaped ONLY, deliberately: its key IS Scanner.ModKeyFor, the exact formula behind
        // Mod.Base, so the comparison is exact. An Archive's pre-filter key is a weak guess at its
        // own filename (its real keys come from its CONTENTS at write time), so excluding archives
        // on a stem collision would throw away the md5 tier's shot at an exact identification — the
        // strongest evidence we have — to prevent a duplicate that IsAlreadyIdentified already
        // catches at write time.
        var rowKeys = _allRows.Select(r => r.Mod.Base).Where(k => !string.IsNullOrWhiteSpace(k));
        var engineShaped = candidates.Where(c => c.Kind == DiscoveryKind.EngineShaped).ToList();
        var keptEngineShaped = DiscoverySweep
            .ExcludeKnownKeys(engineShaped, c => DiscoveryBestGuessKey(c, ctx), rowKeys)
            .ToHashSet();
        candidates = candidates
            .Where(c => c.Kind != DiscoveryKind.EngineShaped || keptEngineShaped.Contains(c))
            .ToList();
        if (candidates.Count == 0)
        {
            if (!auto) StatusText = "Every mod in this game's folder is already in your list.";
            return Array.Empty<AdoptionProposal>();
        }

        // Pre-dialog snapshot for the PRE-FILTER only — deliberately not threaded into
        // ApplyDiscoveriesAsync's own LoadMetadata read below. See that call's comment for why the
        // two reads are intentionally separate rather than one shared snapshot.
        var existing = Scanner.LoadMetadata(ctx);
        var unmanaged = candidates.Where(c => !IsAlreadyIdentified(existing, DiscoveryBestGuessKey(c, ctx))).ToList();
        if (unmanaged.Count == 0)
        {
            if (!auto) StatusText = "No unmanaged mods found in this game's folder.";
            return Array.Empty<AdoptionProposal>();
        }

        var domain = NexusDomains.Effective(ctx.Game);
        var source = NexusSource;

        // Ensure the per-game name index is warm BEFORE tier 2 reads it. MaybeSeedAsync is ALSO
        // fired fire-and-forget from ReloadModsAsync's tail on every game load (general index
        // freshness for catalog browse / a later manual run), but that race can't be trusted to
        // finish before THIS run needs it — a brand-new game's index file doesn't exist yet, so an
        // unseeded Load() would leave tier 2 dead on the very first, flagship auto-run. Awaited and
        // non-fatal: MaybeSeedAsync already swallows its own failures, so a seed miss here just
        // means tier 2 degrades to its already-supported "index empty" case.
        await _nameIndex.MaybeSeedAsync(ctx.DataDir, ctx.Game.Id, domain, _nexus.IsConnected, _appSettings.AutoCheckModUpdates, source);
        var index = _nameIndex.Load(ctx.DataDir);

        // Degrade gracefully: no plugin loaded, signed out, or no Nexus domain for this game all
        // collapse to "md5 tier unavailable" — the sweep still runs and falls to the name index /
        // unidentified tiers below, never silently doing nothing.
        var md5Available = _nexus.IsConnected && source is not null && !string.IsNullOrWhiteSpace(domain);
        var md5Attempts = 0;

        var proposals = new List<AdoptionProposal>(unmanaged.Count);
        foreach (var candidate in unmanaged)
        {
            if (ct.IsCancellationRequested) break;

            AdoptionProposal? proposal = null;

            // Tier 1: archive md5 — Nexus hashes the PUBLISHED archive, so this only ever applies to
            // Archive candidates (Md5Of returns null for anything else). Pass the FULL identify
            // result into FromMd5 — AdoptionProposal.ToMeta() routes it through
            // SourceMetadataMapper.FromIdentify so Version (and everything else that mapper
            // populates) survives; hand-copying a subset here would leave Version null and light a
            // false UPDATE chip on every md5-adopted mod.
            if (md5Available && candidate.Kind == DiscoveryKind.Archive && md5Attempts < DiscoveryMd5TierCap)
            {
                md5Attempts++;
                var md5 = await Task.Run(() => _discovery.Md5Of(ctx.GameRoot, candidate));
                if (md5 is not null)
                {
                    try
                    {
                        var identify = await source!.IdentifyByHashAsync(domain!, md5);
                        if (identify is not null) proposal = AdoptionProposal.FromMd5(candidate, identify);
                    }
                    catch { /* a miss / outage never blocks the sweep — fall through to tier 2 */ }
                }
            }

            // Tier 2: per-game name index — the load-bearing tier for extracted mods.
            if (proposal is null)
            {
                var hit = index.Match(candidate.FileName);
                proposal = hit is not null ? AdoptionProposal.FromIndex(candidate, hit) : AdoptionProposal.Unidentified(candidate);
            }

            proposals.Add(proposal);
        }

        return proposals;
    }

    /// <summary>Persist approved adoptions. Returns the mod keys actually written, so the unified
    /// run can stop a weaker later pass from overwriting them (see LooseIdentify.ExcludeKeys).
    /// <paramref name="auto"/> mirrors <see cref="DiscoverExistingModsAsync"/>'s parameter of the
    /// same name and gates the same "nothing adopted" status lines below; it defaults to false so a
    /// caller composing a bigger run (with no "auto" concept of its own) always gets the status
    /// line.</summary>
    private async Task<IReadOnlyList<string>> ApplyDiscoveriesAsync(
        IReadOnlyList<AdoptionProposal> approved, int proposalCount, GameContext ctx, bool auto = false)
    {
        if (approved.Count == 0)
        {
            if (!auto) StatusText = "Nothing adopted.";
            return Array.Empty<string>();
        }

        // Adoption is metadata-only — no file is moved, renamed, or deleted. One atomic batch write
        // through Scanner.WriteManyMeta, the same route ApplyLooseIdentifyAsync uses above; never a
        // second persistence path. MergeMeta(existing, hit) — the proposal wins per field, existing
        // enrichment (InstalledUtc, image, downloads…) survives, and a manual match still locks.
        //
        // A single proposal can expand to MULTIPLE write keys: an md5-identified archive's metadata
        // belongs to whatever mod keys ITS CONTENTS install under (Scanner.ArchiveModKeysFor — same
        // derivation Scanner.Md5IdentifyArchivesAsync uses), never the archive's own download
        // filename (a real Nexus download like "FasterShips-42-1-0-1699999.zip" would never match
        // the installed file's key). When an identified archive's contents don't map to any known
        // mod key, it expands to zero writes rather than a wrong or inert fallback key.
        //
        // Read AFTER review, not carried over from the propose phase (BuildDiscoveryProposalsAsync
        // has its own, earlier LoadMetadata call for its pre-filter only — see there). Before this
        // method existed, one `existing` snapshot taken pre-dialog was reused for this same
        // already-identified re-check post-dialog, so the guard couldn't see anything written while
        // the review dialog sat open on the UI dispatcher. Re-reading here narrows that window on
        // purpose: the guard now sees anything written during the dialog, including — the reason
        // this matters — an earlier pass of the same unified identify run (md5 adoptions apply
        // before name-search matches; the name-search guard must observe what md5 just wrote, or
        // LooseIdentify.ExcludeKeys' whole reason for existing is defeated). This is an intentional,
        // signed-off behavior narrowing versus the pre-split code, not an equivalent refactor.
        var existing = Scanner.LoadMetadata(ctx);
        var writes = new List<(string ModKey, ModMeta Meta)>();
        // Tracked separately so the "nothing happened" status line (below) can say WHY instead of
        // reusing one caption for two different outcomes: an archive whose contents don't map to
        // any known mod key vs. every resolved key turning out to already be identified.
        var anyZeroKeyProposal = false;
        var anyAlreadyIdentifiedDrop = false;
        foreach (var p in approved)
        {
            var meta = p.ToMeta();
            var keys = await DiscoveryWriteKeysAsync(p, ctx);
            if (keys.Count == 0) { anyZeroKeyProposal = true; continue; }
            foreach (var key in keys)
            {
                // The candidate-level pre-filter above can't see this key for an archive proposal —
                // ArchiveModKeysFor only resolves it here, at write time. Re-check it before writing:
                // a stale leftover archive (an old v1.0 download still sitting next to a correctly
                // identified, updated v2.0 install) would otherwise md5-identify successfully and
                // overwrite the row's Version with the stale one (SourceMetadataMapper.FromIdentify
                // sets Version = the archive's own version, and MergeMeta lets the "new" hit win),
                // planting a permanent false UPDATE chip on a row that was correct before the sweep.
                if (IsAlreadyIdentified(existing, key)) { anyAlreadyIdentifiedDrop = true; continue; }
                writes.Add((key, Scanner.MergeMeta(existing.GetValueOrDefault(key) ?? new ModMeta(), meta)));
            }
        }

        if (writes.Count == 0)
        {
            if (!auto)
            {
                StatusText = anyZeroKeyProposal && !anyAlreadyIdentifiedDrop
                    ? "Nothing to adopt — the matched archive doesn't correspond to an installed file yet."
                    : anyAlreadyIdentifiedDrop && !anyZeroKeyProposal
                        ? "Nothing to adopt — those mods are already identified."
                        : "Nothing to adopt.";
            }
            return Array.Empty<string>();
        }

        Scanner.WriteManyMeta(ctx, writes);

        var adoptedCount = writes.Select(w => w.ModKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        StatusText = adoptedCount == 1
            ? "Adopted 1 mod. Your files were not moved."
            : $"Adopted {adoptedCount} mods. Your files were not moved.";
        await ReloadModsAsync();

        return writes.Select(w => w.ModKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when <paramref name="key"/> already has an identity the sweep must never
    /// overwrite — a manual match, a Nexus id, or any prior source confidence. Mirrors
    /// <see cref="LooseIdentify.Candidates"/>'s exact predicate for the exact same reason: a weaker
    /// tier (name-index or "found, unidentified") must never downgrade a stronger existing match.</summary>
    private static bool IsAlreadyIdentified(IReadOnlyDictionary<string, ModMeta> existing, string key)
        => existing.TryGetValue(key, out var meta) && (meta.IsManual || meta.NexusModId is not null || meta.SourceConfidence is not null);

    /// <summary>The best-guess metadata key for a raw candidate BEFORE any tier has run — used only to
    /// pre-filter already-identified rows out of the proposal list. An EngineShaped candidate's real
    /// key is knowable up front (<see cref="Scanner.ModKeyFor"/>); everything else (Signature,
    /// Archive) falls back to the extension-stripped filename, which is good enough for this filter's
    /// job (a false negative here just costs a redundant tier-2/3 proposal, never a wrong write —
    /// see <see cref="DiscoveryWriteKeysAsync"/> for the write-time key, which is authoritative).</summary>
    private static string DiscoveryBestGuessKey(DiscoveryCandidate candidate, GameContext ctx)
        => candidate.Kind == DiscoveryKind.EngineShaped
            ? Scanner.ModKeyFor(candidate.FileName, ctx)
            : Path.GetFileNameWithoutExtension(candidate.FileName);

    /// <summary>The metadata key(s) an APPROVED proposal writes to. Computed at write time (not
    /// propose time) so a from-md5 archive proposal can resolve its real content-derived keys only
    /// for the rows the user actually approved. An <see cref="DiscoveryKind.EngineShaped"/> hit goes
    /// through <see cref="Scanner.ModKeyFor"/> so the adopted title/author land on the exact row the
    /// regular scan builds for that file. An md5-identified <see cref="DiscoveryKind.Archive"/> goes
    /// through <see cref="Scanner.ArchiveModKeysFor"/> — the archive's CONTENTS, never its own
    /// filename — and can legitimately return zero or several keys. Everything else (Signature, or an
    /// Archive that only cleared tier 2/3) has no scanned row to align with yet, so it keys off the
    /// extension-stripped filename as harmless bookkeeping.</summary>
    private async Task<IReadOnlyList<string>> DiscoveryWriteKeysAsync(AdoptionProposal p, GameContext ctx)
    {
        if (p.Evidence == AdoptionEvidence.Md5 && p.Candidate.Kind == DiscoveryKind.Archive)
        {
            var abs = Path.Combine(ctx.GameRoot, p.Candidate.RelativePath);
            return await Task.Run(() => Scanner.ArchiveModKeysFor(abs, ctx));
        }
        if (p.Candidate.Kind == DiscoveryKind.EngineShaped)
            return new[] { Scanner.ModKeyFor(p.Candidate.FileName, ctx) };

        return new[] { Path.GetFileNameWithoutExtension(p.Candidate.FileName) };
    }

    /// <summary>Rebase a registry-supplied path (which may be absolute OR relative — the same
    /// ambiguity <see cref="Scanner.GameContext"/> resolves for <c>ModLocationCtx.Abs</c>) onto
    /// "relative to <paramref name="gameRoot"/>, forward-slashed" — the shape
    /// <see cref="DiscoverySweep"/>'s skip/mod-path matching expects. Null input, a path that
    /// resolves outside the root, or a path on another drive (which makes
    /// <see cref="Path.GetRelativePath(string,string)"/> hand back an absolute path unchanged) all
    /// return null — the caller drops it rather than pass through something that would either never
    /// match or match the wrong thing.</summary>
    private static string? RelativeToGameRoot(string? path, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var abs = Path.IsPathRooted(path) ? path : Path.Combine(gameRoot, path);
        var rel = Path.GetRelativePath(gameRoot, abs).Replace('\\', '/');
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return null;
        return rel;
    }

    /// <summary>One-click endorse ⇄ abstain for a Nexus-identified row — the give-back half of the
    /// Nexus loop, honors-the-builders and never automatic (one user click per write). Picks the
    /// direction from the row's current <see cref="Mod.Endorsed"/> (endorsed → abstain, else endorse),
    /// version-stamps the POST with the installed version (falling back to the upstream latest), and
    /// only flips the heart + persists when Nexus accepts the write — a refusal (not downloaded / too
    /// soon / any other precondition) surfaces the friendly reason in the status line and leaves the
    /// row honest (no optimistic flip). A 429 / network failure degrades to a refusal line; nothing
    /// throws to the UI.
    ///
    /// <para><b>Routes through the loaded Nexus <see cref="IModSource"/> plugin</b> (resolved from the
    /// shared <see cref="ModSourceRegistry"/>) via <see cref="IModSource.SetEndorsedAsync"/> — not Core's
    /// <c>NexusClient</c>. The plugin degrades every non-2xx (preconditions, 429, offline) to
    /// <c>Refused = true</c> with a message, so this path never sees a throw from the write. When no
    /// plugin is loaded the heart is absent (the row gates on <c>NexusActionsAvailable</c>).</para></summary>
    public async Task ToggleEndorseAsync(ModRowViewModel row)
    {
        if (_ctx is null) return;
        if (NexusSource is not { } source) { StatusText = NexusNotConnectedMessage; return; }
        if (!NexusUserFeaturesAvailable) { StatusText = NexusUnavailableMessage; return; }
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

        var endorse = row.Mod.Endorsed != true; // endorsed -> abstain, else endorse
        var version = row.Mod.Version ?? row.Mod.NexusLatestVersion ?? "";
        var name = row.DisplayName;
        var modRef = new SourceModRef(SourceId: source.Id, GameDomain: domain!, ModId: modId, Version: version);

        IsBusy = true;
        try
        {
            var result = await source.SetEndorsedAsync(modRef, endorse);
            if (!result.Ok)
            {
                // The row stays honest — no flip on a refusal; surface the friendly reason.
                StatusText = result.Message ?? "Nexus declined the endorsement.";
                return;
            }

            // Persist Endorsed onto the existing metadata entry (mutate-in-place) so the rest of the
            // mod's enrichment — title, credit, NexusModId — survives the write. Endorsed is persisted
            // user intent, so it must outlive a rescan. Trust the source's reported NowEndorsed when set,
            // else the direction we asked for.
            row.Mod.Endorsed = result.NowEndorsed ?? endorse;
            meta.Endorsed = row.Mod.Endorsed;
            Scanner.WriteOneMeta(_ctx, row.Mod.Name, meta);
            row.NotifyEndorseChanged();

            StatusText = (result.NowEndorsed ?? endorse)
                ? $"Endorsed \"{name}\" on Nexus."
                : $"Retracted endorsement for \"{name}\".";
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
                        StatusText = NexusNotConnectedMessage;
                        return false;
                    }
                    if (NexusSource is not { } nexusSource)
                    {
                        StatusText = NexusNotConnectedMessage;
                        return false;
                    }
                    // Manual match is identity-authoritative: the user told us exactly which mod this is.
                    // SourceMetadataMapper.Apply (NOT NexusRefresh.Overlay) is right here — it writes the
                    // identity/credit fields, with NexusModId pinned from the parsed URL.
                    var modId = int.Parse(parts.ModRef);
                    var dto = await nexusSource.FetchMetadataAsync(new SourceModRef("nexus", parts.GameKey, modId, ""));
                    if (dto is not null)
                    {
                        // Apply maps dto.LatestVersion -> NexusLatestVersion and never writes the installed
                        // Version (Version is owned by the identify path's file context). A manual match has
                        // no file context, so seed Version from LatestVersion up front — Apply never touches
                        // Version, so it survives. Without this, Version=null + NexusLatestVersion=<v> lights
                        // a false UPDATE chip on a freshly matched mod (UpdateAvailable = latest != installed).
                        hit = SourceMetadataMapper.Apply(new ModMeta { NexusModId = modId, Version = dto.LatestVersion }, dto);
                    }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); return false; }
    }

    /// <summary>Connect the user's Nexus account via the loopback PKCE OAuth flow (system browser). No key
    /// is ever pasted or baked — <see cref="NexusOAuthService.ConnectAsync"/> owns the browser round-trip and
    /// hands the tokens to <see cref="NexusService"/> (DPAPI) on success. On success, re-notify the Nexus
    /// surfaces and kick off the consented first-install plugin fetch (FULL only). On failure/dark-window,
    /// the reason lands in the status line.</summary>
    public async Task ConnectNexusAsync()
    {
        try
        {
            var r = await _oauth.ConnectAsync(CancellationToken.None);
            if (r.Ok)
            {
                StatusText = $"Connected to Nexus as {NexusAccountLine}.";
                RaiseNexusStateChanged();
#if FULL
                // First-ever install is consent-gated inside MaybeFetchOnConnectAsync (the shell's dialog);
                // a switch-account (plugin already installed) is a silent debounced re-check. Fire-and-forget.
                if (_feed is { } feed) _ = feed.MaybeFetchOnConnectAsync();
#endif
            }
            else StatusText = r.Error ?? "Nexus sign-in didn't complete.";
        }
        catch (Exception e) { StatusText = "Nexus connect failed: " + e.Message; }
    }

    public void DisconnectNexus()
    {
        _nexus.Disconnect();
        StatusText = "Disconnected from Nexus.";
        RaiseNexusStateChanged();
    }

    /// <summary>Re-notify every Nexus-derived surface after a connect/disconnect: the toolbar dot, the
    /// action availability (hearts / refresh / identify), the account line + premium tag, and the
    /// dark-window user-features gate. One place so connect and disconnect can't drift apart.</summary>
    private void RaiseNexusStateChanged()
    {
        OnPropertyChanged(nameof(NexusConnected));
        OnPropertyChanged(nameof(NexusStatusBrush));
        OnPropertyChanged(nameof(NexusActionsAvailable));
        OnPropertyChanged(nameof(NexusActionsVisibility));
        OnPropertyChanged(nameof(NexusUserFeaturesAvailable));
        OnPropertyChanged(nameof(CatalogAvailable));
        OnPropertyChanged(nameof(CatalogVisibility));
        OnPropertyChanged(nameof(CatalogBrowseAvailable));
        OnPropertyChanged(nameof(CatalogBrowseVisibility));
        OnPropertyChanged(nameof(CatalogDetailAvailable));
        OnPropertyChanged(nameof(CatalogDetailVisibility));
        OnPropertyChanged(nameof(CatalogActionsAvailable));
        OnPropertyChanged(nameof(CatalogActionsVisibility));
        OnPropertyChanged(nameof(NexusUser));
        OnPropertyChanged(nameof(NexusPremium));
        OnPropertyChanged(nameof(NexusAccountLine));
        OnPropertyChanged(nameof(NexusChipLabel));
        OnPropertyChanged(nameof(NexusChipTooltip));
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
                    try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, NexusSource, paths)).Matched; }
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
            catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
                    try { await Ue4ssLuaInstaller.IdentifyMetadataAsync(_ctx, NexusSource, src, modName); }
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
                try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, NexusSource, remaining)).Matched; }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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

    /// <summary>Launch a detected mod loader (Mod Engine 2, Seamless Co-op, …) via its own launcher
    /// exe. Read-only Process.Start — the loader decides what to do with the game; we only start it.
    /// No save snapshot needed: loaders don't touch save files directly (the loader itself is the
    /// entry point, not a save editor). Status line updates on launch; any OS-level refusal surfaces
    /// via the catch so the user knows something went wrong instead of a silent no-op.</summary>
    public async Task LaunchLoaderAsync(DetectedLoaderRow row)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = row.LauncherPath,
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(row.LauncherPath) ?? "",
            };
            System.Diagnostics.Process.Start(psi);
            StatusText = $"Launching {row.DisplayName}…";
        }
        catch (Exception ex) { StatusText = $"Couldn't launch {row.DisplayName}: {ex.Message}"; }
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
    /// instead of re-running detection. <paramref name="sweep"/> gates the silent first-add discovery
    /// sweep — default on for the single-game paths; the batch add-game branch passes false so adding
    /// N games from one Steam batch doesn't turn into N sequential recursive sweeps and up to N modal
    /// review dialogs stacked under one busy state with no cancel. Batch callers should point the user
    /// at More -> Find existing mods per game instead.</summary>
    public async Task AddGameAsync(GameInput input, string? resolvedSaveDir = null, bool sweep = true)
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

            // Silent first-add sweep: LoadAsync just made the new game active (AddGame sets
            // ActiveGameId), so _ctx is already this game's context. Auto = says nothing when it
            // finds nothing; the review dialog (if anything wired it) still gates every write.
            // DiscoverExistingModsAsync only overwrites StatusText when it actually has something to
            // report (found-nothing and nothing-adopted stay silent under auto), so "Added {name}."
            // above survives unless there's genuinely new information to show.
            if (sweep) await DiscoverExistingModsAsync(auto: true);
        }
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
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
        catch (Exception e) { StatusText = ErrorRemedy.Describe(e); }
        finally { IsBusy = false; }
    }
}
