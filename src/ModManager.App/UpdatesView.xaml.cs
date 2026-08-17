using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

/// <summary>One mod line in the Updates directory — installed version on the left of the arrow, the
/// latest version a previous Nexus refresh recorded on the right. Immutable and built once per open, so
/// every binding is OneTime.</summary>
public sealed class UpdateRow
{
    // Ctor-injected and read-only on purpose — an init-only `required` property makes WinUI's
    // XamlTypeInfo generator emit an activator it can't satisfy (see NexusCatalogCard's note).
    public UpdateRow(PendingUpdate pending) => Pending = pending;

    public PendingUpdate Pending { get; }

    public string RowAutomationId => $"UpdateRow.{Pending.ModKey}";

    public string ModName => string.IsNullOrWhiteSpace(Pending.ModName) ? Pending.ModKey : Pending.ModName;

    /// <summary>"1.2.0 → 1.3.1". A mod with no recorded installed version says so rather than showing a
    /// blank left-hand side — we know what's on Nexus, we just never learned what's on disk.</summary>
    public string VersionText
    {
        get
        {
            var installed = string.IsNullOrWhiteSpace(Pending.InstalledVersion) ? "unknown" : Pending.InstalledVersion!;
            // The right-hand side can be unknown too. A row can be pending purely on Nexus's own
            // per-user flag, which rides in on a search hit and carries no version — so we know the
            // user is behind without ever having been told what they are behind of.
            var latest = string.IsNullOrWhiteSpace(Pending.LatestVersion) ? "unknown" : Pending.LatestVersion!;
            return $"{installed} → {latest}";
        }
    }
}

/// <summary>One game's block in the Updates directory: the game's name, how many of its mods are
/// pending, and the rows themselves. Carries the game id so Open game can land on the right game.</summary>
public sealed class UpdateGameGroup
{
    public UpdateGameGroup(GameUpdateSummary summary)
    {
        GameId = summary.GameId;
        GameName = string.IsNullOrWhiteSpace(summary.GameName) ? summary.GameId : summary.GameName;
        Rows = summary.Pending.Select(p => new UpdateRow(p)).ToList();
    }

    public string GameId { get; }
    public string GameName { get; }

    /// <summary>Per-group name for the Open game button. Every one of them reads "Open game"
    /// otherwise, and this view exists precisely to show several games at once.</summary>
    public string OpenGameAutomationName => "Open " + GameName;
    public IReadOnlyList<UpdateRow> Rows { get; }

    public string CountText => Rows.Count == 1 ? "1 UPDATE" : $"{Rows.Count} UPDATES";
}

/// <summary>
/// The cross-game Updates directory: "what needs updating, everywhere?" answered in one place. A
/// full-size UserControl swapped into MainWindow's <c>UpdatesHost</c>, the same host pattern
/// <see cref="LibraryView"/> and <see cref="NexusCatalogView"/> use, with the same Back affordance.
///
/// <para><b>A directory, not a mechanism.</b> Nothing here applies an update. Every row's game block
/// carries an Open game button that lands the user in that game's mod view, where the existing UPDATE
/// chips and the existing Refresh flow already live. This view adds no new update path.</para>
///
/// <para><b>No network, ever.</b> The whole surface renders from the
/// <see cref="GameUpdateSummary"/> snapshot the Library view-model already built out of each game's
/// persisted <c>metadata.json</c>. It works identically with Nexus disconnected — it is reporting what
/// previous, user-initiated refreshes learned, not asking anything.</para>
///
/// <para><b>Two empty states, never conflated.</b> "Checked, nothing pending" and "nothing has ever
/// been checked" are different facts and read differently. A game that has never been refreshed is
/// absent from the list, so when any such game exists the footer names it — silence about an unchecked
/// game would read as "that one's fine."</para>
/// </summary>
public sealed partial class UpdatesView : UserControl
{
    /// <summary>The game blocks, most-pending first. Public so the ItemsControl can x:Bind it.</summary>
    public ObservableCollection<UpdateGameGroup> Groups { get; } = new();

    /// <summary>Raised by the Back affordance (or Escape) — the shell collapses the host.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Raised by an Open game button, carrying the game id the shell should open.</summary>
    public event EventHandler<string>? OpenGameRequested;

    public UpdatesView(IReadOnlyList<GameUpdateSummary> summaries)
    {
        var all = summaries ?? Array.Empty<GameUpdateSummary>();

        // Most pending first, then alphabetical — the games that need attention lead.
        foreach (var s in all.Where(s => s.Count > 0)
                             .OrderByDescending(s => s.Count)
                             .ThenBy(s => s.GameName, StringComparer.CurrentCultureIgnoreCase))
            Groups.Add(new UpdateGameGroup(s));

        InitializeComponent();

        var total = Groups.Sum(g => g.Rows.Count);
        var anyChecked = all.Any(s => s.Checked);
        var uncheckedCount = all.Count(s => !s.Checked);

        if (total > 0)
        {
            CountLabel.Text = total == 1 ? "1 update" : $"{total:N0} updates";
            ListScroller.Visibility = Visibility.Visible;

            if (uncheckedCount > 0)
            {
                UncheckedNote.Text = uncheckedCount == 1
                    ? "1 game has never been checked, so it isn't listed here. Open it and hit Refresh to find out."
                    : $"{uncheckedCount} games have never been checked, so they aren't listed here. Open one and hit Refresh to find out.";
                UncheckedNote.Visibility = Visibility.Visible;
            }
            return;
        }

        // Nothing pending. Which of the two nothings is it?
        EmptyPanel.Visibility = Visibility.Visible;
        if (anyChecked)
        {
            EmptyTitle.Text = "No updates found.";
            EmptyBody.Text = uncheckedCount > 0
                ? $"Every game 626 has checked is on its latest known version. {uncheckedCount} "
                  + $"{(uncheckedCount == 1 ? "game has" : "games have")} never been checked — open one and hit Refresh to include it."
                : "Every game 626 has checked is on its latest known version.";
        }
        else
        {
            EmptyTitle.Text = "No games have been checked yet.";
            EmptyBody.Text = "This page reports what earlier Nexus refreshes already found — it never checks on its "
                             + "own. Open a game and hit Refresh, and whatever it learns shows up here.";
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // Escape leaves the view, matching Back. Tunnels from the root so it works whichever child has focus.
    private void OnViewPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // Hand the game id up to the shell — this view never navigates itself, and never touches the registry.
    private void OnOpenGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UpdateGameGroup group } && group.GameId.Length > 0)
            OpenGameRequested?.Invoke(this, group.GameId);
    }
}
