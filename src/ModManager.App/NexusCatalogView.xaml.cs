using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModManager.App.ViewModels;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// One card in the Nexus storefront — a display wrapper over a <see cref="SourceSearchHit"/> so the XAML
/// binds plain strings and visibilities instead of formatting in the template. Immutable: cards are built
/// once per page and thrown away on a reload, so every binding is OneTime.
///
/// <para>The badges are the point of the whole surface. <c>Viewer*</c> is a <c>bool?</c> where null means
/// UNKNOWN — the account is disconnected, or an older plugin never set it. A badge shows ONLY on an
/// explicit <c>true</c>; null and false both render nothing, so "we don't know" never reads as "no".</para>
/// </summary>
public sealed class NexusCatalogCard
{
    // Ctor-injected and read-only on purpose: an init-only `required` property makes WinUI's
    // XamlTypeInfo generator emit an activator + setter it can't satisfy (CS9035 / CS8852). No public
    // parameterless ctor => the generator treats the type as non-constructible, which is what we want —
    // XAML only ever reads these cards.
    public NexusCatalogCard(SourceSearchHit hit) => Hit = hit;

    public SourceSearchHit Hit { get; }

    public string Name => Hit.Name;
    public string Author => string.IsNullOrWhiteSpace(Hit.Author) ? "" : $"by {Hit.Author}";
    public string Summary => TrimSummary(Hit.Summary);
    public string Endorsements => Hit.EndorsementCount is { } n ? $"♥ {n:N0}" : "";
    public string Downloads => Hit.DownloadCount is { } n ? $"⬇ {n:N0}" : "";
    public string Initial => Hit.Name.Length > 0 ? Hit.Name[..1].ToUpperInvariant() : "?";

    /// <summary>Category / version / updated date, collapsed into one quiet line — whichever parts the
    /// plugin actually supplied (an older plugin supplies none, and the line disappears).</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Hit.Category)) parts.Add(Hit.Category!);
            if (!string.IsNullOrWhiteSpace(Hit.Version)) parts.Add($"v{Hit.Version!.TrimStart('v', 'V')}");
            if (Hit.UpdatedAt is { } u) parts.Add($"Updated {u.ToLocalTime():MMM d, yyyy}");
            return string.Join("  ·  ", parts);
        }
    }

    public Visibility AuthorVisibility => Vis(Author.Length > 0);
    public Visibility SummaryVisibility => Vis(Summary.Length > 0);
    public Visibility EndorsementsVisibility => Vis(Endorsements.Length > 0);
    public Visibility DownloadsVisibility => Vis(Downloads.Length > 0);
    public Visibility MetaVisibility => Vis(MetaLine.Length > 0);

    // Explicit-true only. See the class remarks — null is unknown, not a no.
    public Visibility InstalledVisibility => Vis(Hit.ViewerDownloaded == true);
    public Visibility EndorsedVisibility => Vis(Hit.ViewerEndorsed == true);
    public Visibility UpdateAvailableVisibility => Vis(Hit.ViewerUpdateAvailable == true);

    /// <summary>Lazily built remote thumbnail. Decode is capped at the on-screen width so a page of cards
    /// costs a couple of MB rather than tens; a non-http (or absent) URL yields null and the card's
    /// placeholder shows through. WinUI fetches and decodes off the UI thread, and a failed fetch raises
    /// ImageFailed rather than throwing.</summary>
    private ImageSource? _thumbnail;
    private bool _thumbnailResolved;
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailResolved)
            {
                _thumbnailResolved = true;
                if (ModManager.Core.SafeUrl.IsHttpUrl(Hit.ThumbnailUrl)
                    && Uri.TryCreate(Hit.ThumbnailUrl, UriKind.Absolute, out var uri))
                    _thumbnail = new BitmapImage(uri) { DecodePixelWidth = 192 };
            }
            return _thumbnail;
        }
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private static string TrimSummary(string? summary)
    {
        var s = (summary ?? "").Trim();
        return s.Length <= 160 ? s : s[..159].TrimEnd() + "…";
    }
}

/// <summary>
/// The in-app Nexus storefront: a full-size surface (a UserControl swapped into MainWindow's
/// <c>CatalogHost</c>, mirroring <see cref="LibraryView"/>) rather than a dialog — a card grid needs the
/// room. Search + sort view + category filter + load-more paging over
/// <see cref="MainViewModel.BrowseCatalogAsync"/>, which is gated on the plugin carrying the Phase 1
/// browse capability. Older (0.12.x) plugins never reach here; the shell keeps them on
/// <see cref="NexusCatalogDialog"/>.
///
/// <para>Download stays a browser handoff: Get opens the mod's Nexus page through the same
/// <c>SafeUrl.IsHttpUrl</c> + <c>Process.Start(UseShellExecute)</c> call the rest of the app uses. Nothing
/// is ever fetched in-app. Adult content is excluded server-side in the plugin, so there is no
/// client-side filtering and no age gate here.</para>
///
/// <para>Never wedges: <c>BrowseCatalogAsync</c> self-timeouts and never throws, and every load carries a
/// generation stamp — a result whose stamp is stale (the user changed a filter mid-flight) is dropped
/// instead of replacing or appending onto a newer page.</para>
/// </summary>
public sealed partial class NexusCatalogView : UserControl
{
    private const int PageSize = 20;

    private readonly MainViewModel _vm;
    private readonly string _gameName;

    /// <summary>The loaded cards, in server order. Public so the GridView can x:Bind it.</summary>
    public ObservableCollection<NexusCatalogCard> Cards { get; } = new();

    // Mod ids already on screen — load-more appends, and a listing can shift between pages, so a
    // duplicate would otherwise slip in.
    private readonly HashSet<int> _seen = new();

    // Category dropdown: labels are what the user sees ("Gameplay (1238)"), values are what the query
    // carries (null for the "All categories" row).
    private readonly List<string?> _categoryValues = new();

    // In-flight guard. Every load bumps the generation; a completing load that no longer owns the
    // current generation drops its page on the floor. Rapid sort/category clicks therefore can't
    // interleave pages out of order, and the newest request always wins (including the busy state,
    // which the winner alone clears).
    private int _generation;

    private bool _suppressFilterEvents;
    private string _query = "";
    private int _total;

    /// <summary>Raised by the Back affordance — the shell collapses the host and returns to the game view.</summary>
    public event EventHandler? BackRequested;

    public NexusCatalogView(MainViewModel vm, string gameName)
    {
        _vm = vm;
        _gameName = string.IsNullOrWhiteSpace(gameName) ? "this game" : gameName;
        InitializeComponent();

        TitleLabel.Text = $"Browse Nexus — {_gameName}";
        ResetCategories();

        // A storefront opens populated, not empty: the first load is the default most-endorsed listing.
        Loaded += (_, _) => _ = LoadAsync(append: false);
    }

    // ---------- input ----------

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnQueryKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        Submit();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        _query = (QueryBox.Text ?? "").Trim();
        _ = LoadAsync(append: false);
    }

    // Sort or category changed — reload from the first page. Suppressed while the category list is being
    // rebuilt in code, which would otherwise fire a second load on top of the one that just landed.
    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || !IsLoaded) return;
        _ = LoadAsync(append: false);
    }

    private void OnLoadMoreClick(object sender, RoutedEventArgs e) => _ = LoadAsync(append: true);

    // Get = browser handoff, unchanged from the dialog it replaces. The download happens on Nexus, in the
    // user's browser, on the author's page; the launcher never fetches a mod file.
    private void OnGetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NexusCatalogCard card }) return;
        var url = card.Hit.Url;
        if (!string.IsNullOrWhiteSpace(url) && ModManager.Core.SafeUrl.IsHttpUrl(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    // A CDN thumbnail that 404s / times out / decodes badly: collapse the Image so the card's neutral
    // placeholder shows through. Never throws, never blanks the card.
    private void OnThumbnailFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img) img.Visibility = Visibility.Collapsed;
    }

    // ---------- loading ----------

    private async Task LoadAsync(bool append)
    {
        var gen = ++_generation;
        var offset = append ? Cards.Count : 0;
        var category = SelectedCategory();

        ShowBusy(append);

        var page = await _vm.BrowseCatalogAsync(
            string.IsNullOrWhiteSpace(_query) ? null : _query,
            SelectedSort(),
            category,
            offset,
            PageSize);

        // Superseded mid-flight — the newer load owns the list and the busy state. Drop this page.
        if (gen != _generation) return;

        if (!append)
        {
            Cards.Clear();
            _seen.Clear();
        }

        foreach (var hit in page.Hits)
            if (_seen.Add(hit.ModId))
                Cards.Add(new NexusCatalogCard(hit));

        _total = page.TotalCount;

        // Facets are scoped to the current filter, so refreshing the dropdown while a category is applied
        // would collapse it to the one bucket the user already picked. Only rebuild on an unfiltered load.
        if (!append && category is null && page.Categories.Count > 0)
            SetCategories(page.Categories);

        ShowResults();
    }

    private void ShowBusy(bool append)
    {
        if (append)
        {
            LoadMoreButton.IsEnabled = false;
            LoadMoreButton.Content = "Loading…";
            return;
        }

        Cards.Clear();
        _seen.Clear();
        LoadMoreButton.Visibility = Visibility.Collapsed;
        CountLabel.Text = "";
        StatusLabel.Text = _query.Length == 0 ? $"Loading {_gameName} mods…" : "Searching…";
        StatusLabel.Visibility = Visibility.Visible;
    }

    private void ShowResults()
    {
        LoadMoreButton.IsEnabled = true;
        LoadMoreButton.Content = "Load more";
        LoadMoreButton.Visibility = Cards.Count > 0 && Cards.Count < _total
            ? Visibility.Visible
            : Visibility.Collapsed;

        CountLabel.Text = Cards.Count == 0 ? "" : $"{Cards.Count:N0} of {_total:N0}";

        if (Cards.Count > 0)
        {
            StatusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        StatusLabel.Text = _query.Length > 0
            ? $"No results for '{_query}'."
            : SelectedCategory() is { } c
                ? $"No {c} mods found for {_gameName}."
                : $"No Nexus mods found for {_gameName}.";
        StatusLabel.Visibility = Visibility.Visible;
    }

    // ---------- filter state ----------

    private CatalogSort SelectedSort() => SortBox.SelectedIndex switch
    {
        1 => CatalogSort.MostDownloaded,
        2 => CatalogSort.RecentlyUpdated,
        3 => CatalogSort.RecentlyAdded,
        _ => CatalogSort.MostEndorsed,
    };

    private string? SelectedCategory()
    {
        var i = CategoryBox.SelectedIndex;
        return i >= 0 && i < _categoryValues.Count ? _categoryValues[i] : null;
    }

    private void ResetCategories()
    {
        _suppressFilterEvents = true;
        _categoryValues.Clear();
        _categoryValues.Add(null);
        CategoryBox.ItemsSource = new List<string> { "All categories" };
        CategoryBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    // Rebuild the dropdown from the page's facet data (the only correct mod-category source), preserving
    // the user's current pick when that category is still present.
    private void SetCategories(IReadOnlyList<CatalogCategory> categories)
    {
        var previous = SelectedCategory();

        _suppressFilterEvents = true;
        _categoryValues.Clear();
        var labels = new List<string> { "All categories" };
        _categoryValues.Add(null);
        foreach (var c in categories)
        {
            labels.Add($"{c.Name} ({c.Count:N0})");
            _categoryValues.Add(c.Name);
        }
        CategoryBox.ItemsSource = labels;
        var restored = previous is null ? 0 : _categoryValues.IndexOf(previous);
        CategoryBox.SelectedIndex = restored >= 0 ? restored : 0;
        _suppressFilterEvents = false;
    }
}
