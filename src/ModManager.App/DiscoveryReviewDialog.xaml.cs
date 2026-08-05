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
            var loader = proposal.Candidate.Kind == DiscoveryKind.ProxyLoader;
            _rows.Add(new DiscoveryReviewRow
            {
                Proposal = proposal,
                // A loader is described as what it is rather than as an unidentified mod. Saying
                // "not identified" about a version.dll implies we failed to name something nameable;
                // we didn't — the name genuinely doesn't determine which loader it is.
                Headline = (identified, loader) switch
                {
                    (true, _) => $"{proposal.Candidate.FileName} — {proposal.Title}",
                    (false, true) => $"{proposal.Candidate.FileName} — mod loader",
                    _ => $"{proposal.Candidate.FileName} — not identified",
                },
                Detail = (proposal.Evidence, loader) switch
                {
                    (AdoptionEvidence.Md5, _) => $"Matched exactly by file hash. {proposal.Candidate.RelativePath}",
                    (AdoptionEvidence.NameIndex, _) => $"Matched by name{(proposal.Author is null ? "" : $" · by {proposal.Author}")}. {proposal.Candidate.RelativePath}",
                    (_, true) => $"Found at {proposal.Candidate.RelativePath}. This is the loader other mods ride on, not a mod itself. Several different loaders ship under this filename, so it can't be named from the file alone.",
                    _ => $"Found at {proposal.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                Approve = identified,
            });
        }

        // A sweep that found only loaders (Cyberpunk's bin/x64 proxies are the common case) gets
        // copy that matches what's actually on screen — the stock blurb promises "mods you
        // installed by hand", which describes none of them.
        if (_rows.Count > 0 && _rows.All(r => r.Proposal.Candidate.Kind == DiscoveryKind.ProxyLoader))
            Blurb.Text = "These are mod loaders — the piece other mods ride on. Adopting one tracks it here; your files are not moved.";

        RowList.ItemsSource = _rows;
        SyncPrimary();
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Approved = _rows.Where(r => r.Approve).Select(r => r.Proposal).ToList();

    private void OnRowClick(object sender, RoutedEventArgs e) => SyncPrimary();

    // The primary button carries the live count so "Adopt" always says exactly what it will write.
    // Counts MODS, not files: one UE mod ships as a pak/ucas/utoc triplet, and the proposals were
    // collapsed to mod-key space upstream (DiscoverySweep.Deduplicate) so this number matches the
    // rows the mod list will actually gain. Says "loaders" when that's all that's checked.
    private void SyncPrimary()
    {
        var approved = _rows.Where(r => r.Approve).ToList();
        var n = approved.Count;
        var noun = n > 0 && approved.All(r => r.Proposal.Candidate.Kind == DiscoveryKind.ProxyLoader)
            ? "loader"
            : "mod";
        PrimaryButtonText = $"Adopt {n} {noun}{(n == 1 ? "" : "s")}";
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
