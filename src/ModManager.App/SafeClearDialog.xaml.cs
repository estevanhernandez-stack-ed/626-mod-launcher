using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core.RestorePoints;

namespace ModManager.App;

/// <summary>
/// Safe Clear dialog — lets the user archive the current launcher state to a restore point,
/// choose the post-clear end-state (vanilla vs mods-active), and optionally preserve the
/// saved Nexus API key. Opened from Settings → Reset launcher.
/// </summary>
public sealed partial class SafeClearDialog : ContentDialog
{
    private readonly RestorePointService _svc;

    /// <summary>True when the clear completed successfully and the dialog closed normally.</summary>
    public bool Cleared { get; private set; }

    /// <summary>The result from <see cref="RestorePointService.SafeClearAsync"/> — populated only
    /// when <see cref="Cleared"/> is true.</summary>
    public SafeClearResult? Result { get; private set; }

    public SafeClearDialog(IntPtr hwnd, RestorePointService svc, bool nexusConnected)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        _ = hwnd; // stored for consistency with the other dialog constructors; not needed here
        _svc = svc;

        // F-037 residue / F-072 pattern: the XAML style danger-fills the primary AT REST, but the
        // hover/pressed visual states re-resolve ButtonBackground* via ThemeResource and win.
        // Element-scoped overrides on the PrimaryButton itself outrank everything, and they're the
        // SAME live brush instances ThemeService mutates — the fill holds through hover, re-themed.
        // Hooked on the Title content's Loaded (Opened races the popup tree; see DialogTheming).
        if (Title is FrameworkElement titleContent)
        {
            titleContent.Loaded += (s, _) =>
            {
                // Search down from THIS dialog first (tight — can't hit another popup's
                // PrimaryButton); the walk-to-root pass is the fallback for template shapes
                // where the part tree doesn't hang off the dialog element.
                var primary = FindDescendant(this, "PrimaryButton") as Button;
                if (primary is null)
                {
                    DependencyObject? node = (DependencyObject)s, root = null;
                    while (node is not null) { root = node; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node); }
                    primary = root is null ? null : FindDescendant(root, "PrimaryButton") as Button;
                }
                if (primary is null) return;
                var res = Application.Current.Resources;
                primary.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
                primary.Resources["ButtonBackgroundPressed"] = res["ThemeDanger"];
                primary.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
                primary.Resources["ButtonForegroundPressed"] = res["ThemeBg"];
            };
        }

        // Seed the ComboBox — set in code, not XAML, to avoid literal-bool/SelectedItem-in-markup
        // parse issues (same pattern as SettingsDialog's BackdropBox seeding).
        EndStateBox.SelectedIndex = 0;

        if (!nexusConnected)
        {
            // No Nexus key on disk → the toggle is meaningless. Hide it and treat KeepNexus as
            // true so the (non-existent) key is never touched.
            KeepNexusSwitch.Visibility = Visibility.Collapsed;
        }
    }

    private SafeClearOptions BuildOptions() => new()
    {
        CreateRestorePoint = CreateRestorePointBox.IsChecked == true,
        // When the switch is hidden (Nexus not connected), always pass true — nothing to keep/delete.
        KeepNexus = KeepNexusSwitch.Visibility != Visibility.Visible || KeepNexusSwitch.IsOn,
        DefaultEndState = EndStateBox.SelectedIndex == 1 ? "modsActive" : "vanilla",
    };

    private async void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Second click on the relabelled "Done" button — the clear already ran and the confirmation
        // was shown. Let the dialog close (Cleared/Result already set; the caller refreshes).
        if (Cleared)
            return;

        var deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            ResultBar.IsOpen = false;

            var result = await _svc.SafeClearAsync(BuildOptions());
            if (!result.Ok)
            {
                ResultBar.Severity = InfoBarSeverity.Error;
                ResultBar.Message = result.RefusedReason ?? "Couldn't reset.";
                ResultBar.IsOpen = true;
                args.Cancel = true;         // keep the dialog open so the user can fix + retry
                IsPrimaryButtonEnabled = true;
                return;
            }

            Result = result;
            Cleared = true;
            // Confirm before close (Task 7 — a silent close gave no confirmation): show the success
            // message in the InfoBar, relabel the button to "Done", and keep the dialog open so the
            // user actually sees that a restore point was made and where to find it.
            ResultBar.Severity = InfoBarSeverity.Success;
            ResultBar.Message = SafeClearSummary.SuccessMessage(result);
            ResultBar.IsOpen = true;
            sender.PrimaryButtonText = "Done";
            args.Cancel = true;
            IsPrimaryButtonEnabled = true;
        }
        catch (Exception ex)
        {
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Message = $"Reset failed: {ex.Message}";
            ResultBar.IsOpen = true;
            args.Cancel = true;
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (FindDescendant(child, name) is { } hit) return hit;
        }
        return null;
    }
}
