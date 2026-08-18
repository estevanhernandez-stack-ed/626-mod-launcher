using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

/// <summary>One MOD line in the Updates directory — not one file line. Immutable and built once per
/// open, so every binding is OneTime.</summary>
public sealed class UpdateRow
{
    // Ctor-injected and read-only on purpose — an init-only `required` property makes WinUI's
    // XamlTypeInfo generator emit an activator it can't satisfy (see NexusCatalogCard's note).
    public UpdateRow(PendingUpdateGroup group) => Group = group;

    public PendingUpdateGroup Group { get; }

    /// <summary>The member the row speaks for. A group of four option files is still one mod, and
    /// the version line comes from whichever of them can justify one.</summary>
    private PendingUpdate Pending => Group.Primary;

    public string RowAutomationId => $"UpdateRow.{Pending.ModKey}";

    public string ModName => string.IsNullOrWhiteSpace(Group.ModName) ? Pending.ModKey : Group.ModName;

    /// <summary>Names the files behind a collapsed row. Grouping is a display decision, so the thing
    /// it hides has to stay readable — otherwise "Faster Ships" silently stands for four downloads
    /// and the user cannot tell which one Nexus is talking about.</summary>
    public string FilesText => Group.IsMulti
        ? $"{Group.Members.Count} files — {string.Join(", ", Group.MemberKeys)}"
        : "";

    public Visibility FilesVisibility => Group.IsMulti ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// What the row says about versions — and only when it can say something true.
    ///
    /// <para>This used to render "installed → latest" for every row regardless of why the row existed,
    /// which produced about twenty consecutive "unknown → 1.2.1" on a real install, plus "1.1 → 1" and
    /// "1.0.1 → 1.0.0". Those last two read as DOWNGRADES: the launcher was inviting the user to go
    /// backwards. The arrow is a claim about direction, and a row pending on Nexus's per-user flag has
    /// no left-hand side to point away from (A10).</para>
    /// </summary>
    public string VersionText
    {
        get
        {
            // Listed because Nexus says so, not because we compared anything. Say that.
            if (Pending.Reason == PendingReason.NexusFlag || string.IsNullOrWhiteSpace(Pending.InstalledVersion))
                return "Nexus says a newer file is available";

            if (string.IsNullOrWhiteSpace(Pending.LatestVersion))
                return $"{Pending.InstalledVersion} installed — a newer file is available";

            // "1" and "1.0" are the same release. Showing that pair implies a change that is not
            // there; the row stays, because Nexus may still be right that a newer FILE exists, and
            // dropping it would hide a real update to tidy away a display problem.
            if (Pending.VersionsLookEquivalent)
                return $"{Pending.InstalledVersion} installed — Nexus has a newer file";

            // The arrow only where the direction is provable. Two known versions differing is not
            // enough: "1.1" and "1" differ, and an arrow between them points backwards. Those were the
            // two examples A10 was filed about and they survived it, because the fix drew the pair
            // whenever both sides were known (A27).
            if (Pending.LatestIsProvablyNewer)
                return $"{Pending.InstalledVersion} → {Pending.LatestVersion}";

            // Both known, order not provable — a descending pair, or a suffix like "1.0.0.1-hotfix"
            // that no strict parse can rank. Name both and imply nothing.
            return $"{Pending.InstalledVersion} installed · Nexus lists {Pending.LatestVersion}";
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
        // One row per mod, not per Nexus FILE (A11).
        Rows = summary.Groups.Select(g => new UpdateRow(g)).ToList();
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
