using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ModManager.App.ViewModels;
using ModManager.Core;
using ModManager.Core.Nexus;
using ModManager.Plugins.Abstractions;

namespace ModManager.App;

/// <summary>
/// One mod, in full, without leaving the launcher: hero art, name, author/uploader, version, category,
/// ♥ endorsements + ⬇ downloads, updated date, the readable description, and the requirements list —
/// plus the two things that honor the author, <b>Endorse</b> and <b>Track</b>, done in-app.
///
/// <para>A <see cref="ContentDialog"/> is the right shape here, unlike the storefront grid: this is a
/// single-item read with a fixed width, no recycled containers, and one obvious way out.</para>
///
/// <para><b>Download stays a browser handoff.</b> Every outbound link on this surface — the mod page and
/// each requirement — goes through <see cref="SafeUrl.IsHttpUrl"/> then
/// <c>Process.Start(UseShellExecute)</c>. The launcher never fetches a mod file.</para>
///
/// <para><b>The writes are real.</b> Endorse and Track mutate the signed-in user's actual Nexus account,
/// so they fire from an explicit click on those two toggles and from nowhere else — never on open, load,
/// refresh, or as a retry. Each toggle is optimistic (the button flips first), disables itself for the
/// duration of its own call so a double-click cannot race, and reverts with a short inline message if the
/// plugin reports failure. Initial state comes from <c>ViewerEndorsed</c>/<c>ViewerTracked</c>, where
/// <c>null</c> means UNKNOWN and shows the un-acted state — never a wrong "already endorsed".</para>
///
/// <para>Never throws: the VM's detail/action calls swallow their own failures, the click handlers are
/// plain (non-async) delegations into guarded tasks so nothing can throw into the dispatcher, and a failed
/// fetch lands on a message that still offers the browser link.</para>
/// </summary>
public sealed partial class NexusModDetailDialog : ContentDialog
{
    /// <summary>One prerequisite row. Public + plain properties because the template binds it with
    /// <c>{Binding}</c> (the established dialog pattern here — see <see cref="LooseIdentifyDialog.Row"/>).
    /// A requirement can be EXTERNAL (not a Nexus mod at all, just an off-site URL), and it can arrive with
    /// no usable link and no notes — both render without breaking the list.</summary>
    public sealed class RequirementRow
    {
        public string Name { get; init; } = "";
        /// <summary>The quiet second line: "Off-site link" for an external requirement, the author's notes,
        /// or both joined. Empty when the requirement carried neither.</summary>
        public string Detail { get; init; } = "";
        /// <summary>Resolved http(s) target, or null when there is nowhere to send the user.</summary>
        public string? Link { get; init; }
        public string LinkTip { get; init; } = "";

        public Visibility LinkVisibility => Link is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PlainVisibility => Link is null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DetailVisibility => Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly MainViewModel _vm;
    private readonly int _gameId;
    private readonly int _modId;

    // The browser target. Seeded from the card so the failure state can still offer it, then upgraded to
    // the detail's own URL once that lands.
    private string? _openUrl;

    // The mod's Nexus UID — the key BOTH mutations take (never the ModId). Null until detail lands, or
    // when the loaded plugin has no actions capability; the toggles stay hidden in that case.
    private string? _uid;

    // Committed action state — what we believe Nexus holds. The toggles' IsChecked is the OPTIMISTIC view;
    // these are what a failed call reverts to.
    private bool _endorsed;
    private bool _tracked;

    // Per-toggle in-flight guards. The IsEnabled=false below is the real double-click defence (set
    // synchronously, before the first await); these are the belt to that pair of braces.
    private bool _endorseBusy;
    private bool _trackBusy;

    private bool _loadStarted;

    /// <param name="hit">The card that was clicked — supplies the fallback name and URL so the failure
    /// state is still useful when the detail fetch comes back empty.</param>
    /// <param name="gameId">NUMERIC Nexus game id. The detail query takes an id, not a domain slug.</param>
    public NexusModDetailDialog(MainViewModel vm, SourceSearchHit hit, int gameId)
    {
        _vm = vm;
        _gameId = gameId;
        _modId = hit.ModId;

        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes

        HeroInitial.Text = InitialOf(hit.Name);
        NameLabel.Text = hit.Name;
        _openUrl = SafeUrl.IsHttpUrl(hit.Url) ? hit.Url : null;

        // Don't let the dialog close out from under an in-flight endorse/track. The optimistic flip has
        // already happened on screen, so closing mid-call would leave the user believing the action stuck
        // while a failure quietly reverted a control nobody can see any more. These calls self-timeout in
        // the view-model, so the block is bounded — and it only holds for a write the user just asked for.
        Closing += (_, e) =>
        {
            if (_endorseBusy || _trackBusy) e.Cancel = true;
        };

        // Opened fires once per ShowAsync; the flag covers the pathological re-entry anyway. LoadAsync
        // never throws, so the discarded task can't surface as an unobserved exception.
        Opened += (_, _) =>
        {
            if (_loadStarted) return;
            _loadStarted = true;
            _ = LoadAsync();
        };
    }

    // ---------- loading ----------

    private async Task LoadAsync()
    {
        CatalogDetail? detail = null;
        try
        {
            // Self-timeouts (~10s) and never throws — the guard here is belt-and-braces only.
            detail = await _vm.GetModDetailAsync(_gameId, _modId);
        }
        catch
        {
            detail = null;
        }

        if (detail is null) { ShowFailure(); return; }

        try { Bind(detail); }
        catch { ShowFailure(); } // a malformed field must not take the dialog down
    }

    private void ShowFailure()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        FailureLabel.Text = _openUrl is null
            ? "Couldn't load this mod's details from Nexus. Check your connection and try again."
            : "Couldn't load this mod's details from Nexus. Check your connection and try again — you can still open the mod page in your browser.";
        FailureOpenButton.Visibility = _openUrl is null ? Visibility.Collapsed : Visibility.Visible;
        FailurePanel.Visibility = Visibility.Visible;
    }

    private void Bind(CatalogDetail detail)
    {
        if (SafeUrl.IsHttpUrl(detail.Url)) _openUrl = detail.Url;
        OpenButton.IsEnabled = _openUrl is not null;

        NameLabel.Text = detail.Name;
        HeroInitial.Text = InitialOf(detail.Name);
        ByLabel.Text = ByLine(detail);
        ByLabel.Visibility = Vis(ByLabel.Text.Length > 0);
        MetaLabel.Text = MetaLine(detail);
        MetaLabel.Visibility = Vis(MetaLabel.Text.Length > 0);

        EndorsementsLabel.Text = detail.EndorsementCount is { } endorsements ? $"♥ {endorsements:N0}" : "";
        EndorsementsLabel.Visibility = Vis(EndorsementsLabel.Text.Length > 0);
        DownloadsLabel.Text = detail.DownloadCount is { } downloads ? $"⬇ {downloads:N0}" : "";
        DownloadsLabel.Visibility = Vis(DownloadsLabel.Text.Length > 0);

        // DecodePixelWidth MUST be set before the source — the BitmapImage(Uri) path starts decoding
        // immediately, so a hint applied afterwards is not reliably honored and we keep a full-size
        // bitmap alive for the life of the dialog. Same rule as the storefront cards.
        if (SafeUrl.IsHttpUrl(detail.ImageUrl)
            && Uri.TryCreate(detail.ImageUrl, UriKind.Absolute, out var imageUri))
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 640 };
            bitmap.UriSource = imageUri;
            HeroImage.Source = bitmap;
        }

        var summary = (detail.Summary ?? "").Trim();
        SummaryLabel.Text = summary;
        SummaryLabel.Visibility = Vis(summary.Length > 0);

        // ALWAYS through the converter. DescriptionRaw is BBCode and HTML mixed; rendering it raw shows
        // the markup.
        var description = ModDescriptionText.ToPlainText(detail.DescriptionRaw);
        DescriptionLabel.Text = description;
        DescriptionSection.Visibility = Vis(description.Length > 0);

        BindRequirements(detail);
        BindActions(detail);

        LoadingPanel.Visibility = Visibility.Collapsed;
        FailurePanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void BindRequirements(CatalogDetail detail)
    {
        var rows = new List<RequirementRow>();
        foreach (var requirement in detail.Requirements)
        {
            var name = (requirement.Name ?? "").Trim();
            if (name.Length == 0) name = "Unnamed requirement";

            var link = ResolveRequirementLink(requirement, detail.Url);

            // Notes come from the author and can carry the same BBCode the description does.
            var notes = OneLine(ModDescriptionText.ToPlainText(requirement.Notes));
            var parts = new List<string>(2);
            if (requirement.External) parts.Add("Off-site link");
            if (notes.Length > 0) parts.Add(notes);

            rows.Add(new RequirementRow
            {
                Name = name,
                Detail = string.Join(" · ", parts),
                Link = link,
                LinkTip = link is null
                    ? ""
                    : requirement.External
                        ? "Opens this requirement's page in your browser."
                        : "Opens this mod's page on Nexus in your browser.",
            });
        }

        RequirementList.ItemsSource = rows;
        RequirementsSection.Visibility = Vis(rows.Count > 0);
    }

    /// <summary>Where a requirement row sends the user, or null when there is nowhere to go. The API
    /// supplies a URL for both internal and external requirements; when it doesn't, an internal
    /// requirement can still be reached by swapping the mod id on this mod's own page URL (same game, same
    /// domain). Everything is <see cref="SafeUrl.IsHttpUrl"/>-gated before it can become a link.</summary>
    private static string? ResolveRequirementLink(CatalogRequirement requirement, string? modUrl)
    {
        if (SafeUrl.IsHttpUrl(requirement.Url)) return requirement.Url;

        if (requirement.ModId is { } id && id > 0 && modUrl is not null)
        {
            var cut = modUrl.LastIndexOf('/');
            if (cut > 0)
            {
                var sibling = modUrl[..(cut + 1)] + id.ToString(CultureInfo.InvariantCulture);
                if (SafeUrl.IsHttpUrl(sibling)) return sibling;
            }
        }

        return null;
    }

    /// <summary>Reveal Endorse/Track only when the loaded plugin carries the actions capability AND we have
    /// the UID they key off. An older plugin, a disconnected account, or a mod without a UID leaves them
    /// hidden rather than dead.</summary>
    private void BindActions(CatalogDetail detail)
    {
        if (!_vm.CatalogActionsAvailable || string.IsNullOrWhiteSpace(detail.Uid)) return;

        _uid = detail.Uid;

        // null is UNKNOWN, not a "yes": an unknown viewer state shows the un-acted button, so the user is
        // never told they have already endorsed something they haven't.
        _endorsed = detail.ViewerEndorsed == true;
        _tracked = detail.ViewerTracked == true;

        EndorseToggle.IsChecked = _endorsed;
        TrackToggle.IsChecked = _tracked;
        SyncEndorseLabel(_endorsed);
        SyncTrackLabel(_tracked);

        EndorseToggle.Visibility = Visibility.Visible;
        TrackToggle.Visibility = Visibility.Visible;
    }

    // ---------- actions (the only writes on this surface) ----------

    // Plain handlers delegating into guarded tasks: an async void handler that throws goes straight into
    // the dispatcher, and this dialog is not allowed to take the app down.
    private void OnEndorseClick(object sender, RoutedEventArgs e) => _ = ToggleEndorseAsync();

    private void OnTrackClick(object sender, RoutedEventArgs e) => _ = ToggleTrackAsync();

    private async Task ToggleEndorseAsync()
    {
        // ToggleButton.IsChecked has already flipped by the time Click lands — that flip IS the optimistic
        // update. Setting it back below is programmatic and does not re-raise Click, so a revert can't
        // loop into another call.
        if (_uid is null || _endorseBusy) { EndorseToggle.IsChecked = _endorsed; return; }

        var desired = EndorseToggle.IsChecked == true;
        _endorseBusy = true;
        EndorseToggle.IsEnabled = false; // synchronous, before any await — no double-click race
        SyncEndorseLabel(desired);
        ClearActionMessage();

        var ok = false;
        try { ok = await _vm.SetEndorsedAsync(_uid, desired); }
        catch { ok = false; }

        if (ok)
        {
            _endorsed = desired;
        }
        else
        {
            EndorseToggle.IsChecked = _endorsed;
            SyncEndorseLabel(_endorsed);
            ShowActionMessage(desired
                ? "Couldn't endorse this on Nexus. Nothing changed."
                : "Couldn't remove your endorsement on Nexus. Nothing changed.");
        }

        EndorseToggle.IsEnabled = true;
        _endorseBusy = false;
    }

    private async Task ToggleTrackAsync()
    {
        if (_uid is null || _trackBusy) { TrackToggle.IsChecked = _tracked; return; }

        var desired = TrackToggle.IsChecked == true;
        _trackBusy = true;
        TrackToggle.IsEnabled = false;
        SyncTrackLabel(desired);
        ClearActionMessage();

        var ok = false;
        try { ok = await _vm.SetTrackedAsync(_uid, desired); }
        catch { ok = false; }

        if (ok)
        {
            _tracked = desired;
        }
        else
        {
            TrackToggle.IsChecked = _tracked;
            SyncTrackLabel(_tracked);
            ShowActionMessage(desired
                ? "Couldn't track this on Nexus. Nothing changed."
                : "Couldn't stop tracking this on Nexus. Nothing changed.");
        }

        TrackToggle.IsEnabled = true;
        _trackBusy = false;
    }

    private void SyncEndorseLabel(bool endorsed) => EndorseToggle.Content = endorsed ? "♥ Endorsed" : "♥ Endorse";

    private void SyncTrackLabel(bool tracked) => TrackToggle.Content = tracked ? "Tracked" : "Track";

    private void ShowActionMessage(string message)
    {
        ActionMessage.Text = message;
        ActionMessage.Visibility = Visibility.Visible;
    }

    private void ClearActionMessage()
    {
        ActionMessage.Text = "";
        ActionMessage.Visibility = Visibility.Collapsed;
    }

    // ---------- browser handoff ----------

    private void OnOpenOnNexus(object sender, RoutedEventArgs e) => OpenInBrowser(_openUrl);

    private void OnRequirementClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RequirementRow row }) return;
        OpenInBrowser(row.Link);
    }

    private static void OpenInBrowser(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !SafeUrl.IsHttpUrl(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser association, or the shell refused. Nothing to recover — never take the dialog down.
        }
    }

    // A CDN picture that 404s / times out / decodes badly: drop the source so the neutral placeholder
    // underneath shows through. One dialog, one image, no container recycling — so clearing Source here is
    // local and can't leak onto anything else.
    private void OnHeroImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image) image.Source = null;
    }

    // ---------- formatting ----------

    private static string ByLine(CatalogDetail detail)
    {
        var author = (detail.Author ?? "").Trim();
        var uploader = (detail.Uploader ?? "").Trim();

        if (author.Length == 0 && uploader.Length == 0) return "";
        if (author.Length == 0) return $"Uploaded by {uploader}";
        if (uploader.Length == 0 || string.Equals(author, uploader, StringComparison.OrdinalIgnoreCase))
            return $"by {author}";
        return $"by {author}  ·  uploaded by {uploader}";
    }

    private static string MetaLine(CatalogDetail detail)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(detail.Category)) parts.Add(detail.Category!.Trim());
        if (!string.IsNullOrWhiteSpace(detail.Version)) parts.Add($"v{detail.Version!.Trim().TrimStart('v', 'V')}");
        if (detail.UpdatedAt is { } updated) parts.Add($"Updated {updated.ToLocalTime():MMM d, yyyy}");
        return string.Join("  ·  ", parts);
    }

    /// <summary>Flatten converted notes onto one line and cap them — a requirement row is a label, not a
    /// second description, and an author's multi-paragraph note would otherwise push the list off-screen.</summary>
    private static string OneLine(string text)
    {
        var flattened = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (flattened.Contains("  ", StringComparison.Ordinal))
            flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);
        return flattened.Length <= 200 ? flattened : flattened[..199].TrimEnd() + "…";
    }

    private static string InitialOf(string? name)
        => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
}
