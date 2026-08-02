using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// In-app Nexus catalog browse: a search box over the active game's Nexus domain wired to
/// <see cref="MainViewModel.SearchCatalogAsync"/>. Results render one row per <see cref="SourceSearchHit"/>
/// — name, author, endorsement heart, trimmed summary — with a Get button that hands off to the browser
/// (the SAME open-URL call the "Find mods" menu uses). No client-side filtering: adult/mature content is
/// excluded server-side in the plugin, so the dialog just renders what it receives. States are simple —
/// Initial → Loading → (results | Empty); SearchCatalogAsync never throws, so a real attempt with no hits
/// is just Empty. Mirrors <see cref="LooseIdentifyDialog"/>'s ctor(VM) + ShowAsync pattern.
/// </summary>
public sealed partial class NexusCatalogDialog : ContentDialog
{
    public sealed class Row
    {
        public required SourceSearchHit Hit { get; init; }
        public string Name => Hit.Name;
        public string Author => Hit.Author ?? "";
        public string Endorsements => Hit.EndorsementCount is { } n ? $"♥ {n:N0}" : "";
        public string Summary => TrimSummary(Hit.Summary);
        public Visibility AuthorVisibility => string.IsNullOrWhiteSpace(Author) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EndorsementsVisibility => Hit.EndorsementCount is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SummaryVisibility => string.IsNullOrEmpty(Summary) ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly MainViewModel _vm;
    private readonly string _gameName;

    public NexusCatalogDialog(MainViewModel vm, string gameName)
    {
        InitializeComponent();
        _vm = vm;
        _gameName = string.IsNullOrWhiteSpace(gameName) ? "this game" : gameName;
        ShowInitial();
        // Auto-load the default (most-endorsed) listing on open — a catalog opens populated, not empty.
        // QueryBox is empty at open, so RunSearchAsync fires a blank query = the listing.
        Opened += (_, _) => _ = RunSearchAsync();
    }

    private void OnQueryKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = RunSearchAsync();
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

    // Get = browser handoff. Opens the mod's Nexus page with the SAME open-URL call MainWindow.OnFindMods
    // uses (SafeUrl.IsHttpUrl gate + Process.Start UseShellExecute). Downloading stays on the website;
    // the user drops the file back into intake unchanged.
    private void OnGetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Row row) return;
        var url = row.Hit.Url;
        if (!string.IsNullOrWhiteSpace(url) && ModManager.Core.SafeUrl.IsHttpUrl(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task RunSearchAsync()
    {
        var query = (QueryBox.Text ?? "").Trim();

        ShowLoading(query);
        // Blank query = the default most-endorsed listing; a typed query narrows by name. Never throws;
        // adult content is excluded server-side.
        var hits = await _vm.SearchCatalogAsync(query);

        if (hits.Count == 0)
        {
            ResultsList.ItemsSource = null;
            StatusLabel.Text = query.Length == 0
                ? $"No Nexus mods found for {_gameName}."
                : $"No results for '{query}'.";
            StatusLabel.Visibility = Visibility.Visible;
            return;
        }

        ResultsList.ItemsSource = hits.Select(h => new Row { Hit = h }).ToList();
        StatusLabel.Visibility = Visibility.Collapsed;
    }

    private void ShowInitial()
    {
        ResultsList.ItemsSource = null;
        StatusLabel.Text = $"Search Nexus for {_gameName} mods.";
        StatusLabel.Visibility = Visibility.Visible;
    }

    private void ShowLoading(string query)
    {
        ResultsList.ItemsSource = null;
        StatusLabel.Text = query.Length == 0 ? $"Loading {_gameName} mods…" : "searching…";
        StatusLabel.Visibility = Visibility.Visible;
    }

    private static string TrimSummary(string? summary)
    {
        var s = (summary ?? "").Trim();
        return s.Length <= 160 ? s : s[..159].TrimEnd() + "…";
    }
}
