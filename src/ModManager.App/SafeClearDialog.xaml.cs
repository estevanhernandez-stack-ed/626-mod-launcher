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
        _ = hwnd; // stored for consistency with the other dialog constructors; not needed here
        _svc = svc;

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
        var deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            ResultBar.IsOpen = false;

            var result = await _svc.SafeClearAsync(BuildOptions());
            if (!result.Ok)
            {
                ResultBar.Message = result.RefusedReason ?? "Couldn't reset.";
                ResultBar.IsOpen = true;
                args.Cancel = true;         // keep the dialog open so the user can fix + retry
                IsPrimaryButtonEnabled = true;
                return;
            }

            Result = result;
            Cleared = true;                 // success — let the dialog close normally
        }
        catch (Exception ex)
        {
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
}
