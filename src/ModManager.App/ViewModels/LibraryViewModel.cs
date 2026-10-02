using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModManager.App.Services;
using ModManager.Core;
using ModManager.Core.Library;
using ModManager.Core.Loaders;
using ModManager.Core.Recency;

namespace ModManager.App.ViewModels;

/// <summary>
/// The cover a library row shows, for both kinds of row (B6): the image, the themed initial when there
/// is none, and the late swap when a cover arrives from Steam's CDN. One copy, so a fix lands once.
/// </summary>
public abstract partial class LibraryCoverRowViewModel : ObservableObject
{
    private string? _coverPath;
    private ImageSource? _cover;

    protected LibraryCoverRowViewModel(string? coverPath) => _coverPath = coverPath;

    public abstract string Name { get; }

    public string? CoverPath => _coverPath;

    /// <summary>Built once per path and decoded at thumbnail size: the list holds every installed game,
    /// and a full-size portrait decode per row per filter keystroke is what made it lag.</summary>
    public ImageSource? Cover => _cover ??= string.IsNullOrEmpty(_coverPath)
        ? null
        : new BitmapImage(new Uri(_coverPath)) { DecodePixelWidth = 300 };

    public bool HasCover => !string.IsNullOrEmpty(_coverPath);
    public Visibility CoverVisibility => HasCover ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility => HasCover ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The single-letter initial on the placeholder swatch when no cover art exists.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    /// <summary>Themed brush for the placeholder swatch — the app's live accent instance (hard-cast:
    /// a missing key fails loud — F-066). App-side only; keeps Core pure.</summary>
    public Brush Placeholder =>
        (Brush)Application.Current.Resources["ThemeAccent"]; // hard-cast: fail loud (F-066)

    /// <summary>Swap in a cover resolved later (e.g. fetched from the Steam CDN). Call on the UI thread —
    /// it raises the cover bindings so the row replaces its placeholder with the image.</summary>
    public void SetCover(string coverPath)
    {
        _coverPath = coverPath;
        _cover = null;
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(CoverVisibility));
        OnPropertyChanged(nameof(PlaceholderVisibility));
    }
}

/// <summary>
/// App-side view row wrapping a pure <see cref="GameLibraryRow"/> for the Library home. Adds the
/// bound <see cref="ImageSource"/> cover (built on the UI thread from the resolved path) plus a
/// themed-initial placeholder for games with no cover art — mirroring <see cref="GameOption"/>'s
/// null-degrade behavior. The Core row stays pure; every WinUI type lives here in the App layer.
/// </summary>
public sealed partial class GameLibraryRowViewModel : LibraryCoverRowViewModel
{
    public GameLibraryRow Row { get; }

    /// <summary>Pending Nexus update count for this game, already collapsed to 0 by the caller when the
    /// game is unchecked (<see cref="ModUpdateSummary.GameUpdateSummary.Checked"/> false) — this VM
    /// never has to know the difference between "unchecked" and "checked, none pending," because both
    /// render identically (no badge). See <see cref="UpdateBadgeVisibility"/>.</summary>
    public int PendingUpdateCount { get; }

    public GameLibraryRowViewModel(GameLibraryRow row, int pendingUpdateCount = 0) : base(row.CoverPath)
    {
        Row = row;
        PendingUpdateCount = pendingUpdateCount;
    }

    public string Id => Row.Id;
    public override string Name => Row.Name;
    public string? StoreSource => Row.StoreSource;
    public LastPlayed Recency => Row.Recency;
    public int ModCount => Row.ModCount;
    public int EnabledCount => Row.EnabledCount;
    public string? ActiveProfile => Row.ActiveProfile;
    public EngineTier Tier => Row.Tier;
    public string? BanRisk => Row.BanRisk;
    public IReadOnlyList<string> DetectedLoaders => Row.DetectedLoaders;
    public string? NexusDomain => Row.NexusDomain;

    /// <summary>Human-readable recency line ("2 days ago" / "Unknown") — never a fake time.</summary>
    public string RecencyText => FormatRecency(Recency.LastPlayedUtc);

    // Presentation-only relative-time label. "Unknown" when we have no timestamp — the home never
    // shows a fabricated time for a game 626 has never seen played.
    internal static string FormatRecency(DateTime? lastPlayedUtc)
    {
        if (lastPlayedUtc is not { } t) return "Unknown";
        var delta = DateTime.UtcNow - t;
        if (delta < TimeSpan.Zero) return "Just now";
        if (delta.TotalMinutes < 1) return "Just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hr ago";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} day{((int)delta.TotalDays == 1 ? "" : "s")} ago";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)} mo ago";
        return $"{(int)(delta.TotalDays / 365)} yr ago";
    }

    // --- Automation identity -----------------------------------------------------------------
    // Follows the ModRowViewModel *AutomationName convention. Ids are built from Id rather than
    // Name because Name is display copy: it gets retitled, and a harness keyed on it goes red for a
    // rename while staying green for a control that moved. The two lists are prefixed apart because
    // the same game appears in BOTH the recent strip and the all-games list, and an unprefixed
    // Id would collide across them — an agent asking for "windrose" would get whichever the tree
    // walker reached first, which is not a thing worth debugging later.

    /// <summary>Stable id for this game's row in the all-games list.</summary>
    public string RowAutomationId => $"GameRow.{Id}";

    /// <summary>Stable id for this game's card in the recent strip. Prefixed apart from the row.</summary>
    public string RecentAutomationId => $"RecentCard.{Id}";

    /// <summary>Per-row name for the Play button. Without it every Play button in the list is the
    /// literal string "Play" and none can be told apart — the UIA spike matched a Text label instead
    /// of any button at all.</summary>
    public string PlayAutomationName => $"Play {Name}";

    /// <summary>Per-card name for the recent-strip card, which otherwise announces only its cover.</summary>
    public string OpenAutomationName => $"Open {Name}";

    /// <summary>"3 mods · 2 on" style summary of the game's mod state.</summary>
    public string ModStateText => ModCount == 0
        ? "No mods"
        : $"{ModCount} mod{(ModCount == 1 ? "" : "s")} · {EnabledCount} on";

    /// <summary>Store-source badge text ("Steam" / "EA" / ""), named the way unmanaged rows name theirs.</summary>
    public string SourceBadge => GameLibraryBuilder.StoreDisplayName(StoreSource);

    // --- Chip presentation (view-thread helpers so the XAML can bind Visibility directly, matching
    // the app's VM-drives-Visibility convention — no converters in this codebase) ------------------

    /// <summary>The source badge only renders when there's a store source to name.</summary>
    public Visibility SourceBadgeVisibility =>
        string.IsNullOrEmpty(SourceBadge) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Short tier chip text ("Curated" / "Nexus" / "Unknown").</summary>
    public string TierChip => Tier switch
    {
        EngineTier.EngineCurated => "Curated",
        EngineTier.NexusOnly => "Nexus",
        _ => "Unknown",
    };

    /// <summary>Tooltip explaining what the tier chip means for this game's tooling.</summary>
    public string TierTooltip => Tier switch
    {
        EngineTier.EngineCurated => "626 has a curated engine profile for this game — full per-engine mod tooling.",
        EngineTier.NexusOnly => "No curated engine profile, but Nexus knows this game — Nexus-only tooling applies.",
        _ => "No curated engine and no Nexus domain — basic file-drop management only.",
    };

    /// <summary>The ban-risk chip only renders on a game the ban catalog flags.</summary>
    public Visibility BanRiskVisibility =>
        BanRisk is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Whether this game has any detected mod loaders to chip.</summary>
    public bool HasLoaders => DetectedLoaders.Count > 0;
    public Visibility LoaderVisibility => HasLoaders ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Comma-joined detected-loader names for the loader chip.</summary>
    public string LoaderChip => HasLoaders ? string.Join(", ", DetectedLoaders) : "";

    /// <summary>The update badge only renders when there's something known AND pending — never "0" and
    /// never for a game that's never had a Nexus refresh. An absent badge honestly means "nothing
    /// known"; the caller (LibraryViewModel.Load) already folded "unchecked" down to 0 for us.</summary>
    public Visibility UpdateBadgeVisibility => PendingUpdateCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Short badge text — "1 UPDATE" / "N UPDATES", matching the Consolas all-caps chip style.</summary>
    public string UpdateBadgeText => PendingUpdateCount == 1 ? "1 UPDATE" : $"{PendingUpdateCount} UPDATES";

    /// <summary>Sentence-case tooltip, singular-correct.</summary>
    public string UpdateBadgeTooltip => PendingUpdateCount == 1
        ? "1 mod has an update available. Open the game to review it."
        : $"{PendingUpdateCount} mods have updates available. Open the game to review them.";
}

/// <summary>
/// An installed game 626 can see and does not manage (B6). It sits in the same list as the managed
/// games, ordered by when it was last played, and offers exactly two things that are honest: Play,
/// through its own store, and Start managing. Nothing about it creates state: no data dir, no registry
/// entry, until the user asks for one.
/// </summary>
public sealed partial class UnmanagedGameRowViewModel : LibraryCoverRowViewModel
{
    public InstalledGame Game { get; }

    public UnmanagedGameRowViewModel(InstalledGame game, string? coverPath) : base(coverPath) => Game = game;

    public string AppId => Game.AppId;
    public override string Name => Game.Name;
    public string StoreKind => Game.StoreKind;

    /// <summary>Stable id for this row. Keyed on store AND store id: a Steam app id and an EA content
    /// id live in different namespaces, and a bare id could collide across them. Prefixed apart from
    /// managed rows (<c>GameRow.</c>), which the harness walks expecting mod state on every one.</summary>
    public string RowAutomationId => $"UnmanagedGame.{StoreKind}.{AppId}";

    public string PlayAutomationName => $"Play {Name}";
    public string ManageAutomationName => $"Start managing {Name}";

    /// <summary>The store, named the way the managed rows name theirs.</summary>
    public string SourceBadge => GameLibraryBuilder.StoreDisplayName(StoreKind);

    /// <summary>The store's own last-played time, in the managed rows' words ("Unknown" when the store
    /// keeps none, as EA's does not).</summary>
    public string RecencyText => GameLibraryRowViewModel.FormatRecency(LibraryList.StoreLastPlayed(Game));

    /// <summary>Play goes through the store's own launcher; a store we can't launch through has no
    /// Play button rather than one that does nothing.</summary>
    public Visibility PlayVisibility => StoreLaunch.UrlFor(Game) is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// The Game Library home view-model. Builds the per-game rows via the pure
/// <see cref="GameLibraryBuilder"/> (recency ladder + mod state + tier + ban risk + loaders + cover),
/// composes them with the installed games 626 does not manage into ONE list (<see cref="LibraryList"/>,
/// B6), exposes the recent strip and that list, and wires the home commands (open, play, start
/// managing) onto the existing App services. Play launches the game's current on-disk
/// state; the vanilla/modded toggle lives in the game view. The launch command reuses the exact same
/// reversible launch path the game view uses — no new mechanism, no scope creep.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private const int RecentCount = 6;

    private readonly LauncherService _svc;
    private readonly IStoreLibrary _store;
    private readonly CoverCache _covers;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    // The full, unfiltered MANAGED row set — the recent strip and OpenGameById read it.
    private readonly List<GameLibraryRowViewModel> _allRows = new();

    // Every game on the page, managed or not, in LibraryList's one order — what search/filter project
    // from — and the unmanaged rows' view-models, keyed by store + store id.
    private IReadOnlyList<LibraryEntry> _entries = Array.Empty<LibraryEntry>();
    private readonly Dictionary<(string Store, string AppId), UnmanagedGameRowViewModel> _unmanaged = new();

    // Each managed entry's view-model, keyed by the Core row INSTANCE the entry wraps. Built once per
    // Load, not per keystroke, and by reference rather than by id: a hand-edited games.json with two
    // entries sharing an id must still render both rows, not throw out of the filter.
    private readonly Dictionary<GameLibraryRow, GameLibraryRowViewModel> _managedByRow = new(ReferenceEqualityComparer.Instance);

    // The cover pass: the one in flight (a newer Load cancels it rather than racing it over the same
    // ids), and the Steam ids the CDN had nothing for this session (asked once, not on every return home).
    private CancellationTokenSource? _coverPass;
    private readonly HashSet<string> _coverMisses = new(StringComparer.Ordinal);

    // Row id -> Steam app id, captured on Load so the async cover pass can fetch missing art by app id.
    private readonly Dictionary<string, string?> _appIdByRow = new();

    /// <summary>Every game on the page — <see cref="GameLibraryRowViewModel"/> for one 626 manages,
    /// <see cref="UnmanagedGameRowViewModel"/> for one it can see and doesn't — most-recently-played
    /// first, after search + filters. The view picks the row template by type.</summary>
    public ObservableCollection<object> Rows { get; } = new();

    /// <summary>The recent cover strip — the top <see cref="RecentCount"/> most-recently-played rows.</summary>
    public ObservableCollection<GameLibraryRowViewModel> RecentRows { get; } = new();

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string? SourceFilter { get; set; }   // null = any store source
    [ObservableProperty] public partial EngineTier? TierFilter { get; set; }  // null = any tier
    [ObservableProperty] public partial bool BanRiskOnly { get; set; }        // true = only ban-risk games

    /// <summary>True when 626 manages no games. NOT the same as "nothing to show" — a machine with zero
    /// registered games can still list every installed game, which is exactly the first-run case. This
    /// gates the recent strip only; see <see cref="HasAnythingToShow"/>.</summary>
    public bool IsEmpty => _allRows.Count == 0;

    /// <summary>True when the page has SOMETHING worth rendering — managed games, unmanaged ones, or
    /// both. Gating the page on "no games registered" would hide every installed game at the one moment
    /// they matter most, on a fresh machine.</summary>
    public bool HasAnythingToShow => _entries.Count > 0;

    /// <summary>Visibility helpers so the view binds directly (the app's VM-drives-Visibility pattern).</summary>
    public Visibility EmptyVisibility => HasAnythingToShow ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ContentVisibility => HasAnythingToShow ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The recent strip only exists once something is managed: it opens games, and there is
    /// nothing to jump back into yet.</summary>
    public Visibility RecentVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The list shows whenever there is anything at all. Deliberately keyed to the FULL set,
    /// not the filtered <see cref="Rows"/>: a search that matches nothing must keep its own search box
    /// on screen, or there's no way to undo the search.</summary>
    public Visibility AllGamesVisibility => HasAnythingToShow ? Visibility.Visible : Visibility.Collapsed;

    // --- Cross-game updates -------------------------------------------------------------------------
    //
    // Computed ONCE per Load, in the same pass that feeds the per-game badges, and cached here. The
    // Updates view consumes this snapshot rather than re-reading every metadata.json for itself, so
    // opening the directory costs nothing and the badges and the list can never disagree. Load() runs
    // on every return to the home, so the snapshot refreshes exactly when the home does.

    private IReadOnlyList<GameUpdateSummary> _updateSummaries = Array.Empty<GameUpdateSummary>();

    /// <summary>Last-built per-game update snapshot — what the Updates view renders from. Includes
    /// unchecked games (Checked = false) so the view can tell "checked, none pending" apart from
    /// "never checked"; collapsing them here would destroy the distinction.</summary>
    public IReadOnlyList<GameUpdateSummary> UpdateSummaries => _updateSummaries;

    /// <summary>Total pending updates across every game. Unchecked games contribute nothing (their
    /// pending list is empty by construction) — this is never inflated by a guess.</summary>
    public int TotalPendingUpdates { get; private set; }

    /// <summary>The Updates entry point shows only when something is actually pending — a home with
    /// nothing to update stays uncluttered, and an unchecked library never advertises a "0".</summary>
    public Visibility UpdatesEntryVisibility =>
        TotalPendingUpdates > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Entry-point label — "1 update" / "N updates".</summary>
    public string UpdatesEntryText => TotalPendingUpdates == 1 ? "1 update" : $"{TotalPendingUpdates} updates";

    /// <summary>Sentence-case tooltip, singular-correct.</summary>
    public string UpdatesEntryTooltip => TotalPendingUpdates == 1
        ? "1 mod across your library has a newer version. Open the list to see which."
        : $"{TotalPendingUpdates} mods across your library have newer versions. Open the list to see which.";

    /// <summary>Open the cross-game Updates directory. The VM only raises the event; the shell owns the
    /// view swap, exactly as it does for <see cref="GameOpened"/>.</summary>
    public void RequestUpdatesView() => UpdatesRequested?.Invoke();

    /// <summary>Open a game by id — the Updates directory's Open game button. Routes through the same
    /// <see cref="OpenGame"/> path a library row uses (set active, then raise GameOpened); an id that
    /// isn't in the library is simply ignored.</summary>
    public void OpenGameById(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        var row = _allRows.FirstOrDefault(r => r.Id == gameId);
        if (row is not null) OpenGame(row);
    }

    /// <summary>Raised when a row is opened — the shell (MainWindow) swaps to that game's mod view.
    /// The VM only sets the active game + fires this; the view swap is the shell's job (Task 7).</summary>
    public event Action<string>? GameOpened;

    /// <summary>Raised when the user asks to start managing an installed game — the shell runs the Add
    /// flow (one click when the game can be added as-is, the dialog otherwise).</summary>
    public event Action<InstalledGame>? AddGameRequested;

    /// <summary>Raised when the user opens the cross-game Updates directory — the shell swaps it in.</summary>
    public event Action? UpdatesRequested;

    public LibraryViewModel(LauncherService svc, IStoreLibrary store)
    {
        _svc = svc;
        _store = store;
        _covers = new CoverCache(store);
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>Read the registry, build every row via the Core builder wired to the real lookups,
    /// then compose them with the unmanaged installs into one list and compute the recent strip. Idempotent — safe to call on every navigation
    /// back to the home (recency + mod counts refresh each time).</summary>
    public void Load()
    {
        var reg = _svc.LoadRegistry();
        var games = reg.Games;

        // Own-launch wins over Steam — order is load-bearing (own first in the ladder).
        var sources = new List<ILastPlayedSource>
        {
            new OwnLaunchLastPlayedSource(reg),
            new SteamLastPlayedSource(_store),
        };

        var rows = GameLibraryBuilder.Build(
            games, sources,
            modState: ModStateFor,
            tier: TierFor,
            banRisk: BanRiskFor,
            loaders: LoadersFor,
            cover: CoverFor);

        // Pending-update counts, read from each game's already-persisted metadata.json — no network,
        // no scan (ModUpdateSummary never throws). A game that's never been refreshed (Checked = false)
        // folds to 0 here, same as "checked, nothing pending" — both render as no badge, and the row
        // VM doesn't need to know which case it was (see GameLibraryRowViewModel.PendingUpdateCount).
        // ONE pass, two consumers: the per-game badge below and the cross-game Updates directory (which
        // reads the cached snapshot). Never computed twice per render.
        _updateSummaries = ModUpdateSummary.ForGames(games);
        var updateByGameId = _updateSummaries.ToDictionary(s => s.GameId, s => s);
        TotalPendingUpdates = _updateSummaries.Sum(s => s.Count);

        _allRows.Clear();
        _managedByRow.Clear();
        foreach (var r in rows)
        {
            var pending = updateByGameId.TryGetValue(r.Id, out var summary) && summary.Checked ? summary.Count : 0;
            var vm = new GameLibraryRowViewModel(r, pending);
            _allRows.Add(vm);
            _managedByRow[r] = vm;
        }

        _appIdByRow.Clear();
        foreach (var g in games) _appIdByRow[g.Id] = g.SteamAppId;

        // The installs 626 can see and does not manage: exactly what discovery would have offered, so a
        // game registered under another id never shows twice (Core decides, keyed by store).
        var unmanaged = UnmanagedInstalls(games);
        _unmanaged.Clear();
        foreach (var ig in unmanaged)
            _unmanaged[(ig.StoreKind, ig.AppId)] = new UnmanagedGameRowViewModel(ig, UnmanagedCover(ig));
        _entries = LibraryList.Compose(rows, unmanaged);

        ApplyFilter();
        // ApplyFilter ran above, so every count these read is final. Order matters only in that sense —
        // nothing here may run before the collections settle.
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAnythingToShow));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(RecentVisibility));
        OnPropertyChanged(nameof(AllGamesVisibility));
        OnPropertyChanged(nameof(TotalPendingUpdates));
        OnPropertyChanged(nameof(UpdatesEntryVisibility));
        OnPropertyChanged(nameof(UpdatesEntryText));
        OnPropertyChanged(nameof(UpdatesEntryTooltip));

        _coverPass?.Cancel();
        _coverPass = new CancellationTokenSource();
        _ = ResolveCoversAsync(_coverPass.Token); // fill covers Steam didn't cache locally from its public CDN (once)
    }

    // Fetch portrait covers for rows that had no local art, then swap them in on the UI thread. Most
    // installed games only have a 32px icon cached locally, so their real cover comes from Steam's public
    // CDN, fetched once and cached. Failures (404 / offline) leave the row's themed placeholder.
    private async Task ResolveCoversAsync(CancellationToken ct)
    {
        // Managed rows by their registered Steam id; unmanaged Steam rows too, since the art is the
        // store's and not state of ours. Steam only: an EA content id is not a Steam app id and must
        // never be looked up as one.
        var wanted = new List<(LibraryCoverRowViewModel Row, string AppId)>();
        foreach (var row in _allRows)
            if (!row.HasCover && _appIdByRow.TryGetValue(row.Id, out var appId) && !string.IsNullOrEmpty(appId))
                wanted.Add((row, appId));
        foreach (var row in _unmanaged.Values)
            if (!row.HasCover && row.StoreKind == "steam" && !string.IsNullOrEmpty(row.AppId))
                wanted.Add((row, row.AppId));

        foreach (var (row, appId) in wanted)
        {
            if (ct.IsCancellationRequested) return;
            if (_coverMisses.Contains(appId)) continue;
            var path = await _covers.FetchPortraitAsync(appId);
            if (path is null) { _coverMisses.Add(appId); continue; }
            if (_dispatcher is null) row.SetCover(path);
            else _dispatcher.TryEnqueue(() => row.SetCover(path));
        }
    }

    // A local cover for an unmanaged install: Steam's own cache for a Steam game, nothing for EA (the EA
    // app keeps no art we can read, and its content id would be meaningless to the Steam lookup).
    private string? UnmanagedCover(InstalledGame game)
        => game.StoreKind == "steam" ? _covers.LocalPortrait(game.AppId) : null;

    // --- Builder delegates (App-side lookups over the existing services) -----------------------------

    private static GameModState ModStateFor(GameEntry g)
    {
        // Same read-only listing path the mod list uses — no active-game switch, no disk write.
        // ActiveProfile stays null by design: profiles are named on-demand snapshots (Scanner
        // Save/Load/ListProfiles) with no persisted "active" marker, so there's no read-only lookup
        // that could name the active profile for a game. The row shows the mods count only — an
        // active-profile display is deferred to Phase 2 (see docs/smoke-tests/pending.md).
        try
        {
            var mods = ModListing.Resolve(g);
            return new GameModState(mods.Count, mods.Count(m => m.Enabled), ActiveProfile: null);
        }
        catch { return new GameModState(0, 0, null); }
    }

    private static EngineTier TierFor(GameEntry g)
    {
        // Curated when we know the engine: either the app-id → engine map (FromSoft et al.) or a
        // recorded engine that maps to a real preset (anything but the "custom" fallback).
        var curated = KnownEngines.ByAppId(g.SteamAppId) is not null
            || (!string.IsNullOrEmpty(g.Engine)
                && !string.Equals(g.Engine, "custom", StringComparison.OrdinalIgnoreCase)
                && EnginePresets.Presets.ContainsKey(g.Engine));
        if (curated) return EngineTier.EngineCurated;
        // No curated engine, but Nexus knows the game by domain — Nexus-only tooling applies.
        if (!string.IsNullOrEmpty(g.NexusGameDomain)) return EngineTier.NexusOnly;
        return EngineTier.Unknown;
    }

    private static string? BanRiskFor(GameEntry g)
    {
        var risk = BanRiskCatalog.Effective(g);
        return risk == GameBanRisk.None ? null : risk.ToString();
    }

    private static IReadOnlyList<string> LoadersFor(GameEntry g)
    {
        if (string.IsNullOrEmpty(g.Engine)) return Array.Empty<string>();
        try
        {
            var playFolder = DirectInjectService.PlayFolder(g.GameRoot) ?? g.GameRoot;
            return LoaderScan.Detect(playFolder, g)
                .Select(d => d.Loader.DisplayName)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private string? CoverFor(GameEntry g) => _covers.LocalPortrait(g.SteamAppId);

    // --- Search + filter ---------------------------------------------------------------------------

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSourceFilterChanged(string? value) => ApplyFilter();
    partial void OnTierFilterChanged(EngineTier? value) => ApplyFilter();
    partial void OnBanRiskOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        // Which entries pass is Core's call (LibraryList.Matches, tested); this only maps each one back
        // to its row view-model.
        Rows.Clear();
        foreach (var e in _entries)
        {
            if (!LibraryList.Matches(e, SearchText, SourceFilter, TierFilter, BanRiskOnly)) continue;
            if (e.Managed is { } m && _managedByRow.TryGetValue(m, out var managedRow)) Rows.Add(managedRow);
            else if (e.Unmanaged is { } u && _unmanaged.TryGetValue((u.StoreKind, u.AppId), out var unmanagedRow)) Rows.Add(unmanagedRow);
        }

        // Recent strip is always the top-N of the FULL set (recency order), independent of the search
        // box — the strip is "jump back in," not a filtered view.
        RecentRows.Clear();
        foreach (var r in _allRows.Take(RecentCount)) RecentRows.Add(r);
    }

    private IReadOnlyList<InstalledGame> UnmanagedInstalls(IReadOnlyList<GameEntry> registered)
    {
        IReadOnlyList<InstalledGame> installed;
        try { installed = _store.InstalledGames(); }
        catch { installed = Array.Empty<InstalledGame>(); }

        // Keyed by store, and EA games only when the manifest knows them (Core decides both).
        return ModManager.Core.Stores.StoreDiscovery.Offerable(
                installed, registered, ModManager.Core.Manifest.EffectiveManifest.Current.Games)
            .ToList();
    }

    // --- Commands ----------------------------------------------------------------------------------

    /// <summary>Open a game: make it active in the registry, then let the shell swap to its mod view.</summary>
    [RelayCommand]
    private void OpenGame(GameLibraryRowViewModel? row)
    {
        if (row is null) return;
        _svc.SetActiveGame(row.Id);
        GameOpened?.Invoke(row.Id);
    }

    /// <summary>Play: launch the game in its current on-disk state (modded if mods are on, vanilla if
    /// they aren't), then stamp recency. The home never toggles mode — the vanilla/modded step-aside
    /// toggle lives in the game view, coupled to the active context. Reuses the existing reversible
    /// launch path — no new launch mechanism, exactly what the game view's Play button drives.</summary>
    [RelayCommand]
    private void Play(GameLibraryRowViewModel? row) => LaunchAndStamp(row);

    private void LaunchAndStamp(GameLibraryRowViewModel? row)
    {
        if (row is null) return;
        var g = _svc.LoadRegistry().Games.FirstOrDefault(x => x.Id == row.Id);
        if (g is null) return;
        try
        {
            if (_svc.Launch(g)) StampLaunch(g.Id);
        }
        catch { /* a launch failure surfaces via the game view; the home stays quiet */ }
    }

    private void StampLaunch(string gameId)
    {
        // Recency stamp is best-effort — a failure degrades to the Steam source next load, never
        // blocks or reports on a launch that already happened.
        try { _svc.StampLaunch(gameId); } catch { /* non-fatal */ }
    }

    /// <summary>Start managing an installed game — the shell runs the Add flow, then calls
    /// <see cref="Load"/> to refresh the home. The first write for this game happens here, because the
    /// user asked for it; listing it never wrote anything.</summary>
    [RelayCommand]
    private void StartManaging(UnmanagedGameRowViewModel? row)
    {
        if (row is null) return;
        AddGameRequested?.Invoke(row.Game);
    }

    /// <summary>Play an unmanaged game through its own store's launcher. Nothing is stamped: recency for
    /// a game 626 doesn't manage is the store's to keep, and stamping would need a registry entry.</summary>
    [RelayCommand]
    private void PlayUnmanaged(UnmanagedGameRowViewModel? row)
    {
        if (row is null || StoreLaunch.UrlFor(row.Game) is not { } url) return;
        try { _svc.Launch(new LaunchTarget("Play", "url", url)); }
        catch { /* the store's own launcher reports its failures; the home stays quiet, as for managed rows */ }
    }
}
