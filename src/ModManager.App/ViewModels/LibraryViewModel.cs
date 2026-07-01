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
/// App-side view row wrapping a pure <see cref="GameLibraryRow"/> for the Library home. Adds the
/// bound <see cref="ImageSource"/> cover (built on the UI thread from the resolved path) plus a
/// themed-initial placeholder for games with no cover art — mirroring <see cref="GameOption"/>'s
/// null-degrade behavior. The Core row stays pure; every WinUI type lives here in the App layer.
/// </summary>
public sealed partial class GameLibraryRowViewModel : ObservableObject
{
    public GameLibraryRow Row { get; }
    private string? _resolvedCover;

    public GameLibraryRowViewModel(GameLibraryRow row)
    {
        Row = row;
        _resolvedCover = row.CoverPath;
    }

    public string Id => Row.Id;
    public string Name => Row.Name;
    public string? StoreSource => Row.StoreSource;
    public string? CoverPath => _resolvedCover;
    public LastPlayed Recency => Row.Recency;
    public int ModCount => Row.ModCount;
    public int EnabledCount => Row.EnabledCount;
    public string? ActiveProfile => Row.ActiveProfile;
    public EngineTier Tier => Row.Tier;
    public string? BanRisk => Row.BanRisk;
    public IReadOnlyList<string> DetectedLoaders => Row.DetectedLoaders;
    public string? NexusDomain => Row.NexusDomain;

    /// <summary>Local cover image, built on the UI thread when the card renders; null degrades to the
    /// themed <see cref="Placeholder"/> swatch. Mirrors <see cref="GameOption.Cover"/>.</summary>
    public ImageSource? Cover => string.IsNullOrEmpty(CoverPath)
        ? null
        : new BitmapImage(new Uri(CoverPath));

    /// <summary>True when there's no cover art — the view shows the placeholder instead.</summary>
    public bool HasCover => !string.IsNullOrEmpty(CoverPath);

    /// <summary>Visibility helpers so the view binds directly (no converters — matches the app pattern).</summary>
    public Visibility CoverVisibility => HasCover ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility => HasCover ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Swap in a cover resolved asynchronously (e.g. fetched from the Steam CDN). Call on the UI
    /// thread — it raises the cover bindings so the card replaces its placeholder with the image.</summary>
    public void SetCover(string coverPath)
    {
        _resolvedCover = coverPath;
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(CoverVisibility));
        OnPropertyChanged(nameof(PlaceholderVisibility));
    }

    /// <summary>The single-letter initial shown on the placeholder swatch when no cover art exists.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    /// <summary>Themed brush for the placeholder swatch — the current accent, degrading to a neutral
    /// when the resource isn't present (e.g. design-time). App-side only; keeps Core pure.</summary>
    public Brush Placeholder =>
        Application.Current?.Resources["ThemeAccent"] as Brush ?? new SolidColorBrush(Colors.SlateGray);

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

    /// <summary>"3 mods · 2 on" style summary of the game's mod state.</summary>
    public string ModStateText => ModCount == 0
        ? "No mods"
        : $"{ModCount} mod{(ModCount == 1 ? "" : "s")} · {EnabledCount} on";

    /// <summary>Store-source badge text ("Steam" / "GOG" / "Manual" / ""). Title-cased for display.</summary>
    public string SourceBadge => string.IsNullOrEmpty(StoreSource)
        ? ""
        : char.ToUpperInvariant(StoreSource[0]) + StoreSource[1..];

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
}

/// <summary>A store-discovered game that isn't in the registry yet — the discovery lane's row.</summary>
public sealed partial class DiscoveredGameViewModel : ObservableObject
{
    public InstalledGame Game { get; }
    private readonly Func<string, string?> _resolveCover;

    public DiscoveredGameViewModel(InstalledGame game, Func<string, string?> resolveCover)
    {
        Game = game;
        _resolveCover = resolveCover;
    }

    public string AppId => Game.AppId;
    public string Name => Game.Name;
    public string StoreKind => Game.StoreKind;

    public string? CoverPath => _resolveCover(Game.AppId);
    public ImageSource? Cover => string.IsNullOrEmpty(CoverPath) ? null : new BitmapImage(new Uri(CoverPath));
    public bool HasCover => !string.IsNullOrEmpty(CoverPath);
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    public Brush Placeholder =>
        Application.Current?.Resources["ThemeAccent"] as Brush ?? new SolidColorBrush(Colors.SlateGray);

    /// <summary>Visibility helpers so the view binds directly (no converters — matches the app pattern).</summary>
    public Visibility CoverVisibility => HasCover ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility => HasCover ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// The Game Library home view-model. Builds the per-game rows via the pure
/// <see cref="GameLibraryBuilder"/> (recency ladder + mod state + tier + ban risk + loaders + cover),
/// exposes the recent strip / all-games list / discovery lane, and wires the home commands (open,
/// play, add discovered) onto the existing App services. Play launches the game's current on-disk
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

    // The full, unfiltered row set — the source the search/filter views project from.
    private readonly List<GameLibraryRowViewModel> _allRows = new();

    // Row id -> Steam app id, captured on Load so the async cover pass can fetch missing art by app id.
    private readonly Dictionary<string, string?> _appIdByRow = new();

    /// <summary>All library rows, most-recently-played first, after search + filters.</summary>
    public ObservableCollection<GameLibraryRowViewModel> Rows { get; } = new();

    /// <summary>The recent cover strip — the top <see cref="RecentCount"/> most-recently-played rows.</summary>
    public ObservableCollection<GameLibraryRowViewModel> RecentRows { get; } = new();

    /// <summary>Installed games discovered from the store that aren't registered yet.</summary>
    public ObservableCollection<DiscoveredGameViewModel> DiscoveryRows { get; } = new();

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string? sourceFilter;   // null = any store source
    [ObservableProperty] private EngineTier? tierFilter;  // null = any tier
    [ObservableProperty] private bool banRiskOnly;        // true = only ban-risk games

    /// <summary>True when the registry has no games — the view shows an empty state.</summary>
    public bool IsEmpty => _allRows.Count == 0;

    /// <summary>Visibility helpers so the view binds directly (the app's VM-drives-Visibility pattern).</summary>
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility => IsEmpty ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The "nothing new to add" line shows only when the discovery lane found no candidates.</summary>
    public Visibility DiscoveryEmptyVisibility =>
        DiscoveryRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Raised when a row is opened — the shell (MainWindow) swaps to that game's mod view.
    /// The VM only sets the active game + fires this; the view swap is the shell's job (Task 7).</summary>
    public event Action<string>? GameOpened;

    /// <summary>Raised when the user asks to add a discovered game — the shell runs the Add flow.</summary>
    public event Action<InstalledGame>? AddGameRequested;

    public LibraryViewModel(LauncherService svc, IStoreLibrary store)
    {
        _svc = svc;
        _store = store;
        _covers = new CoverCache(store);
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>Read the registry, build every row via the Core builder wired to the real lookups,
    /// then compute the recent strip + discovery lane. Idempotent — safe to call on every navigation
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

        _allRows.Clear();
        foreach (var r in rows) _allRows.Add(new GameLibraryRowViewModel(r));

        _appIdByRow.Clear();
        foreach (var g in games) _appIdByRow[g.Id] = g.SteamAppId;

        ApplyFilter();
        RebuildDiscovery(games);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(DiscoveryEmptyVisibility));

        _ = ResolveCoversAsync(); // fill covers Steam didn't cache locally from its public CDN (once)
    }

    // Fetch portrait covers for rows that had no local art, then swap them in on the UI thread. Most
    // installed games only have a 32px icon cached locally, so their real cover comes from Steam's public
    // CDN, fetched once and cached. Failures (404 / offline) leave the row's themed placeholder.
    private async Task ResolveCoversAsync()
    {
        foreach (var row in _allRows.ToList())
        {
            if (row.HasCover) continue;
            if (!_appIdByRow.TryGetValue(row.Id, out var appId) || string.IsNullOrEmpty(appId)) continue;
            var path = await _covers.FetchPortraitAsync(appId);
            if (path is null) continue;
            if (_dispatcher is null) row.SetCover(path);
            else _dispatcher.TryEnqueue(() => row.SetCover(path));
        }
    }

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
        var risk = BanRiskCatalog.ByAppId(g.SteamAppId);
        return risk == GameBanRisk.None ? null : risk.ToString();
    }

    private static IReadOnlyList<string> LoadersFor(GameEntry g)
    {
        if (string.IsNullOrEmpty(g.Engine)) return Array.Empty<string>();
        try
        {
            var playFolder = DirectInjectService.PlayFolder(g.GameRoot) ?? g.GameRoot;
            return LoaderScan.Detect(playFolder, g.Engine, g.SteamAppId)
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
        IEnumerable<GameLibraryRowViewModel> q = _allRows;

        var term = SearchText?.Trim();
        if (!string.IsNullOrEmpty(term))
            q = q.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(SourceFilter))
            q = q.Where(r => string.Equals(r.StoreSource, SourceFilter, StringComparison.OrdinalIgnoreCase));

        if (TierFilter is { } tier)
            q = q.Where(r => r.Tier == tier);

        if (BanRiskOnly)
            q = q.Where(r => r.BanRisk is not null);

        var filtered = q.ToList();
        Rows.Clear();
        foreach (var r in filtered) Rows.Add(r);

        // Recent strip is always the top-N of the FULL set (recency order), independent of the search
        // box — the strip is "jump back in," not a filtered view.
        RecentRows.Clear();
        foreach (var r in _allRows.Take(RecentCount)) RecentRows.Add(r);
    }

    private void RebuildDiscovery(IReadOnlyList<GameEntry> registered)
    {
        DiscoveryRows.Clear();
        var registeredAppIds = registered
            .Where(g => !string.IsNullOrEmpty(g.SteamAppId))
            .Select(g => g.SteamAppId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<InstalledGame> installed;
        try { installed = _store.InstalledGames(); }
        catch { installed = Array.Empty<InstalledGame>(); }

        foreach (var ig in installed)
        {
            if (registeredAppIds.Contains(ig.AppId)) continue;
            DiscoveryRows.Add(new DiscoveredGameViewModel(ig, id => _covers.LocalPortrait(id)));
        }
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

    /// <summary>Add a discovered (installed-but-unregistered) game — the shell runs the Add flow, then
    /// calls <see cref="Load"/> to refresh the home.</summary>
    [RelayCommand]
    private void AddDiscovered(DiscoveredGameViewModel? row)
    {
        if (row is null) return;
        AddGameRequested?.Invoke(row.Game);
    }
}
