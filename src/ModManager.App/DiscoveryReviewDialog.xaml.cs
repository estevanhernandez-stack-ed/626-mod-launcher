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
            // Adoption attaches metadata to mods that ARE installed. A downloaded archive that has
            // never been deployed has nothing to attach to, and saying otherwise is how thirteen
            // Fluffy downloads on a game with no natives/ folder came to sit under a heading
            // reading "Mods already installed" (A14).
            var inert = proposal.Reach == AdoptionReach.NothingToNameYet;
            var named = proposal.Reach == AdoptionReach.AlreadyNamed;
            _rows.Add(new DiscoveryReviewRow
            {
                Proposal = proposal,
                // Only a row adoption can actually write for counts toward "Adopt N mods". An
                // unresolved reach (null) keeps the old optimistic assumption rather than hiding
                // a row we simply failed to check.
                WillWrite = proposal.Reach is null or AdoptionReach.NamesAMod,
                // A loader is described as what it is rather than as an unidentified mod. Saying
                // "not identified" about a version.dll implies we failed to name something nameable;
                // we didn't — the name genuinely doesn't determine which loader it is.
                Headline = (loader, inert, identified) switch
                {
                    (true, _, _) => $"{proposal.Candidate.FileName} — mod loader",
                    (_, true, true) => $"{proposal.Candidate.FileName} — {proposal.Title} (downloaded, not installed)",
                    (_, true, false) => $"{proposal.Candidate.FileName} — downloaded, not installed",
                    (_, _, true) => $"{proposal.Candidate.FileName} — {proposal.Title}",
                    _ => $"{proposal.Candidate.FileName} — not identified",
                },
                Detail = (loader, inert, named, proposal.Evidence) switch
                {
                    (true, _, _, _) => $"Found at {proposal.Candidate.RelativePath}. This is the loader other mods ride on, not a mod itself. Several different loaders ship under this filename, so it can't be named from the file alone.",
                    // The honest version of what used to read "Adopt it to manage it anyway."
                    (_, true, _, _) => $"Found at {proposal.Candidate.RelativePath}. This is the download, not an installed mod — nothing from it is in the game folder. Adopting names mods that are already installed, so it can't help here. Drop the file on the window to install it, and it'll be listed.",
                    (_, _, true, _) => $"Found at {proposal.Candidate.RelativePath}. Already named — nothing to add.",
                    (_, _, _, AdoptionEvidence.Md5) => $"Matched exactly by file hash. {proposal.Candidate.RelativePath}",
                    (_, _, _, AdoptionEvidence.NameIndex) => $"Matched by name{(proposal.Author is null ? "" : $" · by {proposal.Author}")}. {proposal.Candidate.RelativePath}",
                    _ => $"Found at {proposal.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                // Never pre-check a row the apply cannot write for. Checked-by-default is a
                // recommendation, and recommending a no-op is how the count got to thirteen.
                Approve = identified && !inert && !named,
            });
        }

        // A sweep that found only loaders (Cyberpunk's bin/x64 proxies are the common case) gets
        // copy that matches what's actually on screen — the stock blurb promises "mods you
        // installed by hand", which describes none of them.
        if (_rows.Count > 0 && _rows.All(r => r.Proposal.Candidate.Kind == DiscoveryKind.ProxyLoader))
            Blurb.Text = "These are mod loaders — the piece other mods ride on. Adopting one tracks it here; your files are not moved.";

        // Every row is a download that was never installed — the Fluffy / downloads-folder case,
        // and the one the stock copy described worst. Say what these actually are and what would
        // help, rather than heading a list of inert archives with "Mods already installed".
        else if (_rows.Count > 0 && _rows.All(r => !r.WillWrite))
        {
            HeadingText.Text = "Mods you've downloaded";
            Blurb.Text = "These are downloads sitting in this game's folders — none of them is installed, so "
                         + "there's nothing for 626 to name yet. Drop them on the window and it'll install them, "
                         + "and they'll show up in the list.";
        }

        RowList.ItemsSource = _rows;
        SyncPrimary();
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Approved = _rows.Where(r => r.Approve && r.WillWrite).Select(r => r.Proposal).ToList();

    private void OnRowClick(object sender, RoutedEventArgs e) => SyncPrimary();

    // The primary button carries the live count so "Adopt" always says exactly what it will write.
    // Counts MODS, not files: one UE mod ships as a pak/ucas/utoc triplet, and the proposals were
    // collapsed to mod-key space upstream (DiscoverySweep.Deduplicate) so this number matches the
    // rows the mod list will actually gain. Says "loaders" when that's all that's checked.
    private void SyncPrimary()
    {
        // Counts what the apply will WRITE, not what is ticked. A row adoption cannot help stays
        // tickable — the user may know something we do not — but it never inflates the promise.
        var approved = _rows.Where(r => r.Approve && r.WillWrite).ToList();
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
    public string ApproveAutomationName => "Approve " + Headline;

    public AdoptionProposal Proposal { get; init; } = null!;
    public string Headline { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Approve { get; set; }

    /// <summary>True when the apply will actually write something for this row. False for a download
    /// that was never installed — adoption has nothing to attach metadata to (A14).</summary>
    public bool WillWrite { get; init; } = true;
}
