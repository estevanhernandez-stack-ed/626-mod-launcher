using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>What the review came back with. Two different answers, because the dialog now shows two
/// different kinds of thing: mods that are installed and want naming, and downloads that were never
/// installed at all. Adopt is not install, so the outcome cannot be one list (A14).</summary>
/// <param name="Approved">Proposals to adopt — metadata only, no file moves.</param>
/// <param name="ToInstall">Downloads the user asked 626 to install, handed to the normal intake path.</param>
public sealed record DiscoveryReviewOutcome(
    IReadOnlyList<AdoptionProposal> Approved,
    IReadOnlyList<AdoptionProposal> ToInstall)
{
    public static DiscoveryReviewOutcome Nothing { get; } =
        new(Array.Empty<AdoptionProposal>(), Array.Empty<AdoptionProposal>());
}

/// <summary>
/// Review-before-adopt for discovered mods: one row per proposal, checked by default when we
/// identified it, unchecked when we didn't. Apply is the ONLY path that returns approvals;
/// Cancel returns nothing. Adoption writes metadata only — no file is touched either way.
/// </summary>
public sealed partial class DiscoveryReviewDialog : ContentDialog
{
    private readonly List<DiscoveryReviewRow> _rows = new();

    public IReadOnlyList<AdoptionProposal> Approved { get; private set; } = Array.Empty<AdoptionProposal>();

    /// <summary>The downloads the user asked to INSTALL rather than adopt. Adoption attaches
    /// metadata to mods that are already installed; for an archive that was never deployed there is
    /// nothing to attach to, and the action that actually helps is the one drag-and-drop already
    /// does. Non-empty only when the Install button was used (A14).</summary>
    public IReadOnlyList<AdoptionProposal> ToInstall { get; private set; } = Array.Empty<AdoptionProposal>();

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
                // Only a real archive can be handed back to intake. An inert row of any other kind
                // is not a file we could drop on ourselves, so it gets the honest copy and no offer.
                CanInstall = inert && proposal.Candidate.Kind == DiscoveryKind.Archive,
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
            // Nothing here is adoptable, so the only action on offer is Install and pre-ticking
            // cannot inflate an adopt count. In a MIXED sweep they stay unticked — there the ticks
            // mean "adopt", and pre-ticking rows that adoption cannot write for is the thing that
            // produced "Adopt 13 mods" in the first place.
            foreach (var row in _rows.Where(r => r.CanInstall)) row.Approve = true;
            Blurb.Text = "These are downloads sitting in this game's folders — none of them is installed, so "
                         + "there's nothing for 626 to name yet. Drop them on the window and it'll install them, "
                         + "and they'll show up in the list.";
        }

        RowList.ItemsSource = _rows;
        SyncPrimary();
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Approved = _rows.Where(r => r.Approve && r.WillWrite).Select(r => r.Proposal).ToList();

    // Install hands the ticked downloads back to the normal intake path — the same one a
    // drag-and-drop uses, with its ban-risk gate, its replacement confirmation and its
    // validate-then-extract order. This dialog installs nothing itself.
    private void OnInstall(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => ToInstall = _rows.Where(r => r.Approve && r.CanInstall).Select(r => r.Proposal).ToList();

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

        // The second action, offered only when there is something it could do. A dialog that shows
        // an Install button over nothing installable is the same overstatement in a different place.
        var installable = _rows.Count(r => r.Approve && r.CanInstall);
        SecondaryButtonText = installable == 0
            ? ""
            : $"Install {installable} download{(installable == 1 ? "" : "s")}";
        IsSecondaryButtonEnabled = installable > 0;
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

    /// <summary>True when this row is a download 626 could install for the user — an archive that
    /// adoption cannot help with. Drives the Install action (A14).</summary>
    public bool CanInstall { get; init; }
}
