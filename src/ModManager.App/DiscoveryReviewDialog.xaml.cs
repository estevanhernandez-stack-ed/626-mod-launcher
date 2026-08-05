using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>
/// Review-before-adopt for discovered mods: one row per proposal, checked by default when we
/// identified it, unchecked when we didn't. Apply is the ONLY path that returns approvals;
/// Cancel returns nothing. Adoption writes metadata only — no file is touched either way.
/// </summary>
public sealed partial class DiscoveryReviewDialog : ContentDialog
{
    private readonly List<DiscoveryReviewRow> _rows = new();

    public IReadOnlyList<AdoptionProposal> Approved { get; private set; } = Array.Empty<AdoptionProposal>();

    public DiscoveryReviewDialog(IReadOnlyList<AdoptionProposal> proposals)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this);

        foreach (var proposal in proposals)
        {
            var identified = proposal.Evidence != AdoptionEvidence.None;
            _rows.Add(new DiscoveryReviewRow
            {
                Proposal = proposal,
                Headline = identified
                    ? $"{proposal.Candidate.FileName} — {proposal.Title}"
                    : $"{proposal.Candidate.FileName} — not identified",
                Detail = proposal.Evidence switch
                {
                    AdoptionEvidence.Md5 => $"Matched exactly by file hash. {proposal.Candidate.RelativePath}",
                    AdoptionEvidence.NameIndex => $"Matched by name{(proposal.Author is null ? "" : $" · by {proposal.Author}")}. {proposal.Candidate.RelativePath}",
                    _ => $"Found at {proposal.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                Approve = identified,
            });
        }

        RowList.ItemsSource = _rows;
        SyncPrimary();
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Approved = _rows.Where(r => r.Approve).Select(r => r.Proposal).ToList();

    private void OnRowClick(object sender, RoutedEventArgs e) => SyncPrimary();

    // The primary button carries the live count so "Adopt" always says exactly what it will write.
    private void SyncPrimary()
    {
        var n = _rows.Count(r => r.Approve);
        PrimaryButtonText = $"Adopt {n} mod{(n == 1 ? "" : "s")}";
        IsPrimaryButtonEnabled = n > 0;
    }
}

/// <summary>
/// One reviewable row for <see cref="DiscoveryReviewDialog"/>. Top-level (not nested) to match
/// the rest of the App's x:Bind DataTemplate rows (see ProfileRow, SaveFileRow, ...) — nesting
/// this inside DiscoveryReviewDialog and referencing it via x:DataType="local:Outer+Row" compiles
/// to an invalid generic-type-argument cast (WinRT.CastExtensions.As&lt;Outer+Row&gt;) in the
/// generated x:Bind bindings code; the '+' nested-type separator is only valid CLR metadata
/// syntax, not C#, and the XAML compiler doesn't translate it in every generated call site.
/// </summary>
public sealed class DiscoveryReviewRow
{
    public AdoptionProposal Proposal { get; init; } = null!;
    public string Headline { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Approve { get; set; }
}
