using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Discovery;
using ModManager.Core.LooseMods;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// The single review for a whole "Identify my mods" run. Two sections: things not previously in the
/// mod list at all, and existing rows we can now name. Apply is the ONLY write path; Cancel writes
/// nothing.
///
/// <para>What is NOT here is deliberate: the run's fill-blanks pass (fetching a description and
/// cover art for a row whose Nexus mod id we already hold) never appears. That is not a claim about
/// WHICH mod something is — it is detail about an identity already established — and listing a
/// hundred rows of "we added a description" would bury the handful that need real judgment. The
/// rule for anything added to this dialog later: approve identity, do not approve detail.</para>
/// </summary>
public sealed partial class IdentifyReviewDialog : ContentDialog
{
    private readonly List<IdentifyReviewRow> _new = new();
    private readonly List<IdentifyReviewRow> _identified = new();

    public IdentifyReviewDialog(
        IReadOnlyList<AdoptionProposal> newToList,
        IReadOnlyList<LooseIdentifyProposal> nowIdentified)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this);

        foreach (var p in newToList)
        {
            var identified = p.Evidence != AdoptionEvidence.None;
            var loader = p.Candidate.Kind == DiscoveryKind.ProxyLoader;
            // See DiscoveryReviewDialog: a downloaded archive that was never installed has
            // nothing for adoption to attach metadata to. Both review surfaces read the one Core
            // rule so they cannot drift (A14).
            var inert = p.Reach == AdoptionReach.NothingToNameYet;
            var named = p.Reach == AdoptionReach.AlreadyNamed;
            _new.Add(new IdentifyReviewRow
            {
                Adoption = p,
                WillWrite = p.Reach is null or AdoptionReach.NamesAMod,
                // A loader is described as what it is rather than as an unidentified mod. Saying
                // "not identified" about a version.dll implies we failed to name something nameable;
                // we didn't — the name genuinely doesn't determine which loader it is. Wording is
                // kept identical to DiscoveryReviewDialog so the two surfaces can't drift.
                Headline = (loader, inert, identified) switch
                {
                    (true, _, _) => $"{p.Candidate.FileName} — mod loader",
                    (_, true, true) => $"{p.Candidate.FileName} — {p.Title} (downloaded, not installed)",
                    (_, true, false) => $"{p.Candidate.FileName} — downloaded, not installed",
                    (_, _, true) => $"{p.Candidate.FileName} — {p.Title}",
                    _ => $"{p.Candidate.FileName} — not identified",
                },
                Detail = (loader, inert, named, p.Evidence) switch
                {
                    (true, _, _, _) => $"Found at {p.Candidate.RelativePath}. This is the loader other mods ride on, not a mod itself. Several different loaders ship under this filename, so it can't be named from the file alone.",
                    (_, true, _, _) => $"Found at {p.Candidate.RelativePath}. This is the download, not an installed mod — nothing from it is in the game folder. Adopting names mods that are already installed, so it can't help here. Drop the file on the window to install it, and it'll be listed.",
                    (_, _, true, _) => $"Found at {p.Candidate.RelativePath}. Already named — nothing to add.",
                    (_, _, _, AdoptionEvidence.Md5) => $"Exact match by file hash. {p.Candidate.RelativePath}",
                    (_, _, _, AdoptionEvidence.NameIndex) => $"Matched by name{(p.Author is null ? "" : $" · by {p.Author}")}. {p.Candidate.RelativePath}",
                    _ => $"Found at {p.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                Approve = identified && !inert && !named,
            });
        }

        foreach (var p in nowIdentified)
        {
            _identified.Add(p.Match is null
                ? new IdentifyReviewRow { ModKey = p.ModKey, Headline = $"{p.CleanQuery} — no confident match" }
                : new IdentifyReviewRow
                {
                    ModKey = p.ModKey,
                    Hit = p.Match,
                    Approve = true,
                    Headline = $"{p.CleanQuery} → {p.Match.Name}"
                               + (string.IsNullOrWhiteSpace(p.Match.Author) ? "" : $" · by {p.Match.Author}"),
                    Detail = TrimSummary(p.Match.Summary),
                });
        }

        // A run whose whole result is loaders (Cyberpunk's bin/x64 proxies are the common case) gets
        // copy that matches what's actually on screen. Same wording as DiscoveryReviewDialog on
        // purpose — the two review surfaces say the same thing about the same finding.
        if (_identified.Count == 0 && _new.Count > 0
            && _new.All(r => r.Adoption!.Candidate.Kind == DiscoveryKind.ProxyLoader))
            Blurb.Text = "These are mod loaders — the piece other mods ride on. Adopting one tracks it here; your files are not moved.";

        NewList.ItemsSource = _new;
        IdentifiedList.ItemsSource = _identified;
        // A section with nothing in it is noise — hide its header and its list together.
        NewHeader.Visibility = NewList.Visibility = _new.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdentifiedHeader.Visibility = IdentifiedList.Visibility = _identified.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncPrimary();
    }

    public IReadOnlyList<AdoptionProposal> ApprovedAdoptions()
        => _new.Where(r => r.Approve && r.WillWrite && r.Adoption is not null).Select(r => r.Adoption!).ToList();

    public IReadOnlyList<(string ModKey, SourceSearchHit Hit)> ApprovedIdentifications()
        => _identified.Where(r => r.Approve && r.Hit is not null).Select(r => (r.ModKey, r.Hit!)).ToList();

    // No PrimaryButtonClick handler on purpose. DiscoveryReviewDialog needs one because it exposes
    // its result as a PROPERTY that has to be snapshotted before the dialog closes; this dialog
    // exposes METHODS, and the row lists outlive ShowAsync, so the caller reads them after the
    // ContentDialogResult comes back. An empty handler here would be dead code.

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is IdentifyReviewRow row) row.Approve = cb.IsChecked == true;
        SyncPrimary();
    }

    // One count across BOTH sections — the button promises exactly what Apply will write.
    private void SyncPrimary()
    {
        var n = ApprovedAdoptions().Count + ApprovedIdentifications().Count;
        PrimaryButtonText = $"Apply {n} change{(n == 1 ? "" : "s")}";
        IsPrimaryButtonEnabled = n > 0;
    }

    private static string TrimSummary(string? summary)
    {
        var s = (summary ?? "").Trim();
        return s.Length <= 160 ? s : s[..159].TrimEnd() + "…";
    }
}

/// <summary>
/// One reviewable row. Top-level (not nested) to match the rest of the App's x:Bind DataTemplate
/// rows — nesting it and referencing it via x:DataType="local:Outer+Row" compiles to an invalid
/// generic-type-argument cast in the generated bindings; the '+' nested-type separator is CLR
/// metadata syntax, not C#.
/// </summary>
public sealed class IdentifyReviewRow
{
    // Keyed on ModKey, not Headline: the headline is the proposed match and changes as the
    // identify pass revises itself, which is the one thing a review row must not be pinned to.
    public string RowAutomationId => $"IdentifyRow.{ModKey}";
    public string ApproveAutomationName => "Approve the match for " + Headline;

    public AdoptionProposal? Adoption { get; init; }   // set for "new to your list" rows
    public string ModKey { get; init; } = "";          // set for "now identified" rows
    public SourceSearchHit? Hit { get; init; }         // set for "now identified" rows
    public string Headline { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Approve { get; set; }

    /// <summary>True when the apply will actually write for this row. False for a download that was
    /// never installed, so "Apply N changes" counts changes rather than ticks (A14).</summary>
    public bool WillWrite { get; init; } = true;

    // An unmatched row has nothing to approve — show the line, drop the checkbox.
    public Visibility CheckboxVisibility => Adoption is not null || Hit is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
}
