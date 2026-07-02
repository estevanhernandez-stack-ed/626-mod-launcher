using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// Review-first batch dialog for loose-root name-search identify: one row per proposal. Matched
/// rows carry a checkbox (checked by default) reading "query → Title · Author · N endorsements"
/// with the trimmed summary underneath; unmatched rows render greyed as "query — no confident
/// match" with no checkbox. Apply is the ONLY write path out of here — it returns the checked
/// (ModKey, hit) pairs for the VM to merge + persist; Cancel writes nothing. The primary button
/// carries a live count ("Apply N matches") and disables at zero.
/// </summary>
public sealed partial class LooseIdentifyDialog : ContentDialog
{
    public sealed class Row
    {
        public string ModKey { get; init; } = "";
        public string Headline { get; init; } = "";
        public string Detail { get; init; } = "";
        public SourceSearchHit? Hit { get; init; }
        public bool Approve { get; set; }
        public Visibility MatchVisibility => Hit is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoMatchVisibility => Hit is null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly List<Row> _rows = new();

    public LooseIdentifyDialog(IReadOnlyList<LooseIdentifyProposal> proposals)
    {
        InitializeComponent();
        foreach (var p in proposals)
        {
            _rows.Add(p.Match is null
                ? new Row { ModKey = p.ModKey, Headline = $"{p.CleanQuery} — no confident match" }
                : new Row
                {
                    ModKey = p.ModKey,
                    Hit = p.Match,
                    Approve = true, // review-first default: matched rows opt IN, one uncheck opts out
                    Headline = HeadlineOf(p.CleanQuery, p.Match),
                    Detail = TrimSummary(p.Match.Summary),
                });
        }
        ProposalList.ItemsSource = _rows;
        SyncPrimary();
    }

    /// <summary>The pairs the user approved — the only thing the apply path is allowed to write.</summary>
    public IReadOnlyList<(string ModKey, SourceSearchHit Hit)> Approved()
        => _rows.Where(r => r.Approve && r.Hit is not null).Select(r => (r.ModKey, r.Hit!)).ToList();

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is Row row) row.Approve = cb.IsChecked == true;
        SyncPrimary();
    }

    // The primary button carries the live count so "Apply" always says exactly what it will write.
    private void SyncPrimary()
    {
        var n = _rows.Count(r => r.Approve && r.Hit is not null);
        PrimaryButtonText = $"Apply {n} match{(n == 1 ? "" : "es")}";
        IsPrimaryButtonEnabled = n > 0;
    }

    private static string HeadlineOf(string query, SourceSearchHit hit)
    {
        var parts = new List<string> { hit.Name };
        if (!string.IsNullOrWhiteSpace(hit.Author)) parts.Add(hit.Author!);
        if (hit.EndorsementCount is { } n) parts.Add($"{n:N0} endorsement{(n == 1 ? "" : "s")}");
        return $"{query} → {string.Join(" · ", parts)}";
    }

    private static string TrimSummary(string? summary)
    {
        var s = (summary ?? "").Trim();
        return s.Length <= 160 ? s : s[..159].TrimEnd() + "…";
    }
}
